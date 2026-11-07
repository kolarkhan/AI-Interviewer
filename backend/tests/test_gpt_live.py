"""GPT-Live (gpt-live-1) upstream: protocol translation against a fake server that behaves like the real service
as observed in probe sessions (2026-10-18): continuous all-zero audio frames between turns, greeting only when
the session is seeded with a user message, transcript deltas without done events."""

from __future__ import annotations

import asyncio
import base64
import json
import struct

import httpx
from websockets.asyncio.client import connect
from websockets.asyncio.server import serve

from app.modules.realtime.gpt_live_upstream import INTERRUPT_INSTRUCTION, GptLiveUpstream, build_seed_input
from app.modules.realtime.upstream import UpstreamSessionConfig
from tests.conftest import INTERVIEW_BODY, TEST_PASSWORD, LiveServer, build_app, make_settings

VOICED = base64.b64encode(struct.pack("<h", 1500) * 2400).decode()   # 100 ms of "speech"
SILENT = base64.b64encode(b"\x00\x00" * 2400).decode()               # 100 ms of digital silence
SILENT_KEEPALIVE = SILENT  # the upstream's keep-alive frames are 100 ms of silence too


class FakeLiveServer:
    def __init__(self) -> None:
        self.received: list[dict] = []
        self.headers: dict = {}
        self.path = ""
        self.port = 0
        self._server = None
        self._speaking = asyncio.Lock()

    async def __aenter__(self) -> FakeLiveServer:
        self._server = await serve(self._handler, "127.0.0.1", 0)
        self.port = self._server.sockets[0].getsockname()[1]
        return self

    async def __aexit__(self, *exc) -> None:
        self._server.close()
        await self._server.wait_closed()

    @property
    def url(self) -> str:
        return f"ws://127.0.0.1:{self.port}/v1/live/sessions"

    async def _speak(self, ws, words: list[str]) -> None:
        async with self._speaking:
            for i, w in enumerate(words):
                await ws.send(json.dumps({"type": "session.output_audio.delta", "delta": VOICED}))
                await ws.send(json.dumps({"type": "session.output_transcript.delta", "delta": w + " ",
                                          "event_id": f"o{i}", "start_ms": i * 100, "end_ms": i * 100 + 100}))
                await asyncio.sleep(0.02)

    async def _silence(self, ws) -> None:
        while True:
            await asyncio.sleep(0.05)
            if not self._speaking.locked():
                await ws.send(json.dumps({"type": "session.output_audio.delta", "delta": SILENT}))

    async def _handler(self, ws) -> None:
        self.path = ws.request.path
        self.headers = dict(ws.request.headers)
        audio_chunks = 0
        silence = None
        try:
            async for raw in ws:
                msg = json.loads(raw)
                self.received.append(msg)
                t = msg["type"]
                if t == "session.start":
                    await ws.send(json.dumps({"type": "session.started", "event_id": "e0",
                                              "session": {"id": "live_sess_1"}}))
                    silence = asyncio.create_task(self._silence(ws))
                    seeded = msg["session"].get("input") or []
                    if seeded and seeded[-1]["role"] == "user" and msg["session"].get("delegation"):
                        await asyncio.sleep(0.1)
                        await self._speak(ws, ["Hello", "Jamie,", "tell", "me", "about", "yourself."])
                elif t == "session.instructions.append":
                    await ws.send(json.dumps({"type": "session.instructions.appended", "event_id": "ia",
                                              "start_ms": 0, "end_ms": 0}))
                elif t == "session.input_audio.append":
                    if msg["audio"] == SILENT_KEEPALIVE:
                        continue  # keep-alive silence from the upstream pump
                    audio_chunks += 1
                    if audio_chunks == 3:
                        for w in ["I", "build", "APIs."]:
                            await ws.send(json.dumps({"type": "session.input_transcript.delta", "delta": w + " ",
                                                      "event_id": "u", "start_ms": 0, "end_ms": 1}))
                        await asyncio.sleep(0.3)
                        await self._speak(ws, ["Great,", "what", "stack?"])
                elif t == "session.close":
                    await ws.send(json.dumps({"type": "session.closed", "event_id": "c", "reason": "close_requested",
                                              "session": {"id": "live_sess_1"}, "usage": {"seconds": 42.5}}))
                    await ws.close()
                    return
        finally:
            if silence:
                silence.cancel()


