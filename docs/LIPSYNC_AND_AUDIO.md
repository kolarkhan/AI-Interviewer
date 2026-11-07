# Lip-sync and audio

## Pipeline

```
WebSocket (PCM16 24 kHz) ─► StreamingAudioPlayer.Enqueue (network thread: resample to output rate, ring buffer)
                              │
            AudioSource ─► OnAudioFilterRead (audio thread)
                              ├─ copies samples to the speaker
                              └─ SpectralVisemeAnalyzer.Process(same samples) ─► latest VisemeFrame
Main thread: AvatarController.Update ─► IAvatarLipSync.Tick (smoothing) ─► IAvatarRig.ApplyVisemes
```

Because the analyser runs on the samples handed to the audio device, the mouth stays aligned with what is heard; the only offset is the DSP buffer (~10–40 ms with "Best latency"). The Realtime API provides **no viseme timestamps**, so lip-sync is audio-driven by design.

## Providers (`IAvatarLipSync`)

| Provider | Quality | Cost | Notes |
|---|---|---|---|
| `SpectralLipSync` (default) | Natural jaw motion and plausible vowel shapes (AA/E/IH/OH/OU) + sibilants | Negligible CPU, no dependencies | No consonant detection (PP/FF/TH) |
| `ULipSyncProvider` | MFCC phoneme estimation (A, I, U, E, O, N) | Low (Burst/Jobs) | MIT. Requires the package and a calibrated profile |

Contract every provider follows: analyse the played audio, ease between shapes, return to neutral when speech ends, close quickly on interruption (`Interrupt()`), snap to neutral on reset.

### Enabling uLipSync

1. Package Manager → *Add package from git URL* → `https://github.com/hecomi/uLipSync.git#upm` (also install `com.unity.burst` and `com.unity.mathematics` if prompted).
2. The `AIInterviewer.LipSync.ULipSync` assembly compiles automatically (`versionDefines` on `com.hecomi.ulipsync` ≥ 3.0.0).
3. Calibrate a profile with uLipSync's calibration tools, using recordings of the chosen OpenAI voice (e.g. `marin`) saying A/I/U/E/O. Save it at `Assets/AIInterviewer/Resources/LipSync/uLipSyncProfile.asset`.
4. In the app: Profile → *High-fidelity lip-sync*.

## Echo, barge-in and microphone handling

Unity's `Microphone` API does **not** enable the OS echo canceller. On speakerphone the interviewer's voice leaks into the mic, and server-side VAD would hear it as an interruption.

| Mode | When | Behaviour |
|---|---|---|
| `Voice` | headphones selected | full duplex: speak any time to interrupt |
| `VoiceWithEchoGate` (default) | speakerphone | while the AI talks, mic frames are replaced with silence unless clearly louder than the playback echo (`EchoGate`); a hand button allows tap-to-interrupt |
| `TapOnly` | very echoey devices | mic silenced while the AI talks |

**Recommended next step for production:** a native capture plugin using platform AEC — Android `AudioRecord` with `MediaRecorder.AudioSource.VOICE_COMMUNICATION` + `AcousticEchoCanceler`; iOS `AVAudioEngine` with `inputNode.setVoiceProcessingEnabled(true)`. It would replace `MicrophoneCapture` behind the same frame interface (`FrameReady(float[] 24 kHz)`). This is listed in `LIMITATIONS.md`.

iOS also needs *Force iOS Speakers when Recording* (set by *AI Interviewer › Configure Project*), otherwise audio plays through the earpiece.

## Testing lip-sync and audio on devices

1. Unity Test Runner → PlayMode → `RoomPlayModeTests` (streamed tone drives the jaw; interruption reports heard ms).
2. Demo mode (no API key): the interviewer "speaks" a synthetic speech-rhythm tone. Check the mouth opens on syllables and closes in pauses.
3. Real voice: record the screen at 60 fps with external audio. Mouth-open frames should align with audio onsets within about 2 frames (≤ 40 ms).
4. Interrupt mid-sentence: the mouth closes within ~150 ms, and the transcript marks the interviewer line as interrupted (`…`).
5. Latency: `tools/voice_poc.py` prints *speech_stopped → first AI audio*. On device, enable development build logging and compare `user.speech_stopped` to `ai.audio.started` timestamps. Target end-of-speech → first audio under 1.2 s on Wi-Fi in-region.
6. Check on at least one mid-range Android (e.g. 4–6 GB RAM, Snapdragon 6-series) and one iPhone, using both speaker and headphones.
