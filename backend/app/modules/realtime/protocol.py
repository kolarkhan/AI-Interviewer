"""App <-> backend live interview protocol (v1).

Transport: one WebSocket per interview at `/v1/interviews/{id}/live`.

Audio (both directions): **binary** frames containing raw PCM16, mono, little-endian, 24 kHz.

Client -> server JSON (text frames, `type` field):
- `input.mute`        {"muted": bool}
- `response.cancel`   {}                                   manual interrupt (tap)
- `playback.truncate` {"item_id": str, "audio_end_ms": int}  how much of an AI item was actually heard
- `interview.end`     {}                                   user ended the interview
- `ping`              {}

Server -> client JSON:
- `session.ready`, `user.speech_started`, `user.speech_stopped`, `transcript.user`,
  `ai.response.started`, `ai.audio.started`, `ai.audio.done`, `transcript.ai.delta`, `transcript.ai.done`,
  `ai.response.done`, `interview.time`, `interview.wrapping_up`, `interview.completed`, `error`, `pong`.

See docs/API.md for full payloads. Close codes are listed in `CloseCode`.
"""

from __future__ import annotations

from enum import IntEnum
from typing import Any

PROTOCOL_VERSION = 1
SAMPLE_RATE = 24_000
BYTES_PER_SECOND = SAMPLE_RATE * 2  # PCM16 mono


class CloseCode(IntEnum):
    NORMAL = 1000
    INTERNAL_ERROR = 1011
    BAD_REQUEST = 4400
    UNAUTHORIZED = 4401
    PLAN_LIMIT = 4402
    FORBIDDEN = 4403
    NOT_FOUND = 4404
    CONFLICT = 4409
    SESSION_ENDED = 4410
    REPLACED = 4411
    RATE_LIMITED = 4429
    UPSTREAM_UNAVAILABLE = 4503


CLIENT_MESSAGE_TYPES = {"input.mute", "response.cancel", "playback.truncate", "interview.end", "ping"}


class ProtocolError(ValueError):
    pass


def validate_client_message(msg: Any) -> dict:
    if not isinstance(msg, dict):
        raise ProtocolError("message must be a JSON object")
    mtype = msg.get("type")
    if mtype not in CLIENT_MESSAGE_TYPES:
        raise ProtocolError(f"unsupported message type: {str(mtype)[:40]}")
    if mtype == "input.mute" and not isinstance(msg.get("muted"), bool):
        raise ProtocolError("input.mute requires boolean 'muted'")
    if mtype == "playback.truncate":
        item_id, end_ms = msg.get("item_id"), msg.get("audio_end_ms")
        if not isinstance(item_id, str) or not (0 < len(item_id) <= 100):
            raise ProtocolError("playback.truncate requires 'item_id'")
        if not isinstance(end_ms, int) or isinstance(end_ms, bool) or not (0 <= end_ms <= 3_600_000):
            raise ProtocolError("playback.truncate requires integer 'audio_end_ms'")
    return msg


def event(type_: str, **fields: Any) -> dict:
    return {"type": type_, **fields}
