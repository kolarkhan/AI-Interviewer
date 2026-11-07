using System.Collections.Generic;
using AIInterviewer.Core.Graphics;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Branding
{
    /// <summary>Painter2D helpers for drawing the Avintera vector artwork.</summary>
    internal static class AvinteraDraw
    {
        public delegate Vector2 Map(double x, double y);

        public static void FillPath(Painter2D p, List<PathCommand> path, Map map, Color color, FillRule rule)
        {
            p.fillColor = color;
            p.BeginPath();
            foreach (var c in path)
            {
                switch (c.Op)
                {
                    case PathOp.MoveTo: p.MoveTo(map(c.X, c.Y)); break;
                    case PathOp.LineTo: p.LineTo(map(c.X, c.Y)); break;
                    case PathOp.CubicTo: p.BezierCurveTo(map(c.C1X, c.C1Y), map(c.C2X, c.C2Y), map(c.X, c.Y)); break;
                    case PathOp.Close: p.ClosePath(); break;
                }
            }
            p.Fill(rule);
        }

        /// <summary>Rounded bar ("stadium") with a vertical gradient, drawn as thin horizontal slices.</summary>
        public static void FillBar(Painter2D p, Rect r, Color top, Color bottom, float alpha, int slices = 28)
        {
            if (r.height <= 0.5f || alpha <= 0f) return;
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            for (int i = 0; i < slices; i++)
            {
                float y0 = r.yMin + r.height * i / slices;
                float y1 = r.yMin + r.height * (i + 1) / slices + 0.6f; // tiny overlap hides seams
                y1 = Mathf.Min(y1, r.yMax);
                float half0 = HalfWidthAt(r, radius, y0), half1 = HalfWidthAt(r, radius, y1);
                var c = Color.Lerp(top, bottom, (i + 0.5f) / slices);
                c.a *= alpha;
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(new Vector2(r.center.x - half0, y0));
                p.LineTo(new Vector2(r.center.x + half0, y0));
                p.LineTo(new Vector2(r.center.x + half1, y1));
                p.LineTo(new Vector2(r.center.x - half1, y1));
                p.ClosePath();
                p.Fill();
            }
        }

        private static float HalfWidthAt(Rect r, float radius, float y)
        {
            float half = r.width * 0.5f;
            float dy = 0f;
            if (y < r.yMin + radius) dy = r.yMin + radius - y;
            else if (y > r.yMax - radius) dy = y - (r.yMax - radius);
            if (dy <= 0f) return half;
            float inside = radius * radius - dy * dy;
            return inside <= 0f ? 0f : Mathf.Min(half, Mathf.Sqrt(inside));
        }

        /// <summary>Soft radial glow approximated with stacked translucent ellipses (centre opacity ≈ peak).</summary>
        public static void FillGlow(Painter2D p, Vector2 centre, Vector2 radius, Color color, float peakAlpha, int rings = 26)
        {
            if (peakAlpha <= 0.001f) return;
            float perRing = 1f - Mathf.Pow(1f - Mathf.Clamp01(peakAlpha), 1f / rings);
            var c = color;
            c.a = perRing;
            p.fillColor = c;
            for (int i = rings; i >= 1; i--)
            {
                float k = i / (float)rings;
                Ellipse(p, centre, radius * k);
                p.Fill();
            }
        }

        private static void Ellipse(Painter2D p, Vector2 c, Vector2 r)
        {
            const float kappa = 0.5522848f;
            float ox = r.x * kappa, oy = r.y * kappa;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - r.x, c.y));
            p.BezierCurveTo(new Vector2(c.x - r.x, c.y - oy), new Vector2(c.x - ox, c.y - r.y), new Vector2(c.x, c.y - r.y));
            p.BezierCurveTo(new Vector2(c.x + ox, c.y - r.y), new Vector2(c.x + r.x, c.y - oy), new Vector2(c.x + r.x, c.y));
            p.BezierCurveTo(new Vector2(c.x + r.x, c.y + oy), new Vector2(c.x + ox, c.y + r.y), new Vector2(c.x, c.y + r.y));
            p.BezierCurveTo(new Vector2(c.x - ox, c.y + r.y), new Vector2(c.x - r.x, c.y + oy), new Vector2(c.x - r.x, c.y));
            p.ClosePath();
        }
    }

    /// <summary>
    /// Full-screen animated Avintera splash, reproducing the CSS animation of avintera-splash-animated.svg:
    /// head fade-up (0.1 s), glow (0.55 s), bars spring in (0.75 / 0.83 / 0.91 s) and pulse once, wordmark (1.25 s).
    /// </summary>
    public sealed class AvinteraSplashArt : VisualElement
    {
        public const float Duration = 2.05f;

        private static readonly CubicBezierEasing EaseFade = new CubicBezierEasing(0.2, 0.7, 0.2, 1);
        private static readonly CubicBezierEasing EaseBar = new CubicBezierEasing(0.3, 1.4, 0.5, 1);
        private static readonly float[] BarDelays = { 0.75f, 0.83f, 0.91f };

        private float _startTime = -1f;
        private IVisualElementScheduledItem _ticker;

        public AvinteraSplashArt()
        {
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            style.backgroundColor = AvinteraArtwork.Background;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>Restarts the animation.</summary>
        public void Play()
        {
            _startTime = Time.realtimeSinceStartup;
            _ticker?.Pause();
            _ticker = schedule.Execute(() =>
            {
                MarkDirtyRepaint();
                if (Elapsed > Duration + 0.1f) _ticker.Pause(); // static after the animation: no more redraws
            }).Every(16);
        }

        /// <summary>Shows the final frame without animation.</summary>
        public void ShowFinal()
        {
            _startTime = Time.realtimeSinceStartup - Duration - 1f;
            MarkDirtyRepaint();
        }

        private float Elapsed => _startTime < 0f ? 0f : Time.realtimeSinceStartup - _startTime;

        private static float Progress(float t, float delay, float duration) => Mathf.Clamp01((t - delay) / duration);

        private void Draw(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            // preserveAspectRatio="xMidYMid meet"
            float s = Mathf.Min(rect.width / AvinteraArtwork.ViewWidth, rect.height / AvinteraArtwork.ViewHeight);
            var origin = new Vector2((rect.width - AvinteraArtwork.ViewWidth * s) * 0.5f,
                (rect.height - AvinteraArtwork.ViewHeight * s) * 0.5f);
            float t = Elapsed;
            var p = ctx.painter2D;

            // Glow: 0 → .55 (40%) → .18, ease-out, from 0.55 s for 1.1 s.
            float g = Progress(t, 0.55f, 1.1f);
            float glow = g < 0.4f
                ? Mathf.Lerp(0f, 0.55f, (float)CubicBezierEasing.EaseOut.Evaluate(g / 0.4f))
                : Mathf.Lerp(0.55f, 0.18f, (float)CubicBezierEasing.EaseOut.Evaluate((g - 0.4f) / 0.6f));
            AvinteraDraw.FillGlow(p, origin + AvinteraArtwork.GlowCenter * s, AvinteraArtwork.GlowRadius * s,
                AvinteraArtwork.GradientTop, 0.5f * glow);

            // Head: fade + rise 10 units (mark space), from 0.1 s for 0.7 s.
            float h = (float)EaseFade.Evaluate(Progress(t, 0.1f, 0.7f));
            float ms = AvinteraArtwork.MarkScale * s;
            var markOrigin = origin + AvinteraArtwork.MarkOffset * s;
            float rise = (1f - h) * 10f;
            if (h > 0f)
            {
                var ink = AvinteraArtwork.Ink;
                ink.a = h;
                AvinteraDraw.FillPath(p, AvinteraArtwork.Head,
                    (x, y) => markOrigin + new Vector2((float)x, (float)(y + rise)) * ms, ink, FillRule.OddEven);
            }

            // Bars: spring from scaleY .2 with overshoot, then one pulse to .72.
            for (int i = 0; i < AvinteraArtwork.Bars.Length; i++)
            {
                float d = BarDelays[i];
                float bp = Progress(t, d, 0.45f);
                if (bp <= 0f) continue;
                float scale = Mathf.LerpUnclamped(0.2f, 1f, (float)EaseBar.Evaluate(bp));
                float pulse = Progress(t, d + 0.6f, 0.5f); // pulse starts at 1.35 / 1.43 / 1.51 s
                if (pulse > 0f && pulse < 1f)
                {
                    float k = (float)CubicBezierEasing.EaseInOut.Evaluate(pulse < 0.5f ? pulse * 2f : (1f - pulse) * 2f);
                    scale *= Mathf.Lerp(1f, 0.72f, k);
                }
                var b = AvinteraArtwork.Bars[i];
                float barH = b.height * scale;
                var r = new Rect(markOrigin.x + b.x * ms, markOrigin.y + (b.center.y - barH * 0.5f) * ms,
                    b.width * ms, barH * ms);
                AvinteraDraw.FillBar(p, r, AvinteraArtwork.GradientTop, AvinteraArtwork.GradientBottom, Mathf.Clamp01(bp * 1.5f));
            }

            // Wordmark: fade + rise 14 units, from 1.25 s for 0.6 s.
            float w = (float)EaseFade.Evaluate(Progress(t, 1.25f, 0.6f));
            if (w > 0f)
            {
                var ink = AvinteraArtwork.Ink;
                ink.a = w;
                float ws = AvinteraArtwork.WordScale;
                var wordOrigin = AvinteraArtwork.WordOffset + new Vector2(0f, (1f - w) * 14f);
                AvinteraDraw.FillPath(p, AvinteraArtwork.Word,
                    (x, y) => origin + (wordOrigin + new Vector2((float)x, (float)y) * ws) * s, ink, FillRule.NonZero);
            }
        }
    }

    /// <summary>Static Avintera mark (head + voice bars) for logos inside the app.</summary>
    public sealed class AvinteraMark : VisualElement
    {
        // Bounds of head + bars in mark space (head 110..398 × 94..422, bars to x 514).
        private static readonly Rect MarkBounds = new Rect(108f, 92f, 410f, 332f);

        public AvinteraMark(float size)
        {
            style.width = size;
            style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0f) return;
            float pad = rect.width * 0.16f;
            float s = Mathf.Min((rect.width - 2 * pad) / MarkBounds.width, (rect.height - 2 * pad) / MarkBounds.height);
            var origin = new Vector2(rect.center.x - MarkBounds.center.x * s, rect.center.y - MarkBounds.center.y * s);
            var p = ctx.painter2D;
            AvinteraDraw.FillPath(p, AvinteraArtwork.Head, (x, y) => origin + new Vector2((float)x, (float)y) * s,
                AvinteraArtwork.Ink, FillRule.OddEven);
            foreach (var b in AvinteraArtwork.Bars)
            {
                AvinteraDraw.FillBar(p, new Rect(origin.x + b.x * s, origin.y + b.y * s, b.width * s, b.height * s),
                    AvinteraArtwork.GradientTop, AvinteraArtwork.GradientBottom, 1f, 16);
            }
        }
    }
}
