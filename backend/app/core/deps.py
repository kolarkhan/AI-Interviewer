"""FastAPI dependencies: container access and authentication."""

from __future__ import annotations

from typing import Annotated

import jwt
from fastapi import Depends, Request
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from starlette.requests import HTTPConnection

from app.core.container import Container
from app.core.errors import Unauthorized
from app.core.security import decode_access_token

_bearer = HTTPBearer(auto_error=False, description="JWT access token from /v1/auth/login")


def get_container(conn: HTTPConnection) -> Container:
    return conn.app.state.container


ContainerDep = Annotated[Container, Depends(get_container)]


async def authenticate_token(container: Container, token: str | None) -> dict:
    """Validates an access token and returns the active user document."""
    if not token:
        raise Unauthorized("Missing access token", code="token_missing")
    try:
        payload = decode_access_token(container.settings, token)
    except jwt.ExpiredSignatureError as exc:
        raise Unauthorized("Access token expired", code="token_expired") from exc
    except jwt.PyJWTError as exc:
        raise Unauthorized("Invalid access token", code="token_invalid") from exc

    user = await container.db.users.find_one({"_id": payload["sub"]})
    if not user or user.get("status") != "active":
        raise Unauthorized("Account not found or disabled", code="account_inactive")
    if int(payload.get("ver", -1)) != int(user.get("token_version", 0)):
        raise Unauthorized("Session has been revoked", code="token_revoked")
    return user


async def get_current_user(
    request: Request,
    creds: Annotated[HTTPAuthorizationCredentials | None, Depends(_bearer)],
) -> dict:
    return await authenticate_token(get_container(request), creds.credentials if creds else None)


CurrentUser = Annotated[dict, Depends(get_current_user)]


def bearer_from_headers(conn: HTTPConnection) -> str | None:
    header = conn.headers.get("authorization", "")
    scheme, _, value = header.partition(" ")
    if scheme.lower() == "bearer" and value:
        return value.strip()
    return None
