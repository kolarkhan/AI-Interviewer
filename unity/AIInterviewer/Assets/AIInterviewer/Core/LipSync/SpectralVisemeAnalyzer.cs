using System;

namespace AIInterviewer.Core.LipSync
{
    /// <summary>
    /// Lightweight audio-driven viseme estimator (no ML, no allocations per block).
    ///
    /// It analyses the samples that are actually being played: four band filters approximate the
    /// first/second formant regions and fricative energy, and loudness drives the jaw. It reliably
    /// produces natural mouth opening/closing in sync with speech and a plausible vowel shape
    /// (AA / E / IH / OH / OU) plus sibilants (SS). It does NOT recognise consonants such as PP/FF/TH;
    /// for higher fidelity use the uLipSync adapter (MFCC-based phoneme estimation).
    ///
    /// Thread-safety: <see cref="Process"/> is called from the audio thread; <see cref="GetLatest"/>
    /// from the main thread.
    /// </summary>
    public sealed class SpectralVisemeAnalyzer
    {
        private readonly Biquad _low;      // ~500 Hz  : low F1 (O, U)
        private readonly Biquad _mid;      // ~1000 Hz : high F1 (A)
        private readonly Biquad _high;     // ~2300 Hz : high F2 (I, E)
        private readonly Biquad _fric;     // >4.5 kHz : sibilants (S, SH)
        private readonly object _gate = new object();
        private readonly VisemeFrame _latest = new VisemeFrame();

        public SpectralVisemeAnalyzer(int sampleRate)
        {
            SampleRate = sampleRate;
            _low = Biquad.BandPass(sampleRate, 500, 1.0);
            _mid = Biquad.BandPass(sampleRate, 1000, 1.0);
            _high = Biquad.BandPass(sampleRate, 2300, 1.2);
            _fric = Biquad.HighPass(sampleRate, Math.Min(4500, sampleRate * 0.45), 0.707);
            _latest.SetNeutral();
        }

        public int SampleRate { get; }

        /// <summary>Level (dBFS) treated as silence.</summary>
        public float SilenceDb { get; set; } = -48f;

        /// <summary>Level (dBFS) treated as fully open mouth.</summary>
        public float LoudDb { get; set; } = -14f;

        public void Process(float[] samples, int offset, int count, int stride = 1)
        {
            if (count <= 0) return;
            double eLow = 0, eMid = 0, eHigh = 0, eFric = 0, eTotal = 0;
            int n = 0;
            for (int i = offset; i < offset + count; i += stride)
            {
                double x = samples[i];
                eTotal += x * x;
                double l = _low.Next(x), m = _mid.Next(x), h = _high.Next(x), f = _fric.Next(x);
                eLow += l * l;
                eMid += m * m;
                eHigh += h * h;
                eFric += f * f;
                n++;
            }
            if (n == 0) return;
            double rms = Math.Sqrt(eTotal / n);
            var frame = Estimate(rms, eLow, eMid, eHigh, eFric);
            lock (_gate) _latest.CopyFrom(frame);
        }

        public void GetLatest(VisemeFrame destination)
        {
            lock (_gate) destination.CopyFrom(_latest);
        }

        public void Reset()
        {
            _low.Reset();
            _mid.Reset();
            _high.Reset();
            _fric.Reset();
            lock (_gate) _latest.SetNeutral();
        }

        private readonly VisemeFrame _scratch = new VisemeFrame();

        private VisemeFrame Estimate(double rms, double eLow, double eMid, double eHigh, double eFric)
        {
            var f = _scratch;
            Array.Clear(f.Weights, 0, VisemeFrame.Count);
            double db = 20.0 * Math.Log10(rms + 1e-9);
            float loudness = Clamp01((float)((db - SilenceDb) / (LoudDb - SilenceDb)));
            f.Volume = loudness;
            if (loudness <= 0.02f)
            {
                f.Weights[(int)Viseme.Sil] = 1f;
                f.JawOpen = 0f;
                return f;
            }

            double voiced = eLow + eMid + eHigh + 1e-12;
            double r1 = eLow / voiced, r2 = eMid / voiced, r3 = eHigh / voiced;
            double fric = eFric / (voiced + eFric);

            double aa = Smooth((r2 - 0.25) / 0.5);
            double e = 2.0 * r3 * r2;
            double ih = r3 * (1.0 - r2);
            double oh = 2.0 * r1 * r2;
            double ou = r1 * (1.0 - r2) * (1.0 - r3);
            double sum = aa + e + ih + oh + ou + 1e-9;
            double ss = Smooth((fric - 0.35) / 0.5);
            double vowelScale = 1.0 - ss;

            f.Weights[(int)Viseme.AA] = (float)(aa / sum * vowelScale);
            f.Weights[(int)Viseme.E] = (float)(e / sum * vowelScale);
            f.Weights[(int)Viseme.IH] = (float)(ih / sum * vowelScale);
            f.Weights[(int)Viseme.OH] = (float)(oh / sum * vowelScale);
            f.Weights[(int)Viseme.OU] = (float)(ou / sum * vowelScale);
            f.Weights[(int)Viseme.SS] = (float)ss;

            // Openness per shape: A is widest, I/U narrowest, S nearly closed.
            double openness = f.Weights[(int)Viseme.AA] * 1.0 + f.Weights[(int)Viseme.OH] * 0.8 +
                              f.Weights[(int)Viseme.E] * 0.65 + f.Weights[(int)Viseme.IH] * 0.45 +
                              f.Weights[(int)Viseme.OU] * 0.5 + f.Weights[(int)Viseme.SS] * 0.2;
            f.JawOpen = Clamp01((float)(loudness * (0.35 + 0.65 * openness)));
            for (int i = 1; i < VisemeFrame.Count; i++) f.Weights[i] *= loudness;
            f.Weights[(int)Viseme.Sil] = 1f - loudness;
            return f;
        }

        private static double Smooth(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x * x * (3 - 2 * x);
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>RBJ audio-EQ-cookbook biquad.</summary>
        private sealed class Biquad
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
            }

            public static Biquad BandPass(int rate, double freq, double q)
            {
                double w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w);
                return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
            }

            public static Biquad HighPass(int rate, double freq, double q)
            {
                double w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w);
                return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }

            public double Next(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }

            public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;
        }
    }
}
