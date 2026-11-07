from __future__ import annotations

from fastapi import APIRouter, Query, status

from app.core import rate_limit as rl
from app.core.deps import ContainerDep, CurrentUser
from app.core.errors import AppError, Conflict, NotFound
from app.modules.billing.service import BillingService
from app.modules.interviews.schemas import ACTIVE_STATUSES, ConfirmCompletion, InterviewCreate, ReportStatus
from app.modules.interviews.service import InterviewService, public_session
from app.modules.reports.service import ReportService, public_report

router = APIRouter(prefix="/v1", tags=["interviews"])


@router.post("/interviews", status_code=status.HTTP_201_CREATED)
async def create_interview(body: InterviewCreate, user: CurrentUser, c: ContainerDep) -> dict:
    rl.enforce(c.limiter, f"interview-create:{user['_id']}", rl.INTERVIEW_CREATE_PER_USER)
    session = await InterviewService(c).create(user, body)
    return {
        **public_session(session),
        "live": {
            "websocket_path": f"/v1/interviews/{session['_id']}/live",
            "protocol_version": 1,
            "audio": {"format": "pcm16", "sample_rate": 24000, "channels": 1},
        },
    }


@router.get("/interviews")
async def list_interviews(user: CurrentUser, c: ContainerDep,
                          limit: int = Query(20, ge=1, le=50), offset: int = Query(0, ge=0, le=10_000)) -> dict:
    plan = await BillingService(c.db).get_plan(user["_id"])
    return await InterviewService(c).list_for_user(user["_id"], plan=plan, limit=limit, offset=offset)


@router.get("/interviews/{session_id}")
async def get_interview(session_id: str, user: CurrentUser, c: ContainerDep) -> dict:
    return public_session(await InterviewService(c).get_owned(user["_id"], session_id))


@router.delete("/interviews/{session_id}", status_code=status.HTTP_204_NO_CONTENT)
async def delete_interview(session_id: str, user: CurrentUser, c: ContainerDep) -> None:
    await InterviewService(c).delete(user["_id"], session_id)


@router.post("/interviews/{session_id}/complete")
async def complete_interview(session_id: str, body: ConfirmCompletion, user: CurrentUser, c: ContainerDep) -> dict:
    """Ends an interview from outside the live socket (e.g. app killed, user cancelled in the lobby)."""
    svc = InterviewService(c)
    session = await svc.get_owned(user["_id"], session_id)
    if c.live_registry and c.live_registry.get(session_id):
        await c.live_registry.terminate(session_id, reason=body.reason)
    elif session["status"] in {s.value for s in ACTIVE_STATUSES}:
        await svc.finish(session_id, reason=body.reason)
    return public_session(await svc.get_owned(user["_id"], session_id))


@router.get("/interviews/{session_id}/transcript")
async def get_transcript(session_id: str, user: CurrentUser, c: ContainerDep) -> dict:
    svc = InterviewService(c)
    await svc.get_owned(user["_id"], session_id)
    messages = await svc.transcript(session_id)
    return {
        "session_id": session_id,
        "retained": bool(user.get("preferences", {}).get("retain_transcripts")),
        "messages": [{"seq": m["seq"], "role": m["role"], "text": m["text"], "truncated": m.get("truncated", False)}
                     for m in messages],
    }


@router.get("/interviews/{session_id}/report")
async def get_report(session_id: str, user: CurrentUser, c: ContainerDep) -> dict:
    session = await InterviewService(c).get_owned(user["_id"], session_id)
    report_status = session.get("report_status", ReportStatus.NONE)
    if report_status != ReportStatus.READY:
        return {"status": report_status, "report": None, "error": session.get("report_error") and "generation_failed"}
    report = await c.db.reports.find_one({"session_id": session_id, "user_id": user["_id"]})
    if not report:
        raise NotFound("Report not found", code="report_not_found")
    return {"status": report_status, "report": public_report(report)}


@router.post("/interviews/{session_id}/report", status_code=status.HTTP_202_ACCEPTED)
async def regenerate_report(session_id: str, user: CurrentUser, c: ContainerDep) -> dict:
    """Retries report generation after a failure (transcript is kept for up to 7 days)."""
    rl.enforce(c.limiter, f"report-regen:{user['_id']}", rl.REPORT_REGENERATE_PER_USER)
    session = await InterviewService(c).get_owned(user["_id"], session_id)
    current = session.get("report_status")
    if current in (ReportStatus.PENDING, ReportStatus.GENERATING):
        return {"status": current}
    if current != ReportStatus.FAILED:
        raise Conflict("The report can only be regenerated after a failure.", code="report_not_failed")
    if not await c.db.messages.count_documents({"session_id": session_id}):
        raise AppError("The transcript for this interview is no longer available.", code="transcript_gone",
                       status_code=410)
    await c.db.sessions.update_one({"_id": session_id}, {"$set": {"report_status": ReportStatus.PENDING.value}})
    assert c.report_scheduler is not None
    c.report_scheduler.schedule(session_id)
    return {"status": ReportStatus.PENDING.value}


@router.get("/progress")
async def get_progress(user: CurrentUser, c: ContainerDep) -> dict:
    plan = await BillingService(c.db).get_plan(user["_id"])
    return await ReportService(c).progress(user["_id"], full=plan.progress_tracking)
