"""Interview session lifecycle, ownership checks and transcript storage."""

from __future__ import annotations

from datetime import timedelta

from pymongo import ReturnDocument

from app.core.container import Container
from app.core.errors import AppError, Conflict, NotFound, PlanLimitExceeded
from app.core.logging import get_logger
from app.core.utils import as_utc, new_id, utcnow
from app.modules.billing.plans import Plan
from app.modules.billing.service import BillingService
from app.modules.catalog.data import CUSTOM_ROLE_ID, voices_for_engine
from app.modules.interviews.prompts import question_count_for, role_title
from app.modules.interviews.schemas import ACTIVE_STATUSES, InterviewCreate, ReportStatus, SessionStatus
from app.modules.questions.service import pick_seed_questions

log = get_logger(__name__)

# Minimum conversation needed before the session counts as a real interview.
MIN_CANDIDATE_WORDS_FOR_REPORT = 40
MIN_CANDIDATE_TURNS_FOR_REPORT = 2


def _iso(v) -> str | None:
    v = as_utc(v)
    return v.isoformat() if v else None


def public_session(s: dict) -> dict:
    cfg = s["config"]
    return {
        "id": s["_id"],
        "status": s["status"],
        "is_demo": bool(s.get("is_demo")),
        "config": {**cfg, "role_title": role_title(cfg)},
        "created_at": _iso(s.get("created_at")),
        "started_at": _iso(s.get("started_at")),
        "ended_at": _iso(s.get("ended_at")),
        "end_reason": s.get("end_reason"),
        "elapsed_seconds": int(s.get("elapsed_seconds", 0)),
        "duration_seconds": int(cfg["duration_minutes"]) * 60,
        "report_status": s.get("report_status", ReportStatus.NONE),
        "report_id": s.get("report_id"),
        "overall_score": s.get("overall_score"),
    }


def check_plan_allows(plan: Plan, body: InterviewCreate, has_resume: bool) -> None:
    problems: list[str] = []
    if body.interview_type not in plan.allowed_interview_types:
        problems.append(f"{body.interview_type} interviews")
    if body.difficulty not in plan.allowed_difficulties:
        problems.append(f"{body.difficulty} difficulty")
    if body.duration_minutes > plan.max_duration_minutes:
        problems.append(f"{body.duration_minutes}-minute interviews")
    if body.role_id == CUSTOM_ROLE_ID and not plan.custom_role:
        problems.append("custom roles")
    if body.voice not in plan.voices:
        problems.append(f"the {body.voice} voice")
    if body.avatar_id not in plan.avatars:
        problems.append("this avatar")
    if body.use_resume and not plan.resume_questions:
        problems.append("resume-based questions")
    if problems:
        raise PlanLimitExceeded(
            f"Your {plan.title} plan does not include: " + ", ".join(problems) + ".",
            code="plan_feature_locked",
            details={"locked": problems, "upgrade_available": True},
        )
    if body.use_resume and not has_resume:
        raise Conflict("Upload a resume before enabling resume-based questions.", code="resume_missing")


