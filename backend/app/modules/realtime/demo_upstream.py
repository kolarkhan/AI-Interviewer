"""DEMO MODE upstream — used only when no OpenAI API key is configured.

This is NOT an AI. It is a scripted interviewer that:
- reads the seed questions in order (it does not understand the candidate's answers),
- detects when the candidate starts/stops speaking with a simple energy threshold,
- produces a *synthetic, speech-like tone* (not speech) so the avatar's lip-sync, animation
  states, interruption and the whole client pipeline can be demonstrated end to end.

Every interviewer line and every candidate "transcript" is explicitly labeled as demo output.
It emits the same event names as the OpenAI Realtime API so the relay code path is identical.
"""

from __future__ import annotations

import array
import asyncio
import base64
import math
import random

from app.modules.realtime.upstream import RealtimeUpstream, UpstreamSessionConfig

SAMPLE_RATE = 24_000
_CHUNK_SAMPLES = SAMPLE_RATE // 5  # 200 ms

DEMO_TRANSCRIPT_PREFIX = "[Demo mode — not transcribed]"


def _build_wavetable(size: int = 2048) -> list[float]:
    # Harmonic weights loosely resembling a voiced vowel spectrum.
    weights = [1.0, 0.65, 0.45, 0.30, 0.18, 0.10]
    norm = sum(weights)
    return [sum(w * math.sin(2 * math.pi * (h + 1) * i / size) for h, w in enumerate(weights)) / norm
            for i in range(size)]


_WAVETABLE = _build_wavetable()


def synthesize_babble(text: str, *, seed: int = 0, amplitude: float = 0.35) -> bytes:
    """Speech-like synthetic tone whose rhythm follows the words in `text` (PCM16 mono 24 kHz)."""
    rng = random.Random(seed)
    table, tsize = _WAVETABLE, len(_WAVETABLE)
    out = array.array("h")
    phase = 0.0
    for word in text.split():
        syllables = max(1, min(4, sum(1 for ch in word.lower() if ch in "aeiouy")))
        for _ in range(syllables):
            dur = rng.uniform(0.14, 0.24)
            n = int(dur * SAMPLE_RATE)
            f0_start, f0_end = rng.uniform(150, 190), rng.uniform(120, 170)
            peak = amplitude * rng.uniform(0.7, 1.0)
            for i in range(n):
                t = i / n
                f0 = f0_start + (f0_end - f0_start) * t
                phase = (phase + f0 * tsize / SAMPLE_RATE) % tsize
                env = math.sin(math.pi * t) ** 2
                out.append(int(32767 * peak * env * table[int(phase)]))
        gap = 0.32 if word[-1:] in ".?!," else rng.uniform(0.05, 0.11)
        out.extend([0] * int(gap * SAMPLE_RATE))
    return out.tobytes()


def _rms(pcm: bytes) -> float:
    samples = array.array("h")
    samples.frombytes(pcm[: len(pcm) - (len(pcm) % 2)])
    if not samples:
        return 0.0
    return math.sqrt(sum(s * s for s in samples) / len(samples))


