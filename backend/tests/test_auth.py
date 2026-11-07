from __future__ import annotations

from datetime import UTC, datetime, timedelta

import jwt

from tests.conftest import TEST_PASSWORD, auth_headers, register


def test_register_login_and_me(client):
    body = register(client)
    assert body["user"]["email"] == "candidate@example.com"
    assert "password_hash" not in body["user"]

    r = client.get("/v1/me", headers=auth_headers(body))
    assert r.status_code == 200
    assert r.json()["display_name"] == "Jamie Candidate"

    r = client.post("/v1/auth/login", json={"email": "CANDIDATE@example.com", "password": TEST_PASSWORD})
    assert r.status_code == 200


def test_register_validation(client):
    r = client.post("/v1/auth/register", json={"email": "a@example.com", "password": "short",
                                               "display_name": "A", "accept_terms": True})
    assert r.status_code == 422
    assert r.json()["error"]["code"] == "validation_error"

    r = client.post("/v1/auth/register", json={"email": "a@example.com", "password": "onlyletterspassword",
                                               "display_name": "A", "accept_terms": True})
    assert r.status_code == 422

    r = client.post("/v1/auth/register", json={"email": "a@example.com", "password": TEST_PASSWORD,
                                               "display_name": "A", "accept_terms": False})
    assert r.status_code == 422


def test_duplicate_email_rejected(client):
    register(client)
    r = client.post("/v1/auth/register", json={"email": "candidate@example.com", "password": TEST_PASSWORD,
                                               "display_name": "Other", "accept_terms": True})
    assert r.status_code == 409
    assert r.json()["error"]["code"] == "email_taken"


def test_wrong_password_and_unknown_email_look_identical(client):
    register(client)
    a = client.post("/v1/auth/login", json={"email": "candidate@example.com", "password": "wrong-password-1"})
    b = client.post("/v1/auth/login", json={"email": "nobody@example.com", "password": "wrong-password-1"})
    assert a.status_code == b.status_code == 401
    assert a.json()["error"] == b.json()["error"]


def test_protected_route_requires_token(client):
    r = client.get("/v1/me")
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "token_missing"
    r = client.get("/v1/me", headers={"Authorization": "Bearer not-a-jwt"})
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "token_invalid"


def test_expired_access_token(client, app):
    body = register(client)
    settings = app.state.container.settings
    now = datetime.now(UTC)
    token = jwt.encode({"sub": body["user"]["id"], "typ": "access", "ver": 0, "iss": settings.jwt_issuer,
                        "iat": int((now - timedelta(hours=1)).timestamp()),
                        "exp": int((now - timedelta(minutes=1)).timestamp())},
                       settings.jwt_secret.get_secret_value(), algorithm="HS256")
    r = client.get("/v1/me", headers={"Authorization": f"Bearer {token}"})
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "token_expired"


def test_token_signed_with_other_secret_rejected(client):
    body = register(client)
    now = datetime.now(UTC)
    forged = jwt.encode({"sub": body["user"]["id"], "typ": "access", "ver": 0, "iss": "ai-interviewer",
                         "iat": int(now.timestamp()), "exp": int((now + timedelta(minutes=5)).timestamp())},
                        "attacker-secret-attacker-secret-attacker", algorithm="HS256")
    r = client.get("/v1/me", headers={"Authorization": f"Bearer {forged}"})
    assert r.status_code == 401


def test_refresh_rotation_and_reuse_detection(client):
    body = register(client)
    first_refresh = body["tokens"]["refresh_token"]

    r = client.post("/v1/auth/refresh", json={"refresh_token": first_refresh})
    assert r.status_code == 200
    second = r.json()
    assert second["refresh_token"] != first_refresh

    # Reusing the rotated token is treated as theft: the whole family is revoked.
    r = client.post("/v1/auth/refresh", json={"refresh_token": first_refresh})
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "refresh_reused"
    r = client.post("/v1/auth/refresh", json={"refresh_token": second["refresh_token"]})
    assert r.status_code == 401


def test_logout_revokes_refresh_token(client):
    body = register(client)
    refresh = body["tokens"]["refresh_token"]
    assert client.post("/v1/auth/logout", json={"refresh_token": refresh}).status_code == 204
    r = client.post("/v1/auth/refresh", json={"refresh_token": refresh})
    assert r.status_code == 401


def test_logout_all_invalidates_access_tokens(client):
    body = register(client)
    headers = auth_headers(body)
    assert client.post("/v1/auth/logout-all", headers=headers).status_code == 204
    r = client.get("/v1/me", headers=headers)
    assert r.status_code == 401
    assert r.json()["error"]["code"] == "token_revoked"


def test_login_rate_limited(client):
    register(client)
    codes = [client.post("/v1/auth/login", json={"email": "candidate@example.com", "password": "bad-password-1"})
             .status_code for _ in range(7)]
    assert 429 in codes
    r = client.post("/v1/auth/login", json={"email": "candidate@example.com", "password": "bad-password-1"})
    assert r.status_code == 429
    assert "Retry-After" in r.headers


def test_profile_update_and_preferences(client):
    body = register(client)
    h = auth_headers(body)
    r = client.patch("/v1/me", headers=h, json={"display_name": "  Jamie   C ", "onboarding_completed": True,
                                                "preferences": {"voice": "cedar", "retain_transcripts": True}})
    assert r.status_code == 200, r.text
    me = r.json()
    assert me["display_name"] == "Jamie C"
    assert me["onboarding_completed"] is True
    assert me["preferences"]["voice"] == "cedar"
    assert me["preferences"]["retain_transcripts"] is True

    r = client.patch("/v1/me", headers=h, json={"preferences": {"voice": "not-a-voice"}})
    assert r.status_code == 422
    r = client.patch("/v1/me", headers=h, json={"is_admin": True})
    assert r.status_code == 422


def test_delete_account_requires_password(client):
    body = register(client)
    h = auth_headers(body)
    r = client.post("/v1/me/delete-account", headers=h, json={"password": "wrong-password-1", "confirm": True})
    assert r.status_code == 401
    r = client.post("/v1/me/delete-account", headers=h, json={"password": TEST_PASSWORD, "confirm": True})
    assert r.status_code == 204
    r = client.post("/v1/auth/login", json={"email": "candidate@example.com", "password": TEST_PASSWORD})
    assert r.status_code == 401
