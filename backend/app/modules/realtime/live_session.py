"""Bridges one app WebSocket with one upstream realtime connection for a single interview.

Responsibilities:
- forward microphone audio upstream (with mute and a bandwidth guard),
- translate upstream events into the small app protocol (see `protocol.py`),
- barge-in: drop stale audio of interrupted responses, forward client truncation,
- persist transcripts (subject to the user's retention preference),
- enforce the interview duration / plan minutes with a polite AI wrap-up,
- survive short client disconnects (reattach within a grace period).
"""

from __future__ import annotations

import asyncio
import base64
import contextlib
import json
import time
from typing import TYPE_CHECKING, Any

from starlette.websockets import WebSocket, WebSocketDisconnect, WebSocketState

from app.core.container import Container
from app.core.logging import get_logger
from app.modules.catalog.data import interviewer_name_for
from app.modules.interviews.prompts import RESUMED_NOTICE, TIME_UP_NOTICE, TIME_WARNING_NOTICE
from app.modules.interviews.service import InterviewService
from app.modules.realtime.protocol import (
    BYTES_PER_SECOND,
    PROTOCOL_VERSION,
    SAMPLE_RATE,
    CloseCode,
    ProtocolError,
    event,
    validate_client_message,
)
from app.modules.realtime.upstream import RealtimeUpstream, UpstreamError

if TYPE_CHECKING:
    from app.modules.realtime.registry import LiveSessionRegistry

log = get_logger(__name__)

_IGNORED_UPSTREAM_ERRORS = {"response_cancel_not_active"}
TIME_TICK_SECONDS = 5
FLUSH_SECONDS = 15
WARNING_BEFORE_END_SECONDS = 120


class AudioBudget:
    """Token bucket that limits inbound audio to ~1.5x real time (protects upstream spend)."""

    def __init__(self, rate_bytes: float = BYTES_PER_SECOND * 1.5, burst_bytes: float = BYTES_PER_SECOND * 3) -> None:
        self.rate, self.capacity, self.tokens = rate_bytes, burst_bytes, burst_bytes
        self.last = time.monotonic()

    def allow(self, n: int) -> bool:
        now = time.monotonic()
        self.tokens = min(self.capacity, self.tokens + (now - self.last) * self.rate)
        self.last = now
        if n <= self.tokens:
            self.tokens -= n
            return True
        return False


