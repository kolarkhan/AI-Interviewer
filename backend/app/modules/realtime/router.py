"""Live interview WebSocket and (optional) direct-connection client secrets."""

from __future__ import annotations

import contextlib

from fastapi import APIRouter, WebSocket
from starlette.websockets import WebSocketState

from app.core import rate_limit as rl
from app.core.container import Container
from app.core.deps import ContainerDep, CurrentUser, authenticate_token, bearer_from_headers
from app.core.errors import AppError, NotFound, PlanLimitExceeded, Unauthorized
from app.core.logging import get_logger
from app.core.openai_client import get_openai
from app.core.security import safety_identifier
from app.core.utils import as_utc, utcnow
from app.modules.billing.service import BillingService
from app.modules.catalog.data import interviewer_name_for
from app.modules.interviews.prompts import build_interviewer_instructions
from app.modules.interviews.schemas import SessionStatus
from app.modules.interviews.service import InterviewService
from app.modules.realtime.live_session import LiveSession
from app.modules.realtime.protocol import CloseCode, event
from app.modules.realtime.upstream import UpstreamError, UpstreamSessionConfig
from app.modules.resumes.service import combine_documents

log = get_logger(__name__)
router = APIRouter(tags=["live interview"])

_ENDED = {SessionStatus.COMPLETED, SessionStatus.ABANDONED, SessionStatus.EXPIRED, SessionStatus.FAILED}


async def _reject(ws: WebSocket, code: CloseCode, err_code: str, message: str) -> None:
    if ws.application_state == WebSocketState.CONNECTED:
        with contextlib.suppress(Exception):  # client may already be gone
            await ws.send_json(event("error", code=err_code, message=message, fatal=True))
            await ws.close(code=int(code), reason=message[:120])


async def build_upstream_config(c: Container, session: dict, user: dict, *, audio_route: str,
                                include_history: bool) -> UpstreamSessionConfig:
    prior = None
    if include_history:
        prior = [{"role": m["role"], "text": m["text"]}
                 for m in await InterviewService(c).transcript(session["_id"])]
    resume_text = None
    resume_ids = list(session.get("resume_ids") or ([session["resume_id"]] if session.get("resume_id") else []))
    if resume_ids:
        docs = await c.db.resumes.find({"_id": {"$in": resume_ids}, "user_id": user["_id"]}) \
            .sort("created_at", 1).to_list(None)
        resume_text = combine_documents(docs, c.settings.resume_max_chars) or None
    first_name = (user.get("display_name") or "there").split()[0][:30]
    cfg = session["config"]
    instructions = build_interviewer_instructions(
        config=cfg, candidate_first_name=first_name, seed_questions=session.get("seed_questions", []),
        resume_text=resume_text, prior_transcript=prior,
    )
    return UpstreamSessionConfig(
        model=session.get("realtime_model") or c.settings.openai_realtime_model,
        instructions=instructions,
        voice=cfg.get("voice", "marin"),
        transcription_model=c.settings.openai_transcription_model,
        noise_reduction="near_field" if audio_route == "headset" else "far_field",
        demo_questions=list(session.get("seed_questions", [])),
        candidate_first_name=first_name,
        interviewer_name=interviewer_name_for(cfg.get("avatar_id")),
        history=prior or [],
        language=cfg.get("language") or "en",
    )


