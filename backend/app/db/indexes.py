"""Index definitions. Idempotent; run on startup (`create_index` is a no-op if it exists)."""

from __future__ import annotations

from pymongo import ASCENDING, DESCENDING

from app.db.mongo import Database


async def ensure_indexes(db: Database) -> None:
    await db.users.create_index([("email", ASCENDING)], unique=True, name="uniq_email")

    await db.refresh_tokens.create_index([("token_hash", ASCENDING)], unique=True, name="uniq_token_hash")
    await db.refresh_tokens.create_index([("user_id", ASCENDING)], name="by_user")
    await db.refresh_tokens.create_index([("family_id", ASCENDING)], name="by_family")
    # TTL: MongoDB deletes expired refresh tokens automatically.
    await db.refresh_tokens.create_index([("expires_at", ASCENDING)], expireAfterSeconds=0, name="ttl_expires")

    await db.sessions.create_index([("user_id", ASCENDING), ("created_at", DESCENDING)], name="by_user_recent")
    await db.sessions.create_index([("user_id", ASCENDING), ("status", ASCENDING)], name="by_user_status")

    await db.messages.create_index([("session_id", ASCENDING), ("seq", ASCENDING)], name="by_session_seq")
    await db.messages.create_index([("user_id", ASCENDING)], name="by_user")
    # TTL: transcripts expire automatically (purge_after is set per message).
    await db.messages.create_index([("purge_after", ASCENDING)], expireAfterSeconds=0, name="ttl_purge")

    await db.reports.create_index([("session_id", ASCENDING)], unique=True, name="uniq_session")
    await db.reports.create_index([("user_id", ASCENDING), ("created_at", DESCENDING)], name="by_user_recent")

    await db.subscriptions.create_index([("user_id", ASCENDING)], unique=True, name="uniq_user")

    await db.usage.create_index([("user_id", ASCENDING), ("period", ASCENDING), ("kind", ASCENDING)],
                                name="by_user_period_kind")

    # Several documents per user. Older deployments had a unique index here: drop it first.
    try:
        info = await db.resumes.index_information()
        if "uniq_user" in info:
            await db.resumes.drop_index("uniq_user")
    except Exception:  # noqa: BLE001 - index migration is best effort
        pass
    await db.resumes.create_index([("user_id", ASCENDING), ("created_at", ASCENDING)], name="by_user")

    await db.questions.create_index([("role_id", ASCENDING), ("interview_type", ASCENDING),
                                     ("difficulty", ASCENDING)], name="by_role_type_difficulty")
