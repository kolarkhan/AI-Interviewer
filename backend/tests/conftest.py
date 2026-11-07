from __future__ import annotations

import socket
import threading
import time
from collections.abc import Iterator

import pytest
import uvicorn
from fastapi.testclient import TestClient
from mongomock_motor import AsyncMongoMockClient

from app.core.config import Settings
from app.db.mongo import Database
from app.main import create_app
from app.modules.realtime.demo_upstream import DemoUpstream
from app.modules.reports.generator import DemoReportGenerator, ReportGenerator

TEST_PASSWORD = "correct-horse-42"


def make_settings(**overrides) -> Settings:
    base = dict(
        environment="test",
        database_url="memory://",
        jwt_secret="test-secret-that-is-long-enough-0123456789abcdef",
        openai_api_key=None,
        live_reconnect_grace_seconds=2,
        live_wrapup_grace_seconds=3,
        log_level="WARNING",
        voice_preview_warmup=False,
    )
    base.update(overrides)
    return Settings(_env_file=None, **base)


def demo_factory(config, safety_id):
    return DemoUpstream(config, pacing=0.0)


def build_app(settings: Settings | None = None, *, report_generator: ReportGenerator | None = None,
              upstream_factory=demo_factory):
    settings = settings or make_settings()
    db = Database(AsyncMongoMockClient(), "test_db", is_memory=True)
    return create_app(settings, db=db, upstream_factory=upstream_factory,
                      report_generator=report_generator or DemoReportGenerator())


@pytest.fixture
def app():
    return build_app()


@pytest.fixture
def client(app) -> Iterator[TestClient]:
    with TestClient(app) as c:
        yield c


def register(client, email: str = "candidate@example.com", name: str = "Jamie Candidate") -> dict:
    r = client.post("/v1/auth/register", json={"email": email, "password": TEST_PASSWORD,
                                               "display_name": name, "accept_terms": True})
    assert r.status_code == 201, r.text
    return r.json()


def auth_headers(tokens_or_body: dict) -> dict:
    tokens = tokens_or_body.get("tokens", tokens_or_body)
    return {"Authorization": f"Bearer {tokens['access_token']}"}


INTERVIEW_BODY = {
    "role_id": "software_engineer",
    "interview_type": "technical",
    "difficulty": "beginner",
    "experience_level": "junior",
    "duration_minutes": 5,
}


class LiveServer:
    """Runs the app under a real uvicorn server in a background thread (true WebSocket stack)."""

    def __init__(self, app) -> None:
        self.app = app
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.bind(("127.0.0.1", 0))
        self.port = self.sock.getsockname()[1]
        config = uvicorn.Config(app, log_level="warning", ws="websockets-sansio", lifespan="on")
        self.server = uvicorn.Server(config)
        self.thread = threading.Thread(target=self.server.run, kwargs={"sockets": [self.sock]}, daemon=True)

    @property
    def http(self) -> str:
        return f"http://127.0.0.1:{self.port}"

    @property
    def ws(self) -> str:
        return f"ws://127.0.0.1:{self.port}"

    def __enter__(self) -> LiveServer:
        self.thread.start()
        deadline = time.time() + 10
        while not self.server.started:
            if time.time() > deadline:
                raise RuntimeError("server did not start")
            time.sleep(0.02)
        return self

    def __exit__(self, *exc) -> None:
        self.server.should_exit = True
        self.thread.join(timeout=10)


@pytest.fixture
def live_server(app) -> Iterator[LiveServer]:
    with LiveServer(app) as server:
        yield server
