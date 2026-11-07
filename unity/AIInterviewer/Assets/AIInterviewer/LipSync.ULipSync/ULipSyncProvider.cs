using System.Collections.Generic;
using AIInterviewer.Audio;
using AIInterviewer.Avatar;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.LipSync
{
    /// <summary>
    /// <see cref="IAvatarLipSync"/> backed by uLipSync (MIT, https://github.com/hecomi/uLipSync), which estimates
    /// phonemes (A, I, U, E, O, N, silence) from MFCC features of the audio passing through the AudioSource.
    ///
    /// This assembly only compiles when the package "com.hecomi.ulipsync" (&gt;= 3.0.0) is installed.
    /// Setup: docs/LIPSYNC_AND_AUDIO.md. A uLipSync <c>Profile</c> asset must be assigned (calibrate it with
    /// recordings of the chosen OpenAI voice for best results) — place it at Resources/LipSync/uLipSyncProfile.
    /// </summary>
    public sealed class ULipSyncProvider : IAvatarLipSync
    {
        private readonly VisemeSmoother _smoother = new VisemeSmoother { AttackSeconds = 0.03f, ReleaseSeconds = 0.08f };
        private readonly VisemeFrame _target = new VisemeFrame();
        private readonly object _gate = new object();
        private global::uLipSync.uLipSync _component;
        private string _phoneme;
        private float _volume;
        private Dictionary<string, float> _ratios = new Dictionary<string, float>();
        private float _fastReleaseUntil;

        public string ProviderName => "uLipSync (MFCC)";

        public void Bind(StreamingAudioPlayer player, GameObject avatarRoot)
        {
            // uLipSync reads audio in OnAudioFilterRead, so it must live on the AudioSource GameObject and run
            // AFTER StreamingAudioPlayer (components added later run later in the filter chain).
            var go = player.gameObject;
            _component = go.GetComponent<global::uLipSync.uLipSync>();
            if (_component == null) _component = go.AddComponent<global::uLipSync.uLipSync>();
            if (_component.profile == null)
            {
                _component.profile = Resources.Load<global::uLipSync.Profile>("LipSync/uLipSyncProfile");
                if (_component.profile == null)
                {
                    Debug.LogError("[uLipSync] No profile at Resources/LipSync/uLipSyncProfile. Lip-sync will stay neutral.");
                }
            }
            _component.onLipSyncUpdate.AddListener(OnLipSyncUpdate);
            _smoother.SnapNeutral();
        }

        // Called by uLipSync on the main thread (from its Update).
        private void OnLipSyncUpdate(global::uLipSync.LipSyncInfo info)
        {
            lock (_gate)
            {
                _phoneme = info.phoneme;
                _volume = info.volume;
                _ratios = info.phonemeRatios != null ? new Dictionary<string, float>(info.phonemeRatios) : _ratios;
            }
        }

        public void Tick(float deltaTime, bool speaking, VisemeFrame output)
        {
            _target.SetNeutral();
            if (speaking)
            {
                lock (_gate)
                {
                    float vol = Mathf.Clamp01(_volume);
                    float sum = 0f;
                    sum += Map("A", Viseme.AA, vol);
                    sum += Map("I", Viseme.IH, vol);
                    sum += Map("U", Viseme.OU, vol);
                    sum += Map("E", Viseme.E, vol);
                    sum += Map("O", Viseme.OH, vol);
                    sum += Map("N", Viseme.NN, vol);
                    _target[Viseme.Sil] = Mathf.Clamp01(1f - sum);
                    _target.Volume = vol;
                    // Weights already include volume; open vowels open the jaw more.
                    float openness = _target[Viseme.AA] + _target[Viseme.OH] * 0.8f + _target[Viseme.E] * 0.6f +
                                     _target[Viseme.IH] * 0.4f + _target[Viseme.OU] * 0.45f;
                    _target.JawOpen = Mathf.Clamp01(vol * 0.35f + openness * 0.65f);
                }
            }
            _smoother.ReleaseSeconds = Time.unscaledTime < _fastReleaseUntil ? 0.035f : 0.08f;
            output.CopyFrom(_smoother.Step(_target, deltaTime));
        }

        private float Map(string phoneme, Viseme viseme, float volume)
        {
            float r = _ratios != null && _ratios.TryGetValue(phoneme, out float v) ? v : (phoneme == _phoneme ? 1f : 0f);
            float w = Mathf.Clamp01(r) * volume;
            _target[viseme] = w;
            return w;
        }

        public void Interrupt() => _fastReleaseUntil = Time.unscaledTime + 0.25f;

        public void ResetToNeutral() => _smoother.SnapNeutral();
    }
}