class InterviewService:
    def __init__(self, c: Container) -> None:
        self.c = c
        self.db = c.db
        self.billing = BillingService(c.db)

    async def create(self, user: dict, body: InterviewCreate) -> dict:
        plan = await self.billing.get_plan(user["_id"])
        resume_docs = (await self.db.resumes.find({"user_id": user["_id"]}).sort("created_at", 1).to_list(None)
                       if body.use_resume else [])
        resume = resume_docs[0] if resume_docs else None
        engine_voices = {v["id"] for v in voices_for_engine(self.c.settings.openai_voice_engine)}
        if body.voice not in engine_voices:
            raise AppError(f"The {body.voice} voice is not available with the current voice engine.",
                           code="voice_unavailable", status_code=422)
        check_plan_allows(plan, body, has_resume=resume is not None)
        await self.billing.assert_can_start_interview(user["_id"], plan)
        await self.expire_stale(user["_id"])

        now = utcnow()
        seeds = await pick_seed_questions(
            self.db, role_id=body.role_id, interview_type=body.interview_type,
            difficulty=body.difficulty, count=question_count_for(body.duration_minutes),
        )
        session = {
            "_id": new_id(),
            "user_id": user["_id"],
            "config": body.model_dump(mode="json"),
            "plan_at_creation": plan.id.value,
            "report_tier": plan.report_tier.value,
            "status": SessionStatus.CREATED.value,
            "is_demo": self.c.settings.demo_mode,
            "realtime_model": None if self.c.settings.demo_mode else self.c.settings.voice_model_for_plan(plan.id.value),
            "voice_engine": self.c.settings.openai_voice_engine,
            "seed_questions": seeds,
            "resume_ids": [d["_id"] for d in resume_docs],
            "created_at": now,
            "start_deadline": now + timedelta(minutes=self.c.settings.session_start_window_minutes),
            "started_at": None,
            "ended_at": None,
            "end_reason": None,
            "elapsed_seconds": 0,
            "billed_seconds": 0,
            "connection_count": 0,
            "message_seq": 0,
            "usage_counted": False,
            "realtime_usage": {"input_audio_tokens": 0, "output_audio_tokens": 0,
                               "input_text_tokens": 0, "output_text_tokens": 0, "cached_tokens": 0},
            "report_status": ReportStatus.NONE.value,
            "report_id": None,
            "overall_score": None,
        }
        await self.db.sessions.insert_one(session)
        log.info("Interview session created", extra={"ctx": {"session_id": session["_id"], "user_id": user["_id"]}})
        return session

    async def get_owned(self, user_id: str, session_id: str) -> dict:
        # Ownership is part of the query: other users' sessions are indistinguishable from missing ones.
        s = await self.db.sessions.find_one({"_id": session_id, "user_id": user_id})
        if not s:
            raise NotFound("Interview not found", code="interview_not_found")
        return s

    async def list_for_user(self, user_id: str, *, plan: Plan, limit: int, offset: int) -> dict:
        await self.expire_stale(user_id)
        query = {"user_id": user_id, "status": {"$nin": [SessionStatus.EXPIRED.value]}}
        total = await self.db.sessions.count_documents(query)
        if plan.history_limit is not None:
            total = min(total, plan.history_limit)
            limit = max(0, min(limit, plan.history_limit - offset))
        items = []
        if limit > 0:
            items = await self.db.sessions.find(query).sort("created_at", -1).skip(offset).limit(limit).to_list(None)
        return {"items": [public_session(s) for s in items], "total": total, "offset": offset,
                "history_limited": plan.history_limit is not None}

    async def expire_stale(self, user_id: str) -> None:
        await self.db.sessions.update_many(
            {"user_id": user_id, "status": SessionStatus.CREATED.value, "start_deadline": {"$lt": utcnow()}},
            {"$set": {"status": SessionStatus.EXPIRED.value, "end_reason": "not_started"}},
        )

    async def delete(self, user_id: str, session_id: str) -> None:
        session = await self.get_owned(user_id, session_id)
        if self.c.live_registry:
            await self.c.live_registry.terminate(session["_id"], reason="deleted")
        await self.db.messages.delete_many({"session_id": session_id, "user_id": user_id})
        await self.db.reports.delete_many({"session_id": session_id, "user_id": user_id})
        await self.db.sessions.delete_one({"_id": session_id, "user_id": user_id})
        await self.db.usage.update_many({"session_id": session_id}, {"$set": {"session_id": None}})

    # ---- Live lifecycle (used by the realtime relay) ------------------------------------

    async def mark_live(self, session_id: str) -> dict:
        now = utcnow()
        s = await self.db.sessions.find_one_and_update(
            {"_id": session_id},
            {"$set": {"status": SessionStatus.LIVE.value}, "$inc": {"connection_count": 1}},
            return_document=ReturnDocument.AFTER,
        )
        if s and s.get("started_at") is None:
            await self.db.sessions.update_one({"_id": session_id}, {"$set": {"started_at": now}})
            s["started_at"] = now
        return s

    async def count_usage_once(self, session: dict) -> None:
        """Counts the interview against the monthly quota the first time it goes live."""
        res = await self.db.sessions.update_one(
            {"_id": session["_id"], "usage_counted": False}, {"$set": {"usage_counted": True}}
        )
        if res.modified_count:
            from app.modules.billing.service import UsageKind
            await self.billing.record(session["user_id"], UsageKind.INTERVIEW_STARTED, 1, session_id=session["_id"])

    async def add_elapsed(self, session_id: str, seconds: float, usage: dict | None = None) -> None:
        inc: dict = {"elapsed_seconds": round(seconds, 2)}
        for k, v in (usage or {}).items():
            if v:
                inc[f"realtime_usage.{k}"] = int(v)
        await self.db.sessions.update_one({"_id": session_id}, {"$inc": inc})

    async def mark_interrupted(self, session_id: str) -> None:
        await self.db.sessions.update_one(
            {"_id": session_id, "status": SessionStatus.LIVE.value},
            {"$set": {"status": SessionStatus.INTERRUPTED.value}},
        )

    async def append_message(self, session: dict, *, role: str, text: str, item_id: str | None,
                             retain: bool) -> None:
        text = text.strip()
        if not text:
            return
        s = await self.db.sessions.find_one_and_update(
            {"_id": session["_id"]}, {"$inc": {"message_seq": 1}}, return_document=ReturnDocument.AFTER
        )
        if not s:
            return  # session deleted mid-interview
        now = utcnow()
        retention = timedelta(days=self.c.settings.transcript_retention_days) if retain else timedelta(days=7)
        await self.db.messages.insert_one({
            "_id": new_id(),
            "session_id": session["_id"],
            "user_id": session["user_id"],
            "seq": s["message_seq"],
            "role": role,  # "interviewer" | "candidate"
            "item_id": item_id,
            "text": text[:8000],
            "truncated": False,
            "created_at": now,
            "purge_after": now + retention,
        })

    async def mark_message_truncated(self, session_id: str, item_id: str) -> None:
        await self.db.messages.update_one({"session_id": session_id, "item_id": item_id},
                                          {"$set": {"truncated": True}})

    async def transcript(self, session_id: str) -> list[dict]:
        return await self.db.messages.find({"session_id": session_id}).sort("seq", 1).to_list(None)

    async def finish(self, session_id: str, *, reason: str) -> dict | None:
        """Ends a session (idempotent) and schedules report generation when there is enough content."""
        session = await self.db.sessions.find_one({"_id": session_id})
        if not session or session["status"] not in {s.value for s in ACTIVE_STATUSES}:
            return session

        messages = await self.transcript(session_id)
        candidate = [m for m in messages if m["role"] == "candidate"]
        words = sum(len(m["text"].split()) for m in candidate)
        enough_words = words >= MIN_CANDIDATE_WORDS_FOR_REPORT or session.get("is_demo")  # demo can't transcribe
        meaningful = len(candidate) >= MIN_CANDIDATE_TURNS_FOR_REPORT and enough_words

        if session.get("started_at") is None:
            status, report_status = SessionStatus.ABANDONED, ReportStatus.NONE
        elif meaningful:
            status, report_status = SessionStatus.COMPLETED, ReportStatus.PENDING
        else:
            status, report_status = SessionStatus.COMPLETED, ReportStatus.INSUFFICIENT_DATA

        now = utcnow()
        updated = await self.db.sessions.find_one_and_update(
            {"_id": session_id, "status": {"$in": [s.value for s in ACTIVE_STATUSES]}},
            {"$set": {"status": status.value, "ended_at": now, "end_reason": reason,
                      "report_status": report_status.value}},
            return_document=ReturnDocument.AFTER,
        )
        if updated is None:
            return await self.db.sessions.find_one({"_id": session_id})

        from app.modules.billing.service import UsageKind
        billed = int(round(updated.get("elapsed_seconds", 0)))
        if billed > 0:
            await self.billing.record(updated["user_id"], UsageKind.REALTIME_SECONDS, billed, session_id=session_id)
            await self.db.sessions.update_one({"_id": session_id}, {"$set": {"billed_seconds": billed}})

        if report_status == ReportStatus.PENDING and self.c.report_scheduler:
            self.c.report_scheduler.schedule(session_id)
        elif report_status != ReportStatus.PENDING:
            await self._purge_transcript_if_not_retained(updated)
        log.info("Interview finished", extra={"ctx": {"session_id": session_id, "reason": reason,
                                                       "status": status.value, "report": report_status.value}})
        return updated

    async def _purge_transcript_if_not_retained(self, session: dict) -> None:
        user = await self.db.users.find_one({"_id": session["user_id"]})
        if not user or not user.get("preferences", {}).get("retain_transcripts"):
            await self.db.messages.delete_many({"session_id": session["_id"]})
