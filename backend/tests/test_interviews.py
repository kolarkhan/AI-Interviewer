from __future__ import annotations

import asyncio

from app.modules.billing.service import BillingService, UsageKind
from tests.conftest import INTERVIEW_BODY, auth_headers, register


def _run(client, coro):
    """Runs a coroutine on the TestClient's event loop (same loop as the app)."""
    return client.portal.call(lambda: coro)


def test_catalog_marks_premium_features_locked(client):
    h = auth_headers(register(client))
    cat = client.get("/v1/catalog", headers=h).json()
    assert cat["plan"] == "free"
    assert cat["demo_mode"] is True
    types = {t["id"]: t["locked"] for t in cat["interview_types"]}
    assert types == {"technical": False, "hr": True, "behavioral": False, "mixed": True}
    durations = {d["minutes"]: d["locked"] for d in cat["durations"]}
    assert durations == {5: False, 10: False, 20: True, 30: True}
    assert next(r for r in cat["roles"] if r["id"] == "custom")["locked"] is True
    assert cat["resume_questions"]["locked"] is True


def test_create_interview_returns_live_info_and_seed_questions(client, app):
    h = auth_headers(register(client))
    r = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY)
    assert r.status_code == 201, r.text
    body = r.json()
    assert body["status"] == "created"
    assert body["is_demo"] is True
    assert body["config"]["role_title"] == "Software Engineer"
    assert body["live"]["websocket_path"] == f"/v1/interviews/{body['id']}/live"
    db = app.state.container.db
    doc = _run(client, db.sessions.find_one({"_id": body["id"]}))
    assert len(doc["seed_questions"]) == 3


def test_plan_gating(client):
    h = auth_headers(register(client))
    for patch, locked in [({"duration_minutes": 30}, "30-minute interviews"),
                          ({"interview_type": "hr"}, "hr interviews"),
                          ({"difficulty": "advanced"}, "advanced difficulty"),
                          ({"role_id": "custom", "custom_role_title": "Product Manager"}, "custom roles"),
                          ({"voice": "willow", "avatar_id": "maya"}, "the willow voice"),
                          ({"use_resume": True}, "resume-based questions")]:
        r = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, **patch})
        assert r.status_code == 402, (patch, r.text)
        assert r.json()["error"]["code"] == "plan_feature_locked"
        assert locked in r.json()["error"]["details"]["locked"]


def test_premium_unlocks_features(client):
    h = auth_headers(register(client))
    r = client.post("/v1/billing/dev/grant-premium", headers=h)
    assert r.status_code == 200
    assert r.json()["subscription"]["is_test_grant"] is True
    r = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "duration_minutes": 30,
                                                       "interview_type": "mixed", "difficulty": "advanced",
                                                       "role_id": "custom", "custom_role_title": "Product Manager",
                                                       "voice": "cedar"})
    assert r.status_code == 201, r.text
    assert r.json()["config"]["role_title"] == "Product Manager"


def test_dev_grant_disabled_in_production():
    from fastapi.testclient import TestClient

    from tests.conftest import build_app, make_settings
    settings = make_settings(environment="production", database_url="mongodb://unused:27017")
    app = build_app(settings)
    with TestClient(app) as c:
        h = auth_headers(register(c))
        assert c.post("/v1/billing/dev/grant-premium", headers=h).status_code == 404


def test_voice_follows_interviewer(client):
    h = auth_headers(register(client))
    maya = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "avatar_id": "maya"}).json()
    assert maya["config"]["voice"] == "marin"
    alex = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()
    assert alex["config"]["voice"] == "cedar"
    r = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "avatar_id": "alex", "voice": "marin"})
    assert r.status_code == 422  # female voice for the male interviewer
    cat = client.get("/v1/catalog", headers=h).json()
    genders = {v["id"]: v["gender"] for v in cat["voices"]}
    assert genders["cedar"] == "male" and genders["marin"] == "female"
    assert {a["id"]: a["gender"] for a in cat["avatars"]} == {"alex": "male", "maya": "female"}


def test_validation_errors(client):
    h = auth_headers(register(client))
    bad = [
        {**INTERVIEW_BODY, "duration_minutes": 7},
        {**INTERVIEW_BODY, "role_id": "astronaut"},
        {**INTERVIEW_BODY, "role_id": "custom"},
        {**INTERVIEW_BODY, "role_id": "custom", "custom_role_title": "<script>alert(1)</script>"},
        {**INTERVIEW_BODY, "unexpected": 1},
    ]
    for body in bad:
        assert client.post("/v1/interviews", headers=h, json=body).status_code == 422, body


def test_monthly_interview_limit(client, app):
    body = register(client)
    h = auth_headers(body)
    db = app.state.container.db
    for _ in range(3):
        _run(client, BillingService(db).record(body["user"]["id"], UsageKind.INTERVIEW_STARTED, 1))
    r = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY)
    assert r.status_code == 402
    assert r.json()["error"]["code"] == "plan_limit"
    usage = client.get("/v1/billing/usage", headers=h).json()
    assert usage["interviews_remaining"] == 0


