using AIInterviewer.Audio;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    /// <summary>
    /// Brings the interviewer to life: lip-sync, blinking, eye saccades, breathing, head motion and
    /// mood-dependent behaviour (idle / listening / thinking / speaking). Works with any <see cref="IAvatarRig"/>;
    /// a rig with an Animator additionally receives the mood as animator parameters.
    /// </summary>
    public sealed class AvatarController : MonoBehaviour
    {
        private readonly VisemeFrame _visemes = new VisemeFrame();
        private IAvatarRig _rig;
        private IAvatarLipSync _lipSync;
        private StreamingAudioPlayer _player;

        private AvatarMood _mood = AvatarMood.Idle;
        private float _moodBlend;         // 0..1 transition weight into the current mood
        private float _nextBlink;
        private float _blinkStart = -1f;
        private bool _doubleBlink;
        private Vector2 _gazeTarget, _gaze;
        private float _nextSaccade;
        private float _nodTime = -10f;
        private float _smile, _brow;
        private float _seed;

        public IAvatarRig Rig => _rig;
        public IAvatarLipSync LipSync => _lipSync;
        public AvatarMood Mood => _mood;
        public float SpeakingVolume => _visemes.Volume;

        public void Initialize(IAvatarRig rig, IAvatarLipSync lipSync, StreamingAudioPlayer player)
        {
            _rig = rig;
            _lipSync = lipSync;
            _player = player;
            _lipSync.Bind(player, rig.Root.gameObject);
            _seed = Random.value * 100f;
            ScheduleBlink();
            _visemes.SetNeutral();
        }

        public void SetMood(AvatarMood mood)
        {
            if (mood == _mood) return;
            if (_mood == AvatarMood.Speaking && mood != AvatarMood.Speaking) _lipSync?.Interrupt();
            _mood = mood;
            _moodBlend = 0f;
            if (mood == AvatarMood.Listening && Random.value < 0.6f) _nodTime = Time.time + Random.Range(1.5f, 3f);
            _nextSaccade = 0f; // react immediately with the eyes
        }

        /// <summary>Barge-in: close the mouth quickly and shift attention to the candidate.</summary>
        public void OnInterrupted()
        {
            _lipSync?.Interrupt();
            SetMood(AvatarMood.Listening);
        }

        public void ResetFace()
        {
            _lipSync?.ResetToNeutral();
            _visemes.SetNeutral();
            _rig?.ApplyVisemes(_visemes);
        }

        private void Update()
        {
            if (_rig == null) return;
            float dt = Time.deltaTime;
            _moodBlend = Mathf.MoveTowards(_moodBlend, 1f, dt / 0.35f);

            bool speaking = _player != null && _player.IsAudible && _mood == AvatarMood.Speaking;
            _lipSync.Tick(dt, speaking, _visemes);
            _rig.ApplyVisemes(_visemes);

            UpdateBlink();
            UpdateGaze(dt);
            UpdateExpression(dt);
            _rig.SetMood(_mood, _visemes.Volume);
        }

        private void LateUpdate()
        {
            if (_rig == null) return;
            float t = Time.time;
            float breath = 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 0.23f);

            // Organic idle sway from layered noise.
            float yaw = (Mathf.PerlinNoise(_seed, t * 0.18f) - 0.5f) * 6f;
            float pitch = (Mathf.PerlinNoise(_seed + 7f, t * 0.15f) - 0.5f) * 4f;
            float roll = (Mathf.PerlinNoise(_seed + 13f, t * 0.12f) - 0.5f) * 3f;

            switch (_mood)
            {
                case AvatarMood.Listening:
                    roll += 3.5f * _moodBlend;            // attentive head tilt
                    pitch -= 1.5f * _moodBlend;
                    float nodPhase = t - _nodTime;
                    if (nodPhase > 0f && nodPhase < 0.9f) pitch += Mathf.Sin(nodPhase / 0.9f * Mathf.PI * 2f) * 4f;
                    break;
                case AvatarMood.Thinking:
                    yaw += 7f * _moodBlend;                // looks slightly aside while thinking
                    pitch -= 4f * _moodBlend;
                    break;
                case AvatarMood.Speaking:
                    // Emphasis follows speech energy.
                    pitch += (_visemes.Volume - 0.3f) * 5f;
                    yaw *= 1.4f;
                    break;
            }
            _rig.ApplyProceduralPose(Quaternion.Euler(pitch, yaw, roll), breath);
        }

        private void ScheduleBlink()
        {
            _nextBlink = Time.time + Random.Range(2.2f, 5.5f);
            _doubleBlink = Random.value < 0.18f;
        }

        private void UpdateBlink()
        {
            const float duration = 0.16f;
            float t = Time.time;
            if (_blinkStart < 0f && t >= _nextBlink) _blinkStart = t;
            float closed = 0f;
            if (_blinkStart >= 0f)
            {
                float p = (t - _blinkStart) / duration;
                if (p >= 1f)
                {
                    if (_doubleBlink)
                    {
                        _doubleBlink = false;
                        _blinkStart = t + 0.08f;
                    }
                    else
                    {
                        _blinkStart = -1f;
                        ScheduleBlink();
                    }
                }
                else if (p > 0f)
                {
                    closed = p < 0.4f ? p / 0.4f : 1f - (p - 0.4f) / 0.6f; // fast close, slower open
                }
            }
            _rig.SetBlink(Mathf.Clamp01(closed));
        }

        private void UpdateGaze(float dt)
        {
            if (Time.time >= _nextSaccade)
            {
                switch (_mood)
                {
                    case AvatarMood.Thinking:
                        _gazeTarget = new Vector2(Random.Range(8f, 14f), Random.Range(6f, 10f));
                        _nextSaccade = Time.time + Random.Range(0.8f, 1.6f);
                        break;
                    case AvatarMood.Listening:
                        _gazeTarget = new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(-1f, 1f)); // eye contact
                        _nextSaccade = Time.time + Random.Range(1.2f, 2.8f);
                        break;
                    default:
                        bool away = Random.value < 0.25f;
                        _gazeTarget = away
                            ? new Vector2(Random.Range(-9f, 9f), Random.Range(-4f, 3f))
                            : new Vector2(Random.Range(-2f, 2f), Random.Range(-1.5f, 1.5f));
                        _nextSaccade = Time.time + Random.Range(away ? 0.5f : 1.5f, away ? 1.1f : 3.5f);
                        break;
                }
            }
            // Saccades are fast: near-instant eye movement.
            _gaze = Vector2.Lerp(_gaze, _gazeTarget, 1f - Mathf.Exp(-dt / 0.03f));
            _rig.SetGaze(_gaze);
        }

        private void UpdateExpression(float dt)
        {
            float smileTarget = _mood == AvatarMood.Listening ? 0.35f : _mood == AvatarMood.Idle ? 0.25f : 0.15f;
            float browTarget = _mood == AvatarMood.Listening ? 0.25f : _mood == AvatarMood.Thinking ? 0.1f :
                _mood == AvatarMood.Speaking ? _visemes.Volume * 0.4f : 0f;
            _smile = Mathf.Lerp(_smile, smileTarget, 1f - Mathf.Exp(-dt / 0.4f));
            _brow = Mathf.Lerp(_brow, browTarget, 1f - Mathf.Exp(-dt / 0.2f));
            _rig.SetExpression(_smile, _brow);
        }
    }
}
