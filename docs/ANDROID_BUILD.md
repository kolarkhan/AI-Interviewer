# Android build and release

## Prerequisites
- Unity 6.3 LTS with **Android Build Support** (OpenJDK, Android SDK & NDK tools) installed from Unity Hub.
- A Google Play Console developer account (one-time registration fee) for distribution.
- *AI Interviewer › Configure Project* has been run (IL2CPP, ARM64, min API 26, portrait, internet permission).

## Settings to review (Edit › Project Settings › Player › Android)
| Setting | Value |
|---|---|
| Package name | your own reverse-DNS id (replace `com.yourcompany.aiinterviewer`) |
| Version / Bundle Version Code | increment the code on every Play upload |
| Minimum API Level | 26 (Android 8.0) — required by the Keystore plugin |
| Target API Level | *Automatic (highest installed)* — Google Play requires a recent target API each year |
| Scripting Backend / Architectures | IL2CPP / ARM64 |
| Graphics APIs | Vulkan + OpenGLES3 (keep both for device coverage) |
| Internet Access | Require |
| Allow downloads over HTTP | *Development builds only* (production must use HTTPS) |

The `RECORD_AUDIO` permission is added automatically because the app uses `Microphone`. It is requested at runtime before the first interview. In the Play Console **Data safety** form, declare audio access (processed in transit, not stored), account info and app activity.

## Development build (device testing)
1. Enable *Developer options* and *USB debugging* on the phone, then connect it via USB.
2. File › Build Profiles › Android › tick **Development Build** → *Build And Run*.
   Or use the menu *AI Interviewer › Build › Android APK (development)*.
3. Logs: `adb logcat -s Unity` (filter on `[Interview]`, `[Api]`, `[Live]`).

## Release build (Play Store)
1. Create an upload keystore once (Player › Publishing Settings › Keystore Manager), and **back it up** together with its passwords.
2. Build the AAB with secrets from the environment (never commit them):
   ```bash
   export AII_KEYSTORE_PATH=/secure/upload.keystore AII_KEYSTORE_PASS=... AII_KEY_ALIAS=upload AII_KEY_PASS=...
   "<Unity>/Unity" -batchmode -quit -projectPath unity/AIInterviewer \
     -executeMethod AIInterviewer.EditorTools.BuildScripts.BuildAndroid -logFile build-android.log
   ```
   Output: `unity/AIInterviewer/Builds/Android/AIInterviewer.aab`.
3. Set `Resources/AppConfig.json` → `"environment": "production"` and an **https** `apiBaseUrl` before release builds.
4. Play Console: create the app → *Internal testing* track → upload the AAB → enrol Play App Signing → complete the content rating, privacy policy URL and data safety form.
5. Pre-launch report: review the crashes and accessibility findings from Google's device lab.

## Performance checks on a mid-range device
- Frame rate: Unity Profiler (development build, *Autoconnect Profiler*). Target a stable 60 fps in the interview room, or 30 on low-memory devices (set automatically).
- Memory: Profiler › Memory. Target < 400 MB PSS during an interview.
- Thermal: run a 20-minute interview and watch for throttling (fps drop).
- Startup: `adb shell am start -W <package>/com.unity3d.player.UnityPlayerActivity` reports TotalTime. Target < 3 s cold start to splash.
