"""Password hashing (Argon2id) and JWT access tokens."""

from __future__ import annotations

import hashlib
import secrets
from datetime import UTC, datetime, timedelta
from typing import Any

import jwt
from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError, VerifyMismatchError

from app.core.config import Settings

_hasher = PasswordHasher()  # Argon2id with library-recommended parameters.

ACCESS_TOKEN_TYPE = "access"


def hash_password(password: str) -> str:
    return _hasher.hash(password)


def verify_password(password_hash: str, password: str) -> bool:
    try:
        return _hasher.verify(password_hash, password)
    except (VerifyMismatchError, VerificationError, InvalidHashError):
        return False


def password_needs_rehash(password_hash: str) -> bool:
    return _hasher.check_needs_rehash(password_hash)


# A dummy hash used to equalise timing when the e-mail does not exist.
DUMMY_PASSWORD_HASH = _hasher.hash("timing-equaliser-not-a-real-password")


def create_access_token(settings: Settings, *, user_id: str, token_version: int) -> tuple[str, datetime]:
    now = datetime.now(UTC)
    expires = now + timedelta(minutes=settings.access_token_ttl_minutes)
    payload: dict[str, Any] = {
        "sub": user_id,
        "typ": ACCESS_TOKEN_TYPE,
        "ver": token_version,
        "iss": settings.jwt_issuer,
        "iat": int(now.timestamp()),
        "exp": int(expires.timestamp()),
        "jti": secrets.token_hex(8),
    }
    token = jwt.encode(payload, settings.jwt_secret.get_secret_value(), algorithm="HS256")
    return token, expires


def decode_access_token(settings: Settings, token: str) -> dict[str, Any]:
    """Raises `jwt.PyJWTError` on any problem (expired, bad signature, wrong type)."""
    payload = jwt.decode(
        token,
        settings.jwt_secret.get_secret_value(),
        algorithms=["HS256"],
        issuer=settings.jwt_issuer,
        options={"require": ["exp", "iat", "sub", "iss"]},
    )
    if payload.get("typ") != ACCESS_TOKEN_TYPE:
        raise jwt.InvalidTokenError("wrong token type")
    return payload


def new_refresh_token() -> str:
    """Opaque refresh token. Only its SHA-256 hash is stored server-side."""
    return secrets.token_urlsafe(48)


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def safety_identifier(user_id: str) -> str:
    """Stable, non-reversible identifier sent to OpenAI for abuse monitoring."""
    return hashlib.sha256(f"ai-interviewer:{user_id}".encode()).hexdigest()[:32]