def _upstream(url: str, history: list[dict] | None = None) -> GptLiveUpstream:
    settings = make_settings(openai_api_key="sk-live-test-123456", openai_live_url=url)
    cfg = UpstreamSessionConfig(instructions="Be an interviewer", voice="meridian", transcription_model="x",
                                model="gpt-live-1", candidate_first_name="Jamie", history=history or [])
    return GptLiveUpstream(settings, cfg, safety_identifier="hashed", output_idle_ms=200, input_idle_ms=200)


async def _until(up: GptLiveUpstream, etype: str, seen: list[dict], timeout: float = 5) -> dict:
    async def loop():
        while True:
            ev = await up.recv()
            assert ev is not None, "upstream closed early"
            seen.append(ev)
            if ev["type"] == etype:
                return ev
    return await asyncio.wait_for(loop(), timeout)


def test_seed_input_opens_or_resumes_the_conversation():
    fresh = build_seed_input(UpstreamSessionConfig("i", "cedar", "t", candidate_first_name="Jamie"))
    assert fresh == [{"type": "message", "role": "user",
                      "content": [{"type": "input_text", "text": "Hi, I'm Jamie. I'm ready to start the interview."}]}]
    resumed = build_seed_input(UpstreamSessionConfig("i", "cedar", "t", history=[
        {"role": "interviewer", "text": "Tell me about yourself."}, {"role": "candidate", "text": "I build APIs."}]))
    assert [m["role"] for m in resumed] == ["assistant", "user", "user"]
    assert resumed[0]["content"][0] == {"type": "output_text", "text": "Tell me about yourself."}
    assert "restored" in resumed[-1]["content"][0]["text"]


async def test_handshake_session_start_and_delegation():
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        seen: list[dict] = []
        await _until(up, "session.updated", seen)
        await up.close()
    assert srv.path == "/v1/live/sessions"
    assert srv.headers["authorization"] == "Bearer sk-live-test-123456"
    session = srv.received[0]["session"]
    assert session["model"] == "gpt-live-1"
    assert session["audio"] == {"format": {"type": "audio/pcm", "rate": 24000}, "output": {"voice": "meridian"}}
    assert session["delegation"]["type"] == "responses"
    assert session["delegation"]["responses"]["model"] == "gpt-5.6-luna"
    assert session["input"][-1]["role"] == "user"
    assert srv.received[-1]["type"] == "session.close"
    assert up.usage_seconds == 42.5 and up.close_reason == "close_requested"


async def test_greeting_is_held_until_app_attaches_and_silence_is_dropped():
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        seen: list[dict] = []
        await _until(up, "session.updated", seen)
        await asyncio.sleep(1.0)  # the greeting is spoken now, but held
        assert up._events.empty()
        await up.send({"type": "response.create"})  # relay attached the app
        created = await _until(up, "response.created", seen)
        first = await _until(up, "response.output_audio.delta", seen)
        assert first["response_id"] == created["response"]["id"]
        greeting = await _until(up, "response.output_audio_transcript.done", seen)
        assert greeting["transcript"] == "Hello Jamie, tell me about yourself."
        assert (await _until(up, "response.done", seen))["response"]["status"] == "completed"
        # Background silence keeps flowing but must not create turns or reach the app.
        seen.clear()
        await asyncio.sleep(0.8)
        while not up._events.empty():
            seen.append(up._events.get_nowait())
        assert not any(e["type"] in ("response.created", "response.output_audio.delta") for e in seen)
        await up.close()
    assert not any(m["type"] == "session.instructions.append" for m in srv.received)


