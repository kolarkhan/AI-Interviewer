"""Report generation pipeline, background scheduling and progress aggregation."""

from __future__ import annotations

import asyncio
import contextlib

from pymongo import ReturnDocument

from app.core.container import Container
from app.core.logging import get_logger
from app.core.security import safety_identifier
from app.core.utils import as_utc, new_id, utcnow
from app.modules.billing.plans import ReportTier
from app.modules.billing.service import BillingService, UsageKind
from app.modules.catalog.data import language_title
from app.modules.interviews.prompts import role_title
from app.modules.interviews.schemas import ReportStatus
from app.modules.reports.generator import ReportContext, ReportGenerationError
from app.modules.reports.schemas import CATEGORY_KEYS, DISCLAIMER, SCHEMA_VERSION, ReportValidationError, \
    validate_and_normalise

log = get_logger(__name__)

MAX_ATTEMPTS = 2


def _iso(v) -> str | None:
    v = as_utc(v)
    return v.isoformat() if v else None


def public_report(r: dict) -> dict:
    return {
        "id": r["_id"],
        "session_id": r["session_id"],
        "schema_version": r.get("schema_version", SCHEMA_VERSION),
        "tier": r["tier"],
        "is_demo": bool(r.get("is_demo")),
        "overall_score": r["overall_score"],
        "categories": r["categories"],
        "summary": r["summary"],
        "strengths": r["strengths"],
        "improvements": r["improvements"],
        "recommended_topics": r["recommended_topics"],
        "confidence_indicators": r.get("confidence_indicators", []),
        "answer_feedback": r.get("answer_feedback", []),
        "practice_plan": r.get("practice_plan", []),
        "disclaimer": r.get("disclaimer", DISCLAIMER),
        "created_at": _iso(r.get("created_at")),
    }


