using AIInterviewer.Audio;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    /// <summary>
    /// Default, dependency-free lip-sync provider. Uses <see cref="SpectralVisemeAnalyzer"/> running inside the
    /// audio filter of <see cref="StreamingAudioPlayer"/>, i.e. on the exact samples sent to the speaker.
    /// Quality: good jaw/vowel sync; no consonant detection. Use <c>ULipSyncProvider</c> for higher fidelity.
    /// </summary>
    public sealed class SpectralLipSync : IAvatarLipSync
    {
        private readonly VisemeSmoother _smoother = new VisemeSmoother();
        private readonly VisemeFrame _target = new VisemeFrame();
        private StreamingAudioPlayer _player;
        private float _fastReleaseUntil;

        public string ProviderName => "Built-in spectral";

        public void Bind(StreamingAudioPlayer player, GameObject avatarRoot)
        {
            _player = player;
            _smoother.SnapNeutral();
        }

        public void Tick(float deltaTime, bool speaking, VisemeFrame output)
        {
            if (speaking && _player != null)
            {
                _player.Analyzer.GetLatest(_target);
            }
            else
            {
                _target.SetNeutral();
            }
            _smoother.ReleaseSeconds = Time.unscaledTime < _fastReleaseUntil ? 0.035f : 0.09f;
            output.CopyFrom(_smoother.Step(_target, deltaTime));
        }

        public void Interrupt()
        {
            _fastReleaseUntil = Time.unscaledTime + 0.25f;
        }

        public void ResetToNeutral()
        {
            _smoother.SnapNeutral();
        }
    }
}