async def test_candidate_turn_and_reply():
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        seen: list[dict] = []
        await up.send({"type": "response.create"})
        await _until(up, "response.done", seen)
        for _ in range(3):
            await up.send({"type": "input_audio_buffer.append", "audio": VOICED})
        await _until(up, "input_audio_buffer.speech_started", seen)
        await _until(up, "input_audio_buffer.speech_stopped", seen)
        user = await _until(up, "conversation.item.input_audio_transcription.completed", seen)
        assert user["transcript"] == "I build APIs."
        reply = await _until(up, "response.output_audio_transcript.done", seen)
        assert reply["transcript"] == "Great, what stack?"
        await up.close()
    assert sum(1 for m in srv.received if m["type"] == "session.input_audio.append" and m["audio"] == VOICED) == 3


async def test_notices_and_interrupt_become_instruction_appends():
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        seen: list[dict] = []
        await up.send({"type": "response.create"})
        await _until(up, "response.created", seen)
        await up.send({"type": "response.cancel"})  # tap-to-interrupt during the greeting
        assert (await _until(up, "response.done", seen))["response"]["status"] == "cancelled"
        await up.send({"type": "conversation.item.create", "item": {"type": "message", "role": "system",
                       "content": [{"type": "input_text", "text": "[System notice] Time is up. Close now."}]}})
        await up.send({"type": "response.create"})  # must not add another instruction
        await up.send({"type": "conversation.item.truncate", "item_id": "x", "content_index": 0, "audio_end_ms": 5})
        assert (await _until(up, "conversation.item.truncated", seen))["item_id"] == "x"
        await up.close()
    contents = [m["content"] for m in srv.received if m["type"] == "session.instructions.append"]
    assert contents == [INTERRUPT_INSTRUCTION, "[System notice] Time is up. Close now."]


async def test_silence_keepalive_runs_while_app_is_quiet():
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        await asyncio.sleep(0.8)  # the app sends nothing
        await up.close()
    keepalive = [m for m in srv.received if m["type"] == "session.input_audio.append" and m["audio"] == SILENT]
    assert len(keepalive) >= 5


async def test_error_events_are_forwarded_immediately():
    async def handler(ws):
        await ws.recv()
        await ws.send(json.dumps({"type": "error", "event_id": "x", "error": {
            "code": "unknown_parameter", "message": "bad voice", "type": "invalid_request_error",
            "param": "session.audio.output.voice"}}))
        await asyncio.sleep(0.5)

    async with serve(handler, "127.0.0.1", 0) as server:
        port = server.sockets[0].getsockname()[1]
        up = _upstream(f"ws://127.0.0.1:{port}/v1/live/sessions")
        await up.connect()
        ev = await asyncio.wait_for(up.recv(), 5)
        assert ev == {"type": "error", "error": {"code": "unknown_parameter", "type": "invalid_request_error",
                                                 "message": "bad voice"}}
        await up.close()


async def test_full_interview_through_relay_with_gpt_live():
    async with FakeLiveServer() as srv:
        settings = make_settings(openai_api_key="sk-live-test-123456", openai_live_url=srv.url)
        app = build_app(settings, upstream_factory=lambda cfg, sid: GptLiveUpstream(
            settings, cfg, safety_identifier=sid, output_idle_ms=200, input_idle_ms=200))
        with LiveServer(app) as server:
            async with httpx.AsyncClient(base_url=server.http) as http:
                r = await http.post("/v1/auth/register", json={"email": "gl@example.com", "password": TEST_PASSWORD,
                                                               "display_name": "Jamie", "accept_terms": True})
                h = {"Authorization": f"Bearer {r.json()['tokens']['access_token']}"}
                cat = (await http.get("/v1/catalog", headers=h)).json()
                assert cat["voice_engine"] == "live"
                assert {"gleam", "meridian", "vesper", "willow"} <= {v["id"] for v in cat["voices"]}
                created = await http.post("/v1/interviews", headers=h,
                                          json={**INTERVIEW_BODY, "avatar_id": "maya", "voice": "gleam"})
                assert created.status_code == 201, created.text
                sid = created.json()["id"]
                bad = await http.post("/v1/interviews", headers=h, json={**INTERVIEW_BODY, "voice": "coral"})
                assert bad.status_code == 422  # Realtime-only voice is not offered by the GPT-Live engine

                async with connect(f"{server.ws}/v1/interviews/{sid}/live", additional_headers=h) as ws:
                    async def until(t: str) -> dict:
                        async def loop():
                            while True:
                                m = await ws.recv()
                                if isinstance(m, bytes):
                                    continue
                                ev = json.loads(m)
                                if ev["type"] == t:
                                    return ev
                        return await asyncio.wait_for(loop(), 8)

                    ready = await until("session.ready")
                    assert ready["interviewer_name"] == "Maya" and ready["demo"] is False
                    greeting = await until("transcript.ai.done")
                    assert greeting["text"].startswith("Hello Jamie")
                    for _ in range(3):
                        await ws.send(b"\x10\x00" * 960)
                    assert (await until("transcript.user"))["text"] == "I build APIs."
                    assert (await until("transcript.ai.done"))["text"] == "Great, what stack?"
                    await ws.send(json.dumps({"type": "interview.end"}))
                    assert (await until("interview.completed"))["reason"] == "user_ended"

    start = next(m for m in srv.received if m["type"] == "session.start")
    assert start["session"]["audio"]["output"]["voice"] == "gleam"
    assert "You are Maya" in start["session"]["instructions"]


