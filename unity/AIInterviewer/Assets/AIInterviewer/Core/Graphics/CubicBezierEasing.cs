using System;

namespace AIInterviewer.Core.Graphics
{
    /// <summary>CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing function (y may overshoot, like the CSS spec allows).</summary>
    public sealed class CubicBezierEasing
    {
        private readonly double _x1, _y1, _x2, _y2;

        public CubicBezierEasing(double x1, double y1, double x2, double y2)
        {
            if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1) throw new ArgumentOutOfRangeException(nameof(x1), "x values must be in [0,1]");
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        public static readonly CubicBezierEasing Ease = new CubicBezierEasing(0.25, 0.1, 0.25, 1);
        public static readonly CubicBezierEasing EaseOut = new CubicBezierEasing(0, 0, 0.58, 1);
        public static readonly CubicBezierEasing EaseInOut = new CubicBezierEasing(0.42, 0, 0.58, 1);

        /// <summary>Eased value for linear progress <paramref name="x"/> in [0,1].</summary>
        public double Evaluate(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double t = SolveT(x);
            return Sample(_y1, _y2, t);
        }

        private static double Sample(double p1, double p2, double t)
        {
            double u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }

        private static double Derivative(double p1, double p2, double t)
        {
            double u = 1 - t;
            return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
        }

        private double SolveT(double x)
        {
            double t = x;
            for (int i = 0; i < 8; i++) // Newton–Raphson
            {
                double err = Sample(_x1, _x2, t) - x;
                if (Math.Abs(err) < 1e-7) return t;
                double d = Derivative(_x1, _x2, t);
                if (Math.Abs(d) < 1e-6) break;
                t -= err / d;
            }
            double lo = 0, hi = 1; // bisection fallback
            t = x;
            for (int i = 0; i < 40; i++)
            {
                double v = Sample(_x1, _x2, t);
                if (Math.Abs(v - x) < 1e-7) break;
                if (v < x) lo = t; else hi = t;
                t = (lo + hi) / 2;
            }
            return t;
        }
    }
}
