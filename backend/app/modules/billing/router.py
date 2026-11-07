from __future__ import annotations

from typing import Literal

from fastapi import APIRouter
from pydantic import BaseModel, Field

from app.core.deps import ContainerDep, CurrentUser
from app.core.errors import AppError, NotFound
from app.modules.billing.plans import PLANS
from app.modules.billing.service import BillingService, public_subscription

router = APIRouter(prefix="/v1/billing", tags=["billing"])


class VerifyPurchaseRequest(BaseModel):
    platform: Literal["google_play", "app_store"]
    product_id: str = Field(min_length=1, max_length=200)
    purchase_token: str = Field(min_length=1, max_length=4096,
                                description="Google Play purchaseToken or App Store signed transaction (JWS)")


@router.get("/plans")
async def list_plans() -> dict:
    return {"plans": [p.public() for p in PLANS.values()]}


@router.get("/subscription")
async def get_subscription(user: CurrentUser, c: ContainerDep) -> dict:
    svc = BillingService(c.db)
    plan = await svc.get_plan(user["_id"])
    sub = await svc.ensure_subscription(user["_id"])
    usage = await svc.usage_summary(user["_id"], plan)
    return {"subscription": public_subscription(sub, plan), "plan": plan.public(), "usage": usage.public()}


@router.get("/usage")
async def get_usage(user: CurrentUser, c: ContainerDep) -> dict:
    return (await BillingService(c.db).usage_summary(user["_id"])).public()


@router.post("/verify-purchase", status_code=501)
async def verify_purchase(body: VerifyPurchaseRequest, user: CurrentUser) -> dict:
    """Store purchase verification is NOT implemented yet.

    Production requires server-side verification with the Google Play Developer API
    (purchases.subscriptionsv2.get) and the App Store Server API, plus Real-time Developer
    Notifications / App Store Server Notifications V2. See docs/MONETIZATION.md.
    """
    raise AppError(
        "In-app purchase verification is not configured on this server.",
        code="billing_not_configured",
        status_code=501,
        details={"platform": body.platform},
    )


@router.post("/dev/grant-premium")
async def dev_grant_premium(user: CurrentUser, c: ContainerDep) -> dict:
    """Development/demo-only: grants a 30-day unverified premium plan. Disabled in production."""
    if c.settings.is_production:
        raise NotFound("Not found")
    svc = BillingService(c.db)
    sub = await svc.dev_grant_premium(user["_id"])
    plan = await svc.get_plan(user["_id"])
    return {"subscription": public_subscription(sub, plan), "plan": plan.public()}


@router.post("/dev/reset-free")
async def dev_reset_free(user: CurrentUser, c: ContainerDep) -> dict:
    if c.settings.is_production:
        raise NotFound("Not found")
    svc = BillingService(c.db)
    sub = await svc.dev_reset_to_free(user["_id"])
    plan = await svc.get_plan(user["_id"])
    return {"subscription": public_subscription(sub, plan), "plan": plan.public()}
