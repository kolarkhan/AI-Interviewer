from __future__ import annotations

from fastapi import APIRouter, Request, status

from app.core import rate_limit as rl
from app.core.deps import ContainerDep, CurrentUser
from app.modules.auth.schemas import LoginRequest, RefreshRequest, RegisterRequest, TokenPair
from app.modules.auth.service import AuthService
from app.modules.users.service import public_user

router = APIRouter(prefix="/v1/auth", tags=["auth"])


@router.post("/register", status_code=status.HTTP_201_CREATED)
async def register(body: RegisterRequest, request: Request, c: ContainerDep) -> dict:
    rl.enforce(c.limiter, f"register:{rl.client_ip(request)}", rl.REGISTER_PER_IP)
    user, tokens = await AuthService(c.db, c.settings).register(
        email=body.email, password=body.password, display_name=body.display_name
    )
    return {"user": public_user(user), "tokens": tokens.model_dump(mode="json")}


@router.post("/login")
async def login(body: LoginRequest, request: Request, c: ContainerDep) -> dict:
    rl.enforce(c.limiter, f"login-ip:{rl.client_ip(request)}", rl.LOGIN_PER_IP)
    rl.enforce(c.limiter, f"login-email:{body.email.lower()}", rl.LOGIN_PER_EMAIL)
    user, tokens = await AuthService(c.db, c.settings).login(email=body.email, password=body.password)
    return {"user": public_user(user), "tokens": tokens.model_dump(mode="json")}


@router.post("/refresh", response_model=TokenPair)
async def refresh(body: RefreshRequest, request: Request, c: ContainerDep) -> TokenPair:
    rl.enforce(c.limiter, f"refresh:{rl.client_ip(request)}", rl.REFRESH_PER_IP)
    return await AuthService(c.db, c.settings).refresh(body.refresh_token)


@router.post("/logout", status_code=status.HTTP_204_NO_CONTENT)
async def logout(body: RefreshRequest, c: ContainerDep) -> None:
    await AuthService(c.db, c.settings).logout(body.refresh_token)


@router.post("/logout-all", status_code=status.HTTP_204_NO_CONTENT)
async def logout_all(user: CurrentUser, c: ContainerDep) -> None:
    await AuthService(c.db, c.settings).logout_everywhere(user["_id"])
