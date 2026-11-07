"""Voice proof-of-concept client for the AI Interviewer live relay.

Exercises exactly what the mobile app does — sign in, create an interview, open the live
WebSocket, stream candidate audio, play/save interviewer audio, barge in — and reports latency.

Examples (backend running at http://127.0.0.1:8000):

    # Demo mode (no OpenAI key on the server): uses a synthetic tone as the "candidate voice".
    python tools/voice_poc.py --tone --turns 2

    # Real AI: answer with your own recorded voice (any WAV; converted to 24 kHz mono PCM16).
    python tools/voice_poc.py --wav my_answer.wav --turns 2 --out interviewer.wav

    # Barge-in test: start speaking 1.5 s after the interviewer starts talking.
    python tools/voice_poc.py --wav my_answer.wav --interrupt-after 1.5

Requires: httpx, websockets (both in backend/requirements.txt). Run with the backend venv.
"""

from __future__ import annotations

import argparse
import asyncio
import array
import json
import math
import struct
import sys
import time
import uuid
import wave
from pathlib import Path

import httpx
from websockets.asyncio.client import connect
from websockets.exceptions import ConnectionClosed

RATE = 24_000
CHUNK_MS = 40


def load_wav_as_pcm24k(path: Path) -> bytes:
    """Reads a 16-bit PCM WAV and converts it to 24 kHz mono (linear-interpolation resampling)."""
    with wave.open(str(path), "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        frames = w.readframes(w.getnframes())
    if width != 2:
        raise SystemExit("Only 16-bit PCM WAV files are supported")
    samples = array.array("h")
    samples.frombytes(frames)
    if sys.byteorder == "big":
        samples.byteswap()
    if channels > 1:
        samples = array.array("h", (sum(samples[i:i + channels]) // channels
                                    for i in range(0, len(samples), channels)))
    if rate != RATE and samples:
        ratio = rate / RATE
        n_out = int(len(samples) / ratio)
        out = array.array("h", bytes(2 * n_out))
        for i in range(n_out):
            pos = i * ratio
            j = int(pos)
            frac = pos - j
            nxt = samples[j + 1] if j + 1 < len(samples) else samples[j]
            out[i] = int(samples[j] * (1 - frac) + nxt * frac)
        samples = out
    return samples.tobytes()


def tone_answer(seconds: float = 1.5) -> bytes:
    n = int(RATE * seconds)
    return b"".join(struct.pack("<h", int(8000 * math.sin(2 * math.pi * 220 * i / RATE)
                                          * (0.6 + 0.4 * math.sin(2 * math.pi * 4 * i / RATE)))) for i in range(n))


def silence(seconds: float) -> bytes:
    return b"\x00\x00" * int(RATE * seconds)


async def stream_audio(ws, pcm: bytes) -> None:
    """Streams in real time like a microphone would."""
    step = RATE * CHUNK_MS // 1000 * 2
    for i in range(0, len(pcm), step):
        await ws.send(pcm[i:i + step])
        await asyncio.sleep(CHUNK_MS / 1000)


async def get_token(http: httpx.AsyncClient, email: str, password: str) -> str:
    r = await http.post("/v1/auth/login", json={"email": email, "password": password})
    if r.status_code == 401:
        r = await http.post("/v1/auth/register", json={"email": email, "password": password,
                                                       "display_name": "Voice PoC", "accept_terms": True})
    r.raise_for_status()
    return r.json()["tokens"]["access_token"]


async def run(args: argparse.Namespace) -> int:
    answer = tone_answer() if args.tone else load_wav_as_pcm24k(Path(args.wav))
    ws_base = args.server.replace("http://", "ws://").replace("https://", "wss://")
    async with httpx.AsyncClient(base_url=args.server, timeout=30) as http:
        token = await get_token(http, args.email, args.password)
        headers = {"Authorization": f"Bearer {token}"}
        r = await http.post("/v1/interviews", headers=headers, json={
            "role_id": args.role, "interview_type": args.type, "difficulty": "beginner",
            "experience_level": "junior", "duration_minutes": 5})
        if r.status_code != 201:
            print("Could not create interview:", r.status_code, r.text)
            return 1
        sid = r.json()["id"]
        print(f"Interview {sid} created (demo={r.json()['is_demo']})")

        ai_audio = bytearray()
        metrics: dict[str, list[float]] = {"response_latency_ms": []}
        speech_stopped_at: float | None = None
        ai_speaking = asyncio.Event()
        turn_done = asyncio.Event()
        completed = asyncio.Event()

        async with connect(f"{ws_base}/v1/interviews/{sid}/live", additional_headers=headers,
                           max_size=None) as ws:
            async def reader() -> None:
                nonlocal speech_stopped_at
                try:
                    async for msg in ws:
                        if isinstance(msg, bytes):
                            ai_audio.extend(msg)
                            continue
                        ev = json.loads(msg)
                        t = ev["type"]
                        if t == "ai.audio.started":
                            ai_speaking.set()
                            if speech_stopped_at is not None:
                                metrics["response_latency_ms"].append((time.monotonic() - speech_stopped_at) * 1000)
                                speech_stopped_at = None
                        elif t == "user.speech_stopped":
                            speech_stopped_at = time.monotonic()
                        elif t == "ai.response.done":
                            ai_speaking.clear()
                            if ev.get("status") == "completed":  # cancelled = barge-in, turn continues
                                turn_done.set()
                        if t in ("transcript.ai.done", "transcript.user"):
                            who = "INTERVIEWER" if t == "transcript.ai.done" else "YOU"
                            print(f"  {who}: {ev.get('text')}")
                        elif t not in ("transcript.ai.delta", "interview.time", "pong"):
                            print(f"  <- {t} {({k: v for k, v in ev.items() if k != 'type'})}")
                        if t == "interview.completed":
                            completed.set()
                except ConnectionClosed as exc:
                    print(f"  socket closed: {exc.rcvd.code if exc.rcvd else '?'}")
                    completed.set()

            reader_task = asyncio.create_task(reader())
            await asyncio.wait_for(turn_done.wait(), 60)  # interviewer's opening

            for turn in range(args.turns):
                turn_done.clear()
                print(f"-- candidate turn {turn + 1}: streaming {len(answer) / 2 / RATE:.1f}s of audio")
                await stream_audio(ws, answer + silence(1.5))
                if args.interrupt_after is not None and turn == 0:
                    await asyncio.wait_for(ai_speaking.wait(), 30)
                    await asyncio.sleep(args.interrupt_after)
                    print(f"-- barging in after {args.interrupt_after}s of interviewer speech")
                    await stream_audio(ws, answer + silence(1.5))
                await asyncio.wait_for(turn_done.wait(), 60)
                await asyncio.sleep(0.5)

            await ws.send(json.dumps({"type": "interview.end"}))
            await asyncio.wait_for(completed.wait(), 30)
            reader_task.cancel()

        if args.out:
            with wave.open(args.out, "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(RATE)
                w.writeframes(bytes(ai_audio))
            print(f"Saved {len(ai_audio) / 2 / RATE:.1f}s of interviewer audio to {args.out}")

        lat = metrics["response_latency_ms"]
        if lat:
            print(f"Response latency (speech_stopped -> first AI audio): "
                  f"avg {sum(lat) / len(lat):.0f} ms, max {max(lat):.0f} ms over {len(lat)} turns")

        for _ in range(60):
            rep = (await http.get(f"/v1/interviews/{sid}/report", headers=headers)).json()
            if rep["status"] not in ("pending", "generating"):
                break
            await asyncio.sleep(1)
        print(f"Report status: {rep['status']}")
        if rep.get("report"):
            print(f"Overall score: {rep['report']['overall_score']} (demo={rep['report']['is_demo']})")
            print(f"Summary: {rep['report']['summary']}")
    return 0


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--server", default="http://127.0.0.1:8000")
    p.add_argument("--email", default=f"poc-{uuid.uuid4().hex[:8]}@example.com")
    p.add_argument("--password", default="poc-password-123")
    src = p.add_mutually_exclusive_group(required=True)
    src.add_argument("--wav", help="WAV file with a spoken answer")
    src.add_argument("--tone", action="store_true", help="synthetic tone (demo mode only)")
    p.add_argument("--turns", type=int, default=2)
    p.add_argument("--interrupt-after", type=float, default=None)
    p.add_argument("--role", default="software_engineer")
    p.add_argument("--type", default="technical")
    p.add_argument("--out", default="interviewer_audio.wav")
    sys.exit(asyncio.run(run(p.parse_args())))


if __name__ == "__main__":
    main()
