from __future__ import annotations

from fastapi import APIRouter, status

from app.core.deps import ContainerDep, CurrentUser
from app.core.errors import Unauthorized
from app.core.security import verify_password
from app.core.utils import utcnow
from app.modules.users.schemas import DeleteAccountRequest, ProfileUpdate
from app.modules.users.service import delete_account, delete_interview_data, export_user_data, public_user

router = APIRouter(prefix="/v1/me", tags=["profile"])


@router.get("")
async def get_me(user: CurrentUser) -> dict:
    return public_user(user)


@router.patch("")
async def update_me(body: ProfileUpdate, user: CurrentUser, c: ContainerDep) -> dict:
    now = utcnow()
    update: dict = {"updated_at": now}
    if body.display_name is not None:
        update["display_name"] = " ".join(body.display_name.split())
    if body.onboarding_completed is not None:
        update["onboarding_completed"] = body.onboarding_completed
    if body.preferences is not None:
        for key, value in body.preferences.model_dump(exclude_unset=True).items():
            update[f"preferences.{key}"] = value
        if body.preferences.retain_transcripts is True:
            update["consents.transcript_retention_at"] = now
        elif body.preferences.retain_transcripts is False:
            update["consents.transcript_retention_at"] = None
    await c.db.users.update_one({"_id": user["_id"]}, {"$set": update})
    return public_user(await c.db.users.find_one({"_id": user["_id"]}))


@router.get("/export")
async def export_me(user: CurrentUser, c: ContainerDep) -> dict:
    return await export_user_data(c, user)


@router.delete("/data")
async def delete_my_data(user: CurrentUser, c: ContainerDep) -> dict:
    return await delete_interview_data(c, user["_id"])


@router.post("/delete-account", status_code=status.HTTP_204_NO_CONTENT)
async def delete_my_account(body: DeleteAccountRequest, user: CurrentUser, c: ContainerDep) -> None:
    if not verify_password(user["password_hash"], body.password):
        raise Unauthorized("Password is incorrect", code="invalid_credentials")
    await delete_account(c, user["_id"])
