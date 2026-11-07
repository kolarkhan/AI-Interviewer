"""Registration, login and refresh-token rotation with reuse detection."""

from __future__ import annotations

from datetime import timedelta

from pymongo.errors import DuplicateKeyError

from app.core.config import Settings
from app.core.errors import Conflict, Unauthorized
from app.core.logging import get_logger
from app.core.security import (
    DUMMY_PASSWORD_HASH,
    create_access_token,
    hash_password,
    hash_token,
    new_refresh_token,
    password_needs_rehash,
    verify_password,
)
from app.core.utils import as_utc, new_id, utcnow
from app.db.mongo import Database
from app.modules.auth.schemas import TokenPair
from app.modules.billing.service import BillingService

log = get_logger(__name__)


def default_preferences() -> dict:
    return {
        "target_role_id": None,
        "experience_level": None,
        "voice": "cedar",
        "avatar_id": "alex",
        "show_live_transcript": True,
        # Privacy: transcripts are deleted after the report is generated unless enabled.
        "retain_transcripts": False,
    }


class AuthService:
    def __init__(self, db: Database, settings: Settings) -> None:
        self.db = db
        self.settings = settings

    async def register(self, *, email: str, password: str, display_name: str) -> tuple[dict, TokenPair]:
        now = utcnow()
        user = {
            "_id": new_id(),
            "email": email.lower().strip(),
            "password_hash": hash_password(password),
            "display_name": display_name,
            "status": "active",
            "token_version": 0,
            "onboarding_completed": False,
            "preferences": default_preferences(),
            "consents": {"terms_accepted_at": now, "transcript_retention_at": None, "resume_processing_at": None},
            "created_at": now,
            "updated_at": now,
            "last_login_at": now,
        }
        try:
            await self.db.users.insert_one(user)
        except DuplicateKeyError as exc:
            raise Conflict("An account with this email already exists", code="email_taken") from exc
        await BillingService(self.db).ensure_subscription(user["_id"])
        log.info("User registered", extra={"ctx": {"user_id": user["_id"]}})
        return user, await self._issue_tokens(user)

    async def login(self, *, email: str, password: str) -> tuple[dict, TokenPair]:
        user = await self.db.users.find_one({"email": email.lower().strip()})
        if not user or user.get("status") != "active":
            verify_password(DUMMY_PASSWORD_HASH, password)  # equalise timing
            raise Unauthorized("Incorrect email or password", code="invalid_credentials")
        if not verify_password(user["password_hash"], password):
            raise Unauthorized("Incorrect email or password", code="invalid_credentials")

        update: dict = {"last_login_at": utcnow()}
        if password_needs_rehash(user["password_hash"]):
            update["password_hash"] = hash_password(password)
        await self.db.users.update_one({"_id": user["_id"]}, {"$set": update})
        return user, await self._issue_tokens(user)

    async def refresh(self, refresh_token: str) -> TokenPair:
        token_hash = hash_token(refresh_token)
        record = await self.db.refresh_tokens.find_one({"token_hash": token_hash})
        if not record:
            raise Unauthorized("Invalid refresh token", code="refresh_invalid")

        if record.get("revoked_at") is not None:
            # Reuse of a rotated token: assume theft, revoke the whole family.
            await self.db.refresh_tokens.update_many(
                {"family_id": record["family_id"], "revoked_at": None}, {"$set": {"revoked_at": utcnow()}}
            )
            log.warning("Refresh token reuse detected; family revoked", extra={"ctx": {"user_id": record["user_id"]}})
            raise Unauthorized("Session has been revoked. Please sign in again.", code="refresh_reused")

        if as_utc(record["expires_at"]) <= utcnow():
            raise Unauthorized("Refresh token expired. Please sign in again.", code="refresh_expired")

        user = await self.db.users.find_one({"_id": record["user_id"]})
        if not user or user.get("status") != "active":
            raise Unauthorized("Account not found or disabled", code="account_inactive")

        pair = await self._issue_tokens(user, family_id=record["family_id"])
        await self.db.refresh_tokens.update_one(
            {"_id": record["_id"]},
            {"$set": {"revoked_at": utcnow(), "replaced_by": hash_token(pair.refresh_token)}},
        )
        return pair

    async def logout(self, refresh_token: str) -> None:
        await self.db.refresh_tokens.update_one(
            {"token_hash": hash_token(refresh_token), "revoked_at": None}, {"$set": {"revoked_at": utcnow()}}
        )

    async def logout_everywhere(self, user_id: str) -> None:
        await self.db.users.update_one({"_id": user_id}, {"$inc": {"token_version": 1}})
        await self.db.refresh_tokens.update_many(
            {"user_id": user_id, "revoked_at": None}, {"$set": {"revoked_at": utcnow()}}
        )

    async def _issue_tokens(self, user: dict, family_id: str | None = None) -> TokenPair:
        access, access_exp = create_access_token(
            self.settings, user_id=user["_id"], token_version=int(user.get("token_version", 0))
        )
        refresh = new_refresh_token()
        now = utcnow()
        refresh_exp = now + timedelta(days=self.settings.refresh_token_ttl_days)
        await self.db.refresh_tokens.insert_one({
            "_id": new_id(),
            "user_id": user["_id"],
            "family_id": family_id or new_id(),
            "token_hash": hash_token(refresh),
            "created_at": now,
            "expires_at": refresh_exp,
            "revoked_at": None,
            "replaced_by": None,
        })
        return TokenPair(
            access_token=access,
            access_token_expires_at=access_exp,
            refresh_token=refresh,
            refresh_token_expires_at=refresh_exp,
        )
