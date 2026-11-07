# Beginner setup guide

This guide takes you from zero to the app running in the Unity Editor and on a phone. Allow 1–2 hours, most of it downloads.

## 1. Install tools

| Tool | Why | Notes |
|---|---|---|
| Python 3.12+ | backend | python.org; tick "Add to PATH" on Windows |
| Git | source control | git-scm.com |
| Unity Hub + **Unity 6.3 LTS** (6000.3.x) | mobile app | add modules **Android Build Support** (incl. OpenJDK + SDK/NDK) and, on a Mac, **iOS Build Support**. ~12–15 GB |
| Xcode (Mac only) | iOS builds | App Store |
| VS Code or JetBrains Rider | editing | optional |

## 2. Run the backend (demo mode)

```bash
cd backend
python -m venv .venv
# Windows:
.venv\Scripts\pip install -r requirements-dev.txt
# macOS/Linux:
.venv/bin/pip install -r requirements-dev.txt
cp .env.example .env            # Windows: copy .env.example .env
.venv/Scripts/python -m uvicorn app.asgi:app --reload --host 0.0.0.0 --port 8000
```

Open http://127.0.0.1:8000/docs. The log says `DEMO MODE` and `IN-MEMORY database` — expected for now (data resets when the server restarts).

Run the tests: `.venv/Scripts/python -m pytest -q`. The first run downloads a MongoDB binary for the integration tests. Set `AII_SKIP_MONGO_INTEGRATION=1` to skip them.

## 3. Add real services

### OpenAI (real interviewer)
1. Create an API key at platform.openai.com. Use a **project key** with a monthly budget limit.
2. Put it in `backend/.env`: `OPENAI_API_KEY=sk-...`. Never put it in the Unity project.
3. Restart the server. The log no longer says DEMO MODE.
4. Check end to end: record yourself answering "Tell me about yourself" as a WAV, then run
   `backend/.venv/Scripts/python tools/voice_poc.py --wav answer.wav --turns 1`.

### MongoDB (persistent data)
1. Create a free cluster at MongoDB Atlas (or run MongoDB 7+/8 locally).
2. Create a database user and allow your IP.
3. Set `DATABASE_URL=mongodb+srv://USER:PASSWORD@CLUSTER.mongodb.net/?retryWrites=true&w=majority` in `.env`.

### JWT secret
`python -c "import secrets; print(secrets.token_urlsafe(48))"` → `JWT_SECRET=` in `.env`.

## 4. Open the Unity project

1. Unity Hub → *Add project from disk* → select `unity/AIInterviewer`. Open with **6000.3.x**. If Hub asks to upgrade or switch versions, accept.
2. Wait for the first import (several minutes). Unity resolves the package versions in `Packages/manifest.json` to the ones bundled with your editor.
3. If the Console shows compile errors, read them top-down. The Unity code was written without an editor available, so a few API mismatches are possible and usually fixed in minutes. Please report them back.
4. Menu **AI Interviewer › Configure Project**. This creates the URP pipeline, base materials and Main scene, and applies player settings.
5. Open `Assets/AIInterviewer/Scenes/Main.unity` and press **Play**. Use the Game view in a portrait resolution (e.g. 1080×2400).

The app talks to `http://127.0.0.1:8000` (from `Resources/AppConfig.json`).

## 5. Run on a phone (development)

1. Find your computer's LAN IP (e.g. `192.168.1.20`). The phone must be on the same Wi-Fi.
2. Create `Assets/AIInterviewer/Resources/AppConfig.Local.json` (git-ignored):
   ```json
   { "apiBaseUrl": "http://192.168.1.20:8000" }
   ```
   Alternatively expose the backend over HTTPS with a tunnel (e.g. `cloudflared tunnel --url http://localhost:8000`) and use the https URL. This avoids cleartext restrictions.
3. Start the backend with `--host 0.0.0.0` and allow it through the firewall.
4. Follow [ANDROID_BUILD.md](ANDROID_BUILD.md) or [IOS_BUILD.md](IOS_BUILD.md) for a **Development Build**.

## 6. Useful developer shortcuts

- Profile → *Grant test Premium* (non-production builds only) unlocks all features without payments.
- Demo mode (no API key) is useful for UI and avatar work at zero AI cost.
- `GET /health/ready` shows DB status, demo mode and live session count.

## Troubleshooting

| Symptom | Fix |
|---|---|
| App shows "Can't reach AI Interviewer" | wrong `apiBaseUrl`, server not on `0.0.0.0`, firewall, phone on another network |
| Interviewer interrupts itself on speakerphone | enable "I'm using headphones" only with headphones; otherwise keep the echo gate on (see LIPSYNC_AND_AUDIO.md) |
| Audio plays through the iPhone earpiece | run *Configure Project* (Force iOS Speakers When Recording) |
| `401 token_expired` loops | device clock wrong; the app refreshes automatically, otherwise sign in again |
| Pink materials | URP not assigned — run *AI Interviewer › Setup › Render Pipeline* and *Base Materials* |
