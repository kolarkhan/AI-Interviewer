# API reference

- Interactive docs (non-production): `GET /docs` (Swagger) and `/redoc`. The machine-readable spec is in [`openapi.json`](openapi.json).
- Auth: `Authorization: Bearer <access_token>` on every `/v1` route except register, login, refresh and logout.
- Errors always use one envelope: `{"error": {"code": "plan_limit", "message": "…", "details": {…}}}`.

| Status | Typical codes |
|---|---|
| 401 | `token_missing`, `token_expired` (refresh then retry), `token_invalid`, `token_revoked`, `invalid_credentials`, `refresh_reused` |
| 402 | `plan_limit`, `plan_feature_locked` (`details.locked`, `details.upgrade_available`) |
| 404 | `not_found`, `interview_not_found`, `resume_missing` |
| 409 | `email_taken`, `resume_missing`, `report_not_failed` |
| 422 | `validation_error` (`details` lists fields), `invalid_resume`, `consent_required` |
| 429 | `rate_limited` (+ `Retry-After` header) |
| 501 | `billing_not_configured` |

## REST endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/v1/auth/register` | `{email, password (≥10, letters + number/symbol), display_name, accept_terms}` → `{user, tokens}` |
| POST | `/v1/auth/login` | `{email, password}` → `{user, tokens}` |
| POST | `/v1/auth/refresh` | `{refresh_token}` → new token pair (rotation) |
| POST | `/v1/auth/logout` | revoke one refresh token |
| POST | `/v1/auth/logout-all` | revoke all sessions (access tokens too) |
| GET/PATCH | `/v1/me` | profile; PATCH `{display_name?, onboarding_completed?, preferences?{voice, avatar_id, show_live_transcript, retain_transcripts, …}}` |
| GET | `/v1/me/export` | JSON export of all stored user data |
| DELETE | `/v1/me/data` | delete sessions, transcripts, reports and resume |
| POST | `/v1/me/delete-account` | `{password, confirm: true}` |
| GET | `/v1/catalog` | roles, types, difficulties, levels, durations, voices, avatars, `languages` — each with `locked` for the user's plan; `demo_mode` |
| GET | `/v1/catalog/voices/{voice_id}/preview` | short spoken sample of a voice (`audio/wav`, mono PCM16 24 kHz); generated once per voice with the voice engine and stored in `voice_previews`; 503 `voice_preview_unavailable` in demo mode |
| GET | `/v1/billing/plans` | plan definitions |
| GET | `/v1/billing/subscription` | `{subscription, plan, usage}` |
| GET | `/v1/billing/usage` | monthly usage counters |
| POST | `/v1/billing/verify-purchase` | **501** until store verification is implemented |
| POST | `/v1/billing/dev/grant-premium`, `/dev/reset-free` | development only (404 in production) |
| POST | `/v1/interviews` | create a session (validates plan; optional `language`, ISO 639-1, default `"en"`) → session + `live.websocket_path` |
| GET | `/v1/interviews?limit&offset` | history (free plan: latest 5) |
| GET/DELETE | `/v1/interviews/{id}` | details / delete with transcript and report |
| POST | `/v1/interviews/{id}/complete` | end outside the socket `{reason}` |
| GET | `/v1/interviews/{id}/transcript` | stored transcript (empty unless retention is on, or before the report) |
| GET | `/v1/interviews/{id}/report` | `{status: none|pending|generating|ready|insufficient_data|failed, report}` |
| POST | `/v1/interviews/{id}/report` | retry a failed report (202) |
| POST | `/v1/interviews/{id}/realtime-client-secret` | ephemeral OpenAI secret (disabled unless `ENABLE_DIRECT_REALTIME=true`) |
| GET | `/v1/progress` | summary, trend and per-category progress (trend/categories Premium) |
| POST | `/v1/resumes` | multipart `files` (one or more PDF/DOCX/TXT, ≤ 2 MB each; legacy single `file` accepted) + `consent=true` (Premium). Up to 5 documents per user; all-or-nothing validation → `{uploaded, documents, max_documents}` |
| GET | `/v1/resumes` | list stored documents |
| DELETE | `/v1/resumes/{id}` · `/v1/resumes` | delete one document · delete all |
| GET | `/v1/resumes/current` | first document (older clients) |
| GET | `/health`, `/health/ready` | liveness / readiness (DB ping, demo flag) |

## Live interview protocol (v1)

`GET wss://<host>/v1/interviews/{id}/live?audio_route=speaker|headset` with header `Authorization: Bearer <access_token>`.

**Audio:** binary frames, PCM16 little-endian, mono, 24 kHz, both directions. Client frames must be ≤ 64 KB and even-length; recommended 40 ms (1,920 bytes). Inbound audio above ~1.5× real time is dropped.

### Client → server (text frames)

| Message | Effect |
|---|---|
| `{"type":"input.mute","muted":true}` | stop forwarding mic audio (clears the server's input buffer) |
| `{"type":"response.cancel"}` | tap-to-interrupt the interviewer |
| `{"type":"playback.truncate","item_id":"…","audio_end_ms":1234}` | tell the model how much of an item was actually heard |
| `{"type":"interview.end"}` | end the interview (report is generated) |
| `{"type":"ping"}` | → `pong` |

Anything else gets a non-fatal `error` with `code: bad_message`. Clients cannot change instructions, model or voice.

### Server → client (text frames)

| Event | Fields |
|---|---|
| `session.ready` | `protocol_version, session_id, resumed, demo, interviewer_name, audio{format,sample_rate,channels}, elapsed_seconds, remaining_seconds, live_transcript` |
| `user.speech_started` / `user.speech_stopped` | `audio_start_ms` (started) |
| `transcript.user` | `item_id, text` (`failed: true` if transcription failed) |
| `ai.response.started` | `response_id` |
| `ai.audio.started` | `item_id, response_id` — binary audio for this item follows |
| `ai.audio.done` | `item_id` |
| `transcript.ai.delta` / `transcript.ai.done` | `item_id, delta` / `item_id, text, interrupted` |
| `ai.response.done` | `response_id, status` (`completed`, `cancelled`, `failed`, `incomplete`) |
| `interview.time` | `elapsed_seconds, remaining_seconds` (every 5 s) |
| `interview.wrapping_up` | `reason: time_limit` — the interviewer closes politely |
| `interview.completed` | `reason, report_status` — drain playback, then show completion |
| `error` | `code, message, fatal` |

### Close codes

| Code | Meaning | Client action |
|---|---|---|
| 1000 | normal (completed) | finish |
| 4401 | unauthorized | refresh token, reconnect once |
| 4402 | plan limit / no time left | show upgrade |
| 4404 | interview not found | leave |
| 4409 | another interview is attached | inform user |
| 4410 | interview already ended / expired | show completion or setup |
| 4411 | replaced by another connection | inform user |
| 4429 | too many connection attempts | back off |
| 4503 / 1011 / 1006 | AI or network failure | reconnect with backoff (session resumes) |
