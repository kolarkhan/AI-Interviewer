"""Integration tests against a REAL MongoDB server (production code path: PyMongo AsyncMongoClient).

Uses `pymongo-inmemory` to download and start an ephemeral official `mongod`. Skipped automatically if the
server cannot be started (offline machine) or when AII_SKIP_MONGO_INTEGRATION=1.
"""

from __future__ import annotations

import asyncio
import json
import math
import os
import struct
import uuid

import httpx
import pymongo
import pytest
from fastapi.testclient import TestClient
from websockets.asyncio.client import connect

from app.main import create_app
from app.modules.realtime.demo_upstream import DemoUpstream
from app.modules.reports.generator import DemoReportGenerator
from tests.conftest import INTERVIEW_BODY, TEST_PASSWORD, LiveServer, make_settings

pytestmark = pytest.mark.skipif(os.environ.get("AII_SKIP_MONGO_INTEGRATION") == "1", reason="disabled by env")


def _mongod_binary() -> str | None:
    """Downloads (once, hash-verified by pymongo-inmemory) and returns the official mongod binary path."""
    try:
        from pymongo_inmemory.context import Context
        from pymongo_inmemory.downloader import download
    except ImportError:
        return None
    ctx = Context()
    bin_dir = download(ctx)
    exe = "mongod.exe" if os.name == "nt" else "mongod"
    path = os.path.join(bin_dir, exe)
    return path if os.path.exists(path) else None


@pytest.fixture(scope="module")
def mongo_uri(tmp_path_factory):
    import socket
    import subprocess
    import time

    binary = os.environ.get("AII_MONGOD_PATH") or _mongod_binary()
    if not binary:
        pytest.skip("mongod binary not available (install pymongo-inmemory or set AII_MONGOD_PATH)")
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    data = tmp_path_factory.mktemp("mongo-data")
    log = data / "mongod.log"
    proc = subprocess.Popen([binary, "--dbpath", str(data), "--port", str(port), "--bind_ip", "127.0.0.1",
                             "--logpath", str(log), "--nounixsocket" if os.name != "nt" else "--quiet"])
    uri = f"mongodb://127.0.0.1:{port}"
    deadline = time.time() + 60
    while True:
        try:
            pymongo.MongoClient(uri, serverSelectionTimeoutMS=1000).admin.command("ping")
            break
        except pymongo.errors.PyMongoError:
            if proc.poll() is not None or time.time() > deadline:
                proc.kill()
                tail = log.read_text(errors="replace")[-1500:] if log.exists() else "(no log)"
                pytest.skip(f"mongod did not start: {tail}")
            time.sleep(0.5)
    yield uri
    proc.terminate()
    try:
        proc.wait(timeout=15)
    except subprocess.TimeoutExpired:
        proc.kill()


def real_app(uri: str):
    settings = make_settings(database_url=uri, database_name=f"it_{uuid.uuid4().hex[:8]}")
    return create_app(settings, upstream_factory=lambda cfg, sid: DemoUpstream(cfg, pacing=0.0),
                      report_generator=DemoReportGenerator()), settings


def register(client, email="it@example.com"):
    r = client.post("/v1/auth/register", json={"email": email, "password": TEST_PASSWORD,
                                               "display_name": "Integration", "accept_terms": True})
    assert r.status_code == 201, r.text
    return r.json()


def test_indexes_auth_and_ownership_on_real_mongo(mongo_uri):
    app, settings = real_app(mongo_uri)
    with TestClient(app) as client:
        body = register(client)
        h = {"Authorization": f"Bearer {body['tokens']['access_token']}"}

        # Unique index enforced by the real server.
        dup = client.post("/v1/auth/register", json={"email": "IT@example.com", "password": TEST_PASSWORD,
                                                     "display_name": "x", "accept_terms": True})
        assert dup.status_code == 409

        # Refresh rotation + reuse detection with real update semantics.
        first = body["tokens"]["refresh_token"]
        second = client.post("/v1/auth/refresh", json={"refresh_token": first}).json()["refresh_token"]
        assert client.post("/v1/auth/refresh", json={"refresh_token": first}).status_code == 401
        assert client.post("/v1/auth/refresh", json={"refresh_token": second}).status_code == 401

        login = client.post("/v1/auth/login", json={"email": "it@example.com", "password": TEST_PASSWORD}).json()
        h = {"Authorization": f"Bearer {login['tokens']['access_token']}"}
        ids = [client.post("/v1/interviews", headers=h, json=INTERVIEW_BODY).json()["id"] for _ in range(2)]
        listing = client.get("/v1/interviews", headers=h).json()
        assert listing["total"] == 2 and listing["items"][0]["id"] == ids[1]
        assert client.get("/v1/catalog", headers=h).json()["plan"] == "free"
        assert client.get("/health/ready").json()["database"] is True

        other = register(client, "other@example.com")
        oh = {"Authorization": f"Bearer {other['tokens']['access_token']}"}
        assert client.get(f"/v1/interviews/{ids[0]}", headers=oh).status_code == 404

        assert client.delete("/v1/me/data", headers=h).json()["sessions_deleted"] == 2

    raw = pymongo.MongoClient(mongo_uri)[settings.database_name]
    rt_indexes = {i["name"]: i for i in raw.refresh_tokens.list_indexes()}
    assert rt_indexes["ttl_expires"]["expireAfterSeconds"] == 0
    assert rt_indexes["uniq_token_hash"]["unique"] is True
    msg_indexes = {i["name"] for i in raw.interview_messages.list_indexes()}
    assert "ttl_purge" in msg_indexes
    assert raw.question_bank.count_documents({}) > 50
    user = raw.users.find_one({"email": "it@example.com"})
    assert user["password_hash"].startswith("$argon2id$") and "password" not in user


