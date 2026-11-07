using System.Collections.Generic;
using System.Threading;
using AIInterviewer.Core.Audio;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.Audio
{
    /// <summary>
    /// Plays the interviewer's streamed PCM16 (24 kHz) audio with low latency.
    ///
    /// - Network thread: <see cref="BeginItem"/> / <see cref="Enqueue"/> resample to the output rate and fill a ring buffer.
    /// - Audio thread: <see cref="OnAudioFilterRead"/> copies from the ring buffer (no allocations) and feeds the
    ///   lip-sync analyser with exactly the samples being sent to the speaker.
    /// - Main thread: <see cref="Interrupt"/> stops playback and reports how much of the current item was heard.
    ///
    /// Put a uLipSync component BELOW this component on the same GameObject if you use the uLipSync provider,
    /// so it receives the audio after this filter writes it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class StreamingAudioPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("Audio buffered before a response starts playing (absorbs network jitter).")]
        private float prebufferSeconds = 0.12f;

        [SerializeField, Tooltip("Maximum buffered interviewer audio.")]
        private float bufferSeconds = 45f;

        [Range(0f, 1.5f)] public float volume = 1f;

        private readonly object _writeGate = new object();
        private readonly List<float> _resampled = new List<float>(8192);
        private PcmRingBuffer _ring;
        private StreamingResampler _resampler;
        private PlaybackTracker _tracker;
        private int _outputRate;
        private int _prebufferSamples;
        private int _latencySamples;
        private float[] _mono = new float[4096];
        private volatile bool _prebuffering = true;
        private volatile bool _endOfStream;
        private volatile float _playbackRms;
        private float _lastAudibleTime = -10f;
        private AudioSource _source;

        public int OutputSampleRate => _outputRate;

        /// <summary>Lip-sync analysis of the audio actually being played.</summary>
        public SpectralVisemeAnalyzer Analyzer { get; private set; }

        public float PlaybackRms => _playbackRms;

        /// <summary>Buffered audio not yet played.</summary>
        public bool HasBufferedAudio => _ring != null && _ring.Count > 0;

        /// <summary>True while audio is coming out of the speaker (with a short hold to bridge gaps).</summary>
        public bool IsAudible => HasBufferedAudio || Time.unscaledTime - _lastAudibleTime < 0.15f;

        public float BufferedSeconds => _ring == null ? 0f : (float)_ring.Count / _outputRate;

        public AudioSource Source => _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _outputRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            _latencySamples = bufferLength * Mathf.Max(1, numBuffers);
            _ring = new PcmRingBuffer(Mathf.CeilToInt(_outputRate * bufferSeconds));
            _resampler = new StreamingResampler(MicrophoneCapture.TargetRate, _outputRate);
            _tracker = new PlaybackTracker(_outputRate);
            _prebufferSamples = Mathf.CeilToInt(_outputRate * prebufferSeconds);
            Analyzer = new SpectralVisemeAnalyzer(_outputRate);

            // OnAudioFilterRead only runs while the AudioSource plays, so loop a silent clip.
            var silent = AudioClip.Create("ai-voice-carrier", _outputRate, 1, _outputRate, false);
            _source.clip = silent;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.priority = 0;
            _source.Play();
        }

        private void OnEnable()
        {
            // Deactivating the GameObject stops the AudioSource; restart it so OnAudioFilterRead keeps running.
            if (_source != null && _source.clip != null && !_source.isPlaying) _source.Play();
        }

        /// <summary>Network thread: marks where a new conversation item's audio starts.</summary>
        public void BeginItem(string itemId)
        {
            lock (_writeGate)
            {
                _endOfStream = false;
                _tracker.BeginItem(itemId, _ring.TotalWritten);
            }
        }

        /// <summary>Network thread: appends PCM16 mono 24 kHz little-endian audio.</summary>
        public void Enqueue(byte[] pcm16, int offset, int count)
        {
            if (count < 2) return;
            float[] samples = Core.Audio.Pcm16.ToFloats(pcm16, offset, count);
            lock (_writeGate)
            {
                _resampled.Clear();
                _resampler.Process(samples, 0, samples.Length, _resampled);
                if (_resampled.Count == 0) return;
                float[] chunk = _resampled.ToArray();
                _ring.Write(chunk, 0, chunk.Length);
            }
        }

        /// <summary>The server finished sending audio for the current response: play out what remains.</summary>
        public void MarkEndOfStream() => _endOfStream = true;

        public struct InterruptResult
        {
            public bool HadAudio;
            public string ItemId;
            public int PlayedMs;
        }

        /// <summary>Main thread: stops playback immediately and reports what the listener heard.</summary>
        public InterruptResult Interrupt()
        {
            var result = new InterruptResult();
            if (_ring == null) return result; // not started yet, or already torn down (exiting play mode)
            lock (_writeGate)
            {
                long heard = System.Math.Max(0, _ring.TotalRead - _latencySamples);
                result.HadAudio = _ring.Count > 0 || IsAudible;
                if (_tracker.TryGetPlaying(heard, out string item, out int ms))
                {
                    result.ItemId = item;
                    result.PlayedMs = ms;
                }
                _ring.Clear();
                _resampler.Reset();
                _tracker.Clear();
                _prebuffering = true;
                _endOfStream = false;
            }
            Analyzer.Reset();
            _lastAudibleTime = -10f;
            return result;
        }

        private void Update()
        {
            if (HasBufferedAudio) _lastAudibleTime = Time.unscaledTime;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_ring == null) return;
            int frames = data.Length / channels;
            if (_mono.Length < frames) _mono = new float[frames]; // rare; only if the DSP buffer grows

            int got = 0;
            if (!_prebuffering || _ring.Count >= _prebufferSamples || _endOfStream)
            {
                _prebuffering = false;
                got = _ring.Read(_mono, 0, frames);
            }
            for (int i = got; i < frames; i++) _mono[i] = 0f;
            if (got < frames && !_endOfStream && got == 0)
            {
                _prebuffering = true; // underrun: rebuild a small cushion before resuming
            }

            float v = volume;
            double sum = 0;
            for (int i = 0; i < frames; i++)
            {
                float s = _mono[i] * v;
                sum += s * s;
                int baseIndex = i * channels;
                for (int c = 0; c < channels; c++) data[baseIndex + c] = s;
            }
            _playbackRms = frames > 0 ? (float)System.Math.Sqrt(sum / frames) : 0f;
            Analyzer?.Process(_mono, 0, frames);
        }

        private void OnDestroy()
        {
            if (_source != null && _source.clip != null) Destroy(_source.clip);
        }
    }
}
