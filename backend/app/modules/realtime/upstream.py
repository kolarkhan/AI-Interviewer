"""Upstream realtime connections (OpenAI Realtime API over WebSocket, or the demo interviewer).

Verified against the OpenAI Realtime API reference (GA, 2026-10):
- URL `wss://api.openai.com/v1/realtime?model=<model>`, header `Authorization: Bearer <key>`.
- `session.update` with `session.type = "realtime"`, `audio.input.format = {"type":"audio/pcm","rate":24000}`,
  `audio.input.turn_detection` (semantic_vad), `audio.input.transcription.model`, `audio.output.voice`.
"""

from __future__ import annotations

import asyncio
import json
from abc import ABC, abstractmethod
from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any

from app.core.config import Settings
from app.core.logging import get_logger

log = get_logger(__name__)


class UpstreamError(Exception):
    """Raised when the upstream connection cannot be established or fails."""

    def __init__(self, message: str, *, retryable: bool = True, status: int | None = None) -> None:
        super().__init__(message)
        self.retryable = retryable
        self.status = status


@dataclass
class UpstreamSessionConfig:
    instructions: str
    voice: str
    transcription_model: str
    model: str | None = None  # realtime model; None = settings default
    noise_reduction: str | None = "far_field"  # "near_field" for headsets
    vad_eagerness: str = "low"  # interviews need room for thinking pauses
    # Demo upstream only: questions to script.
    demo_questions: list[str] = field(default_factory=list)
    candidate_first_name: str = "there"
    # Prior conversation ({"role": "interviewer"|"candidate", "text"}) used to resume a GPT-Live session.
    history: list[dict] = field(default_factory=list)
    interviewer_name: str = "Alex"
    language: str = "en"  # ISO 639-1 interview language
    # GPT-Live only: the user turn that makes the model speak first (default: the candidate says hello).
    seed_prompt: str | None = None

    def to_session_update(self) -> dict:
        audio_input: dict[str, Any] = {
            "format": {"type": "audio/pcm", "rate": 24000},
            "transcription": {"model": self.transcription_model, "language": self.language},
            "turn_detection": {
                "type": "semantic_vad",
                "eagerness": self.vad_eagerness,
                "create_response": True,
                "interrupt_response": True,
            },
        }
        audio_input["noise_reduction"] = {"type": self.noise_reduction} if self.noise_reduction else None
        return {
            "type": "session.update",
            "session": {
                "type": "realtime",
                "instructions": self.instructions,
                "output_modalities": ["audio"],
                "audio": {
                    "input": audio_input,
                    "output": {"format": {"type": "audio/pcm", "rate": 24000}, "voice": self.voice},
                },
            },
        }


class RealtimeUpstream(ABC):
    is_demo: bool = False

    @abstractmethod
    async def connect(self) -> None: ...

    @abstractmethod
    async def send(self, event: dict) -> None: ...

    @abstractmethod
    async def recv(self) -> dict | None:
        """Next upstream event, or None when the connection is closed."""

    @abstractmethod
    async def close(self) -> None: ...


class OpenAIRealtimeUpstream(RealtimeUpstream):
    def __init__(self, settings: Settings, config: UpstreamSessionConfig, *, safety_identifier: str) -> None:
        self._settings = settings
        self._config = config
        self._safety_identifier = safety_identifier
        self._ws: Any = None
        self._send_lock = asyncio.Lock()

    async def connect(self) -> None:
        from websockets.asyncio.client import connect
        from websockets.exceptions import InvalidStatus, WebSocketException

        assert self._settings.openai_api_key is not None
        model = self._config.model or self._settings.openai_realtime_model
        url = f"{self._settings.openai_realtime_url}?model={model}"
        headers = {
            "Authorization": f"Bearer {self._settings.openai_api_key.get_secret_value()}",
            "OpenAI-Safety-Identifier": self._safety_identifier,
        }
        try:
            self._ws = await connect(url, additional_headers=headers, max_size=16 * 1024 * 1024,
                                     open_timeout=10, ping_interval=20, ping_timeout=20)
        except InvalidStatus as exc:
            status = exc.response.status_code
            log.error("OpenAI realtime handshake rejected", extra={"ctx": {"status": status}})
            raise UpstreamError("AI service rejected the connection", retryable=status in (429, 500, 502, 503),
                                status=status) from exc
        except (OSError, TimeoutError, WebSocketException) as exc:
            log.error("OpenAI realtime connection failed", extra={"ctx": {"error": type(exc).__name__}})
            raise UpstreamError("AI service is unreachable", retryable=True) from exc
        await self.send(self._config.to_session_update())

    async def send(self, event: dict) -> None:
        from websockets.exceptions import ConnectionClosed

        if self._ws is None:
            raise UpstreamError("not connected")
        try:
            async with self._send_lock:
                await self._ws.send(json.dumps(event))
        except ConnectionClosed as exc:
            raise UpstreamError("AI connection closed", retryable=True) from exc

    async def recv(self) -> dict | None:
        from websockets.exceptions import ConnectionClosed

        if self._ws is None:
            return None
        try:
            raw = await self._ws.recv()
        except ConnectionClosed:
            return None
        try:
            return json.loads(raw)
        except (TypeError, ValueError):
            log.warning("Ignoring non-JSON upstream message")
            return {"type": "_ignored"}

    async def close(self) -> None:
        if self._ws is not None:
            try:
                await self._ws.close()
            except Exception:  # noqa: BLE001 - closing must not raise
                pass


UpstreamFactory = Callable[[UpstreamSessionConfig, str], RealtimeUpstream]


def default_upstream_factory(settings: Settings) -> UpstreamFactory:
    def factory(config: UpstreamSessionConfig, safety_identifier: str) -> RealtimeUpstream:
        if settings.demo_mode:
            from app.modules.realtime.demo_upstream import DemoUpstream
            return DemoUpstream(config)
        if settings.openai_voice_engine == "live":
            from app.modules.realtime.gpt_live_upstream import GptLiveUpstream
            return GptLiveUpstream(settings, config, safety_identifier=safety_identifier)
        return OpenAIRealtimeUpstream(settings, config, safety_identifier=safety_identifier)

    return factory
