"""User profile, privacy preferences and data deletion."""

from __future__ import annotations

from app.core.container import Container
from app.core.logging import get_logger
from app.core.utils import as_utc, utcnow

log = get_logger(__name__)


def _iso(value) -> str | None:
    v = as_utc(value)
    return v.isoformat() if v else None


def public_user(user: dict) -> dict:
    return {
        "id": user["_id"],
        "email": user["email"],
        "display_name": user["display_name"],
        "onboarding_completed": bool(user.get("onboarding_completed")),
        "preferences": user.get("preferences", {}),
        "created_at": _iso(user.get("created_at")),
    }


async def delete_interview_data(c: Container, user_id: str) -> dict:
    """Deletes all interview sessions, transcripts, reports, resume text and usage-linked session refs.

    Usage counters are kept (without content) so that deleting data cannot be used to
    reset monthly plan limits.
    """
    if c.live_registry:
        await c.live_registry.terminate_user_sessions(user_id, reason="data_deleted")
    sessions = await c.db.sessions.delete_many({"user_id": user_id})
    messages = await c.db.messages.delete_many({"user_id": user_id})
    reports = await c.db.reports.delete_many({"user_id": user_id})
    resumes = await c.db.resumes.delete_many({"user_id": user_id})
    await c.db.usage.update_many({"user_id": user_id}, {"$set": {"session_id": None}})
    log.info("User interview data deleted", extra={"ctx": {"user_id": user_id}})
    return {
        "sessions_deleted": sessions.deleted_count,
        "messages_deleted": messages.deleted_count,
        "reports_deleted": reports.deleted_count,
        "resumes_deleted": resumes.deleted_count,
    }


async def delete_account(c: Container, user_id: str) -> None:
    await delete_interview_data(c, user_id)
    await c.db.refresh_tokens.delete_many({"user_id": user_id})
    await c.db.subscriptions.delete_many({"user_id": user_id})
    await c.db.usage.delete_many({"user_id": user_id})
    await c.db.users.delete_one({"_id": user_id})
    log.info("User account deleted", extra={"ctx": {"user_id": user_id}})


async def export_user_data(c: Container, user: dict) -> dict:
    """Machine-readable export of the user's stored data (data portability)."""
    uid = user["_id"]
    sessions = await c.db.sessions.find({"user_id": uid}).to_list(None)
    reports = await c.db.reports.find({"user_id": uid}).to_list(None)
    messages = await c.db.messages.find({"user_id": uid}).sort("seq", 1).to_list(None)
    resumes = await c.db.resumes.find({"user_id": uid}).sort("created_at", 1).to_list(None)
    for doc in (*sessions, *reports, *messages):
        for k, v in list(doc.items()):
            if hasattr(v, "isoformat"):
                doc[k] = _iso(v)
    return {
        "exported_at": utcnow().isoformat(),
        "profile": public_user(user),
        "interview_sessions": sessions,
        "interview_reports": reports,
        "transcript_messages": messages,
        "documents": [{"filename": r["filename"], "uploaded_at": _iso(r["created_at"])} for r in resumes],
    }
