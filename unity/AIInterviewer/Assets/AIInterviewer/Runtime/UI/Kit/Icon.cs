using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Kit
{
    public enum IconKind
    {
        Mic, MicOff, CallEnd, Back, Close, Check, Lock, Clock, Home, History, Chart, User, Sparkle, Wifi, WifiOff,
        ChevronRight, ChevronDown, Refresh, Stop, Trash, Document, Headphones, Hand, Crown, Info, Warning, Play, Eye, Shield, Logout
    }

    /// <summary>
    /// Resolution-independent vector icons drawn with Painter2D (no texture assets, crisp on every DPI).
    /// Coordinates are authored on a 24x24 grid and scaled to the element size.
    /// </summary>
    public sealed class Icon : VisualElement
    {
        private IconKind _kind;
        private Color _color;

        public Icon(IconKind kind, float size = 22f, Color? color = null)
        {
            _kind = kind;
            _color = color ?? new Color32(0xF3, 0xF6, 0xFC, 0xFF);
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public IconKind Kind
        {
            get => _kind;
            set { _kind = value; MarkDirtyRepaint(); }
        }

        public Color Color
        {
            get => _color;
            set { _color = value; MarkDirtyRepaint(); }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
            if (s <= 0f) return;
            var p = ctx.painter2D;
            p.strokeColor = _color;
            p.fillColor = _color;
            p.lineWidth = 2f * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            Vector2 V(float x, float y) => new Vector2(x * s, y * s);

            void Line(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.Stroke();
            }

            void Circle(float cx, float cy, float r, bool fill = false)
            {
                p.BeginPath();
                p.Arc(V(cx, cy), r * s, 0f, 360f);
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }

            void Arc(float cx, float cy, float r, float from, float to)
            {
                p.BeginPath();
                p.Arc(V(cx, cy), r * s, from, to);
                p.Stroke();
            }

            void RoundRect(float x, float y, float w, float h, float r, bool fill = false)
            {
                p.BeginPath();
                p.MoveTo(V(x + r, y));
                p.LineTo(V(x + w - r, y));
                p.ArcTo(V(x + w, y), V(x + w, y + r), r * s);
                p.LineTo(V(x + w, y + h - r));
                p.ArcTo(V(x + w, y + h), V(x + w - r, y + h), r * s);
                p.LineTo(V(x + r, y + h));
                p.ArcTo(V(x, y + h), V(x, y + h - r), r * s);
                p.LineTo(V(x, y + r));
                p.ArcTo(V(x, y), V(x + r, y), r * s);
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }

            switch (_kind)
            {
                case IconKind.Mic:
                    RoundRect(9f, 3f, 6f, 11f, 3f);
                    Arc(12f, 11f, 6f, 0f, 180f);
                    Line(V(12f, 17f), V(12f, 21f));
                    break;
                case IconKind.MicOff:
                    RoundRect(9f, 3f, 6f, 11f, 3f);
                    Arc(12f, 11f, 6f, 0f, 180f);
                    Line(V(12f, 17f), V(12f, 21f));
                    Line(V(4f, 4f), V(20f, 20f));
                    break;
                case IconKind.CallEnd:
                    p.lineWidth = 2.6f * s;
                    Arc(12f, 15f, 9f, 200f, 340f);
                    Line(V(3.8f, 12f), V(4.5f, 15.5f), V(8f, 15f));
                    Line(V(20.2f, 12f), V(19.5f, 15.5f), V(16f, 15f));
                    break;
                case IconKind.Back:
                    Line(V(15f, 5f), V(8f, 12f), V(15f, 19f));
                    break;
                case IconKind.ChevronRight:
                    Line(V(9f, 5f), V(16f, 12f), V(9f, 19f));
                    break;
                case IconKind.ChevronDown:
                    Line(V(6f, 9f), V(12f, 15f), V(18f, 9f));
                    break;
                case IconKind.Close:
                    Line(V(6f, 6f), V(18f, 18f));
                    Line(V(18f, 6f), V(6f, 18f));
                    break;
                case IconKind.Check:
                    Line(V(5f, 12.5f), V(10f, 17.5f), V(19f, 7f));
                    break;
                case IconKind.Lock:
                    RoundRect(5f, 11f, 14f, 10f, 2f);
                    Arc(12f, 11f, 4f, 180f, 360f);
                    Line(V(8f, 11f), V(8f, 8f));
                    Line(V(16f, 11f), V(16f, 8f));
                    break;
                case IconKind.Clock:
                    Circle(12f, 12f, 9f);
                    Line(V(12f, 7f), V(12f, 12f), V(15.5f, 14f));
                    break;
                case IconKind.Home:
                    Line(V(3.5f, 11f), V(12f, 4f), V(20.5f, 11f));
                    Line(V(6f, 9.5f), V(6f, 20f), V(18f, 20f), V(18f, 9.5f));
                    break;
                case IconKind.History:
                    Arc(12f, 12f, 8.5f, 200f, 520f);
                    Line(V(3.2f, 8.5f), V(3.5f, 12.5f), V(7f, 11f));
                    Line(V(12f, 8f), V(12f, 12f), V(15f, 14f));
                    break;
                case IconKind.Chart:
                    Line(V(4f, 4f), V(4f, 20f), V(20f, 20f));
                    Line(V(7f, 15f), V(11f, 10f), V(14f, 13f), V(19f, 6f));
                    break;
                case IconKind.User:
                    Circle(12f, 8f, 4f);
                    Arc(12f, 21f, 7.5f, 180f, 360f);
                    break;
                case IconKind.Sparkle:
                    p.BeginPath();
                    p.MoveTo(V(12f, 2.5f));
                    p.QuadraticCurveTo(V(13.2f, 10.8f), V(21.5f, 12f));
                    p.QuadraticCurveTo(V(13.2f, 13.2f), V(12f, 21.5f));
                    p.QuadraticCurveTo(V(10.8f, 13.2f), V(2.5f, 12f));
                    p.QuadraticCurveTo(V(10.8f, 10.8f), V(12f, 2.5f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case IconKind.Wifi:
                    Arc(12f, 19f, 13f, 225f, 315f);
                    Arc(12f, 19f, 8.5f, 225f, 315f);
                    Arc(12f, 19f, 4f, 225f, 315f);
                    Circle(12f, 19f, 1.2f, true);
                    break;
                case IconKind.WifiOff:
                    Arc(12f, 19f, 13f, 225f, 315f);
                    Arc(12f, 19f, 8.5f, 225f, 315f);
                    Circle(12f, 19f, 1.2f, true);
                    Line(V(4f, 4f), V(20f, 20f));
                    break;
                case IconKind.Refresh:
                    Arc(12f, 12f, 7.5f, 300f, 600f);
                    Line(V(15.5f, 3.5f), V(16.2f, 6.2f), V(13.4f, 7f));
                    break;
                case IconKind.Trash:
                    Line(V(4f, 6.5f), V(20f, 6.5f));
                    Line(V(9f, 6.5f), V(9.5f, 3.5f), V(14.5f, 3.5f), V(15f, 6.5f));
                    Line(V(6f, 6.5f), V(7f, 20.5f), V(17f, 20.5f), V(18f, 6.5f));
                    break;
                case IconKind.Document:
                    Line(V(6f, 3f), V(14f, 3f), V(19f, 8f), V(19f, 21f), V(6f, 21f), V(6f, 3f));
                    Line(V(9f, 12f), V(16f, 12f));
                    Line(V(9f, 16f), V(16f, 16f));
                    break;
                case IconKind.Headphones:
                    Arc(12f, 13f, 8f, 180f, 360f);
                    RoundRect(3.5f, 13f, 4f, 7f, 1.5f);
                    RoundRect(16.5f, 13f, 4f, 7f, 1.5f);
                    break;
                case IconKind.Hand:
                    Line(V(8f, 13f), V(8f, 6f));
                    Line(V(11f, 12f), V(11f, 4f));
                    Line(V(14f, 12f), V(14f, 5f));
                    Line(V(17f, 13f), V(17f, 8f));
                    Line(V(17f, 13f), V(17f, 16f), V(14f, 21f), V(10f, 21f), V(5f, 15f), V(8f, 13f));
                    break;
                case IconKind.Crown:
                    Line(V(4f, 18f), V(3f, 8f), V(8.5f, 12f), V(12f, 5f), V(15.5f, 12f), V(21f, 8f), V(20f, 18f), V(4f, 18f));
                    break;
                case IconKind.Info:
                    Circle(12f, 12f, 9f);
                    Line(V(12f, 11f), V(12f, 16.5f));
                    Circle(12f, 7.8f, 0.9f, true);
                    break;
                case IconKind.Warning:
                    Line(V(12f, 3.5f), V(21.5f, 20f), V(2.5f, 20f), V(12f, 3.5f));
                    Line(V(12f, 9.5f), V(12f, 14f));
                    Circle(12f, 17f, 0.9f, true);
                    break;
                case IconKind.Stop:
                    p.BeginPath();
                    p.MoveTo(V(7f, 7f));
                    p.LineTo(V(17f, 7f));
                    p.LineTo(V(17f, 17f));
                    p.LineTo(V(7f, 17f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case IconKind.Play:
                    p.BeginPath();
                    p.MoveTo(V(8f, 5f));
                    p.LineTo(V(19f, 12f));
                    p.LineTo(V(8f, 19f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case IconKind.Eye:
                    p.BeginPath();
                    p.MoveTo(V(2.5f, 12f));
                    p.QuadraticCurveTo(V(12f, 2f), V(21.5f, 12f));
                    p.QuadraticCurveTo(V(12f, 22f), V(2.5f, 12f));
                    p.Stroke();
                    Circle(12f, 12f, 3f);
                    break;
                case IconKind.Shield:
                    p.BeginPath();
                    p.MoveTo(V(12f, 3f));
                    p.LineTo(V(19.5f, 6f));
                    p.QuadraticCurveTo(V(19.5f, 17f), V(12f, 21f));
                    p.QuadraticCurveTo(V(4.5f, 17f), V(4.5f, 6f));
                    p.ClosePath();
                    p.Stroke();
                    Line(V(8.5f, 12f), V(11f, 14.5f), V(15.5f, 9.5f));
                    break;
                case IconKind.Logout:
                    Line(V(14f, 4f), V(5f, 4f), V(5f, 20f), V(14f, 20f));
                    Line(V(10f, 12f), V(20f, 12f));
                    Line(V(16.5f, 8.5f), V(20f, 12f), V(16.5f, 15.5f));
                    break;
            }
        }
    }
}
