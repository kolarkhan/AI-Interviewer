# iOS build and release

## Prerequisites
- A Mac with Xcode (current version), Unity 6.3 LTS with **iOS Build Support**.
- Apple Developer Program membership (annual fee) for device testing beyond 7-day personal provisioning and for TestFlight/App Store.
- *AI Interviewer › Configure Project* has been run. It sets the microphone usage description, iOS 15+ target, portrait, and *Force iOS Speakers when Recording*.

## Settings to review (Project Settings › Player › iOS)
| Setting | Value |
|---|---|
| Bundle Identifier | your own id, matching the App ID in the Apple Developer portal |
| Signing Team ID | your team (or set in Xcode) |
| Target minimum iOS version | 15.0 |
| Microphone Usage Description | pre-filled; must explain the voice interview (App Review checks it) |
| Force iOS Speakers when Recording | **enabled**, otherwise audio plays through the earpiece |
| Allow downloads over HTTP | Development builds only |

## Build
1. File › Build Profiles › iOS → *Build* (or *AI Interviewer › Build › iOS Xcode Project*, or CLI:
   `Unity -batchmode -quit -projectPath unity/AIInterviewer -executeMethod AIInterviewer.EditorTools.BuildScripts.BuildIOS`).
2. `IosBuildPostprocessor` adds `Security.framework` (Keychain plugin) and `ITSAppUsesNonExemptEncryption = NO` (the app uses standard HTTPS/TLS only).
3. Open `Builds/iOS/Unity-iPhone.xcodeproj` in Xcode → select the team under *Signing & Capabilities* → run on a connected iPhone.
4. Logs: Xcode console (filter `[Interview]`).

## TestFlight / App Store
1. App Store Connect → New App (same bundle id).
2. Xcode → *Product › Archive* → Distribute → App Store Connect → upload.
3. TestFlight: add internal testers and test the full interview flow on several devices, with headphones and on speakerphone.
4. App Review notes:
   - Provide a demo account (email/password) whose backend has an AI key, or explain demo mode.
   - Explain that microphone audio is streamed to the AI service during the interview and not stored.
   - Account deletion is available in-app (Profile › Delete account), as Apple requires for apps with account creation.
5. Privacy "nutrition label": declare audio data (not linked to tracking, not stored), contact info (email), user content (transcripts/reports, optional retention), and usage data if you add analytics.
6. If you add third-party sign-in (Google, etc.), Sign in with Apple becomes mandatory. The current email/password flow does not require it.

## Performance checks
- Xcode Instruments: *Time Profiler*, *Allocations*, *Energy Log* during a 20-minute interview.
- Unity Profiler over Wi-Fi for frame time; target 60 fps on iPhone 11 and later.
- Audio route changes: plug or unplug headphones and connect or disconnect AirPods during an interview. Audio must continue, and the mic must still work after the route change.
