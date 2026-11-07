from __future__ import annotations

from fastapi import APIRouter, Response

from app.core import rate_limit as rl
from app.core.deps import ContainerDep, CurrentUser
from app.core.security import safety_identifier
from app.modules.billing.service import BillingService
from app.modules.catalog import data
from app.modules.catalog.voice_preview import cache_version, get_preview_wav

router = APIRouter(prefix="/v1/catalog", tags=["catalog"])


@router.get("")
async def get_catalog(user: CurrentUser, c: ContainerDep) -> dict:
    """Interview setup options, annotated with `locked` according to the user's plan."""
    plan = await BillingService(c.db).get_plan(user["_id"])
    return {
        "plan": plan.id,
        "roles": [
            {**r, "locked": r["id"] == data.CUSTOM_ROLE_ID and not plan.custom_role} for r in data.ROLES
        ],
        "interview_types": [
            {**t, "id": str(t["id"]), "locked": str(t["id"]) not in plan.allowed_interview_types}
            for t in data.INTERVIEW_TYPES
        ],
        "difficulties": [
            {**d, "id": str(d["id"]), "locked": str(d["id"]) not in plan.allowed_difficulties}
            for d in data.DIFFICULTIES
        ],
        "experience_levels": [{**e, "id": str(e["id"])} for e in data.EXPERIENCE_LEVELS],
        "languages": [dict(lang) for lang in data.LANGUAGES],
        "durations": [
            {"minutes": m, "locked": m > plan.max_duration_minutes} for m in data.DURATIONS_MINUTES
        ],
        "voices": [{**v, "locked": v["id"] not in plan.voices}
                   for v in data.voices_for_engine(c.settings.openai_voice_engine)],
        "voice_engine": c.settings.openai_voice_engine,
        "voice_preview_version": cache_version(c.settings.openai_voice_engine),
        "avatars": [{**a, "locked": a["id"] not in plan.avatars} for a in data.AVATARS],
        "resume_questions": {"locked": not plan.resume_questions},
        "demo_mode": c.settings.demo_mode,
    }


@router.get("/voices/{voice_id}/preview", response_class=Response,
            responses={200: {"content": {"audio/wav": {}}, "description": "Mono PCM16 WAV, 24 kHz"}})
async def voice_preview(voice_id: str, user: CurrentUser, c: ContainerDep) -> Response:
    """A short spoken sample of an interviewer voice (generated once per voice, then served from storage)."""
    rl.enforce(c.limiter, f"voice-preview:{user['_id']}", rl.VOICE_PREVIEW_PER_USER)
    wav = await get_preview_wav(c, voice_id, safety_identifier(user["_id"]))
    return Response(content=wav, media_type="audio/wav", headers={"Cache-Control": "private, max-age=86400"})
