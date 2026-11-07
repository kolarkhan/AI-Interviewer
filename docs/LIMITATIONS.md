# Known limitations and incomplete features

Status as of 2026-10-15. Nothing below is presented as complete in the app or the docs.

## Not verified on this build machine
1. **Unity project never opened in the Unity Editor.** Unity is not installed here. The C# was checked with Roslyn (C# 9 syntax, plus semantic checks of everything except Unity API calls), and the engine-free Core is unit-tested with .NET. Unity API usage (UI Toolkit, Painter2D, audio, PlayerSettings and URP editor APIs) was written from documentation knowledge and may need small fixes on first compile.
2. **No real OpenAI session has been run.** No API key was available. The OpenAI upstream follows the verified GA protocol and is tested against a local fake server. Run `tools/voice_poc.py --wav ...` with a key to confirm.
3. **No Android/iOS device builds** (no SDKs or Mac available). Native plugins (Android Keystore Java, iOS Keychain Obj-C++) are untested on device.
4. **Blender script** (`tools/blender/prepare_avatar.py`) is untested (Blender not installed).

## Product gaps
5. **Avatars:** two realistic Microsoft Rocketbox interviewers (Alex, Maya; MIT) are included and set up automatically in Unity. Their Humanoid mapping, materials and performance still need checking on device. A bespoke branded character would need an artist or a licensed asset (AVATAR_ASSETS.md).
6. **Echo cancellation:** Unity's microphone has no AEC. Speakerphone uses an energy echo gate (interrupting by voice requires speaking clearly louder) plus tap-to-interrupt. A native AEC capture plugin is the recommended next step.
7. **Payments:** Google Play Billing and Apple IAP are not integrated; `verify-purchase` returns 501. Only a development test grant exists.
8. **Document upload:** up to 5 PDF/DOCX/TXT documents (resume, cover letter, LinkedIn …) via yasirkula's Native File Picker (MIT, git package pinned in `Packages/manifest.json`); several files at once on Android/iOS, one per pick in the Editor; paste-text fallback. Not yet tried on a device. Scanned/image-only PDFs are rejected (no OCR).
9. **No password reset / email verification** (needs a transactional email provider). Account deletion and data export exist.
10. **GPT-Live engine (default):** verified end to end with real `gpt-live-1` sessions through the relay on 2026-10-18 (greeting, turn-taking, follow-ups, transcripts, report). GPT-Live has no turn/done events, so turns are inferred from voice energy and quiet gaps (interviewer 1.4 s, candidate 2.5 s); tap-to-interrupt is sent as an instruction. Tune the gaps after real user testing. Set `OPENAI_VOICE_ENGINE=realtime` to use the Realtime API instead.
11. **Direct WebRTC transport** is not implemented; the relay WebSocket is used. The ephemeral-key endpoint exists behind a flag.
11. **Lip-sync fidelity:** the built-in provider handles jaw and vowels but not consonants; uLipSync (optional) needs installation and profile calibration.
12. **Localization:** English only (interviewer can follow another language if the candidate uses it, UI is English).
13. **Analytics / crash reporting** are not integrated.

## Scaling
14. Live sessions, rate limits and report jobs are in-process. Multi-instance deployments need sticky routing by interview id plus Redis/durable queues (ARCHITECTURE.md).
15. The in-memory database (`memory://`) is for development only. The production guard refuses it.

## AI behaviour
16. Scores are AI practice estimates and can vary between runs. The UI and reports carry a disclaimer. The system never infers emotions, honesty or personality; "delivery patterns" are limited to observable answer features.
17. The demo-mode interviewer is scripted and does not understand answers. Demo reports contain sample content and are labelled as such everywhere.
