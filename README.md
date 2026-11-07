# AI Interviewer

A mobile (Android/iOS) interview-practice app: candidates talk by voice with a 3D AI interviewer, then receive a structured feedback report and track their progress over time.

| Layer | Technology |
|---|---|
| Mobile app + 3D | Unity 6.3 LTS, URP, UI Toolkit, C# |
| Realtime voice | OpenAI Realtime API (`gpt-realtime-2.1`, `-mini` on the free plan) through a secure backend relay |
| Feedback reports | OpenAI Responses API with strict Structured Outputs (`gpt-6.1-sol` / `gpt-6-luna`) |
| Backend | FastAPI (Python 3.12), PyMongo async, JWT + Argon2id |
| Database | MongoDB 7+/8 (Atlas or self-hosted) |

> **3D avatar assets are not included in this repo.** The Microsoft Rocketbox models, textures and animations (~220 MB, MIT licensed) are excluded to keep the repository small. Download `Business_Female_01`, `Business_Male_01` and the listed animations from the [Microsoft Rocketbox Avatar Library](https://github.com/microsoft/Microsoft-Rocketbox) into `unity/AIInterviewer/Assets/ThirdParty/Rocketbox/` - see [`docs/AVATAR_ASSETS.md`](docs/AVATAR_ASSETS.md).

## Current status (honest)

| Area | Status |
|---|---|
| Backend API (auth, profiles, catalog, sessions, usage limits, reports, history, progress, resume, data deletion) | **Implemented and tested** — 85 automated tests, including a real MongoDB 8.0 integration run |
| Realtime relay (audio streaming, barge-in, reconnection, time limits, wrap-up) | **Implemented and tested.** Default voice engine GPT-Live (`gpt-live-1`) **verified with real sessions** (greeting, follow-ups, transcripts, report). Realtime API engine tested against a protocol-faithful fake server |
| Voice PoC client (`tools/voice_poc.py`) | **Working** against the local server (demo mode). Run it with a real key to validate OpenAI connectivity and latency |
| Unity core logic (state machine, protocol, audio buffers, resampling, echo gate, lip-sync analysis) | **Implemented and unit-tested** with .NET (29 tests, C# 9 to match Unity) |
| Unity app (screens, avatar, audio, networking, native secure storage) | **Implemented, not yet compiled in Unity** — Unity is not installed on the build machine. Verified by Roslyn syntax + semantic checks (0 issues outside Unity API surface). First open in Unity may need small fixes |
| 3D avatar | **Two realistic interviewers** (Alex, Maya) from Microsoft Rocketbox (MIT), auto-configured in Unity; placeholder fallback. Device check pending — see `docs/AVATAR_ASSETS.md` |
| Payments | **Not integrated** (abstraction + honest UI). See `docs/MONETIZATION.md` |

Full list: [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).

## Repository layout

```
backend/                FastAPI service (app/, tests/, Dockerfile, .env.example)
unity/AIInterviewer/    Unity project
  Assets/AIInterviewer/
    Core/               Engine-free logic (state machine, protocol, audio, lip-sync analysis)
    Runtime/            App, networking, audio, avatar, 3D room, UI Toolkit screens
    LipSync.ULipSync/   Optional uLipSync adapter (compiles only when the package is installed)
    Editor/             One-click project setup, avatar tools, build scripts
    Tests/              Unity EditMode/PlayMode tests
    Resources/          Theme (USS), app config
  Assets/Plugins/       Android Keystore (Java) and iOS Keychain (Obj-C++) plugins
  CoreTests~/           .NET unit tests for Core (ignored by Unity)
tools/voice_poc.py      Realtime voice proof-of-concept client
tools/blender/          Blender script for avatar asset preparation
docs/                   Architecture, API, data model, guides, QA, costs, limitations
```

## Quick start (backend in demo mode, ~5 minutes)

```bash
cd backend
python -m venv .venv
.venv/Scripts/pip install -r requirements-dev.txt     # macOS/Linux: .venv/bin/pip
cp .env.example .env                                  # leave OPENAI_API_KEY empty for demo mode
.venv/Scripts/python -m uvicorn app.asgi:app --reload --port 8000
# API docs: http://127.0.0.1:8000/docs
.venv/Scripts/python -m pytest -q                     # run the test suite
```

Try a full voice interview from the command line:

```bash
backend/.venv/Scripts/python tools/voice_poc.py --tone --turns 2 --interrupt-after 0.5
```

Set `OPENAI_API_KEY` in `backend/.env` and use `--wav your_answer.wav` for a real AI interview.

## Documentation

- [Beginner setup guide](docs/SETUP_BEGINNER.md) — start here
- [Architecture](docs/ARCHITECTURE.md) · [API & live protocol](docs/API.md) · [Data model](docs/DATA_MODEL.md)
- [Avatar asset requirements](docs/AVATAR_ASSETS.md) · [Lip-sync & audio](docs/LIPSYNC_AND_AUDIO.md)
- [Android build](docs/ANDROID_BUILD.md) · [iOS build](docs/IOS_BUILD.md)
- [Paid services & costs](docs/COSTS_AND_SERVICES.md) · [Monetization](docs/MONETIZATION.md)
- [QA checklist](docs/QA_CHECKLIST.md) · [Limitations](docs/LIMITATIONS.md) · [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
