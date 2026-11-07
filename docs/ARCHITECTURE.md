# Architecture

## System overview

```
┌──────────────────────── Unity app (Android / iOS) ────────────────────────┐
│ UI Toolkit screens ── AppServices ── LiveInterviewController              │
│                                        │  ├─ InterviewStateMachine (Core) │
│ InterviewRoom (URP) ── AvatarController │  ├─ MicrophoneCapture → EchoGate │
│    └─ IAvatarRig + IAvatarLipSync ◄─────┼──┤  StreamingAudioPlayer         │
│                                        │  └─ LiveConnection (WebSocket)    │
│ AuthSession ── ApiClient (HTTPS) ── SecureStorage (Keystore / Keychain)   │
└──────────────┬────────────────────────────────┬───────────────────────────┘
               │ REST (JWT)                      │ WSS /v1/interviews/{id}/live
               ▼                                 ▼   PCM16 binary + JSON control
┌─────────────────────────────── FastAPI backend ───────────────────────────┐
│ auth · users · catalog · billing/usage · interviews · resumes · reports   │
│ realtime: LiveSessionRegistry → LiveSession → RealtimeUpstream            │
│ ReportScheduler → ReportGenerator (Responses API, strict JSON schema)     │
└──────────────┬────────────────────────────────┬───────────────────────────┘
               │ PyMongo async                   │ WSS (server API key)
               ▼                                 ▼
          MongoDB                       OpenAI Realtime API (+ Responses API)
```

## Voice engines

The relay supports two OpenAI voice APIs behind the same app protocol (`OPENAI_VOICE_ENGINE`):

| Engine | Model | Voices | Notes |
|---|---|---|---|
| `live` (default) | `gpt-live-1` | 19 natural voices (Cedar, Marin, Gleam, Meridian, Vesper, Ripple, Willow, Stone, + Portuguese/German/French/Hindi/Japanese/Korean) | full duplex; `GptLiveUpstream` translates GPT-Live events and infers turn boundaries |
| `realtime` | `gpt-realtime-2.1` / `-mini` | Cedar, Marin, Ash, Echo, Ballad, Verse, Coral, Sage, Shimmer | explicit turn events, server VAD, truncation |

The app shows only the voices of the active engine (`/v1/catalog` → `voices`, `voice_engine`), filtered by the interviewer's gender.

## Why the backend relays realtime audio

| Concern | Direct client → OpenAI (ephemeral key) | Backend relay (chosen) |
|---|---|---|
| Prompt/instruction integrity | Client can send `session.update` and repurpose the session | Instructions, model and voice are fixed server-side; only whitelisted client messages are forwarded |
| Duration / plan minutes | Client-enforced | Server-enforced (wrap-up + hard stop) |
| Transcript used for scoring | Client-supplied (forgeable) | Captured by the server from OpenAI events |
| Latency | Lowest (WebRTC) | +1 hop (~10–60 ms in-region) |
| Server bandwidth | None | ~48 KB/s per direction per interview |

`POST /v1/interviews/{id}/realtime-client-secret` exists (disabled by default) for a future WebRTC transport.

## A conversational turn

1. The microphone is captured at the device rate, resampled to 24 kHz and sent as 40 ms PCM16 frames (`MicrophoneCapture` → `EchoGate` → `LiveConnection.SendAudio`).
2. The relay forwards the audio as `input_audio_buffer.append`. OpenAI's `semantic_vad` (eagerness `low`, so thinking pauses are tolerated) detects end of speech and creates a response automatically.
3. The relay translates events: `user.speech_started/stopped`, `transcript.user`, `ai.audio.started` (+ binary audio), `transcript.ai.delta/done`, `ai.response.done`.
4. In the app, the receive thread hands audio to `StreamingAudioPlayer` (jitter buffer, resampled to the output rate). `OnAudioFilterRead` plays it, and the lip-sync analyser processes the exact samples sent to the speaker.
5. The state machine moves Listening → Processing → Speaking → Listening, and the avatar's mood follows.

## Barge-in (interruption)

```
candidate speaks while AI talks
  OpenAI: input_audio_buffer.speech_started ─► relay marks the active response "dropped"
                                               (late audio deltas are discarded) and forwards user.speech_started
  OpenAI auto-cancels the response (interrupt_response=true)
  App: StreamingAudioPlayer.Interrupt() → which item was heard and for how many ms (minus DSP latency)
  App → relay: playback.truncate {item_id, audio_end_ms} → OpenAI conversation.item.truncate
  Result: the model's memory matches what the candidate actually heard.
```

Tap-to-interrupt sends `response.cancel` + `playback.truncate`.

## Resilience

- **Short drop (≤ 30 s):** the `LiveSession` keeps the upstream open, cancels the in-flight response, then re-attaches when the client reconnects. The interviewer resumes with a brief "connection restored" acknowledgement.
- **Long drop / server restart:** the session is suspended (`interrupted`). On reconnect a new upstream is created with the transcript so far in its instructions.
- **Client:** exponential backoff with jitter (`ReconnectPolicy`). App background → disconnect cleanly; foreground → resume.
- **Upstream failure:** close code 4503; the client reconnects.
- **Report generation:** retried once on validation/transient errors; `failed` reports can be regenerated; stuck jobs are re-queued at startup.

## Security model

- Passwords: Argon2id. Access JWT (HS256, 15 min, includes `token_version` for global revocation). Refresh tokens are opaque, stored hashed, rotated on every use; reuse of a rotated token revokes the whole token family.
- Mobile token storage: refresh token in the Android Keystore (AES-GCM) or iOS Keychain (`AfterFirstUnlockThisDeviceOnly`). The access token stays in memory only.
- Ownership: every query includes `user_id`, so other users' resources return 404 (no enumeration).
- Rate limits: login (IP + email), registration, refresh, interview creation, live connections, report retries, uploads; inbound audio is capped at 1.5× real time.
- Uploads: magic-byte and extension agreement, size cap, DOCX zip-bomb guard, PDF page cap, text-only storage. Resume text is fenced in the prompt as untrusted data.
- Logging: JSON logs with a redaction filter for keys, tokens, JWTs and passwords. Transcripts are never logged.
- Production guard: refuses to start with the dev JWT secret, an in-memory DB, or `*` CORS. Docs/OpenAPI are disabled in production. Security headers (HSTS in production).
- OpenAI: server-side key only, `safety_identifier` = salted SHA-256 of the user id, `store=false` for report requests.

## Scaling notes

- The live relay is stateful per interview. Run several API instances behind a load balancer with **sticky sessions keyed on the interview id** for best reconnection. Without stickiness, interviews still resume (new upstream + transcript context).
- Rate limiting and report jobs are in-process. For multiple instances, move them to Redis and a durable job queue (interfaces are isolated in `core/rate_limit.py` and `reports/service.py`).
- MongoDB: all hot queries are indexed (see `DATA_MODEL.md`). TTL indexes expire refresh tokens and transcripts automatically.
