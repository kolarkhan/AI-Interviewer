# AI Interviewer — Implementation Plan

_Last updated: 2026-10-15_

## Phase 1 audit (result)

| Item | Status on the build machine |
|---|---|
| Existing code | **None** — repository was empty. Nothing to preserve. |
| Python 3.12 / pip | Installed. Backend uses an isolated venv at `backend/.venv`. |
| Node 25 / npm | Installed (not required by the current architecture). |
| git | Installed. |
| Unity Hub / Unity 6.3 LTS | **Missing.** Unity project source is authored by hand; it must be opened and verified in the editor. |
| Android SDK / NDK / JDK | **Missing.** Installed via Unity Hub "Android Build Support" module. |
| Xcode / macOS | **Not available** (Windows machine). iOS builds require a Mac. |
| MongoDB / Docker | **Missing.** Tests use `mongomock-motor`; dev server can run with an in-memory DB (`DATABASE_URL=memory://`). Production: MongoDB Atlas or self-hosted MongoDB 7+. |
| Blender | **Missing.** No avatar can be generated or rigged on this machine. |
| OpenAI API key | **Not configured.** Real voice cannot be exercised here; demo mode is used and clearly labeled. |
| Disk | ~22 GB free on D: — enough for Unity + Android module (~12–15 GB) but tight. |

## Verified external facts (checked 2026-10-15 against developers.openai.com)

- Realtime models: `gpt-realtime-2.1` (default), `gpt-realtime-2.1-mini`. `gpt-realtime-mini` is deprecated.
- WebSocket: `wss://api.openai.com/v1/realtime?model=<model>`, `Authorization: Bearer <key>`.
- Ephemeral client secrets: `POST /v1/realtime/client_secrets` (`expires_after.seconds` 10–7200, default 600). Response: `value`, `expires_at`, `session`.
- GA event names: `response.output_audio.delta`, `response.output_audio_transcript.delta/done`, `conversation.item.input_audio_transcription.completed`, `input_audio_buffer.speech_started/stopped`, `response.done` (`status`: completed/cancelled/failed/incomplete).
- Audio: `{"type":"audio/pcm","rate":24000}` (PCM16 mono LE, only 24 kHz).
- Turn detection: `semantic_vad` (eagerness low/medium/high/auto) or `server_vad`; `create_response`, `interrupt_response`.
- Over WebSocket **the client owns playback** and must call `conversation.item.truncate` (item_id, content_index 0, audio_end_ms) on interruption.
- OpenAI recommends WebRTC for browser/mobile clients connecting **directly**. We do not connect directly (see below).
- Structured output: Python SDK `client.responses.parse(model=..., input=..., text_format=PydanticModel)` → `response.output_parsed`.
- Motor is deprecated → backend uses PyMongo's native async client (`AsyncMongoClient`).
- Unity: 6.3 LTS is current (6.0 LTS support ends Oct 2026). Lip-sync: `uLipSync` (MIT, MFCC-based, analyses AudioSource via `OnAudioFilterRead`), UPM git URL `https://github.com/hecomi/uLipSync.git#upm`.

## Key architecture decision: backend realtime relay

```
Unity app ──(WSS, JWT, PCM16 binary frames + JSON control)──▶ FastAPI relay ──(WSS, server API key)──▶ OpenAI Realtime
```

Why a relay instead of the app talking to OpenAI directly:

1. **Prompt integrity / abuse prevention** — instructions, model, voice and tools are set server-side; the client can only send audio and a small set of whitelisted control messages. A direct ephemeral key would allow the client to `session.update` the instructions and use our account as a general voice assistant.
2. **Hard server-side limits** — interview duration, plan minutes and concurrency are enforced where the client can't tamper with them.
3. **Transcript for reports** — the server receives transcripts directly; the client cannot inject a fake transcript to get a better score.
4. **Testability** — the whole protocol is covered by automated tests with a fake upstream.

Trade-off: one extra network hop (~10–60 ms) and server bandwidth (~50 KB/s per direction per interview). `POST /v1/interviews/{id}/realtime-client-secret` is still implemented (behind `ENABLE_DIRECT_REALTIME`) so a lower-latency WebRTC transport can be added later.

## Phases and status

| Phase | Scope | Status |
|---|---|---|
| 1. Audit | tools, prerequisites, verified API facts, plan | **Done** |
| 2. Voice proof of concept | FastAPI backend, secure relay, demo upstream, PoC client, interruption, tests | **Done** (verified end to end in demo mode; real OpenAI run pending an API key) |
| 3. 3D avatar | `IAvatarLipSync` + built-in spectral provider + uLipSync adapter, rigs, procedural behaviour, animator template, validator | **Code done**; placeholder avatar only; needs Unity compile + device test |
| 4. Mobile app | all 14 screen groups, navigation, API client, secure storage, permissions, offline/empty/error states | **Code done**; needs Unity compile + device test |
| 5. Reports | strict structured outputs, validation, retry, history, progress | **Done** (backend tested; UI needs Unity) |
| 6. Release readiness | tests (85 backend incl. real MongoDB, 29 .NET Core, Unity Edit/PlayMode), build scripts, guides, costs, limitations | **Docs and scripts done**; store billing and device builds remain |

## Next steps (recommended order)
1. Open the Unity project in 6000.3 LTS, fix any first-compile issues, run *Configure Project*, then the EditMode and PlayMode tests.
2. Set `OPENAI_API_KEY`, run `tools/voice_poc.py --wav …`, and record latency and cost per interview from `realtime_usage`.
3. Android development build on a mid-range device; then iOS via TestFlight.
4. License and import the production avatar, then calibrate uLipSync.
5. Native AEC capture plugin; Play Billing / StoreKit with server verification; email verification and password reset.