def _tone(ms: int) -> bytes:
    return b"".join(struct.pack("<h", int(8000 * math.sin(2 * math.pi * 440 * i / 24000))) for i in range(24 * ms))


async def test_live_interview_and_report_on_real_mongo(mongo_uri):
    app, settings = real_app(mongo_uri)
    with LiveServer(app) as server:
        async with httpx.AsyncClient(base_url=server.http) as http:
            r = await http.post("/v1/auth/register", json={"email": "live-it@example.com", "password": TEST_PASSWORD,
                                                           "display_name": "Live", "accept_terms": True})
            h = {"Authorization": f"Bearer {r.json()['tokens']['access_token']}"}
            sid = (await http.post("/v1/interviews", headers=h, json=INTERVIEW_BODY)).json()["id"]

            async with connect(f"{server.ws}/v1/interviews/{sid}/live", additional_headers=h) as ws:
                async def until(t: str, timeout: float = 8.0) -> dict:
                    async def loop():
                        while True:
                            m = await ws.recv()
                            if isinstance(m, str) and json.loads(m)["type"] == t:
                                return json.loads(m)
                    return await asyncio.wait_for(loop(), timeout)

                await until("ai.response.done")
                for _ in range(2):
                    for i in range(0, 24 * 1700 * 2, 4800):
                        await ws.send((_tone(600) + b"\x00\x00" * 24 * 1100)[i:i + 4800])
                    await until("transcript.user")
                    await until("ai.response.done")
                    await asyncio.sleep(0.3)
                await ws.send(json.dumps({"type": "interview.end"}))
                done = await until("interview.completed")
                assert done["report_status"] == "pending"

            for _ in range(50):
                rep = (await http.get(f"/v1/interviews/{sid}/report", headers=h)).json()
                if rep["status"] == "ready":
                    break
                await asyncio.sleep(0.1)
            assert rep["status"] == "ready"
            usage = (await http.get("/v1/billing/usage", headers=h)).json()
            assert usage["interviews_used"] == 1

    raw = pymongo.MongoClient(mongo_uri)[settings.database_name]
    session = raw.interview_sessions.find_one({"_id": sid})
    assert session["status"] == "completed" and session["report_status"] == "ready"
    assert session["started_at"].tzinfo is None or session["started_at"]  # stored as BSON datetime
    # Transcript purged after the report (user did not opt in to retention).
    assert raw.interview_messages.count_documents({"session_id": sid}) == 0
    assert raw.interview_reports.count_documents({"session_id": sid}) == 1
    kinds = {u["kind"] for u in raw.usage_records.find({"session_id": sid})}
    assert {"interview_started", "report_generated"} <= kinds


def test_resume_index_migration_on_real_mongo(mongo_uri):
    """Older deployments had a unique index (one resume per user); startup must replace it."""
    app, settings = real_app(mongo_uri)
    raw = pymongo.MongoClient(mongo_uri)[settings.database_name]
    raw.resumes.create_index([("user_id", pymongo.ASCENDING)], unique=True, name="uniq_user")
    with TestClient(app) as client:
        indexes = raw.resumes.index_information()
        assert "uniq_user" not in indexes and "by_user" in indexes
        h = {"Authorization": f"Bearer {register(client, 'docs@example.com')['tokens']['access_token']}"}
        client.post("/v1/billing/dev/grant-premium", headers=h)
        text = ("Backend engineer with Python, FastAPI and PostgreSQL experience building payment APIs "
                "and leading database migrations for high-traffic services.").encode()
        r = client.post("/v1/resumes", headers=h, data={"consent": "true"},
                        files=[("files", ("cv.txt", text, "text/plain")), ("files", ("cover.txt", text, "text/plain"))])
        assert r.status_code == 201, r.text
        assert len(client.get("/v1/resumes", headers=h).json()["documents"]) == 2
