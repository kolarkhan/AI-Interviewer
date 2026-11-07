using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AIInterviewer.Core.Audio;
using UnityEngine;

namespace AIInterviewer.Audio
{
    /// <summary>
    /// Plays the short voice samples shown next to each interviewer voice. Samples are prefetched when the voice
    /// list opens, kept in memory as AudioClips and saved on the device, so a play tap starts immediately.
    /// Separate from the interview's streaming voice.
    /// </summary>
    public sealed class VoicePreviewPlayer : MonoBehaviour
    {
        private const int MaxParallelDownloads = 3;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, Task<AudioClip>> _pending = new Dictionary<string, Task<AudioClip>>();
        private readonly Queue<string> _prefetchQueue = new Queue<string>();
        private AudioSource _source;
        private int _request;
        private int _downloads;
        private string _version = "v0";
        private string _dir;

        /// <summary>Downloads the WAV sample for a voice id (set by the app root).</summary>
        public Func<string, Task<byte[]>> Fetch;

        /// <summary>Voice currently playing, or null.</summary>
        public string PlayingVoice { get; private set; }

        /// <summary>Voice the user asked to play while its sample is still downloading, or null.</summary>
        public string LoadingVoice { get; private set; }

        /// <summary>Raised when <see cref="PlayingVoice"/> or <see cref="LoadingVoice"/> changes.</summary>
        public event Action Changed;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _dir = Path.Combine(Application.persistentDataPath, "voice-previews");
        }

        private void Update()
        {
            if (PlayingVoice != null && !_source.isPlaying) SetState(null, LoadingVoice);
        }

        /// <summary>
        /// Server cache version (catalog <c>voice_preview_version</c>). Samples saved for another version are
        /// deleted, because the server's samples changed.
        /// </summary>
        public void SetCacheVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || version == _version) return;
            _version = version;
            foreach (var clip in _clips.Values)
                if (clip != null) Destroy(clip);
            _clips.Clear();
            try
            {
                if (!Directory.Exists(_dir)) return;
                foreach (var file in Directory.GetFiles(_dir, "*.wav"))
                    if (!Path.GetFileName(file).StartsWith(Safe(version) + "_", StringComparison.Ordinal)) File.Delete(file);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VoicePreview] Could not clean the sample cache: " + ex.Message);
            }
        }

        /// <summary>Downloads samples in the background (a few at a time) so play taps start immediately.</summary>
        public void Prefetch(IEnumerable<string> voiceIds)
        {
            foreach (var id in voiceIds)
                if (!string.IsNullOrEmpty(id) && !_clips.ContainsKey(id) && !_pending.ContainsKey(id) &&
                    !_prefetchQueue.Contains(id))
                    _prefetchQueue.Enqueue(id);
            PumpPrefetch();
        }

        /// <summary>Plays the voice's sample; tapping the voice that is already playing stops it.</summary>
        public async Task ToggleAsync(string voiceId)
        {
            if (PlayingVoice == voiceId || LoadingVoice == voiceId)
            {
                Stop();
                return;
            }
            Stop();
            int request = ++_request;
            if (!_clips.TryGetValue(voiceId, out var clip))
            {
                SetState(null, voiceId);
                try
                {
                    clip = await LoadAsync(voiceId);
                }
                finally
                {
                    if (request == _request && LoadingVoice == voiceId) SetState(null, null);
                }
            }
            if (request != _request || clip == null) return; // superseded while loading
            _source.clip = clip;
            _source.Play();
            SetState(voiceId, null);
        }

        public void Stop()
        {
            _request++;
            if (_source != null && _source.isPlaying) _source.Stop();
            SetState(null, null);
        }

        private void PumpPrefetch()
        {
            while (_downloads < MaxParallelDownloads && _prefetchQueue.Count > 0)
            {
                string id = _prefetchQueue.Dequeue();
                if (_clips.ContainsKey(id) || _pending.ContainsKey(id)) continue;
                _ = PrefetchOneAsync(id);
            }
        }

        private async Task PrefetchOneAsync(string voiceId)
        {
            try
            {
                await LoadAsync(voiceId);
            }
            catch (Exception ex)
            {
                // Background only: the play button reports errors if the user taps this voice.
                Debug.Log("[VoicePreview] Prefetch of " + voiceId + " failed: " + ex.Message);
            }
        }

        /// <summary>One load per voice at a time (a tap during a prefetch waits for the same download).</summary>
        private Task<AudioClip> LoadAsync(string voiceId)
        {
            if (_clips.TryGetValue(voiceId, out var clip)) return Task.FromResult(clip);
            if (_pending.TryGetValue(voiceId, out var pending)) return pending;
            var task = LoadCoreAsync(voiceId);
            _pending[voiceId] = task;
            return task;
        }

        private async Task<AudioClip> LoadCoreAsync(string voiceId)
        {
            string version = _version;
            _downloads++;
            try
            {
                byte[] wav = ReadDisk(version, voiceId);
                bool fromDisk = wav != null;
                if (wav == null)
                {
                    if (Fetch == null) throw new InvalidOperationException("Voice previews are not configured");
                    wav = await Fetch(voiceId);
                }
                var clip = ToClip(voiceId, wav);
                if (clip == null)
                {
                    if (fromDisk) DeleteDisk(version, voiceId);
                    throw new InvalidOperationException("The voice sample could not be read");
                }
                if (version != _version) return clip; // cache was reset meanwhile: use once, don't keep
                _clips[voiceId] = clip;
                if (!fromDisk) WriteDisk(version, voiceId, wav);
                return clip;
            }
            finally
            {
                _pending.Remove(voiceId);
                _downloads--;
                PumpPrefetch();
            }
        }

        private string PathFor(string version, string voiceId) =>
            Path.Combine(_dir, Safe(version) + "_" + Safe(voiceId) + ".wav");

        private byte[] ReadDisk(string version, string voiceId)
        {
            try
            {
                string path = PathFor(version, voiceId);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void WriteDisk(string version, string voiceId, byte[] wav)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllBytes(PathFor(version, voiceId), wav);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VoicePreview] Could not save sample: " + ex.Message);
            }
        }

        private void DeleteDisk(string version, string voiceId)
        {
            try
            {
                File.Delete(PathFor(version, voiceId));
            }
            catch (Exception)
            {
                // ignored: the next download overwrites it
            }
        }

        private static string Safe(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '.') chars[i] = '-';
            return new string(chars);
        }

        private static AudioClip ToClip(string voiceId, byte[] wav)
        {
            var pcm = WavFile.Decode(wav);
            if (pcm == null || pcm.Samples.Length == 0) return null;
            var clip = AudioClip.Create("voice-preview-" + voiceId, pcm.Samples.Length / pcm.Channels, pcm.Channels,
                pcm.SampleRate, false);
            clip.SetData(pcm.Samples, 0);
            return clip;
        }

        private void SetState(string playing, string loading)
        {
            if (PlayingVoice == playing && LoadingVoice == loading) return;
            PlayingVoice = playing;
            LoadingVoice = loading;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            foreach (var clip in _clips.Values)
                if (clip != null) Destroy(clip);
            _clips.Clear();
        }
    }
}