class LiveSession:
    def __init__(self, *, c: Container, session: dict, user: dict, upstream: RealtimeUpstream,
                 registry: LiveSessionRegistry, allowed_total_seconds: float, resumed_with_context: bool) -> None:
        self.c = c
        self.session = session
        self.session_id: str = session["_id"]
        self.user_id: str = session["user_id"]
        self.upstream = upstream
        self.registry = registry
        self.interviews = InterviewService(c)
        self.retain_transcripts = bool(user.get("preferences", {}).get("retain_transcripts"))

        self.allowed_total_seconds = allowed_total_seconds
        self._prior_elapsed = float(session.get("elapsed_seconds", 0))
        self._started = time.monotonic()
        self._flushed_elapsed = 0.0
        self._pending_usage: dict[str, int] = {}

        self._resumed_with_context = resumed_with_context
        self._opened = False
        self._ready = asyncio.Event()

        self._client: WebSocket | None = None
        self._send_lock = asyncio.Lock()
        self._muted = False
        self._budget = AudioBudget()

        self._active_response_id: str | None = None
        self._dropped_responses: set[str] = set()
        self._audio_items_started: set[str] = set()
        self._ai_text: dict[str, list[str]] = {}

        self._warned = False
        self._wrapping_up = False
        self._wrapup_deadline: float | None = None

        self.ended = False
        self.end_reason: str | None = None
        self._tasks: list[asyncio.Task] = []
        self._grace_task: asyncio.Task | None = None
        self._attached_event = asyncio.Event()

    # ------------------------------------------------------------------ lifecycle
    @property
    def attached(self) -> bool:
        return self._client is not None

    def elapsed_seconds(self) -> float:
        return self._prior_elapsed + (time.monotonic() - self._started)

    def remaining_seconds(self) -> float:
        return max(0.0, self.allowed_total_seconds - self.elapsed_seconds())

    async def start(self, ready_timeout: float = 15.0) -> None:
        """Connects upstream and waits until the session configuration is applied."""
        await self.upstream.connect()
        self._started = time.monotonic()
        self._tasks.append(asyncio.create_task(self._upstream_loop(), name=f"upstream:{self.session_id}"))
        self._tasks.append(asyncio.create_task(self._clock_loop(), name=f"clock:{self.session_id}"))
        try:
            await asyncio.wait_for(self._ready.wait(), timeout=ready_timeout)
        except TimeoutError as exc:
            await self._shutdown_tasks()
            await self.upstream.close()
            raise UpstreamError("AI session did not become ready in time", retryable=True) from exc

    async def attach(self, ws: WebSocket) -> None:
        """Runs the client side until the socket disconnects or the interview ends."""
        previous = self._client
        # Swap first so the previous socket's loop does not treat its closure as a lost client.
        self._client = ws
        if previous is not None:
            await self._close_ws(previous, CloseCode.REPLACED, "Connected from another device or tab")
        if self._grace_task:
            self._grace_task.cancel()
            self._grace_task = None
        reattach = self._opened
        self._attached_event.set()
        self._muted = False

        await self._send_json(event(
            "session.ready",
            protocol_version=PROTOCOL_VERSION,
            session_id=self.session_id,
            resumed=reattach or self._resumed_with_context,
            demo=self.upstream.is_demo,
            interviewer_name=interviewer_name_for(self.session["config"].get("avatar_id")),
            audio={"format": "pcm16", "sample_rate": SAMPLE_RATE, "channels": 1},
            elapsed_seconds=int(self.elapsed_seconds()),
            remaining_seconds=int(self.remaining_seconds()),
            live_transcript=bool(self.session["config"].get("live_transcript", True)),
        ))

        if not self._opened:
            self._opened = True
            if self._resumed_with_context:
                await self._system_notice(RESUMED_NOTICE, respond=True)
            else:
                await self._upstream_send({"type": "response.create"})
        else:
            await self._system_notice(RESUMED_NOTICE, respond=True)

        try:
            await self._client_loop(ws)
        finally:
            if self._client is ws:
                self._client = None
                self._attached_event.clear()
                if not self.ended:
                    await self._on_client_lost()

    async def end(self, reason: str) -> None:
        """Ends the interview (idempotent). Safe to call from any task, including our own."""
        if self.ended:
            return
        self.ended = True
        self.end_reason = reason
        await self._shutdown_tasks()
        await self.upstream.close()
        await self._flush(final=True)
        session = None
        try:
            session = await self.interviews.finish(self.session_id, reason=reason)
        except Exception:  # noqa: BLE001
            log.exception("Failed to finish session", extra={"ctx": {"session_id": self.session_id}})
        ws = self._client
        if ws is not None:
            await self._send_json(event("interview.completed", reason=reason,
                                        report_status=(session or {}).get("report_status")))
            await self._close_ws(ws, CloseCode.NORMAL, "Interview completed")
        self.registry.remove(self)
        log.info("Live session ended", extra={"ctx": {"session_id": self.session_id, "reason": reason}})

    async def suspend(self) -> None:
        """Closes the upstream without completing (resumable later with transcript context)."""
        if self.ended:
            return
        self.ended = True
        self.end_reason = "suspended"
        await self._shutdown_tasks()
        await self.upstream.close()
        await self._flush(final=True)
        await self.interviews.mark_interrupted(self.session_id)
        self.registry.remove(self)

    async def _shutdown_tasks(self) -> None:
        current = asyncio.current_task()
        tasks = [t for t in [*self._tasks, self._grace_task] if t and t is not current and not t.done()]
        for t in tasks:
            t.cancel()
        for t in tasks:
            with contextlib.suppress(asyncio.CancelledError, Exception):
                await t

    async def _on_client_lost(self) -> None:
        """Client disconnected without ending: stop the AI talking and wait for a reconnect."""
        if self._active_response_id:
            self._dropped_responses.add(self._active_response_id)
            await self._upstream_send({"type": "response.cancel"})
        await self._upstream_send({"type": "input_audio_buffer.clear"})
        self._grace_task = asyncio.create_task(self._grace_timer(), name=f"grace:{self.session_id}")

    async def _grace_timer(self) -> None:
        await asyncio.sleep(self.c.settings.live_reconnect_grace_seconds)
        if not self.attached and not self.ended:
            log.info("Reconnect grace expired; suspending", extra={"ctx": {"session_id": self.session_id}})
            await self.suspend()

    # ------------------------------------------------------------------ client side
    async def _client_loop(self, ws: WebSocket) -> None:
        while not self.ended:
            try:
                msg = await ws.receive()
            except (WebSocketDisconnect, RuntimeError):
                return
            mtype = msg.get("type")
            if mtype == "websocket.disconnect":
                return
            if msg.get("bytes") is not None:
                await self._on_client_audio(msg["bytes"])
            elif msg.get("text") is not None:
                await self._on_client_text(msg["text"])

    async def _on_client_audio(self, data: bytes) -> None:
        if self._muted or self._wrapping_up or not data:
            return
        if len(data) % 2 or len(data) > self.c.settings.max_client_audio_frame_bytes:
            await self._send_json(event("error", code="bad_audio_frame",
                                        message="Audio frames must be PCM16 and at most 64 KB", fatal=False))
            return
        if not self._budget.allow(len(data)):
            return  # silently drop: client is sending faster than real time
        await self._upstream_send({"type": "input_audio_buffer.append", "audio": base64.b64encode(data).decode()})

    async def _on_client_text(self, text: str) -> None:
        try:
            msg = validate_client_message(json.loads(text))
        except (ValueError, ProtocolError) as exc:
            await self._send_json(event("error", code="bad_message", message=str(exc)[:200], fatal=False))
            return
        mtype = msg["type"]
        if mtype == "ping":
            await self._send_json(event("pong"))
        elif mtype == "input.mute":
            self._muted = msg["muted"]
            if self._muted:
                await self._upstream_send({"type": "input_audio_buffer.clear"})
        elif mtype == "response.cancel":
            if self._active_response_id:
                self._dropped_responses.add(self._active_response_id)
                await self._upstream_send({"type": "response.cancel"})
        elif mtype == "playback.truncate":
            await self._upstream_send({"type": "conversation.item.truncate", "item_id": msg["item_id"],
                                       "content_index": 0, "audio_end_ms": msg["audio_end_ms"]})
        elif mtype == "interview.end":
            asyncio.create_task(self.end("user_ended"))

    # ------------------------------------------------------------------ upstream side
    async def _upstream_send(self, ev: dict) -> None:
        try:
            await self.upstream.send(ev)
        except UpstreamError:
            log.warning("Upstream send failed", extra={"ctx": {"session_id": self.session_id, "type": ev.get("type")}})

    async def _system_notice(self, text: str, *, respond: bool) -> None:
        await self._upstream_send({
            "type": "conversation.item.create",
            "item": {"type": "message", "role": "system", "content": [{"type": "input_text", "text": text}]},
        })
        if respond:
            await self._upstream_send({"type": "response.create"})

    async def _upstream_loop(self) -> None:
        try:
            while True:
                ev = await self.upstream.recv()
                if ev is None:
                    break
                try:
                    await self._handle_upstream(ev)
                except Exception:  # noqa: BLE001 - one bad event must not kill the session
                    log.exception("Error handling upstream event",
                                  extra={"ctx": {"session_id": self.session_id, "type": ev.get("type")}})
        except asyncio.CancelledError:
            raise
        if not self.ended:
            log.warning("Upstream closed unexpectedly", extra={"ctx": {"session_id": self.session_id}})
            ws = self._client
            if ws is not None:
                await self._send_json(event("error", code="upstream_lost", fatal=False,
                                            message="Connection to the AI interviewer was lost. Reconnecting…"))
            await self.suspend()
            if ws is not None:
                await self._close_ws(ws, CloseCode.UPSTREAM_UNAVAILABLE, "AI connection lost")

    async def _handle_upstream(self, ev: dict) -> None:
        etype = ev.get("type", "")
        if etype in ("session.updated",):
            self._ready.set()
        elif etype == "input_audio_buffer.speech_started":
            if self._active_response_id:
                # interrupt_response=true: the server cancels it; drop any late audio for it.
                self._dropped_responses.add(self._active_response_id)
            await self._send_json(event("user.speech_started", audio_start_ms=ev.get("audio_start_ms")))
        elif etype == "input_audio_buffer.speech_stopped":
            await self._send_json(event("user.speech_stopped"))
        elif etype == "conversation.item.input_audio_transcription.completed":
            text = (ev.get("transcript") or "").strip()
            await self.interviews.append_message(self.session, role="candidate", text=text,
                                                 item_id=ev.get("item_id"), retain=self.retain_transcripts)
            await self._send_json(event("transcript.user", item_id=ev.get("item_id"), text=text))
        elif etype == "conversation.item.input_audio_transcription.failed":
            await self._send_json(event("transcript.user", item_id=ev.get("item_id"), text=None, failed=True))
        elif etype == "response.created":
            rid = (ev.get("response") or {}).get("id")
            self._active_response_id = rid
            await self._send_json(event("ai.response.started", response_id=rid))
        elif etype == "response.output_audio.delta":
            rid, item_id = ev.get("response_id"), ev.get("item_id")
            if rid in self._dropped_responses:
                return
            if item_id not in self._audio_items_started:
                self._audio_items_started.add(item_id)
                await self._send_json(event("ai.audio.started", item_id=item_id, response_id=rid))
            try:
                pcm = base64.b64decode(ev.get("delta", ""))
            except (ValueError, TypeError):
                return
            await self._send_bytes(pcm)
        elif etype == "response.output_audio.done":
            if ev.get("response_id") not in self._dropped_responses:
                await self._send_json(event("ai.audio.done", item_id=ev.get("item_id")))
        elif etype == "response.output_audio_transcript.delta":
            item_id = ev.get("item_id")
            self._ai_text.setdefault(item_id, []).append(ev.get("delta", ""))
            if ev.get("response_id") not in self._dropped_responses:
                await self._send_json(event("transcript.ai.delta", item_id=item_id, delta=ev.get("delta", "")))
        elif etype == "response.output_audio_transcript.done":
            item_id = ev.get("item_id")
            text = ev.get("transcript") or "".join(self._ai_text.pop(item_id, []))
            self._ai_text.pop(item_id, None)
            interrupted = ev.get("response_id") in self._dropped_responses
            await self.interviews.append_message(self.session, role="interviewer", text=text, item_id=item_id,
                                                 retain=self.retain_transcripts)
            if interrupted:
                await self.interviews.mark_message_truncated(self.session_id, item_id)
            await self._send_json(event("transcript.ai.done", item_id=item_id, text=text, interrupted=interrupted))
        elif etype == "response.done":
            await self._on_response_done(ev.get("response") or {})
        elif etype == "conversation.item.truncated":
            await self.interviews.mark_message_truncated(self.session_id, ev.get("item_id", ""))
        elif etype == "error":
            err = ev.get("error") or {}
            if err.get("code") in _IGNORED_UPSTREAM_ERRORS:
                return
            log.warning("Upstream error event", extra={"ctx": {"session_id": self.session_id,
                                                                "code": err.get("code"), "type": err.get("type")}})
            await self._send_json(event("error", code="ai_error", fatal=False,
                                        message="The AI interviewer reported a problem. Please continue."))

    async def _on_response_done(self, response: dict) -> None:
        rid, status = response.get("id"), response.get("status")
        if rid == self._active_response_id:
            self._active_response_id = None
        usage = response.get("usage") or {}
        in_d, out_d = usage.get("input_token_details") or {}, usage.get("output_token_details") or {}
        for key, value in (("input_audio_tokens", in_d.get("audio_tokens")),
                           ("input_text_tokens", in_d.get("text_tokens")),
                           ("cached_tokens", in_d.get("cached_tokens")),
                           ("output_audio_tokens", out_d.get("audio_tokens")),
                           ("output_text_tokens", out_d.get("text_tokens"))):
            if isinstance(value, int) and value > 0:
                self._pending_usage[key] = self._pending_usage.get(key, 0) + value
        await self._send_json(event("ai.response.done", response_id=rid, status=status))
        self._dropped_responses.discard(rid)
        if self._wrapping_up and status in ("completed", "cancelled", "failed", "incomplete"):
            # Closing remarks delivered: finish. The client drains its playback buffer first.
            asyncio.create_task(self.end("time_limit"))

    # ------------------------------------------------------------------ timing
    async def _clock_loop(self) -> None:
        last_tick = last_flush = 0.0
        while not self.ended:
            await asyncio.sleep(1)
            now = time.monotonic()
            remaining = self.remaining_seconds()
            if now - last_tick >= TIME_TICK_SECONDS:
                last_tick = now
                await self._send_json(event("interview.time", elapsed_seconds=int(self.elapsed_seconds()),
                                            remaining_seconds=int(remaining)))
            if now - last_flush >= FLUSH_SECONDS:
                last_flush = now
                await self._flush()
            if (not self._warned and not self._wrapping_up and remaining <= WARNING_BEFORE_END_SECONDS
                    and self.allowed_total_seconds > WARNING_BEFORE_END_SECONDS + 60):
                self._warned = True
                await self._system_notice(TIME_WARNING_NOTICE, respond=False)
            if remaining <= 0 and not self._wrapping_up:
                await self._begin_wrapup()
            if self._wrapup_deadline is not None and now >= self._wrapup_deadline:
                asyncio.create_task(self.end("time_limit"))
                return

    async def _begin_wrapup(self) -> None:
        self._wrapping_up = True
        self._wrapup_deadline = time.monotonic() + self.c.settings.live_wrapup_grace_seconds
        await self._send_json(event("interview.wrapping_up", reason="time_limit"))
        if self._active_response_id:
            self._dropped_responses.add(self._active_response_id)
            await self._upstream_send({"type": "response.cancel"})
        await self._upstream_send({"type": "input_audio_buffer.clear"})
        await self._system_notice(TIME_UP_NOTICE, respond=True)

    async def _flush(self, final: bool = False) -> None:
        elapsed_now = time.monotonic() - self._started
        delta = elapsed_now - self._flushed_elapsed
        usage, self._pending_usage = self._pending_usage, {}
        if delta <= 0 and not usage:
            return
        self._flushed_elapsed = elapsed_now
        try:
            await self.interviews.add_elapsed(self.session_id, max(0.0, delta), usage)
        except Exception:  # noqa: BLE001
            log.exception("Failed to persist elapsed time", extra={"ctx": {"session_id": self.session_id}})

    # ------------------------------------------------------------------ socket helpers
    async def _send_json(self, payload: dict[str, Any]) -> None:
        ws = self._client
        if ws is None:
            return
        async with self._send_lock:
            with contextlib.suppress(Exception):
                if ws.application_state == WebSocketState.CONNECTED:
                    await ws.send_text(json.dumps(payload))

    async def _send_bytes(self, data: bytes) -> None:
        ws = self._client
        if ws is None or not data:
            return
        async with self._send_lock:
            with contextlib.suppress(Exception):
                if ws.application_state == WebSocketState.CONNECTED:
                    await ws.send_bytes(data)

    async def _close_ws(self, ws: WebSocket, code: int, reason: str) -> None:
        async with self._send_lock:
            with contextlib.suppress(Exception):
                if ws.application_state == WebSocketState.CONNECTED:
                    await ws.close(code=int(code), reason=reason)
