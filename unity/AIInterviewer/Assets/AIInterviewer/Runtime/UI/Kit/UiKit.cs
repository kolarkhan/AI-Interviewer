using System;
using AIInterviewer.Scene;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Kit
{
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Gold
    }

    /// <summary>One choice in a dropdown sheet.</summary>
    public sealed class PickerItem
    {
        public PickerItem(string id, string title, string description = null, bool locked = false)
        {
            Id = id;
            Title = title;
            Description = description;
            Locked = locked;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public bool Locked { get; }
    }

    /// <summary>Factory helpers for consistently styled UI elements (classes defined in Resources/UI/Theme.uss).</summary>
    public static class UiKit
    {
        public static Label Text(string text, string classes = "body")
        {
            var l = new Label(text);
            foreach (var c in classes.Split(' ')) if (c.Length > 0) l.AddToClassList(c);
            return l;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var r = new VisualElement();
            r.AddToClassList("row");
            foreach (var c in children) r.Add(c);
            return r;
        }

        public static VisualElement Column(params VisualElement[] children)
        {
            var r = new VisualElement();
            r.AddToClassList("col");
            foreach (var c in children) r.Add(c);
            return r;
        }

        public static VisualElement Spacer(float height = 12f, bool grow = false)
        {
            var s = new VisualElement();
            s.style.height = height;
            s.style.flexShrink = 0;
            if (grow) s.style.flexGrow = 1;
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        public static VisualElement HSpacer(float width = 8f)
        {
            var s = new VisualElement();
            s.style.width = width;
            s.style.flexShrink = 0;
            return s;
        }

        public static VisualElement Card(string extraClass = null)
        {
            var c = new VisualElement();
            c.AddToClassList("card");
            if (!string.IsNullOrEmpty(extraClass))
            {
                foreach (var cls in extraClass.Split(' ')) c.AddToClassList(cls);
            }
            return c;
        }

        public static Button Button(string text, Action onClick, ButtonStyle style = ButtonStyle.Primary,
            IconKind? icon = null, bool small = false)
        {
            var b = new Button(() => onClick?.Invoke()) { text = string.Empty };
            b.AddToClassList("btn");
            if (style != ButtonStyle.Primary) b.AddToClassList("btn--" + style.ToString().ToLowerInvariant());
            if (small) b.AddToClassList("btn--small");
            if (icon.HasValue)
            {
                Color iconColor = style == ButtonStyle.Ghost ? Palette.Accent :
                    style == ButtonStyle.Gold ? (Color)new Color32(0x1B, 0x14, 0x05, 0xFF) : Palette.Text;
                var i = new Icon(icon.Value, small ? 16f : 20f, iconColor);
                i.AddToClassList("btn__icon");
                b.Add(i);
            }
            var label = new Label(text);
            label.AddToClassList("btn__label");
            label.pickingMode = PickingMode.Ignore;
            b.Add(label);
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var label = b.Q<Label>(className: "btn__label");
            if (label != null) label.text = text;
        }

        /// <summary>Puts a button in a loading state (spinner, disabled) and returns a restore action.</summary>
        public static Action SetLoading(Button b, string loadingText = null)
        {
            var label = b.Q<Label>(className: "btn__label");
            string original = label?.text;
            b.SetEnabled(false);
            if (label != null && loadingText != null) label.text = loadingText;
            var spinner = new Spinner(18f, Color.white);
            spinner.style.marginRight = 8;
            b.Insert(0, spinner);
            return () =>
            {
                b.SetEnabled(true);
                if (label != null) label.text = original;
                spinner.RemoveFromHierarchy();
            };
        }

        public static Button IconButton(IconKind icon, Action onClick, float size = 22f, Color? color = null)
        {
            var b = new Button(() => onClick?.Invoke()) { text = string.Empty };
            b.AddToClassList("icon-btn");
            b.Add(new Icon(icon, size, color));
            return b;
        }

        public static VisualElement TopBar(string title, Action onBack, VisualElement trailing = null)
        {
            var bar = new VisualElement();
            bar.AddToClassList("top-bar");
            var left = new VisualElement();
            left.AddToClassList("top-bar__side");
            if (onBack != null) left.Add(IconButton(IconKind.Back, onBack));
            var t = Text(title, "top-bar__title");
            var right = new VisualElement();
            right.AddToClassList("top-bar__side");
            if (trailing != null) right.Add(trailing);
            bar.Add(left);
            bar.Add(t);
            bar.Add(right);
            return bar;
        }

        public static (VisualElement container, TextField input, Label error) Field(string label, string placeholder,
            bool password = false, TouchScreenKeyboardType keyboard = TouchScreenKeyboardType.Default, bool multiline = false)
        {
            var c = new VisualElement();
            c.AddToClassList("field");
            if (!string.IsNullOrEmpty(label)) c.Add(Text(label, "field__label"));
            var input = new TextField { isPasswordField = password, multiline = multiline };
            input.AddToClassList("input");
            if (multiline) input.AddToClassList("input--multiline");
            input.keyboardType = keyboard;
            input.textEdition.placeholder = placeholder;
            input.textEdition.hidePlaceholderOnFocus = true;
            c.Add(input);
            var err = Text(string.Empty, "field__error");
            err.style.display = DisplayStyle.None;
            c.Add(err);
            return (c, input, err);
        }

        public static void SetFieldError(TextField input, Label error, string message)
        {
            bool has = !string.IsNullOrEmpty(message);
            input.EnableInClassList("input--error", has);
            error.text = message ?? string.Empty;
            error.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static VisualElement Chip(string text, bool selected, bool locked, Action onClick)
        {
            var chip = new VisualElement();
            chip.AddToClassList("chip");
            chip.EnableInClassList("chip--selected", selected);
            chip.EnableInClassList("chip--locked", locked);
            if (locked)
            {
                var lockIcon = new Icon(IconKind.Lock, 14f, Palette.Gold);
                lockIcon.AddToClassList("chip__icon");
                chip.Add(lockIcon);
            }
            var label = Text(text, "chip__label");
            label.pickingMode = PickingMode.Ignore;
            chip.Add(label);
            chip.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return chip;
        }

        public static VisualElement OptionCard(string title, string description, bool selected, bool locked, Action onClick)
        {
            var card = new VisualElement();
            card.AddToClassList("option-card");
            card.EnableInClassList("option-card--selected", selected);
            card.EnableInClassList("option-card--locked", locked);
            var radio = new VisualElement();
            radio.AddToClassList("option-card__radio");
            var dot = new VisualElement();
            dot.AddToClassList("option-card__dot");
            radio.Add(dot);
            card.Add(radio);
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.Add(Text(title, "h3"));
            if (!string.IsNullOrEmpty(description)) body.Add(Text(description, "caption"));
            card.Add(body);
            if (locked) card.Add(new Icon(IconKind.Lock, 16f, Palette.Gold));
            card.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return card;
        }

        public static VisualElement Badge(string text, string variant = null, IconKind? icon = null)
        {
            var b = new VisualElement();
            b.AddToClassList("badge");
            if (!string.IsNullOrEmpty(variant)) b.AddToClassList("badge--" + variant);
            if (icon.HasValue)
            {
                var color = variant == "gold" ? Palette.Gold : variant == "success" ? Palette.Success :
                    variant == "warning" ? Palette.Warning : variant == "danger" ? Palette.Danger : Palette.Accent;
                var i = new Icon(icon.Value, 12f, color);
                i.style.marginRight = 4;
                b.Add(i);
            }
            b.Add(Text(text, "badge__label"));
            return b;
        }

        public static VisualElement Banner(string text, string variant = "info", IconKind icon = IconKind.Info)
        {
            var b = new VisualElement();
            b.AddToClassList("banner");
            b.AddToClassList("banner--" + variant);
            var color = variant == "warning" ? Palette.Warning : variant == "danger" ? Palette.Danger : Palette.Accent;
            b.Add(new Icon(icon, 18f, color));
            b.Add(Text(text, "banner__text"));
            return b;
        }

        public static VisualElement ListItem(string title, string subtitle, IconKind icon, VisualElement trailing,
            Action onClick, Color? iconColor = null)
        {
            var item = new VisualElement();
            item.AddToClassList("list-item");
            var iconBox = new VisualElement();
            iconBox.AddToClassList("list-item__icon");
            iconBox.Add(new Icon(icon, 20f, iconColor ?? Palette.Accent));
            item.Add(iconBox);
            var body = new VisualElement();
            body.AddToClassList("list-item__body");
            body.Add(Text(title, "list-item__title"));
            if (!string.IsNullOrEmpty(subtitle)) body.Add(Text(subtitle, "list-item__subtitle"));
            item.Add(body);
            if (trailing != null)
            {
                var t = new VisualElement();
                t.AddToClassList("list-item__trailing");
                t.Add(trailing);
                item.Add(t);
            }
            if (onClick != null) item.RegisterCallback<ClickEvent>(_ => onClick());
            return item;
        }

        public static VisualElement SettingsRow(string title, string subtitle, VisualElement trailing, Action onClick = null,
            bool danger = false)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            if (danger) row.AddToClassList("settings-row--danger");
            var text = new VisualElement();
            text.AddToClassList("settings-row__text");
            text.Add(Text(title, "settings-row__title"));
            if (!string.IsNullOrEmpty(subtitle)) text.Add(Text(subtitle, "settings-row__subtitle"));
            row.Add(text);
            if (trailing != null) row.Add(trailing);
            if (onClick != null) row.RegisterCallback<ClickEvent>(_ => onClick());
            return row;
        }

        public static VisualElement SettingsGroup(params VisualElement[] rows)
        {
            var g = new VisualElement();
            g.AddToClassList("settings-group");
            for (int i = 0; i < rows.Length; i++)
            {
                if (i == rows.Length - 1) rows[i].AddToClassList("settings-row--last");
                g.Add(rows[i]);
            }
            return g;
        }

        public static VisualElement Bullet(string text, IconKind icon, Color color)
        {
            var row = new VisualElement();
            row.AddToClassList("bullet");
            var i = new Icon(icon, 16f, color);
            i.AddToClassList("bullet__icon");
            row.Add(i);
            row.Add(Text(text, "bullet__text"));
            return row;
        }

        public static VisualElement Metric(string value, string label, bool last = false, Color? valueColor = null)
        {
            var m = new VisualElement();
            m.AddToClassList("metric");
            if (last) m.AddToClassList("metric--last");
            var v = Text(value, "metric__value");
            if (valueColor.HasValue) v.style.color = valueColor.Value;
            m.Add(v);
            m.Add(Text(label, "metric__label"));
            return m;
        }

        public static VisualElement StatusView(IconKind icon, string title, string body, string primaryText = null,
            Action primary = null, string secondaryText = null, Action secondary = null, Color? iconColor = null)
        {
            var v = new VisualElement();
            v.AddToClassList("status-view");
            var iconBox = new VisualElement();
            iconBox.AddToClassList("status-view__icon");
            iconBox.Add(new Icon(icon, 34f, iconColor ?? Palette.Accent));
            v.Add(iconBox);
            v.Add(Text(title, "status-view__title"));
            if (!string.IsNullOrEmpty(body)) v.Add(Text(body, "status-view__body"));
            if (primaryText != null)
            {
                var b = Button(primaryText, primary);
                b.AddToClassList("btn--block");
                b.style.minWidth = 220;
                v.Add(b);
            }
            if (secondaryText != null)
            {
                var b = Button(secondaryText, secondary, ButtonStyle.Ghost);
                b.style.marginTop = 6;
                v.Add(b);
            }
            return v;
        }

        /// <summary>Dropdown-style field showing the current choice; <paramref name="onClick"/> opens the option sheet.</summary>
        public static VisualElement SelectField(string value, string caption, Action onClick, VisualElement accessory = null)
        {
            var field = new VisualElement();
            field.AddToClassList("select");
            var body = new VisualElement();
            body.AddToClassList("select__body");
            body.pickingMode = PickingMode.Ignore;
            var v = Text(value, "select__value");
            v.pickingMode = PickingMode.Ignore;
            body.Add(v);
            if (!string.IsNullOrEmpty(caption))
            {
                var c = Text(caption, "select__caption");
                c.pickingMode = PickingMode.Ignore;
                body.Add(c);
            }
            field.Add(body);
            if (accessory != null)
            {
                accessory.style.marginRight = 8;
                field.Add(accessory);
            }
            var chevron = new Icon(IconKind.ChevronDown, 20f, Palette.TextMuted);
            chevron.pickingMode = PickingMode.Ignore;
            field.Add(chevron);
            field.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return field;
        }

        public static VisualElement Dots(int count, int active)
        {
            var d = new VisualElement();
            d.AddToClassList("dots");
            for (int i = 0; i < count; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("dot");
                dot.EnableInClassList("dot--active", i == active);
                d.Add(dot);
            }
            return d;
        }

        public static ScrollView Scroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.AddToClassList("scroll");
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            s.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            return s;
        }

        private static Texture2D _heroGradient;

        /// <summary>Applies the brand gradient (accent blue to violet) as a background image.</summary>
        public static void HeroGradient(VisualElement e)
        {
            if (_heroGradient == null)
            {
                _heroGradient = MaterialLibrary.VerticalGradient(new Color32(0x3F, 0x5E, 0xE8, 0xFF), new Color32(0x7A, 0x5C, 0xF0, 0xFF));
            }
            e.style.backgroundImage = new StyleBackground(_heroGradient);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        }

        private static Texture2D _fadeGradient;

        /// <summary>Transparent-to-background gradient (behind bottom controls over the 3D scene).</summary>
        public static void BottomFade(VisualElement e)
        {
            if (_fadeGradient == null)
            {
                var bg = (Color)Palette.Bg;
                _fadeGradient = MaterialLibrary.VerticalGradient(new Color(bg.r, bg.g, bg.b, 0.95f), new Color(bg.r, bg.g, bg.b, 0f));
            }
            e.style.backgroundImage = new StyleBackground(_fadeGradient);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        }

        public static string FormatDuration(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string FormatDate(DateTime? utc)
        {
            if (!utc.HasValue) return string.Empty;
            var local = utc.Value.ToLocalTime();
            var days = (DateTime.Now.Date - local.Date).TotalDays;
            if (days < 1) return "Today, " + local.ToString("HH:mm");
            if (days < 2) return "Yesterday, " + local.ToString("HH:mm");
            return local.ToString("d MMM yyyy");
        }
    }
}