@router.websocket("/v1/interviews/{session_id}/live")
async def live_interview(ws: WebSocket, session_id: str, audio_route: str = "speaker") -> None:
    c: Container = ws.app.state.container
    registry = c.live_registry
    assert registry is not None
    await ws.accept()

    try:
        user = await authenticate_token(c, bearer_from_headers(ws))
    except Unauthorized as exc:
        await _reject(ws, CloseCode.UNAUTHORIZED, exc.code, exc.message)
        return

    if c.limiter.hit(f"live:{user['_id']}", rl.LIVE_CONNECT_PER_USER) is not None:
        await _reject(ws, CloseCode.RATE_LIMITED, "rate_limited", "Too many connection attempts. Please wait.")
        return

    async with registry.lock_for(session_id):
        session = await c.db.sessions.find_one({"_id": session_id, "user_id": user["_id"]})
        if not session:
            await _reject(ws, CloseCode.NOT_FOUND, "interview_not_found", "Interview not found")
            return
        status = SessionStatus(session["status"])
        if status in _ENDED:
            await _reject(ws, CloseCode.SESSION_ENDED, "interview_ended", "This interview has already ended")
            return
        if status == SessionStatus.CREATED and as_utc(session["start_deadline"]) < utcnow():
            await InterviewService(c).expire_stale(user["_id"])
            await _reject(ws, CloseCode.SESSION_ENDED, "interview_expired",
                          "This interview was not started in time. Please set up a new one.")
            return

        live = registry.get(session_id)
        if live is None:
            for other in registry.for_user(user["_id"]):
                if other.attached:
                    await _reject(ws, CloseCode.CONFLICT, "interview_in_progress",
                                  "Another interview is already in progress.")
                    return
                await other.end("superseded")

            billing = BillingService(c.db)
            plan = await billing.get_plan(user["_id"])
            usage = await billing.usage_summary(user["_id"], plan)
            first_start = not session.get("usage_counted")
            if first_start and usage.interviews_remaining <= 0:
                await _reject(ws, CloseCode.PLAN_LIMIT, "plan_limit", "Monthly interview limit reached.")
                return
            prior_elapsed = float(session.get("elapsed_seconds", 0))
            allowed_total = min(int(session["config"]["duration_minutes"]) * 60,
                                prior_elapsed + usage.realtime_seconds_remaining)
            if allowed_total - prior_elapsed < 10:
                reason = "No interview time remaining." if prior_elapsed else "No voice minutes remaining this month."
                await _reject(ws, CloseCode.PLAN_LIMIT, "no_time_remaining", reason)
                return

            resumed = session.get("started_at") is not None
            config = await build_upstream_config(c, session, user, audio_route=audio_route,
                                                 include_history=resumed)
            assert c.upstream_factory is not None
            upstream = c.upstream_factory(config, safety_identifier(user["_id"]))
            live = LiveSession(c=c, session=session, user=user, upstream=upstream, registry=registry,
                               allowed_total_seconds=allowed_total, resumed_with_context=resumed)
            try:
                await live.start()
            except UpstreamError as exc:
                log.warning("Live start failed", extra={"ctx": {"session_id": session_id, "status": exc.status}})
                await _reject(ws, CloseCode.UPSTREAM_UNAVAILABLE, "upstream_unavailable",
                              "The AI interviewer is temporarily unavailable. Please try again.")
                return
            interviews = InterviewService(c)
            session = await interviews.mark_live(session_id)
            live.session = session
            await interviews.count_usage_once(session)
            registry.add(live)

    await live.attach(ws)


@router.post("/v1/interviews/{session_id}/realtime-client-secret")
async def create_realtime_client_secret(session_id: str, user: CurrentUser, c: ContainerDep) -> dict:
    """Mints a short-lived OpenAI client secret for a *direct* client connection (WebRTC).

    Disabled by default (`ENABLE_DIRECT_REALTIME=false`). The relay WebSocket is the supported
    transport because it keeps the interviewer instructions and limits server-side. If enabled,
    note that a direct client can change its own session configuration.
    """
    if not c.settings.enable_direct_realtime:
        raise NotFound("Direct realtime connections are disabled", code="direct_realtime_disabled")
    svc = InterviewService(c)
    session = await svc.get_owned(user["_id"], session_id)
    if session["status"] in {s.value for s in _ENDED}:
        raise AppError("This interview has already ended", code="interview_ended", status_code=410)
    billing = BillingService(c.db)
    plan = await billing.get_plan(user["_id"])
    if not session.get("usage_counted"):
        await billing.assert_can_start_interview(user["_id"], plan)
    usage = await billing.usage_summary(user["_id"], plan)
    if usage.realtime_seconds_remaining < 60:
        raise PlanLimitExceeded("No voice minutes remaining this month.")

    config = await build_upstream_config(c, session, user, audio_route="speaker",
                                         include_history=session.get("started_at") is not None)
    session_cfg = config.to_session_update()["session"]
    session_cfg["model"] = config.model
    client = get_openai(c.settings)
    secret = await client.realtime.client_secrets.create(
        expires_after={"anchor": "created_at", "seconds": c.settings.direct_realtime_secret_ttl_seconds},
        session=session_cfg,
    )
    await svc.count_usage_once(session)
    return {"client_secret": secret.value, "expires_at": secret.expires_at,
            "model": config.model}