async def test_connect_retries_transient_handshake_failures(monkeypatch):
    import websockets.asyncio.client as wsclient

    from app.modules.realtime import gpt_live_upstream as glu

    real_connect = wsclient.connect
    calls = {"n": 0}

    def flaky_connect(*args, **kwargs):
        calls["n"] += 1
        if calls["n"] == 1:
            raise TimeoutError("handshake timed out")
        return real_connect(*args, **kwargs)

    monkeypatch.setattr(glu, "CONNECT_BACKOFF_S", 0.0)
    monkeypatch.setattr(wsclient, "connect", flaky_connect)
    async with FakeLiveServer() as srv:
        up = _upstream(srv.url)
        await up.connect()
        await _until(up, "session.updated", [])
        await up.close()
    assert calls["n"] == 2


async def test_connect_gives_up_after_bounded_attempts(monkeypatch):
    import websockets.asyncio.client as wsclient

    from app.modules.realtime import gpt_live_upstream as glu
    from app.modules.realtime.upstream import UpstreamError

    calls = {"n": 0}

    def dead_connect(*args, **kwargs):
        calls["n"] += 1
        raise TimeoutError("handshake timed out")

    monkeypatch.setattr(glu, "CONNECT_BACKOFF_S", 0.0)
    monkeypatch.setattr(wsclient, "connect", dead_connect)
    up = _upstream("ws://127.0.0.1:9/v1/live/sessions")
    try:
        await up.connect()
        raise AssertionError("connect should fail")
    except UpstreamError as exc:
        assert exc.retryable
    assert calls["n"] == glu.CONNECT_ATTEMPTS


async def test_unexpected_socket_drop_is_logged_and_ends_the_stream(caplog):
    async def drop_handler(ws):
        async for raw in ws:
            if json.loads(raw)["type"] == "session.start":
                await ws.send(json.dumps({"type": "session.started", "event_id": "e0", "session": {"id": "s"}}))
                await asyncio.sleep(0.1)
                await ws.close(code=1011, reason="upstream maintenance")
                return

    server = await serve(drop_handler, "127.0.0.1", 0)
    port = server.sockets[0].getsockname()[1]
    try:
        up = _upstream(f"ws://127.0.0.1:{port}/v1/live/sessions")
        await up.connect()
        up._open_gate()
        caplog.set_level("WARNING", logger="app.modules.realtime.gpt_live_upstream")

        async def drain():
            while (ev := await up.recv()) is not None:
                pass
        await asyncio.wait_for(drain(), 5)
        await up.close()
    finally:
        server.close()
        await server.wait_closed()
    drops = [r for r in caplog.records if r.getMessage() == "GPT-Live socket dropped"]
    assert drops and drops[0].ctx["code"] == 1011 and drops[0].ctx["by"] == "server"
    assert drops[0].ctx["reason"] == "upstream maintenance"
