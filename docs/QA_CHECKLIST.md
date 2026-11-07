# QA checklist

✅ = covered by automated tests in this repo. ☐ = manual / device test.

## Authentication & authorization
- ✅ Register validation (password strength, terms), duplicate email, login, identical errors for unknown email vs wrong password
- ✅ Missing, invalid, expired and forged access tokens rejected; revoked sessions (logout-all)
- ✅ Refresh rotation; reuse of an old refresh token revokes the family; logout revokes
- ✅ Login rate limiting with `Retry-After`
- ✅ Ownership: other users' interviews, reports and transcripts return 404; WebSocket returns 4404
- ☐ App: session restore after relaunch; sign-out clears the Keystore/Keychain token; expired refresh token → login screen

## Backend endpoints & data
- ✅ Catalog plan gating; interview creation validation; plan feature locks (402); monthly limits
- ✅ History pagination/limits, deletion, data deletion, account deletion
- ✅ Resume: plan gate, consent, type/size/magic-byte validation, DOCX zip guard
- ✅ Real MongoDB 8.0 integration: unique and TTL indexes, refresh flow, live interview, report, transcript purge
- ☐ MongoDB failure: stop the DB during use → API returns 500 with a generic error envelope, `/health/ready` degraded; restart recovers

## Live interview
- ✅ Session ready, greeting, turn-taking, user transcript, AI transcript
- ✅ Barge-in drops stale audio and accepts truncation; manual cancel; mute
- ✅ Protocol errors are non-fatal; clients cannot send `session.update`
- ✅ Reconnect within grace re-attaches; after grace, resume with transcript context; no double usage counting
- ✅ Rejections: unauthenticated (4401), wrong owner (4404), ended (4410), concurrent interview (4409), no minutes (4402)
- ✅ Time limit → wrap-up → completion → report
- ✅ OpenAI upstream: handshake headers, model per plan, session.update schema, rejected and unreachable handshakes
- ☐ Real OpenAI key: greeting latency, follow-up quality, semantic VAD pauses, transcription accuracy
- ☐ Microphone permission: first request, deny → permission screen → Settings → return grants and continues
- ☐ Network loss: airplane mode mid-answer → "Reconnecting…" → restore within 30 s continues the conversation
- ☐ App background/foreground mid-interview (both platforms)
- ☐ Session expiry: create an interview, wait over 30 min, start → "not started in time"
- ☐ End interview confirm; Android back button asks for confirmation

## Audio, avatar & lip-sync
- ✅ Core: resampler accuracy, ring buffer thread safety, playback→item mapping, echo gate, viseme analyser, smoothing (.NET)
- ☐ PlayMode tests in Unity: `RoomPlayModeTests`
- ☐ Lip-sync alignment ≤ 2 frames on device recordings; mouth returns to neutral at end/interruption
- ☐ Speakerphone: the interviewer does not interrupt itself; talking loudly over it interrupts
- ☐ Headphones/AirPods: full-duplex interruption; route changes mid-interview
- ☐ Missing avatar asset: choosing an uninstalled avatar falls back to the placeholder with a notice

## Reports
- ✅ Schema validation (ranges, assessed flags), server-side overall score, retry then failure, regenerate, insufficient data, demo labelling
- ✅ OpenAI Responses API request shape (strict JSON schema, `store=false`, safety identifier), error mapping
- ☐ Real model output quality review on 10+ transcripts (no claims about emotions or honesty, evidence quoted)

## Subscription & limits
- ✅ Free vs premium gating, monthly counters, dev grant disabled in production
- ☐ Paywall copy; "coming soon" dialog; test grant in development builds only

## Builds & performance (per release)
- ☐ Android AAB builds on CI; install on API 26 and the latest API
- ☐ iOS archive builds; TestFlight install on iOS 15 and the latest iOS
- ☐ Measure (record in a release sheet):

| Metric | Target | How |
|---|---|---|
| Cold start to splash | < 3 s mid-range Android | `adb shell am start -W`, Xcode Instruments |
| Interview room frame rate | 60 fps (30 on low-memory devices) | Unity Profiler |
| Memory during interview | < 400 MB | Profiler / Instruments |
| End-of-speech → first AI audio | < 1.2 s Wi-Fi | `tools/voice_poc.py`, device logs |
| Barge-in stop time | < 300 ms from speech start | screen recording |
| Reconnect after 10 s outage | resumes without losing context | manual |
| Battery, 20-min interview | < 8 % | OS battery stats |
