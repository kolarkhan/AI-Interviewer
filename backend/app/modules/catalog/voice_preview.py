"""Voice previews: a short spoken sample per interviewer voice, so users can pick a voice by ear.

Each sample is produced once with the configured voice engine (GPT-Live or Realtime), stored in MongoDB and
served from there afterwards, so previews cost a few seconds of voice usage per voice in total, not per user.
"""

from __future__ import annotations

import array
import asyncio
import base64
import contextlib
import io
import time
import wave

from bson import Binary

from app.core.container import Container
from app.core.errors import NotFound, ServiceUnavailable
from app.core.logging import get_logger
from app.core.utils import utcnow
from app.modules.catalog import data
from app.modules.realtime.upstream import UpstreamError, UpstreamSessionConfig

log = get_logger(__name__)

SAMPLE_RATE = 24_000
PREVIEW_VERSION = 1             # bump to regenerate every stored sample (e.g. after changing the line)
MAX_SECONDS = 10.0              # audio kept per sample (the line takes ~7 s)
ATTEMPT_TIMEOUT_S = 22.0        # connect + speak, per attempt
SPEECH_START_TIMEOUT_S = 9.0    # GPT-Live occasionally never takes the first turn: retry with a new session
ATTEMPTS = 2
_closing: set[asyncio.Task] = set()
_locks: dict[str, asyncio.Lock] = {}


def preview_line(gender: str | None) -> tuple[str, str]:
    """Interviewer name and the sentence the voice reads."""
    name = "Maya" if gender == "female" else "Alex"
    return name, (f"Hi, I'm {name}, and I'll be your interviewer today. Let's start with something simple: "
                  f"tell me a little about yourself.")


def _instructions(name: str, line: str) -> str:
    return (f"You are {name}, a friendly, professional job interviewer recording a short voice sample.\n"
            f"Say exactly the following sentence once, in English, in a warm and natural interview tone, "
            f"then stop. Do not add anything before or after it.\n\"{line}\"")


def to_wav(pcm16: bytes, rate: int = SAMPLE_RATE) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm16)
    return buf.getvalue()


def trim_silence(pcm16: bytes, threshold: int = 300, keep_ms: int = 250) -> bytes:
    """Drops leading/trailing near-silence (GPT-Live streams silent frames until a turn is closed)."""
    samples = array.array("h")
    samples.frombytes(pcm16)
    loud = [i for i in range(0, len(samples), 240) if max(abs(x) for x in samples[i:i + 240]) >= threshold]
    if not loud:
        return pcm16
    pad = SAMPLE_RATE * keep_ms // 1000
    start = max(0, loud[0] - pad)
    end = min(len(samples), loud[-1] + 240 + pad)
    return samples[start:end].tobytes()


def _key(engine: str, voice: str) -> str:
    return f"{engine}:{voice}:v{PREVIEW_VERSION}"


def cache_version(engine: str) -> str:
    """Changes whenever stored samples change, so apps can drop samples they saved on the device."""
    return f"{engine}-v{PREVIEW_VERSION}"


async def get_preview_wav(c: Container, voice_id: str, safety_identifier: str) -> bytes:
    engine = c.settings.openai_voice_engine
    voice = next((v for v in data.voices_for_engine(engine) if v["id"] == voice_id), None)
    if voice is None:
        raise NotFound("Unknown voice", code="voice_not_found")
    if c.settings.demo_mode:
        raise ServiceUnavailable("Voice previews need the AI service, which isn't configured (demo mode).",
                                 code="voice_preview_unavailable")
    return to_wav(await _ensure_pcm(c, voice, safety_identifier))


async def _ensure_pcm(c: Container, voice: dict, safety_identifier: str) -> bytes:
    """Stored sample for the voice, generating and storing it first if needed."""
    engine = c.settings.openai_voice_engine
    voice_id = voice["id"]
    key = _key(engine, voice_id)
    cached = await c.db.voice_previews.find_one({"_id": key})
    if cached:
        return bytes(cached["pcm"])
    lock = _locks.setdefault(key, asyncio.Lock())
    async with lock:  # concurrent first requests generate the sample once
        cached = await c.db.voice_previews.find_one({"_id": key})
        if cached:
            return bytes(cached["pcm"])
        pcm = await _generate(c, voice, safety_identifier)
        await c.db.voice_previews.replace_one(
            {"_id": key},
            {"_id": key, "engine": engine, "voice": voice_id, "version": PREVIEW_VERSION,
             "sample_rate": SAMPLE_RATE, "pcm": Binary(pcm), "created_at": utcnow()},
            upsert=True,
        )
        log.info("Voice preview generated", extra={"ctx": {"voice": voice_id, "engine": engine,
                                                            "seconds": round(len(pcm) / 2 / SAMPLE_RATE, 1)}})
        return pcm


