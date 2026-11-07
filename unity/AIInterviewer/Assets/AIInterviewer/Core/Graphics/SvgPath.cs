using System;
using System.Collections.Generic;
using System.Globalization;

namespace AIInterviewer.Core.Graphics
{
    public enum PathOp
    {
        MoveTo,
        LineTo,
        CubicTo,
        Close
    }

    /// <summary>One drawing command in absolute coordinates. Cubic uses C1, C2 and P.</summary>
    public struct PathCommand
    {
        public PathOp Op;
        public double C1X, C1Y, C2X, C2Y, X, Y;

        public override string ToString() => $"{Op} {X:0.##},{Y:0.##}";
    }

    /// <summary>
    /// Minimal SVG path-data parser (M L H V C S Q T A Z, absolute and relative, implicit repeats).
    /// Everything is normalised to MoveTo / LineTo / CubicTo / Close so any vector API can draw it;
    /// quadratic curves and elliptical arcs are converted to cubic Béziers.
    /// </summary>
    public static class SvgPath
    {
        public static List<PathCommand> Parse(string d)
        {
            var result = new List<PathCommand>();
            var t = new Tokenizer(d);
            char cmd = ' ';
            double cx = 0, cy = 0, startX = 0, startY = 0;
            double lastCtrlX = 0, lastCtrlY = 0;
            char lastCmd = ' ';

            while (t.SkipSeparators())
            {
                if (t.PeekCommand(out char c))
                {
                    cmd = c;
                    t.Advance();
                }
                else if (cmd == ' ')
                {
                    throw new FormatException("Path data must start with a command");
                }
                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);
                switch (up)
                {
                    case 'M':
                    {
                        double x = t.Number(), y = t.Number();
                        if (rel) { x += cx; y += cy; }
                        result.Add(new PathCommand { Op = PathOp.MoveTo, X = x, Y = y });
                        cx = startX = x;
                        cy = startY = y;
                        cmd = rel ? 'l' : 'L'; // following pairs are implicit line-tos
                        break;
                    }
                    case 'L':
                    {
                        double x = t.Number(), y = t.Number();
                        if (rel) { x += cx; y += cy; }
                        Line(result, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'H':
                    {
                        double x = t.Number();
                        if (rel) x += cx;
                        Line(result, x, cy);
                        cx = x;
                        break;
                    }
                    case 'V':
                    {
                        double y = t.Number();
                        if (rel) y += cy;
                        Line(result, cx, y);
                        cy = y;
                        break;
                    }
                    case 'C':
                    {
                        double x1 = t.Number(), y1 = t.Number(), x2 = t.Number(), y2 = t.Number(), x = t.Number(), y = t.Number();
                        if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                        Cubic(result, x1, y1, x2, y2, x, y);
                        lastCtrlX = x2; lastCtrlY = y2;
                        cx = x; cy = y;
                        break;
                    }
                    case 'S':
                    {
                        double x2 = t.Number(), y2 = t.Number(), x = t.Number(), y = t.Number();
                        if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; }
                        bool smooth = lastCmd == 'C' || lastCmd == 'S';
                        double x1 = smooth ? 2 * cx - lastCtrlX : cx, y1 = smooth ? 2 * cy - lastCtrlY : cy;
                        Cubic(result, x1, y1, x2, y2, x, y);
                        lastCtrlX = x2; lastCtrlY = y2;
                        cx = x; cy = y;
                        break;
                    }
                    case 'Q':
                    {
                        double qx = t.Number(), qy = t.Number(), x = t.Number(), y = t.Number();
                        if (rel) { qx += cx; qy += cy; x += cx; y += cy; }
                        Quad(result, cx, cy, qx, qy, x, y);
                        lastCtrlX = qx; lastCtrlY = qy;
                        cx = x; cy = y;
                        break;
                    }
                    case 'T':
                    {
                        double x = t.Number(), y = t.Number();
                        if (rel) { x += cx; y += cy; }
                        bool smooth = lastCmd == 'Q' || lastCmd == 'T';
                        double qx = smooth ? 2 * cx - lastCtrlX : cx, qy = smooth ? 2 * cy - lastCtrlY : cy;
                        Quad(result, cx, cy, qx, qy, x, y);
                        lastCtrlX = qx; lastCtrlY = qy;
                        cx = x; cy = y;
                        break;
                    }
                    case 'A':
                    {
                        double rx = t.Number(), ry = t.Number(), rot = t.Number();
                        bool large = t.Flag(), sweep = t.Flag();
                        double x = t.Number(), y = t.Number();
                        if (rel) { x += cx; y += cy; }
                        Arc(result, cx, cy, rx, ry, rot, large, sweep, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'Z':
                        result.Add(new PathCommand { Op = PathOp.Close, X = startX, Y = startY });
                        cx = startX; cy = startY;
                        break;
                    default:
                        throw new FormatException("Unsupported path command: " + cmd);
                }
                lastCmd = up;
            }
            return result;
        }

        /// <summary>Axis-aligned bounds of the path's points (control points included).</summary>
        public static (double minX, double minY, double maxX, double maxY) Bounds(IEnumerable<PathCommand> path)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in path)
            {
                if (c.Op == PathOp.Close) continue;
                Extend(c.X, c.Y);
                if (c.Op == PathOp.CubicTo)
                {
                    Extend(c.C1X, c.C1Y);
                    Extend(c.C2X, c.C2Y);
                }
            }
            return (minX, minY, maxX, maxY);

            void Extend(double x, double y)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        private static void Line(List<PathCommand> r, double x, double y) =>
            r.Add(new PathCommand { Op = PathOp.LineTo, X = x, Y = y });

        private static void Cubic(List<PathCommand> r, double x1, double y1, double x2, double y2, double x, double y) =>
            r.Add(new PathCommand { Op = PathOp.CubicTo, C1X = x1, C1Y = y1, C2X = x2, C2Y = y2, X = x, Y = y });

        private static void Quad(List<PathCommand> r, double x0, double y0, double qx, double qy, double x, double y) =>
            Cubic(r, x0 + 2.0 / 3.0 * (qx - x0), y0 + 2.0 / 3.0 * (qy - y0),
                x + 2.0 / 3.0 * (qx - x), y + 2.0 / 3.0 * (qy - y), x, y);

        /// <summary>SVG endpoint arc → centre parameterisation → cubic segments of at most 90°.</summary>
        private static void Arc(List<PathCommand> r, double x1, double y1, double rx, double ry, double angleDeg,
            bool largeArc, bool sweep, double x2, double y2)
        {
            if (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9) return;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx < 1e-9 || ry < 1e-9)
            {
                Line(r, x2, y2);
                return;
            }
            double phi = angleDeg * Math.PI / 180.0, cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
            double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
            double x1p = cosPhi * dx + sinPhi * dy, y1p = -sinPhi * dx + cosPhi * dy;
            double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1)
            {
                double s = Math.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }
            double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (largeArc == sweep ? -1 : 1);
            double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
            double ccx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2, ccy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2;

