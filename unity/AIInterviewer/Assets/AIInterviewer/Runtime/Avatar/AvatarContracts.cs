using AIInterviewer.Audio;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    /// <summary>High-level behaviour the avatar should express.</summary>
    public enum AvatarMood
    {
        Idle = 0,
        Listening = 1,
        Thinking = 2,
        Speaking = 3
    }

    /// <summary>
    /// A lip-sync provider. Implementations must analyse (or receive data aligned with) the audio that is
    /// actually being played, so the mouth stays in sync with the speaker output.
    /// The interview system only talks to this interface, so providers can be swapped freely.
    /// </summary>
    public interface IAvatarLipSync
    {
        string ProviderName { get; }

        /// <summary>Connects the provider to the playback pipeline and the avatar instance.</summary>
        void Bind(StreamingAudioPlayer player, GameObject avatarRoot);

        /// <summary>Called every frame; writes smoothed viseme weights into <paramref name="output"/>.</summary>
        void Tick(float deltaTime, bool speaking, VisemeFrame output);

        /// <summary>Speech was interrupted: return to neutral quickly (not instantly, to avoid popping).</summary>
        void Interrupt();

        /// <summary>Snap to the neutral mouth (e.g. when the avatar is replaced or the interview ends).</summary>
        void ResetToNeutral();
    }

    /// <summary>Drives a concrete avatar model (placeholder geometry or a rigged blendshape character).</summary>
    public interface IAvatarRig
    {
        Transform Root { get; }

        /// <summary>Head transform used for procedural motion and camera framing.</summary>
        Transform Head { get; }

        /// <summary>True for the built-in placeholder (the UI shows a "placeholder avatar" badge).</summary>
        bool IsPlaceholder { get; }

        void ApplyVisemes(VisemeFrame frame);

        /// <summary>0 = eyes open, 1 = closed.</summary>
        void SetBlink(float closed);

        /// <summary>Eye direction in degrees (x = yaw, y = pitch) relative to straight ahead.</summary>
        void SetGaze(Vector2 yawPitch);

        void SetMood(AvatarMood mood, float speakingIntensity);

        void SetExpression(float smile, float browRaise);

        /// <summary>Additive head rotation and breathing, applied after the Animator each frame.</summary>
        void ApplyProceduralPose(Quaternion headOffset, float breath);
    }
}
