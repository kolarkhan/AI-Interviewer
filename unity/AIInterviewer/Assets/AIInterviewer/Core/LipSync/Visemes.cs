using System;

namespace AIInterviewer.Core.LipSync
{
    /// <summary>
    /// The 15 visemes of the widely used Oculus/Meta set (also used by Ready Player Me style avatars as
    /// `viseme_sil`, `viseme_PP`, ...). Avatars map these to their own blendshapes.
    /// </summary>
    public enum Viseme
    {
        Sil = 0, PP, FF, TH, DD, KK, CH, SS, NN, RR, AA, E, IH, OH, OU
    }

    /// <summary>One frame of facial animation data produced by a lip-sync provider.</summary>
    public sealed class VisemeFrame
    {
        public const int Count = 15;

        /// <summary>Weights 0..1 per <see cref="Viseme"/>.</summary>
        public readonly float[] Weights = new float[Count];

        /// <summary>Overall mouth opening 0..1 (drives a jaw bone or `jawOpen` blendshape).</summary>
        public float JawOpen;

        /// <summary>Loudness 0..1 of the analysed audio (useful for gestures / head motion).</summary>
        public float Volume;

        public float this[Viseme v]
        {
            get => Weights[(int)v];
            set => Weights[(int)v] = value;
        }

        public void SetNeutral()
        {
            Array.Clear(Weights, 0, Count);
            Weights[(int)Viseme.Sil] = 1f;
            JawOpen = 0f;
            Volume = 0f;
        }

        public void CopyFrom(VisemeFrame other)
        {
            Array.Copy(other.Weights, Weights, Count);
            JawOpen = other.JawOpen;
            Volume = other.Volume;
        }

        public Viseme Dominant()
        {
            int best = 0;
            for (int i = 1; i < Count; i++)
            {
                if (Weights[i] > Weights[best]) best = i;
            }
            return (Viseme)best;
        }
    }

    /// <summary>
    /// Exponential attack/release smoothing so mouth shapes blend naturally instead of popping.
    /// </summary>
    public sealed class VisemeSmoother
    {
        private readonly VisemeFrame _state = new VisemeFrame();

        public VisemeSmoother()
        {
            _state.SetNeutral();
        }

        /// <summary>Time constant (seconds) when a weight increases.</summary>
        public float AttackSeconds { get; set; } = 0.04f;

        /// <summary>Time constant (seconds) when a weight decreases.</summary>
        public float ReleaseSeconds { get; set; } = 0.09f;

        public VisemeFrame Current => _state;

        public VisemeFrame Step(VisemeFrame target, float deltaTime)
        {
            for (int i = 0; i < VisemeFrame.Count; i++)
            {
                _state.Weights[i] = Approach(_state.Weights[i], target.Weights[i], deltaTime);
            }
            _state.JawOpen = Approach(_state.JawOpen, target.JawOpen, deltaTime);
            _state.Volume = Approach(_state.Volume, target.Volume, deltaTime);
            return _state;
        }

        public void SnapNeutral()
        {
            _state.SetNeutral();
        }

        private float Approach(float current, float target, float dt)
        {
            float tau = target > current ? AttackSeconds : ReleaseSeconds;
            if (tau <= 0f || dt <= 0f) return dt <= 0f ? current : target;
            float k = 1f - (float)Math.Exp(-dt / tau);
            return current + (target - current) * k;
        }
    }
}