async def warm_up(c: Container, safety_identifier: str) -> int:
    """Generates every missing sample, one voice at a time, so users never wait for the first play."""
    if c.settings.demo_mode or not c.settings.voice_preview_warmup:
        return 0
    engine = c.settings.openai_voice_engine
    voices = data.voices_for_engine(engine)
    stored = {d["_id"] async for d in c.db.voice_previews.find({}, {"_id": 1})}
    missing = [v for v in voices if _key(engine, v["id"]) not in stored]
    if not missing:
        return 0
    log.info("Voice preview warm-up started", extra={"ctx": {"missing": len(missing)}})
    made = 0
    for voice in missing:
        try:
            await _ensure_pcm(c, voice, safety_identifier)
            made += 1
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001 - one voice failing must not stop the others
            log.warning("Voice preview warm-up failed", extra={"ctx": {"voice": voice["id"], "error": str(exc)}})
    log.info("Voice preview warm-up finished", extra={"ctx": {"generated": made, "missing": len(missing)}})
    return made


def _seed_prompts(name: str, line: str) -> list[str]:
    """User turns that make the voice speak first; the second attempt words it differently."""
    return [f'Hello {name}! Please say your sample line now, exactly as written: "{line}"',
            "Hi, I'm ready. Please go ahead and say your line."]


async def _generate(c: Container, voice: dict, safety_identifier: str) -> bytes:
    name, line = preview_line(voice.get("gender"))
    seeds = _seed_prompts(name, line)
    for attempt in range(1, ATTEMPTS + 1):
        pcm = await _attempt(c, voice, safety_identifier, name, line, seeds[min(attempt, len(seeds)) - 1], attempt)
        if len(pcm) >= SAMPLE_RATE:  # at least half a second of audio
            return trim_silence(pcm)
    raise ServiceUnavailable("The voice preview couldn't be created right now. Please try again.",
                             code="voice_preview_unavailable")


async def _attempt(c: Container, voice: dict, safety_identifier: str, name: str, line: str, seed: str,
                   attempt: int) -> bytes:
    config = UpstreamSessionConfig(
        instructions=_instructions(name, line),
        voice=voice["id"],
        transcription_model=c.settings.openai_transcription_model,
        noise_reduction=None,
        interviewer_name=name,
        seed_prompt=seed,
    )
    assert c.upstream_factory is not None
    upstream = c.upstream_factory(config, safety_identifier)
    pcm = bytearray()
    max_bytes = int(MAX_SECONDS * SAMPLE_RATE) * 2

    async def next_event(timeout: float | None = None) -> dict | None:
        return await (asyncio.wait_for(upstream.recv(), timeout) if timeout else upstream.recv())

    async def run() -> None:
        await upstream.connect()
        while True:  # wait until the session config is applied
            ev = await next_event()
            if ev is None:
                raise UpstreamError("AI connection closed before the session was ready")
            if ev.get("type") == "session.updated":
                break
        await upstream.send({"type": "response.create"})
        asked = time.monotonic()
        while len(pcm) < max_bytes:
            # Until the voice starts, wait at most SPEECH_START_TIMEOUT_S; then until the turn ends.
            remaining = None if pcm else SPEECH_START_TIMEOUT_S - (time.monotonic() - asked)
            if remaining is not None and remaining <= 0:
                raise TimeoutError
            ev = await next_event(remaining)
            if ev is None:
                break
            etype = ev.get("type")
            if etype == "response.output_audio.delta":
                pcm.extend(base64.b64decode(ev.get("delta") or ""))
            elif etype == "response.done" and pcm:
                break
            elif etype == "error":
                log.warning("Voice preview upstream error", extra={"ctx": {"error": ev.get("error")}})

    started = time.monotonic()
    try:
        await asyncio.wait_for(run(), timeout=ATTEMPT_TIMEOUT_S)
    except TimeoutError:
        log.warning("Voice preview timed out", extra={"ctx": {
            "voice": voice["id"], "attempt": attempt, "spoke": bool(pcm),
            "after_s": round(time.monotonic() - started, 1)}})
    except UpstreamError as exc:
        log.warning("Voice preview failed", extra={"ctx": {"voice": voice["id"], "attempt": attempt,
                                                            "error": str(exc)}})
    finally:
        # Closing a GPT-Live session waits for `session.closed`; don't make the user wait for it.
        task = asyncio.create_task(_close_quietly(upstream))
        _closing.add(task)
        task.add_done_callback(_closing.discard)
    data_bytes = bytes(pcm[:max_bytes])
    return data_bytes[:len(data_bytes) - (len(data_bytes) % 2)]


async def _close_quietly(upstream) -> None:
    with contextlib.suppress(Exception):
        await upstream.close()