class ReportService:
    def __init__(self, c: Container) -> None:
        self.c = c
        self.db = c.db

    async def generate_for_session(self, session_id: str) -> str:
        """Generates and stores the report. Returns the final report status."""
        session = await self.db.sessions.find_one_and_update(
            {"_id": session_id, "report_status": {"$in": [ReportStatus.PENDING.value, ReportStatus.FAILED.value]}},
            {"$set": {"report_status": ReportStatus.GENERATING.value}},
            return_document=ReturnDocument.AFTER,
        )
        if not session:
            return "skipped"  # already generating/ready, or deleted

        messages = await self.db.messages.find({"session_id": session_id}).sort("seq", 1).to_list(None)
        if not any(m["role"] == "candidate" for m in messages):
            await self._set_status(session_id, ReportStatus.INSUFFICIENT_DATA)
            return ReportStatus.INSUFFICIENT_DATA.value

        cfg = session["config"]
        ctx = ReportContext(
            session_id=session_id,
            user_safety_id=safety_identifier(session["user_id"]),
            tier=ReportTier(session.get("report_tier", ReportTier.BASIC.value)),
            role_title=role_title(cfg),
            interview_type=cfg["interview_type"],
            difficulty=cfg["difficulty"],
            experience_level=cfg["experience_level"],
            language=language_title(cfg.get("language")),
            duration_minutes=int(cfg["duration_minutes"]),
            transcript=[{"role": m["role"], "text": m["text"], "truncated": m.get("truncated", False)}
                        for m in messages],
            is_demo=bool(session.get("is_demo")),
        )
        assert self.c.report_generator is not None

        last_error = "unknown"
        normalised: dict | None = None
        for attempt in range(1, MAX_ATTEMPTS + 1):
            try:
                generated = await self.c.report_generator.generate(ctx)
                normalised = validate_and_normalise(generated.content)
                break
            except ReportValidationError as exc:
                last_error = f"validation: {exc}"
            except ReportGenerationError as exc:
                last_error = str(exc)
                if not exc.retryable:
                    break
            except Exception as exc:  # noqa: BLE001
                last_error = type(exc).__name__
                log.exception("Unexpected report generation error", extra={"ctx": {"session_id": session_id}})
            log.warning("Report attempt failed", extra={"ctx": {"session_id": session_id, "attempt": attempt,
                                                                "error": last_error}})
            if attempt < MAX_ATTEMPTS:
                await asyncio.sleep(1.5 * attempt)
        if normalised is None:
            await self._set_status(session_id, ReportStatus.FAILED, error=last_error)
            return ReportStatus.FAILED.value

        now = utcnow()
        report = {
            "_id": new_id(),
            "session_id": session_id,
            "user_id": session["user_id"],
            "schema_version": SCHEMA_VERSION,
            "tier": ctx.tier.value,
            "is_demo": ctx.is_demo or generated.model == "demo",
            "model": generated.model,
            "generation_ms": generated.generation_ms,
            "token_usage": generated.usage,
            "disclaimer": DISCLAIMER,
            "role_id": cfg["role_id"],
            "interview_type": cfg["interview_type"],
            "created_at": now,
            **normalised,
        }
        # Session may have been deleted while generating — don't resurrect data.
        if not await self.db.sessions.find_one({"_id": session_id}):
            return "skipped"
        await self.db.reports.delete_many({"session_id": session_id})
        await self.db.reports.insert_one(report)
        await self.db.sessions.update_one(
            {"_id": session_id},
            {"$set": {"report_status": ReportStatus.READY.value, "report_id": report["_id"],
                      "overall_score": report["overall_score"], "report_error": None}},
        )
        await BillingService(self.db).record(session["user_id"], UsageKind.REPORT_GENERATED, 1, session_id=session_id)
        user = await self.db.users.find_one({"_id": session["user_id"]})
        if not user or not user.get("preferences", {}).get("retain_transcripts"):
            await self.db.messages.delete_many({"session_id": session_id})
        log.info("Report generated", extra={"ctx": {"session_id": session_id, "model": generated.model,
                                                     "ms": generated.generation_ms}})
        return ReportStatus.READY.value

    async def _set_status(self, session_id: str, status: ReportStatus, error: str | None = None) -> None:
        await self.db.sessions.update_one({"_id": session_id},
                                          {"$set": {"report_status": status.value, "report_error": error}})

    async def progress(self, user_id: str, *, full: bool) -> dict:
        reports = await self.db.reports.find({"user_id": user_id, "is_demo": {"$ne": True}}) \
            .sort("created_at", 1).to_list(None)
        sessions = await self.db.sessions.find({"user_id": user_id, "status": "completed"}).to_list(None)
        practice_minutes = round(sum(s.get("billed_seconds", 0) for s in sessions) / 60, 1)
        overall = [r["overall_score"] for r in reports]
        summary = {
            "interviews_completed": len(sessions),
            "reports": len(reports),
            "practice_minutes": practice_minutes,
            "average_score": round(sum(overall) / len(overall)) if overall else None,
            "latest_score": overall[-1] if overall else None,
            "progress_tracking_locked": not full,
        }
        if not full or not reports:
            return {"summary": summary, "trend": [], "categories": {}}

        categories: dict[str, dict] = {}
        for key in CATEGORY_KEYS:
            scores = [r["categories"][key]["score"] for r in reports if r["categories"].get(key, {}).get("assessed")]
            if scores:
                recent = scores[-3:]
                categories[key] = {
                    "average": round(sum(scores) / len(scores)),
                    "recent_average": round(sum(recent) / len(recent)),
                    "first": scores[0],
                    "latest": scores[-1],
                    "change": scores[-1] - scores[0],
                }
        ranked = sorted(categories.items(), key=lambda kv: kv[1]["recent_average"])
        summary["strongest_category"] = ranked[-1][0] if ranked else None
        summary["focus_category"] = ranked[0][0] if ranked else None
        summary["first_score"] = overall[0]
        summary["improvement"] = overall[-1] - overall[0]
        trend = [{"session_id": r["session_id"], "date": _iso(r["created_at"]), "overall_score": r["overall_score"],
                  "interview_type": r.get("interview_type"), "role_id": r.get("role_id")} for r in reports[-30:]]
        return {"summary": summary, "trend": trend, "categories": categories}


class ReportScheduler:
    """Runs report generation in background tasks (in-process).

    For multi-instance deployments move this to a durable queue (e.g. a MongoDB-backed job
    collection polled by a worker, or Redis/RQ/Celery). Sessions left in `pending`/`generating`
    after a restart are re-queued on startup by `recover()`.
    """

    def __init__(self, c: Container) -> None:
        self.c = c
        self._tasks: set[asyncio.Task] = set()

    def schedule(self, session_id: str) -> None:
        task = asyncio.create_task(self._run(session_id), name=f"report:{session_id}")
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    async def _run(self, session_id: str) -> None:
        try:
            await ReportService(self.c).generate_for_session(session_id)
        except Exception:  # noqa: BLE001
            log.exception("Report task crashed", extra={"ctx": {"session_id": session_id}})

    async def recover(self) -> int:
        stuck = await self.c.db.sessions.find(
            {"report_status": {"$in": [ReportStatus.PENDING.value, ReportStatus.GENERATING.value]}}
        ).to_list(None)
        for s in stuck:
            await self.c.db.sessions.update_one({"_id": s["_id"]},
                                                {"$set": {"report_status": ReportStatus.PENDING.value}})
            self.schedule(s["_id"])
        return len(stuck)

    async def wait_idle(self, timeout: float = 10) -> None:
        if self._tasks:
            await asyncio.wait(set(self._tasks), timeout=timeout)

    async def shutdown(self) -> None:
        for t in list(self._tasks):
            t.cancel()
        for t in list(self._tasks):
            with contextlib.suppress(asyncio.CancelledError, Exception):
                await t
