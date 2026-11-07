"""Plan definitions (freemium).

Limits are sized against AI cost: a 10-minute realtime interview is the dominant cost
(audio tokens both directions) — see docs/COSTS_AND_SERVICES.md. Tune these numbers once
real usage/cost data is available.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum

from app.modules.catalog.data import FREE_VOICE_IDS, VOICES, Difficulty, InterviewType


class PlanId(StrEnum):
    FREE = "free"
    PREMIUM = "premium"


class ReportTier(StrEnum):
    BASIC = "basic"
    DETAILED = "detailed"


@dataclass(frozen=True)
class Plan:
    id: PlanId
    title: str
    interviews_per_month: int
    realtime_minutes_per_month: int
    max_duration_minutes: int
    allowed_interview_types: frozenset[str]
    allowed_difficulties: frozenset[str]
    custom_role: bool
    resume_questions: bool
    report_tier: ReportTier
    progress_tracking: bool
    history_limit: int | None
    voices: tuple[str, ...]
    avatars: tuple[str, ...]
    features: tuple[str, ...] = field(default_factory=tuple)

    def public(self) -> dict:
        return {
            "id": self.id,
            "title": self.title,
            "interviews_per_month": self.interviews_per_month,
            "realtime_minutes_per_month": self.realtime_minutes_per_month,
            "max_duration_minutes": self.max_duration_minutes,
            "allowed_interview_types": sorted(self.allowed_interview_types),
            "allowed_difficulties": sorted(self.allowed_difficulties),
            "custom_role": self.custom_role,
            "resume_questions": self.resume_questions,
            "report_tier": self.report_tier,
            "progress_tracking": self.progress_tracking,
            "history_limit": self.history_limit,
            "voices": list(self.voices),
            "avatars": list(self.avatars),
            "features": list(self.features),
        }


FREE_PLAN = Plan(
    id=PlanId.FREE,
    title="Free",
    interviews_per_month=3,
    realtime_minutes_per_month=30,
    max_duration_minutes=10,
    allowed_interview_types=frozenset({InterviewType.TECHNICAL, InterviewType.BEHAVIORAL}),
    allowed_difficulties=frozenset({Difficulty.BEGINNER, Difficulty.INTERMEDIATE}),
    custom_role=False,
    resume_questions=False,
    report_tier=ReportTier.BASIC,
    progress_tracking=False,
    history_limit=5,
    voices=tuple(sorted(FREE_VOICE_IDS)),
    avatars=("alex", "maya", "alex_placeholder"),
    features=(
        "3 practice interviews per month",
        "Up to 10 minutes per interview",
        "Technical & behavioral interviews",
        "Basic AI feedback",
        "Realistic 3D interviewers (Alex & Maya) with natural voices",
    ),
)

PREMIUM_PLAN = Plan(
    id=PlanId.PREMIUM,
    title="Premium",
    interviews_per_month=40,
    realtime_minutes_per_month=600,
    max_duration_minutes=30,
    allowed_interview_types=frozenset(t.value for t in InterviewType),
    allowed_difficulties=frozenset(d.value for d in Difficulty),
    custom_role=True,
    resume_questions=True,
    report_tier=ReportTier.DETAILED,
    progress_tracking=True,
    history_limit=None,
    voices=tuple(v["id"] for v in VOICES),
    avatars=("alex", "maya", "alex_placeholder"),
    features=(
        "40 interviews per month (fair use: 600 voice minutes)",
        "Up to 30 minutes per interview",
        "All interview types, advanced difficulty and custom roles",
        "Detailed reports with model answers and study plans",
        "Resume-based questions",
        "Full history and progress tracking",
        "All interviewer voices, including international accents and languages",
    ),
)

PLANS: dict[PlanId, Plan] = {FREE_PLAN.id: FREE_PLAN, PREMIUM_PLAN.id: PREMIUM_PLAN}


def get_plan(plan_id: str) -> Plan:
    return PLANS.get(PlanId(plan_id), FREE_PLAN) if plan_id in PlanId._value2member_map_ else FREE_PLAN
