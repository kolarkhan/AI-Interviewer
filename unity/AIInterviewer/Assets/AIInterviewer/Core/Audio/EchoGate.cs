using System;

namespace AIInterviewer.Core.Audio
{
    public enum BargeInMode
    {
        /// <summary>Send the microphone continuously (use with headphones or a native echo canceller).</summary>
        Voice,
        /// <summary>Speakerphone without AEC: while the AI speaks, only clearly louder-than-echo speech passes.</summary>
        VoiceWithEchoGate,
        /// <summary>Microphone is silenced while the AI speaks; the candidate taps to interrupt.</summary>
        TapOnly
    }

    /// <summary>
    /// Simple energy-based double-talk gate for devices without acoustic echo cancellation.
    /// While interviewer audio is playing through the loudspeaker, the microphone hears it too; if that
    /// echo reached the server's voice activity detection, the AI would interrupt itself. The gate replaces
    /// microphone frames with silence unless the frame is clearly louder than the expected echo level.
    /// Silence (not dropped frames) is sent so the server-side audio timeline stays continuous.
    ///
    /// This is a heuristic fallback, NOT a replacement for real AEC (see docs/LIPSYNC_AND_AUDIO.md).
    /// </summary>
    public sealed class EchoGate
    {
        private float _echoEstimate;
        private int _holdFrames;

        public EchoGate(BargeInMode mode)
        {
            Mode = mode;
        }

        public BargeInMode Mode { get; set; }

        /// <summary>Mic RMS must exceed playback RMS times this factor to count as the candidate speaking.</summary>
        public float EchoRatio { get; set; } = 2.5f;

        /// <summary>Absolute floor (RMS, 0..1) for speech detection while the AI is talking.</summary>
        public float SpeechFloor { get; set; } = 0.04f;

        /// <summary>Frames to keep the gate open after speech is detected (avoids chopping words).</summary>
        public int HoldFrames { get; set; } = 8;

        /// <summary>True when the gate decided the last frame was candidate speech during AI playback.</summary>
        public bool LastFrameWasBargeIn { get; private set; }

        /// <summary>
        /// Processes one microphone frame in place.
        /// </summary>
        /// <param name="mic">Microphone samples (modified in place when gated).</param>
        /// <param name="playbackRms">RMS of the audio currently being played (0 when silent).</param>
        /// <param name="aiSpeaking">Whether interviewer audio is playing.</param>
        /// <returns>True if the frame passes unchanged; false if it was replaced by silence.</returns>
        public bool Process(float[] mic, int offset, int count, float playbackRms, bool aiSpeaking)
        {
            LastFrameWasBargeIn = false;
            _echoEstimate = Math.Max(playbackRms, _echoEstimate * 0.85f);
            if (!aiSpeaking || Mode == BargeInMode.Voice)
            {
                _holdFrames = 0;
                return true;
            }
            if (Mode == BargeInMode.TapOnly)
            {
                Array.Clear(mic, offset, count);
                return false;
            }
            float micRms = Pcm16.Rms(mic, offset, count);
            bool speech = micRms >= SpeechFloor && micRms >= _echoEstimate * EchoRatio;
            if (speech)
            {
                _holdFrames = HoldFrames;
                LastFrameWasBargeIn = true;
                return true;
            }
            if (_holdFrames > 0)
            {
                _holdFrames--;
                return true;
            }
            Array.Clear(mic, offset, count);
            return false;
        }
    }
}
