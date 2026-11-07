# Paid services and cost categories

Prices change; verify on each vendor's pricing page before quoting a client. OpenAI prices below were checked on 2026-10-15.

## Required services

| Service | Purpose | Cost type |
|---|---|---|
| OpenAI API | realtime interviewer, transcription, report generation | usage-based (tokens / minutes) — dominant variable cost |
| Backend hosting | FastAPI relay (long-lived WebSockets: choose a host that supports them, e.g. a VM, container service, Fly.io, Render, Cloud Run with long request timeouts) | monthly; scales with concurrent interviews (~50 KB/s each way per interview) |
| MongoDB Atlas (or self-hosted) | data | free tier for development; dedicated tier for production backups/SLA |
| Domain + TLS | `https://api.yourdomain` | domain yearly; TLS free via Let's Encrypt / platform |
| Apple Developer Program | iOS distribution | annual fee |
| Google Play Console | Android distribution | one-time registration fee |
| Unity | engine | Unity Personal is free below Unity's revenue/funding threshold; above it Unity Pro is required — check current Unity terms |
| Avatar asset / artist | licensed rigged character + animations | one-off licence or commission |

## Optional services

Crash reporting (Firebase Crashlytics, Sentry), product analytics, transactional email (password reset — not yet implemented), a CDN for future downloadable avatars, and a uLipSync profile calibration session (free; MIT).

## OpenAI unit prices (per 1M tokens, checked 2026-10-15)

| Model | Use | Text in / cached / out | Audio in / cached / out |
|---|---|---|---|
| `gpt-realtime-2.1` | Premium interviewer | $4.00 / $0.40 / $24.00 | $32.00 / $0.40 / $64.00 |
| `gpt-realtime-2.1-mini` | Free-plan interviewer | $0.60 / $0.06 / $2.40 | $10.00 / $0.30 / $20.00 |
| `gpt-4o-mini-transcribe` | candidate transcript | ≈ $0.003 per audio minute | |
| `gpt-6.1-sol` | detailed reports | $2.00 in / $10.00 out | |
| `gpt-6-luna` | basic reports | $0.10 in / $0.50 out | |

## GPT-Live (default voice engine)

`gpt-live-1` is billed **per second of session audio** at the model's current rate. OpenAI's cost guide uses $0.05/minute as an *illustrative* example; check the model page for the live price. Usage is reported in `session.usage.updated` / `session.closed` (`usage.seconds`). At $0.05/min a 10-minute interview would be about $0.50 of voice time; transcripts come with the session, and report generation is billed separately (Responses API, see table above). The Realtime estimates below apply when `OPENAI_VOICE_ENGINE=realtime`.

OpenAI's guide also confirms the Realtime audio token rates used below: user audio = 1 token per 100 ms, assistant audio = 1 token per 50 ms.

## Per-interview estimate (Realtime engine, planning only)

Realtime cost is driven by **audio output** (the interviewer speaking) and by **re-processing the conversation context** on every response. Most of that context is cached, which is much cheaper.

Assumptions for a 10-minute interview: about 3 minutes of interviewer speech, 5 minutes of candidate speech, about 15 responses, and audio tokenisation of 10 tokens/s for input and 20 tokens/s for output (confirmed by OpenAI's cost guide). Turn counts and speaking time are assumptions; verify them with real usage data.

| Item | gpt-realtime-2.1 | gpt-realtime-2.1-mini |
|---|---|---|
| Output audio (~3,600 tokens) | ≈ $0.23 | ≈ $0.07 |
| New (uncached) input audio + text (~15–20k tokens) | ≈ $0.50–0.65 | ≈ $0.15–0.20 |
| Cached context (~60–80k tokens) | ≈ $0.03 | ≈ $0.02 |
| Transcription (5 min) | ≈ $0.015 | ≈ $0.015 |
| Report | ≈ $0.04 (sol, detailed) | ≈ $0.002 (luna, basic) |
| **Total (rough)** | **≈ $0.80–1.00** | **≈ $0.25–0.30** |

**Measure, don't guess:** the backend stores actual token usage per session (`interview_sessions.realtime_usage`, from `response.done` usage) and report usage (`interview_reports.token_usage`). Compute real cost per interview from these fields after your first test users, then tune the plan limits in `backend/app/modules/billing/plans.py`.

### What this means for pricing
- Free plan (3 × ≤ 10 min, mini model): worst case ≈ $1 per active free user per month.
- Premium (40 interviews, 600 min cap, full model): heavy users could cost ≈ $40–60 per month at the cap, so price Premium accordingly, or lower the cap, or use the mini model for longer sessions. Store fees (typically 15–30 %) come off revenue.
- Abuse controls already in place: per-user monthly interview and minute limits, one live interview per user, inbound audio rate cap, server-side duration enforcement, rate-limited signup and login. Recommended: email verification and device attestation (Play Integrity / App Attest) before a public free tier.
