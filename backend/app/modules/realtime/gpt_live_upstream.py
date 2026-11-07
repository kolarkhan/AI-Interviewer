"""GPT-Live (`gpt-live-1`) upstream.

GPT-Live is a separate OpenAI API from the Realtime API. Protocol facts (OpenAI docs + `openai` SDK 3.31
`openai/types/live`, and verified with live probe sessions on 2026-10-18):

- WebSocket `wss://api.openai.com/v1/live/sessions`, `Authorization: Bearer <server key>`.
- First message `session.start` {session: {model, instructions, audio: {format, output: {voice}}, delegation,
  input}}; wait for `session.started`. Voice, format and instructions are immutable after startup.
- **A Responses delegation backend is needed for the model to answer** (without it, probes showed the model
  neither transcribed nor replied).
- **The model does not open the conversation from instructions** (`session.instructions.append` alone produced
  no speech); seeding `input` with a short user message makes the interviewer greet immediately.
- **Output audio is a continuous real-time stream**: silent periods arrive as all-zero PCM frames. Speech is
  detected from frame energy; silent frames outside a turn are not forwarded to the app.
- Transcript deltas (`session.input_transcript.delta`, `session.output_transcript.delta`) carry `start_ms`/
  `end_ms` but there are no turn/done events, so turns end after a quiet gap.
- **The session clock is driven by input audio**: without a continuous input stream the model does not progress
  and the session ends as `expired` (~30 s). A keep-alive pump sends silence whenever the app sends no audio
  (muted, reconnecting, or before the first microphone frame).
- `session.instructions.append` {content, delegation_id: null} can interrupt speech in progress (tap-to-interrupt,
  time notices). `session.close` → `session.closed` {reason, usage.seconds}.

The class translates GPT-Live into the Realtime-style events the relay (`LiveSession`) already understands,
so the app protocol is identical for both voice engines.
"""

from __future__ import annotations

import array
import asyncio
import base64
import contextlib
import json
import math
import time
from typing import Any

from app.core.config import Settings
from app.core.logging import get_logger
from app.modules.realtime.upstream import RealtimeUpstream, UpstreamError, UpstreamSessionConfig

log = get_logger(__name__)

INTERRUPT_INSTRUCTION = "The candidate wants to speak. Stop talking now and listen."
SPEECH_RMS = 20.0          # silence frames are exactly zero; any speech is far above this
MAX_SEED_MESSAGES = 60     # GPT-Live accepts up to 128 history messages / 8,192 tokens
MAX_SEED_CHARS = 20_000
GATE_TIMEOUT_S = 5.0       # release held output if the app never signals readiness
SILENCE_FRAME_S = 0.1      # keep-alive frame length
CONNECT_ATTEMPTS = 3       # handshake tries per (re)connect
CONNECT_TIMEOUT_S = 12.0   # per handshake
CONNECT_BACKOFF_S = 1.0    # grows linearly between tries
SILENCE_B64 = base64.b64encode(bytes(4800)).decode()  # 100 ms of PCM16 silence @ 24 kHz


