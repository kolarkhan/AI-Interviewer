"""Subscriptions and usage limits.

Payments are NOT live: Google Play Billing / App Store purchase verification must be
integrated (see docs/MONETIZATION.md). Until then a subscription can only become premium
through the development-only grant endpoint, which is disabled in production.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from enum import StrEnum

from app.core.errors import PlanLimitExceeded
from app.core.utils import as_utc, month_key, new_id, utcnow
from app.db.mongo import Database
from app.modules.billing.plans import FREE_PLAN, PLANS, Plan, PlanId


class UsageKind(StrEnum):
    INTERVIEW_STARTED = "interview_started"
    REALTIME_SECONDS = "realtime_seconds"
    REPORT_GENERATED = "report_generated"


@dataclass
class UsageSummary:
    period: str
    interviews_used: int
    interviews_limit: int
    realtime_seconds_used: int
    realtime_minutes_limit: int

    @property
    def interviews_remaining(self) -> int:
        return max(0, self.interviews_limit - self.interviews_used)

    @property
    def realtime_seconds_remaining(self) -> int:
        return max(0, self.realtime_minutes_limit * 60 - self.realtime_seconds_used)

    def public(self) -> dict:
        return {
            "period": self.period,
            "interviews_used": self.interviews_used,
            "interviews_limit": self.interviews_limit,
            "interviews_remaining": self.interviews_remaining,
            "realtime_minutes_used": round(self.realtime_seconds_used / 60, 1),
            "realtime_minutes_limit": self.realtime_minutes_limit,
            "realtime_minutes_remaining": round(self.realtime_seconds_remaining / 60, 1),
        }


class BillingService:
    def __init__(self, db: Database) -> None:
        self.db = db

    async def ensure_subscription(self, user_id: str) -> dict:
        sub = await self.db.subscriptions.find_one({"user_id": user_id})
        if sub:
            return sub
        now = utcnow()
        sub = {
            "_id": new_id(),
            "user_id": user_id,
            "plan": PlanId.FREE.value,
            "status": "active",
            "source": "none",
            "product_id": None,
            "store_transaction_id": None,
            "verified": False,
            "current_period_end": None,
            "created_at": now,
            "updated_at": now,
        }
        await self.db.subscriptions.insert_one(sub)
        return sub

    async def get_plan(self, user_id: str) -> Plan:
        sub = await self.ensure_subscription(user_id)
        if sub.get("plan") != PlanId.PREMIUM.value or sub.get("status") != "active":
            return FREE_PLAN
        end = as_utc(sub.get("current_period_end"))
        if end is not None and end < utcnow():
            return FREE_PLAN
        return PLANS[PlanId.PREMIUM]

    async def usage_summary(self, user_id: str, plan: Plan | None = None) -> UsageSummary:
        plan = plan or await self.get_plan(user_id)
        period = month_key()
        records = await self.db.usage.find({"user_id": user_id, "period": period}).to_list(None)
        interviews = sum(r["quantity"] for r in records if r["kind"] == UsageKind.INTERVIEW_STARTED)
        seconds = sum(r["quantity"] for r in records if r["kind"] == UsageKind.REALTIME_SECONDS)
        return UsageSummary(period, interviews, plan.interviews_per_month, seconds, plan.realtime_minutes_per_month)

    async def record(self, user_id: str, kind: UsageKind, quantity: int, *, session_id: str | None = None) -> None:
        if quantity <= 0:
            return
        await self.db.usage.insert_one({
            "_id": new_id(),
            "user_id": user_id,
            "kind": kind.value,
            "quantity": int(quantity),
            "session_id": session_id,
            "period": month_key(),
            "created_at": utcnow(),
        })

    async def assert_can_start_interview(self, user_id: str, plan: Plan) -> UsageSummary:
        usage = await self.usage_summary(user_id, plan)
        if usage.interviews_remaining <= 0:
            raise PlanLimitExceeded(
                f"You have used all {plan.interviews_per_month} interviews for this month.",
                details={"usage": usage.public(), "upgrade_available": plan.id == PlanId.FREE},
            )
        if usage.realtime_seconds_remaining < 60:
            raise PlanLimitExceeded(
                "You have used all voice minutes included in your plan this month.",
                details={"usage": usage.public(), "upgrade_available": plan.id == PlanId.FREE},
            )
        return usage

    async def dev_grant_premium(self, user_id: str, days: int = 30) -> dict:
        """Development/demo only — callers must check the environment."""
        await self.ensure_subscription(user_id)
        now = utcnow()
        await self.db.subscriptions.update_one(
            {"user_id": user_id},
            {"$set": {"plan": PlanId.PREMIUM.value, "status": "active", "source": "manual_dev",
                      "verified": False, "current_period_end": now + timedelta(days=days), "updated_at": now}},
        )
        return await self.db.subscriptions.find_one({"user_id": user_id})

    async def dev_reset_to_free(self, user_id: str) -> dict:
        await self.ensure_subscription(user_id)
        await self.db.subscriptions.update_one(
            {"user_id": user_id},
            {"$set": {"plan": PlanId.FREE.value, "status": "active", "source": "none",
                      "current_period_end": None, "updated_at": utcnow()}},
        )
        return await self.db.subscriptions.find_one({"user_id": user_id})


def public_subscription(sub: dict, plan: Plan) -> dict:
    end = as_utc(sub.get("current_period_end"))
    return {
        "plan": plan.id,
        "status": sub.get("status"),
        "source": sub.get("source"),
        "verified": bool(sub.get("verified")),
        "current_period_end": end.isoformat() if end else None,
        "is_test_grant": sub.get("source") == "manual_dev",
    }
