using System;
using System.Collections.Generic;
using AIInterviewer.Core.Audio;
using UnityEngine;

namespace AIInterviewer.Audio
{
    /// <summary>
    /// Captures the device microphone with Unity's <see cref="Microphone"/> API, resamples to 24 kHz mono and
    /// emits 40 ms frames on the main thread.
    ///
    /// Limitation: Unity's Microphone API does not enable the platform's acoustic echo cancellation. See
    /// <see cref="EchoGate"/> and docs/LIPSYNC_AND_AUDIO.md for the speakerphone strategy and the native AEC plan.
    /// </summary>
    public sealed class MicrophoneCapture : MonoBehaviour
    {
        public const int TargetRate = 24000;
        public const int FrameSamples = TargetRate / 25; // 40 ms

        private AudioClip _clip;
        private string _device;
        private int _rate;
        private int _readPos;
        private int _block;
        private float[] _blockBuffer;
        private StreamingResampler _resampler;
        private readonly List<float> _pending = new List<float>(FrameSamples * 4);
        private float _startDeadline;
        private bool _waitingForStart;

        public bool IsRunning { get; private set; }
        public int DeviceSampleRate => _rate;

        /// <summary>Smoothed input level 0..1 (for the UI meter).</summary>
        public float Level { get; private set; }

        /// <summary>40 ms of 24 kHz mono audio. Each invocation receives a new array.</summary>
        public event Action<float[]> FrameReady;

        /// <summary>Raised if the microphone could not be started.</summary>
        public event Action<string> Failed;

        public bool StartCapture(string deviceName = null)
        {
            StopCapture();
            if (Microphone.devices.Length == 0)
            {
                Failed?.Invoke("No microphone was found on this device.");
                return false;
            }
            _device = deviceName;
            Microphone.GetDeviceCaps(_device, out int minFreq, out int maxFreq);
            _rate = ChooseRate(minFreq, maxFreq);
            _clip = Microphone.Start(_device, true, 1, _rate);
            if (_clip == null)
            {
                Failed?.Invoke("The microphone could not be started.");
                return false;
            }
            _rate = _clip.frequency;
            _block = Math.Max(1, _rate / 100); // 10 ms blocks
            _blockBuffer = new float[_block];
            _resampler = new StreamingResampler(_rate, TargetRate);
            _pending.Clear();
            _readPos = 0;
            _waitingForStart = true;
            _startDeadline = Time.unscaledTime + 3f;
            IsRunning = true;
            return true;
        }

        public void StopCapture()
        {
            if (!IsRunning) return;
            IsRunning = false;
            Microphone.End(_device);
            if (_clip != null) Destroy(_clip);
            _clip = null;
            Level = 0f;
        }

        private static int ChooseRate(int min, int max)
        {
            if (min == 0 && max == 0) return 48000; // "any rate" — 48 kHz is native on most phones
            foreach (int preferred in new[] { TargetRate, 48000, 44100 })
            {
                if (preferred >= min && preferred <= max) return preferred;
            }
            return max;
        }

        private void Update()
        {
            if (!IsRunning || _clip == null) return;
            int pos = Microphone.GetPosition(_device);
            if (_waitingForStart)
            {
                if (pos <= 0)
                {
                    if (Time.unscaledTime > _startDeadline)
                    {
                        StopCapture();
                        Failed?.Invoke("The microphone did not start. Another app may be using it.");
                    }
                    return;
                }
                _waitingForStart = false;
                _readPos = pos; // discard the start-up buffer
                return;
            }

            int total = _clip.samples;
            int available = pos - _readPos;
            if (available < 0) available += total;
            if (available > total / 2)
            {
                // We fell far behind (app hitch): skip ahead instead of sending stale audio.
                _readPos = (pos - _block + total) % total;
                available = _block;
            }

            while (available >= _block)
            {
                _clip.GetData(_blockBuffer, _readPos); // wraps around the looping clip automatically
                _readPos = (_readPos + _block) % total;
                available -= _block;
                float rms = Pcm16.Rms(_blockBuffer, 0, _block);
                Level = Mathf.Lerp(Level, Mathf.Clamp01(rms * 6f), 0.3f);
                _resampler.Process(_blockBuffer, 0, _block, _pending);
            }

            while (_pending.Count >= FrameSamples)
            {
                var frame = new float[FrameSamples];
                _pending.CopyTo(0, frame, 0, FrameSamples);
                _pending.RemoveRange(0, FrameSamples);
                FrameReady?.Invoke(frame);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            // The OS revokes microphone access in the background; the controller restarts capture on resume.
            if (paused) StopCapture();
        }

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