def _rms(b64: str) -> float:
    try:
        raw = base64.b64decode(b64)
    except (ValueError, TypeError):
        return 0.0
    samples = array.array("h")
    samples.frombytes(raw[: len(raw) // 2 * 2])
    if not samples:
        return 0.0
    # Subsample for speed: 1 in 4 samples is plenty for a speech/silence decision.
    sub = samples[::4]
    return math.sqrt(sum(s * s for s in sub) / len(sub))


def build_seed_input(config: UpstreamSessionConfig) -> list[dict]:
    """Conversation history for `session.start.input`; always ends with a user turn so the model speaks first."""
    items: list[dict] = []
    total = 0
    for m in reversed(config.history[-MAX_SEED_MESSAGES:]):
        text = (m.get("text") or "").strip()
        if not text:
            continue
        total += len(text)
        if total > MAX_SEED_CHARS:
            break
        if m.get("role") == "interviewer":
            items.append({"type": "message", "role": "assistant", "content": [{"type": "output_text", "text": text}]})
        else:
            items.append({"type": "message", "role": "user", "content": [{"type": "input_text", "text": text}]})
    items.reverse()
    if config.seed_prompt:
        opener = config.seed_prompt
    elif items:
        opener = "[The connection dropped briefly and is now restored.] I'm back — let's continue."
    else:
        opener = f"Hi, I'm {config.candidate_first_name}. I'm ready to start the interview."
    items.append({"type": "message", "role": "user", "content": [{"type": "input_text", "text": opener}]})
    return items


class GptLiveUpstream(RealtimeUpstream):
    def __init__(self, settings: Settings, config: UpstreamSessionConfig, *, safety_identifier: str,
                 output_idle_ms: int = 1400, input_idle_ms: int = 2500) -> None:
        self._settings = settings
        self._config = config
        self._safety_identifier = safety_identifier
        self._output_idle = output_idle_ms / 1000
        self._input_idle = input_idle_ms / 1000
        self._ws: Any = None
        self._events: asyncio.Queue[dict | None] = asyncio.Queue()
        self._send_lock = asyncio.Lock()
        self._tasks: list[asyncio.Task] = []
        self._closed = False
        self._counter = 0
        self._pending_notice: str | None = None
        self.usage_seconds: float = 0.0
        self.close_reason: str | None = None
        # Output is held until the relay has attached the app (first response.create), so the greeting isn't lost.
        self._gate_open = False
        self._held: list[dict] = []
        self._started_at = 0.0
        self._last_input = 0.0
        # assistant turn
        self._ai_response: str | None = None
        self._ai_item: str | None = None
        self._ai_text: list[str] = []
        self._ai_last = 0.0
        # candidate turn
        self._user_item: str | None = None
        self._user_text: list[str] = []
        self._user_last = 0.0

    # ------------------------------------------------------------------ RealtimeUpstream
    async def connect(self) -> None:
        from websockets.asyncio.client import connect
        from websockets.exceptions import InvalidStatus, WebSocketException

        assert self._settings.openai_api_key is not None
        headers = {
            "Authorization": f"Bearer {self._settings.openai_api_key.get_secret_value()}",
            "OpenAI-Safety-Identifier": self._safety_identifier,
        }
        # Transient network/handshake failures are retried a few times before giving up (the app keeps waiting).
        for attempt in range(1, CONNECT_ATTEMPTS + 1):
            try:
                self._ws = await connect(self._settings.openai_live_url, additional_headers=headers,
                                         max_size=16 * 1024 * 1024, open_timeout=CONNECT_TIMEOUT_S,
                                         ping_interval=20, ping_timeout=40)
                break
            except InvalidStatus as exc:
                status = exc.response.status_code
                retryable = status in (429, 500, 502, 503, 504)
                log.error("GPT-Live handshake rejected", extra={"ctx": {"status": status, "attempt": attempt}})
                if not retryable or attempt == CONNECT_ATTEMPTS:
                    raise UpstreamError("AI service rejected the connection", retryable=retryable,
                                        status=status) from exc
            except (OSError, TimeoutError, WebSocketException) as exc:
                log.error("GPT-Live connection failed",
                          extra={"ctx": {"error": type(exc).__name__, "attempt": attempt}})
                if attempt == CONNECT_ATTEMPTS:
                    raise UpstreamError("AI service is unreachable", retryable=True) from exc
            await asyncio.sleep(CONNECT_BACKOFF_S * attempt)

        model = self._config.model if (self._config.model or "").startswith("gpt-live") else self._settings.openai_live_model
        await self._send_raw({
            "type": "session.start",
            "session": {
                "model": model,
                "instructions": self._config.instructions,
                "audio": {
                    "format": {"type": "audio/pcm", "rate": 24000},
                    "output": {"voice": self._config.voice},
                },
                "delegation": {
                    "type": "responses",
                    "responses": {
                        "model": self._settings.openai_live_delegation_model,
                        "instructions": self._config.instructions,
                        "reasoning": {"effort": self._settings.openai_live_delegation_reasoning},
                    },
                },
                "input": build_seed_input(self._config),
            },
        })
        self._started_at = time.monotonic()
        self._tasks.append(asyncio.create_task(self._reader(), name="gpt-live-reader"))
        self._tasks.append(asyncio.create_task(self._watchdog(), name="gpt-live-watchdog"))
        self._tasks.append(asyncio.create_task(self._silence_pump(), name="gpt-live-silence"))

    async def recv(self) -> dict | None:
        return await self._events.get()

    async def send(self, event: dict) -> None:
        """Translates the relay's Realtime-style commands into GPT-Live commands."""
        etype = event.get("type")
        if etype == "input_audio_buffer.append":
            self._last_input = time.monotonic()
            await self._send_raw({"type": "session.input_audio.append", "audio": event.get("audio", "")})
        elif etype == "conversation.item.create":
            # System notices from the relay (time warning, time up, connection restored).
            text = " ".join(c.get("text", "") for c in (event.get("item") or {}).get("content", []) if c.get("text"))
            if text:
                self._pending_notice = text
                await self._append_instruction(text)
        elif etype == "response.create":
            # The app is attached: release the held greeting. The seeded user turn already made the model speak.
            self._open_gate()
            self._pending_notice = None
        elif etype == "response.cancel":
            if self._ai_response:
                self._finish_ai_turn("cancelled")
            await self._append_instruction(INTERRUPT_INSTRUCTION)
        elif etype == "conversation.item.truncate":
            self._emit({"type": "conversation.item.truncated", "item_id": event.get("item_id"),
                        "content_index": 0, "audio_end_ms": event.get("audio_end_ms")})
        # session.update / input_audio_buffer.clear have no GPT-Live equivalent (config is fixed at startup).

    async def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        if self._ws is not None:
            with contextlib.suppress(Exception):
                await self._send_raw({"type": "session.close"})
                await asyncio.wait_for(self._closed_seen(), timeout=3)
            with contextlib.suppress(Exception):
                await self._ws.close()
        for t in self._tasks:
            t.cancel()
        self._events.put_nowait(None)

    # ------------------------------------------------------------------ internals
    async def _closed_seen(self) -> None:
        while self.close_reason is None:
            await asyncio.sleep(0.05)

    async def _append_instruction(self, text: str) -> None:
        await self._send_raw({"type": "session.instructions.append", "content": text[:2000], "delegation_id": None})

    async def _send_raw(self, payload: dict) -> None:
        from websockets.exceptions import ConnectionClosed

        if self._ws is None:
            raise UpstreamError("not connected")
        try:
            async with self._send_lock:
                await self._ws.send(json.dumps(payload))
        except ConnectionClosed as exc:
            raise UpstreamError("AI connection closed", retryable=True) from exc

    def _emit(self, event: dict, *, always: bool = False) -> None:
        if self._gate_open or always:
            self._events.put_nowait(event)
        else:
            self._held.append(event)

    def _open_gate(self) -> None:
        if self._gate_open:
            return
        self._gate_open = True
        for ev in self._held:
            self._events.put_nowait(ev)
        self._held.clear()

    def _next_id(self, prefix: str) -> str:
        self._counter += 1
        return f"{prefix}_live_{self._counter}"

    async def _reader(self) -> None:
        from websockets.exceptions import ConnectionClosed

        try:
            async for raw in self._ws:
                try:
                    ev = json.loads(raw)
                except (TypeError, ValueError):
                    continue
                self._translate(ev)
        except ConnectionClosed as exc:
            if not self._closed:
                # No session.closed was received: record why the socket dropped (1011 = keepalive ping timeout,
                # 1006 = network drop without a close frame).
                rcvd, sent = exc.rcvd, exc.sent
                log.warning("GPT-Live socket dropped", extra={"ctx": {
                    "code": rcvd.code if rcvd else (sent.code if sent else None),
                    "reason": (rcvd.reason if rcvd else (sent.reason if sent else ""))[:200],
                    "by": "server" if rcvd else ("client" if sent else "network"),
                    "after_s": round(time.monotonic() - self._started_at, 1)}})
        except asyncio.CancelledError:
            raise
        finally:
            self._finish_ai_turn("incomplete")
            self._finish_user_turn()
            self._open_gate()
            if not self._closed:
                self._events.put_nowait(None)  # upstream lost

    def _translate(self, ev: dict) -> None:
        etype = ev.get("type", "")
        now = time.monotonic()
        if etype == "session.started":
            self._emit({"type": "session.updated", "session": ev.get("session", {})}, always=True)
        elif etype == "session.output_audio.delta":
            delta = ev.get("delta", "")
            voiced = _rms(delta) >= SPEECH_RMS
            if voiced:
                self._start_ai_turn()
                self._ai_last = now
            if self._ai_response is None:
                return  # background silence between turns: not forwarded to the app
            self._emit({"type": "response.output_audio.delta", "response_id": self._ai_response,
                        "item_id": self._ai_item, "output_index": 0, "content_index": 0, "delta": delta})
        elif etype == "session.output_transcript.delta":
            self._start_ai_turn()
            self._ai_last = now
            delta = ev.get("delta", "")
            self._ai_text.append(delta)
            self._emit({"type": "response.output_audio_transcript.delta", "response_id": self._ai_response,
                        "item_id": self._ai_item, "output_index": 0, "content_index": 0, "delta": delta})
        elif etype == "session.input_transcript.delta":
            delta = ev.get("delta", "")
            if not delta.strip() and self._user_item is None:
                return
            if self._user_item is None:
                # The candidate started talking; if the interviewer was speaking, GPT-Live yields the floor itself.
                # Close the assistant turn so the app stops any buffered playback.
                if self._ai_response:
                    self._finish_ai_turn("cancelled")
                self._user_item = self._next_id("item")
                self._user_text = []
                self._emit({"type": "input_audio_buffer.speech_started", "item_id": self._user_item,
                            "audio_start_ms": ev.get("start_ms")})
            self._user_last = now
            self._user_text.append(delta)
        elif etype == "session.usage.updated":
            self.usage_seconds = float((ev.get("usage") or {}).get("seconds") or 0.0)
        elif etype == "session.closed":
            self.close_reason = ev.get("reason")
            self.usage_seconds = float((ev.get("usage") or {}).get("seconds") or self.usage_seconds)
            log.info("GPT-Live session closed", extra={"ctx": {"reason": self.close_reason,
                                                                "seconds": self.usage_seconds}})
        elif etype == "error":
            err = ev.get("error") or {}
            log.warning("GPT-Live error", extra={"ctx": {"code": err.get("code"), "param": err.get("param")}})
            self._emit({"type": "error", "error": {"code": err.get("code"), "type": err.get("type"),
                                                   "message": err.get("message")}}, always=True)

    def _start_ai_turn(self) -> None:
        if self._ai_response is not None:
            return
        if self._user_item is not None:
            self._finish_user_turn()
        self._ai_response = self._next_id("resp")
        self._ai_item = self._next_id("item")
        self._ai_text = []
        self._emit({"type": "response.created", "response": {"id": self._ai_response, "status": "in_progress"}})

    def _finish_ai_turn(self, status: str) -> None:
        if self._ai_response is None:
            return
        rid, item = self._ai_response, self._ai_item
        self._ai_response = self._ai_item = None
        self._emit({"type": "response.output_audio.done", "response_id": rid, "item_id": item})
        self._emit({"type": "response.output_audio_transcript.done", "response_id": rid, "item_id": item,
                    "transcript": "".join(self._ai_text).strip()})
        self._emit({"type": "response.done", "response": {"id": rid, "status": status}})

    def _finish_user_turn(self) -> None:
        if self._user_item is None:
            return
        item = self._user_item
        self._user_item = None
        self._emit({"type": "input_audio_buffer.speech_stopped", "item_id": item})
        self._emit({"type": "conversation.item.input_audio_transcription.completed", "item_id": item,
                    "content_index": 0, "transcript": "".join(self._user_text).strip()})

    async def _silence_pump(self) -> None:
        """Feeds silence while the app sends no audio, so GPT-Live's audio-driven session clock keeps running."""
        with contextlib.suppress(UpstreamError):
            while not self._closed:
                await asyncio.sleep(SILENCE_FRAME_S / 2)
                if time.monotonic() - self._last_input >= SILENCE_FRAME_S * 1.2:
                    self._last_input = time.monotonic()
                    await self._send_raw({"type": "session.input_audio.append", "audio": SILENCE_B64})

    async def _watchdog(self) -> None:
        """GPT-Live has no turn-done events: close turns after a quiet gap; release held output on timeout."""
        while not self._closed:
            await asyncio.sleep(0.1)
            now = time.monotonic()
            if not self._gate_open and now - self._started_at > GATE_TIMEOUT_S:
                self._open_gate()
            if self._ai_response is not None and now - self._ai_last >= self._output_idle:
                self._finish_ai_turn("completed")
            if self._user_item is not None and now - self._user_last >= self._input_idle:
                self._finish_user_turn()