class DemoUpstream(RealtimeUpstream):
    is_demo = True

    SPEECH_RMS_THRESHOLD = 900.0
    SPEECH_START_MS = 120
    SPEECH_END_SILENCE_MS = 900

    def __init__(self, config: UpstreamSessionConfig, *, pacing: float = 0.5) -> None:
        self._config = config
        self._pacing = pacing
        self._queue: asyncio.Queue[dict | None] = asyncio.Queue()
        self._response_task: asyncio.Task | None = None
        self._question_index = 0
        self._counter = 0
        self._closed = False
        self._closing_requested = False
        self._resumed = False
        # VAD state
        self._audio_ms = 0.0
        self._speaking = False
        self._speech_ms = 0.0
        self._silence_ms = 0.0
        self._speech_start_ms = 0.0
        self._input_item: str | None = None

    # -- RealtimeUpstream ------------------------------------------------------------
    async def connect(self) -> None:
        # Mirrors OpenAIRealtimeUpstream, which applies the session config on connect.
        self._emit({"type": "session.created", "session": {"id": "sess_demo", "model": "demo"}})
        self._emit({"type": "session.updated", "session": {"id": "sess_demo"}})

    async def recv(self) -> dict | None:
        return await self._queue.get()

    async def close(self) -> None:
        self._closed = True
        if self._response_task and not self._response_task.done():
            self._response_task.cancel()
        self._queue.put_nowait(None)

    async def send(self, event: dict) -> None:
        if self._closed:
            return
        etype = event.get("type")
        if etype == "session.update":
            self._emit({"type": "session.updated", "session": {"id": "sess_demo"}})
        elif etype == "response.create":
            self._start_response()
        elif etype == "response.cancel":
            await self._cancel_response()
        elif etype == "input_audio_buffer.append":
            await self._on_audio(base64.b64decode(event.get("audio", "")))
        elif etype == "input_audio_buffer.clear":
            self._speaking, self._speech_ms, self._silence_ms = False, 0.0, 0.0
            self._emit({"type": "input_audio_buffer.cleared"})
        elif etype == "conversation.item.truncate":
            self._emit({"type": "conversation.item.truncated", "item_id": event.get("item_id"),
                        "content_index": 0, "audio_end_ms": event.get("audio_end_ms")})
        elif etype == "conversation.item.create":
            text = " ".join(c.get("text", "") for c in event.get("item", {}).get("content", []))
            if "Time is up" in text:
                self._closing_requested = True
            if "connection has been restored" in text:
                self._resumed = True

    # -- internals --------------------------------------------------------------------
    def _emit(self, event: dict) -> None:
        if not self._closed:
            self._queue.put_nowait(event)

    def _next_id(self, prefix: str) -> str:
        self._counter += 1
        return f"{prefix}_demo_{self._counter}"

    def _next_line(self) -> str:
        qs = self._config.demo_questions
        if self._closing_requested:
            return ("We're out of time. Thank you for practicing with me today. "
                    "Your demo report will be ready shortly. Goodbye!")
        if self._resumed:
            self._resumed = False
            return "Sorry about that, we lost the connection for a moment. Let's continue where we left off."
        if self._question_index == 0:
            self._question_index = 1
            first = qs[0] if qs else "Tell me about yourself."
            return (f"Hi {self._config.candidate_first_name}, I'm {self._config.interviewer_name}. This is demo mode, so I'm "
                    f"not connected to the AI and can't understand your answers yet. Let's practice anyway. {first}")
        if self._question_index < len(qs):
            q = qs[self._question_index]
            self._question_index += 1
            return f"Thank you. Next question. {q}"
        return "Thanks, those are all the demo questions. You can end the interview whenever you're ready."

    def _start_response(self) -> None:
        if self._response_task and not self._response_task.done():
            return  # one response at a time, like the real API
        self._response_task = asyncio.create_task(self._run_response(self._next_line()))

    async def _cancel_response(self) -> None:
        task = self._response_task
        if task and not task.done():
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass

    async def _run_response(self, text: str) -> None:
        response_id, item_id = self._next_id("resp"), self._next_id("item")
        self._emit({"type": "response.created", "response": {"id": response_id, "status": "in_progress"}})
        self._emit({"type": "response.output_item.added", "response_id": response_id,
                    "item": {"id": item_id, "type": "message", "role": "assistant"}})
        words = text.split()
        chunk_bytes = _CHUNK_SAMPLES * 2
        sent_words = 0
        try:
            audio = await asyncio.to_thread(synthesize_babble, text, seed=self._counter)
            n_chunks = max(1, math.ceil(len(audio) / chunk_bytes))
            for i in range(n_chunks):
                upto = math.ceil(len(words) * (i + 1) / n_chunks)
                if upto > sent_words:
                    delta = " ".join(words[sent_words:upto]) + " "
                    sent_words = upto
                    self._emit({"type": "response.output_audio_transcript.delta", "response_id": response_id,
                                "item_id": item_id, "output_index": 0, "content_index": 0, "delta": delta})
                chunk = audio[i * chunk_bytes:(i + 1) * chunk_bytes]
                self._emit({"type": "response.output_audio.delta", "response_id": response_id, "item_id": item_id,
                            "output_index": 0, "content_index": 0, "delta": base64.b64encode(chunk).decode()})
                await asyncio.sleep(0.2 * self._pacing)
            self._emit({"type": "response.output_audio.done", "response_id": response_id, "item_id": item_id})
            self._emit({"type": "response.output_audio_transcript.done", "response_id": response_id,
                        "item_id": item_id, "transcript": text})
            self._emit({"type": "response.done", "response": {"id": response_id, "status": "completed",
                                                               "usage": {"total_tokens": 0}}})
        except asyncio.CancelledError:
            self._emit({"type": "response.done", "response": {"id": response_id, "status": "cancelled",
                                                               "status_details": {"type": "cancelled",
                                                                                  "reason": "turn_detected"}}})
            raise

    async def _on_audio(self, pcm: bytes) -> None:
        if not pcm:
            return
        ms = len(pcm) / 2 / SAMPLE_RATE * 1000
        loud = _rms(pcm) >= self.SPEECH_RMS_THRESHOLD
        self._audio_ms += ms
        if not self._speaking:
            self._speech_ms = self._speech_ms + ms if loud else 0.0
            if self._speech_ms >= self.SPEECH_START_MS:
                self._speaking, self._silence_ms = True, 0.0
                self._speech_start_ms = self._audio_ms - self._speech_ms
                self._input_item = self._next_id("item")
                self._emit({"type": "input_audio_buffer.speech_started",
                            "audio_start_ms": int(self._speech_start_ms), "item_id": self._input_item})
                await self._cancel_response()  # interrupt_response = true
            return
        self._silence_ms = 0.0 if loud else self._silence_ms + ms
        if self._silence_ms >= self.SPEECH_END_SILENCE_MS:
            self._speaking, self._speech_ms = False, 0.0
            spoken_s = (self._audio_ms - self._silence_ms - self._speech_start_ms) / 1000
            item = self._input_item
            self._emit({"type": "input_audio_buffer.speech_stopped", "audio_end_ms": int(self._audio_ms),
                        "item_id": item})
            self._emit({"type": "input_audio_buffer.committed", "item_id": item})
            self._emit({"type": "conversation.item.input_audio_transcription.completed", "item_id": item,
                        "content_index": 0,
                        "transcript": f"{DEMO_TRANSCRIPT_PREFIX} The candidate answered for about "
                                      f"{spoken_s:.1f} seconds."})
            self._start_response()  # create_response = true