def test_ownership_is_enforced(client):
    alice = auth_headers(register(client, "alice@example.com", "Alice"))
    bob = auth_headers(register(client, "bob@example.com", "Bob"))
    sid = client.post("/v1/interviews", headers=alice, json=INTERVIEW_BODY).json()["id"]
    for method, path in [("get", f"/v1/interviews/{sid}"), ("get", f"/v1/interviews/{sid}/report"),
                         ("get", f"/v1/interviews/{sid}/transcript"), ("delete", f"/v1/interviews/{sid}")]:
        r = getattr(client, method)(path, headers=bob)
        assert r.status_code == 404, (method, path)
    assert client.get(f"/v1/interviews/{sid}", headers=alice).status_code == 200
    assert client.get("/v1/interviews", headers=bob).json()["total"] == 0


def test_history_list_and_delete(client):
    h = auth_headers(register(client))
    ids = [client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()["id"] for _ in range(2)]
    listing = client.get("/v1/interviews", headers=h).json()
    assert listing["total"] == 2
    assert [i["id"] for i in listing["items"]] == list(reversed(ids))
    assert client.delete(f"/v1/interviews/{ids[0]}", headers=h).status_code == 204
    assert client.get("/v1/interviews", headers=h).json()["total"] == 1


def test_complete_without_starting_is_abandoned(client):
    h = auth_headers(register(client))
    sid = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()["id"]
    r = client.post(f"/v1/interviews/{sid}/complete", headers=h, json={"reason": "user_cancelled"})
    assert r.status_code == 200
    assert r.json()["status"] == "abandoned"
    assert r.json()["report_status"] == "none"


def test_direct_realtime_secret_disabled_by_default(client):
    h = auth_headers(register(client))
    sid = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()["id"]
    r = client.post(f"/v1/interviews/{sid}/realtime-client-secret", headers=h)
    assert r.status_code == 404
    assert r.json()["error"]["code"] == "direct_realtime_disabled"


def test_delete_all_data(client, app):
    h = auth_headers(register(client))
    client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY)
    r = client.delete("/v1/me/data", headers=h)
    assert r.status_code == 200
    assert r.json()["sessions_deleted"] == 1
    assert client.get("/v1/interviews", headers=h).json()["total"] == 0


def test_health(client):
    assert client.get("/health").json() == {"status": "ok"}
    ready = client.get("/health/ready").json()
    assert ready["database"] is True and ready["demo_mode"] is True


def test_unknown_route_uses_error_envelope(client):
    r = client.get("/v1/nope")
    assert r.status_code == 404
    assert r.json()["error"]["code"] == "not_found"


asyncio  # keep import for type checkers using _run


def test_interview_language(client, app):
    from app.modules.interviews.prompts import build_interviewer_instructions
    from app.modules.realtime.upstream import UpstreamSessionConfig

    h = auth_headers(register(client))
    cat = client.get("/v1/catalog", headers=h).json()
    languages = {lang["id"]: lang["title"] for lang in cat["languages"]}
    assert languages["en"] == "English" and languages["ur"] == "Urdu" and len(languages) >= 10

    default = client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()
    assert default["config"]["language"] == "en"
    urdu = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "language": "ur"})
    assert urdu.status_code == 201, urdu.text
    assert urdu.json()["config"]["language"] == "ur"
    bad = client.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "language": "xx"})
    assert bad.status_code == 422

    cfg = {**INTERVIEW_BODY, "language": "ur", "avatar_id": "alex"}
    text = build_interviewer_instructions(config=cfg, candidate_first_name="Jamie", seed_questions=["Q?"])
    assert "Conduct the WHOLE interview in Urdu" in text and "translate them naturally into Urdu" in text
    english = build_interviewer_instructions(config=INTERVIEW_BODY, candidate_first_name="Jamie", seed_questions=[])
    assert "Conduct the whole interview in English" in english
    session_update = UpstreamSessionConfig(instructions="x", voice="cedar", transcription_model="t",
                                           language="ur").to_session_update()
    assert session_update["session"]["audio"]["input"]["transcription"]["language"] == "ur"


def test_report_prompt_mentions_non_english_language():
    from app.modules.billing.plans import ReportTier
    from app.modules.reports.generator import ReportContext, build_evaluation_input

    ctx = ReportContext("s", "u", ReportTier.BASIC, "SE", "technical", "beginner", "junior", 5,
                        [{"role": "candidate", "text": "salaam"}], language="Urdu")
    text = build_evaluation_input(ctx)
    assert "Interview language: Urdu" in text and "write the whole report in English" in text
    ctx.language = "English"
    assert "translate any quoted evidence" not in build_evaluation_input(ctx)