            double theta1 = VectorAngle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double delta = VectorAngle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            else if (sweep && delta < 0) delta += 2 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
            double step = delta / Math.Max(1, segments);
            double k = 4.0 / 3.0 * Math.Tan(step / 4);
            double a = theta1;
            for (int i = 0; i < segments; i++)
            {
                double a2 = a + step;
                double cos1 = Math.Cos(a), sin1 = Math.Sin(a), cos2 = Math.Cos(a2), sin2 = Math.Sin(a2);
                (double px, double py) P(double ex, double ey) =>
                    (ccx + rx * ex * cosPhi - ry * ey * sinPhi, ccy + rx * ex * sinPhi + ry * ey * cosPhi);
                var c1 = P(cos1 - k * sin1, sin1 + k * cos1);
                var c2 = P(cos2 + k * sin2, sin2 - k * cos2);
                var end = i == segments - 1 ? (x2, y2) : P(cos2, sin2);
                Cubic(r, c1.px, c1.py, c2.px, c2.py, end.Item1, end.Item2);
                a = a2;
            }
        }

        private static double VectorAngle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy, len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double ang = Math.Acos(Math.Max(-1, Math.Min(1, dot / len)));
            return ux * vy - uy * vx < 0 ? -ang : ang;
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _i;

            public Tokenizer(string s)
            {
                _s = s ?? string.Empty;
            }

            public bool SkipSeparators()
            {
                while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',')) _i++;
                return _i < _s.Length;
            }

            public bool PeekCommand(out char c)
            {
                c = _s[_i];
                return char.IsLetter(c) && c != 'e' && c != 'E';
            }

            public void Advance() => _i++;

            public bool Flag()
            {
                SkipSeparators();
                char c = _s[_i++];
                if (c != '0' && c != '1') throw new FormatException("Invalid arc flag");
                return c == '1';
            }

            public double Number()
            {
                SkipSeparators();
                int start = _i;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                bool dot = false, exp = false;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (char.IsDigit(c)) { _i++; continue; }
                    if (c == '.' && !dot && !exp) { dot = true; _i++; continue; }
                    if ((c == 'e' || c == 'E') && !exp)
                    {
                        exp = true;
                        _i++;
                        if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                        continue;
                    }
                    break;
                }
                if (_i == start) throw new FormatException("Expected a number at " + start);
                return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
