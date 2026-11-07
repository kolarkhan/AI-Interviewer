from __future__ import annotations

import asyncio
import base64
import io
import struct
import wave

from fastapi.testclient import TestClient

from app.modules.realtime.gpt_live_upstream import build_seed_input
from app.modules.realtime.upstream import RealtimeUpstream, UpstreamSessionConfig
from tests.conftest import auth_headers, build_app, make_settings, register

ONE_SECOND = struct.pack("<h", 1200) * 24_000


class FakeVoiceUpstream(RealtimeUpstream):
    """Speaks one second of audio when asked, like the real engines do after `response.create`."""

    created: list[UpstreamSessionConfig] = []

    def __init__(self, config: UpstreamSessionConfig) -> None:
        self.config = config
        self._q: asyncio.Queue[dict | None] = asyncio.Queue()
        FakeVoiceUpstream.created.append(config)

    async def connect(self) -> None:
        self._q.put_nowait({"type": "session.updated"})

    async def send(self, event: dict) -> None:
        if event["type"] == "response.create":
            for i in range(0, len(ONE_SECOND), 9600):
                self._q.put_nowait({"type": "response.output_audio.delta",
                                    "delta": base64.b64encode(ONE_SECOND[i:i + 9600]).decode()})
            self._q.put_nowait({"type": "response.done", "response": {"status": "completed"}})

    async def recv(self) -> dict | None:
        return await self._q.get()

    async def close(self) -> None:
        self._q.put_nowait(None)


def _client_with_fake_engine() -> TestClient:
    FakeVoiceUpstream.created = []
    settings = make_settings(openai_api_key="sk-test-voice-preview-123")
    return TestClient(build_app(settings, upstream_factory=lambda cfg, sid: FakeVoiceUpstream(cfg)))


def test_preview_is_generated_once_and_served_as_wav():
    with _client_with_fake_engine() as client:
        h = auth_headers(register(client))
        r = client.get("/v1/catalog/voices/cedar/preview", headers=h)
        assert r.status_code == 200, r.text
        assert r.headers["content-type"] == "audio/wav"
        with wave.open(io.BytesIO(r.content)) as w:
            assert (w.getnchannels(), w.getsampwidth(), w.getframerate()) == (1, 2, 24_000)
            assert w.getnframes() == 24_000
        assert len(FakeVoiceUpstream.created) == 1
        cfg = FakeVoiceUpstream.created[0]
        assert cfg.voice == "cedar" and "Alex" in cfg.instructions and cfg.seed_prompt

        again = client.get("/v1/catalog/voices/cedar/preview", headers=h)
        assert again.status_code == 200 and again.content == r.content
        assert len(FakeVoiceUpstream.created) == 1  # served from storage

        female = client.get("/v1/catalog/voices/marin/preview", headers=h)
        assert female.status_code == 200
        assert "Maya" in FakeVoiceUpstream.created[-1].instructions


def test_preview_errors():
    with _client_with_fake_engine() as client:
        h = auth_headers(register(client))
        assert client.get("/v1/catalog/voices/nope/preview", headers=h).status_code == 404
        assert client.get("/v1/catalog/voices/cedar/preview").status_code == 401


def test_preview_unavailable_in_demo_mode(client):
    h = auth_headers(register(client))
    r = client.get("/v1/catalog/voices/cedar/preview", headers=h)
    assert r.status_code == 503
    assert r.json()["error"]["code"] == "voice_preview_unavailable"


def test_gpt_live_seed_prompt_overrides_the_opener():
    cfg = UpstreamSessionConfig(instructions="x", voice="cedar", transcription_model="t",
                                seed_prompt="Please read your voice sample now.")
    items = build_seed_input(cfg)
    assert items[-1]["content"][0]["text"] == "Please read your voice sample now."


def test_trim_silence_keeps_speech_with_padding():
    from app.modules.catalog.voice_preview import trim_silence

    silence = bytes(24_000 * 2)  # 1 s
    speech = struct.pack("<h", 3000) * 12_000  # 0.5 s
    out = trim_silence(silence + speech + silence)
    n = len(out) // 2
    assert 12_000 <= n <= 12_000 + 2 * 6_000 + 480  # speech + ~250 ms padding each side
    assert trim_silence(silence) == silence  # all quiet: unchanged


class SilentOnceUpstream(FakeVoiceUpstream):
    """First session never speaks (as GPT-Live occasionally does); later sessions speak normally."""

    sessions = 0

    def __init__(self, config: UpstreamSessionConfig) -> None:
        super().__init__(config)
        SilentOnceUpstream.sessions += 1
        self._silent = SilentOnceUpstream.sessions == 1

    async def send(self, event: dict) -> None:
        if not self._silent:
            await super().send(event)


def test_silent_session_is_retried_with_a_new_session(monkeypatch):
    from app.modules.catalog import voice_preview

    monkeypatch.setattr(voice_preview, "SPEECH_START_TIMEOUT_S", 0.3)
    FakeVoiceUpstream.created = []
    SilentOnceUpstream.sessions = 0
    settings = make_settings(openai_api_key="sk-test-voice-preview-123")
    with TestClient(build_app(settings, upstream_factory=lambda cfg, sid: SilentOnceUpstream(cfg))) as client:
        h = auth_headers(register(client))
        r = client.get("/v1/catalog/voices/flitz/preview", headers=h)
        assert r.status_code == 200, r.text
    assert SilentOnceUpstream.sessions == 2
    first, second = FakeVoiceUpstream.created
    assert "exactly as written" in first.seed_prompt and second.seed_prompt != first.seed_prompt


def test_always_silent_voice_returns_503(monkeypatch):
    from app.modules.catalog import voice_preview

    class Mute(FakeVoiceUpstream):
        async def send(self, event: dict) -> None:
            return None

    monkeypatch.setattr(voice_preview, "SPEECH_START_TIMEOUT_S", 0.2)
    settings = make_settings(openai_api_key="sk-test-voice-preview-123")
    with TestClient(build_app(settings, upstream_factory=lambda cfg, sid: Mute(cfg))) as client:
        h = auth_headers(register(client))
        r = client.get("/v1/catalog/voices/flitz/preview", headers=h)
        assert r.status_code == 503 and r.json()["error"]["code"] == "voice_preview_unavailable"


def test_warm_up_generates_missing_samples_once(monkeypatch):
    from app.modules.catalog import data, voice_preview

    FakeVoiceUpstream.created = []
    settings = make_settings(openai_api_key="sk-test-voice-preview-123", voice_preview_warmup=True)
    app = build_app(settings, upstream_factory=lambda cfg, sid: FakeVoiceUpstream(cfg))
    c = app.state.container
    with TestClient(app) as client:
        made = client.portal.call(lambda: voice_preview.warm_up(c, "sys"))
        total = len(data.voices_for_engine(settings.openai_voice_engine))
        # the startup task may already have generated some; together they cover every voice exactly once
        assert len(FakeVoiceUpstream.created) == total and made <= total
        assert client.portal.call(lambda: voice_preview.warm_up(c, "sys")) == 0
        h = auth_headers(register(client))
        cat = client.get("/v1/catalog", headers=h).json()
        assert cat["voice_preview_version"] == f"{settings.openai_voice_engine}-v{voice_preview.PREVIEW_VERSION}"
        assert client.get("/v1/catalog/voices/cedar/preview", headers=h).status_code == 200
        assert len(FakeVoiceUpstream.created) == total  # served from storage
