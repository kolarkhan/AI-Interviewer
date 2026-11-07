using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Kit
{
    public static class Palette
    {
        public static readonly Color Bg = new Color32(0x0B, 0x11, 0x20, 0xFF);
        public static readonly Color Surface = new Color32(0x16, 0x1F, 0x35, 0xFF);
        public static readonly Color Surface3 = new Color32(0x24, 0x31, 0x4F, 0xFF);
        public static readonly Color Text = new Color32(0xF3, 0xF6, 0xFC, 0xFF);
        public static readonly Color TextSecondary = new Color32(0xAA, 0xB5, 0xCD, 0xFF);
        public static readonly Color TextMuted = new Color32(0x6F, 0x7C, 0x98, 0xFF);
        public static readonly Color Accent = new Color32(0x5B, 0x8C, 0xFF, 0xFF);
        public static readonly Color Accent2 = new Color32(0x8B, 0x6C, 0xFF, 0xFF);
        public static readonly Color Success = new Color32(0x34, 0xD3, 0x99, 0xFF);
        public static readonly Color Warning = new Color32(0xFB, 0xBF, 0x24, 0xFF);
        public static readonly Color Danger = new Color32(0xF8, 0x71, 0x71, 0xFF);
        public static readonly Color Gold = new Color32(0xF5, 0xC7, 0x6B, 0xFF);

        public static Color ForScore(int score) => score >= 75 ? Success : score >= 55 ? Accent : score >= 40 ? Warning : Danger;
    }

    /// <summary>Animated circular score gauge.</summary>
    public sealed class ScoreRing : VisualElement
    {
        private readonly Label _value;
        private readonly Label _caption;
        private float _shown;
        private float _target;
        private IVisualElementScheduledItem _anim;

        public ScoreRing(float size = 150f, string caption = "Overall")
        {
            style.width = size;
            style.height = size;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            generateVisualContent += Draw;
            _value = new Label("–");
            _value.style.fontSize = size * 0.27f;
            _value.style.unityFontStyleAndWeight = FontStyle.Bold;
            _caption = new Label(caption);
            _caption.AddToClassList("caption");
            Add(_value);
            Add(_caption);
        }

        public void SetScore(int score, bool animate = true)
        {
            _target = Mathf.Clamp(score, 0, 100);
            if (!animate)
            {
                _shown = _target;
                Refresh();
                return;
            }
            _anim?.Pause();
            _anim = schedule.Execute(() =>
            {
                _shown = Mathf.MoveTowards(_shown, _target, Mathf.Max(1f, Mathf.Abs(_target - _shown) * 0.12f));
                Refresh();
                if (Mathf.Approximately(_shown, _target)) _anim.Pause();
            }).Every(16);
        }

        private void Refresh()
        {
            _value.text = Mathf.RoundToInt(_shown).ToString();
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float stroke = r.width * 0.075f;
            var c = r.center;
            float radius = r.width * 0.5f - stroke;
            var p = ctx.painter2D;
            p.lineWidth = stroke;
            p.lineCap = LineCap.Round;
            p.strokeColor = Palette.Surface3;
            p.BeginPath();
            p.Arc(c, radius, 0f, 360f);
            p.Stroke();
            if (_shown <= 0f) return;
            p.strokeColor = Palette.ForScore(Mathf.RoundToInt(_target));
            p.BeginPath();
            p.Arc(c, radius, -90f, -90f + 360f * (_shown / 100f));
            p.Stroke();
        }
    }

    /// <summary>Indeterminate spinner.</summary>
    public sealed class Spinner : VisualElement
    {
        private float _angle;

        public Spinner(float size = 28f, Color? color = null)
        {
            style.width = size;
            style.height = size;
            var col = color ?? Palette.Accent;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.lineWidth = size * 0.11f;
                p.lineCap = LineCap.Round;
                p.strokeColor = new Color(col.r, col.g, col.b, 0.18f);
                p.BeginPath();
                p.Arc(contentRect.center, size * 0.4f, 0f, 360f);
                p.Stroke();
                p.strokeColor = col;
                p.BeginPath();
                p.Arc(contentRect.center, size * 0.4f, _angle, _angle + 100f);
                p.Stroke();
            };
            schedule.Execute(() =>
            {
                _angle = (_angle + 9f) % 360f;
                MarkDirtyRepaint();
            }).Every(16);
            pickingMode = PickingMode.Ignore;
        }
    }

    /// <summary>Bar-style voice level visualiser (driven every frame by a level provider).</summary>
    public sealed class VoiceMeter : VisualElement
    {
        private const int Bars = 5;
        private readonly float[] _levels = new float[Bars];
        private readonly Func<float> _source;
        private Color _color;

        public VoiceMeter(Func<float> levelSource, Color color, float width = 44f, float height = 22f)
        {
            _source = levelSource;
            _color = color;
            style.width = width;
            style.height = height;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(Tick).Every(33);
        }

        public Color Color
        {
            get => _color;
            set { _color = value; MarkDirtyRepaint(); }
        }

        private void Tick()
        {
            float level = Mathf.Clamp01(_source?.Invoke() ?? 0f);
            float t = Time.unscaledTime;
            for (int i = 0; i < Bars; i++)
            {
                float shape = 0.55f + 0.45f * Mathf.Sin(t * (7f + i * 1.7f) + i * 1.3f);
                float centreWeight = 1f - Mathf.Abs(i - (Bars - 1) * 0.5f) / Bars;
                float target = Mathf.Clamp01(0.12f + level * shape * (0.6f + centreWeight));
                _levels[i] = Mathf.Lerp(_levels[i], target, 0.45f);
            }
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float gap = r.width / (Bars * 2f);
            float barW = gap;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            p.lineWidth = barW;
            p.strokeColor = _color;
            for (int i = 0; i < Bars; i++)
            {
                float x = gap * 0.5f + i * gap * 2f + barW * 0.5f;
                float h = Mathf.Max(barW, _levels[i] * r.height);
                p.BeginPath();
                p.MoveTo(new Vector2(x, r.height * 0.5f - h * 0.5f + barW * 0.5f));
                p.LineTo(new Vector2(x, r.height * 0.5f + h * 0.5f - barW * 0.5f));
                p.Stroke();
            }
        }
    }

    /// <summary>Simple line chart for score trends (0..100).</summary>
    public sealed class LineChart : VisualElement
    {
        private IReadOnlyList<int> _values = Array.Empty<int>();

        public LineChart(float height = 160f)
        {
            style.height = height;
            generateVisualContent += Draw;
        }

        public void SetValues(IReadOnlyList<int> values)
        {
            _values = values ?? Array.Empty<int>();
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            var p = ctx.painter2D;
            float padX = 8f, padY = 10f;
            float w = r.width - padX * 2f, h = r.height - padY * 2f;

            p.lineWidth = 1f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.06f);
            for (int g = 0; g <= 4; g++)
            {
                float y = padY + h * g / 4f;
                p.BeginPath();
                p.MoveTo(new Vector2(padX, y));
                p.LineTo(new Vector2(padX + w, y));
                p.Stroke();
            }
            if (_values.Count == 0) return;

            Vector2 Point(int i) => new Vector2(
                padX + (_values.Count == 1 ? w * 0.5f : w * i / (_values.Count - 1)),
                padY + h * (1f - Mathf.Clamp01(_values[i] / 100f)));

            p.lineWidth = 3f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = Palette.Accent;
            p.BeginPath();
            p.MoveTo(Point(0));
            for (int i = 1; i < _values.Count; i++) p.LineTo(Point(i));
            p.Stroke();

            for (int i = 0; i < _values.Count; i++)
            {
                p.fillColor = i == _values.Count - 1 ? Palette.Accent2 : Palette.Accent;
                p.BeginPath();
                p.Arc(Point(i), i == _values.Count - 1 ? 6f : 4f, 0f, 360f);
                p.Fill();
            }
        }
    }

    /// <summary>iOS-style switch.</summary>
    public sealed class Switch : VisualElement
    {
        private bool _value;

        public Switch(bool value, Action<bool> onChanged)
        {
            AddToClassList("switch");
            var knob = new VisualElement();
            knob.AddToClassList("switch__knob");
            knob.pickingMode = PickingMode.Ignore;
            Add(knob);
            SetValueWithoutNotify(value);
            RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation(); // the switch handles its own tap; don't also trigger the row's action
                SetValueWithoutNotify(!_value);
                onChanged?.Invoke(_value);
            });
        }

        public bool Value => _value;

        public void SetValueWithoutNotify(bool value)
        {
            _value = value;
            EnableInClassList("switch--on", value);
        }
    }

    public sealed class ProgressTrack : VisualElement
    {
        private readonly VisualElement _fill;

        public ProgressTrack(float value01 = 0f, string variant = null, bool thin = false)
        {
            AddToClassList("progress");
            if (thin) AddToClassList("progress--thin");
            if (!string.IsNullOrEmpty(variant)) AddToClassList("progress--" + variant);
            _fill = new VisualElement();
            _fill.AddToClassList("progress__fill");
            Add(_fill);
            // Set after the first layout so the width transition animates.
            schedule.Execute(() => SetValue(value01)).StartingIn(50);
        }

        public void SetValue(float value01)
        {
            _fill.style.width = Length.Percent(Mathf.Clamp01(value01) * 100f);
        }
    }
}
