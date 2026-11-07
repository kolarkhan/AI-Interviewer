using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Framework
{
    public sealed class DialogAction
    {
        public DialogAction(string text, ButtonStyle style = ButtonStyle.Primary, Action onClick = null)
        {
            Text = text;
            Style = style;
            OnClick = onClick;
        }

        public string Text { get; }
        public ButtonStyle Style { get; }
        public Action OnClick { get; }
    }

    /// <summary>Toasts, modal dialogs and the blocking loading veil, drawn above all screens.</summary>
    public sealed class Overlay
    {
        private readonly VisualElement _layer;
        private readonly VisualElement _toastHost;
        private readonly Queue<(string text, IconKind icon, Color color)> _toasts = new Queue<(string, IconKind, Color)>();
        private bool _toastShowing;
        private VisualElement _dialogScrim;
        private Action _dialogDismiss;
        private VisualElement _veil;
        private float _safeTop;
        private float _safeBottom;

        public Overlay(VisualElement layer)
        {
            _layer = layer;
            _layer.pickingMode = PickingMode.Ignore;
            _toastHost = new VisualElement();
            _toastHost.AddToClassList("toast-host");
            _toastHost.pickingMode = PickingMode.Ignore;
            _layer.Add(_toastHost);
        }

        public bool IsDialogOpen => _dialogScrim != null;

        public void SetSafeTop(float top)
        {
            _safeTop = top;
            _toastHost.style.top = top + 8f;
        }

        public void SetSafeBottom(float bottom) => _safeBottom = bottom;

        /// <summary>
        /// Bottom sheet listing <paramref name="items"/> (dropdown menu). Picking an item closes the sheet, then
        /// calls <paramref name="onPick"/>; locked items are passed too so the caller can offer an upgrade.
        /// </summary>
        public void OptionSheet(string title, IList<PickerItem> items, string selectedId, Action<PickerItem> onPick,
            Func<PickerItem, VisualElement> accessory = null)
        {
            CloseDialog();
            var scrim = new VisualElement();
            scrim.AddToClassList("scrim");
            scrim.AddToClassList("scrim--sheet");
            var sheet = new VisualElement();
            sheet.AddToClassList("sheet");
            sheet.style.paddingBottom = 16f + _safeBottom;
            sheet.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            var handle = new VisualElement();
            handle.AddToClassList("sheet__handle");
            sheet.Add(handle);
            var header = new VisualElement();
            header.AddToClassList("sheet__header");
            header.Add(UiKit.Text(title, "h2"));
            header.Add(UiKit.IconButton(IconKind.Close, CloseDialog, 20f, Palette.TextMuted));
            sheet.Add(header);

            var list = UiKit.Scroll();
            list.AddToClassList("sheet__list");
            float estimated = 0f;
            int selectedIndex = -1;
            for (int k = 0; k < items.Count; k++)
            {
                var item = items[k];
                bool selected = item.Id == selectedId;
                if (selected) selectedIndex = k;
                var row = new VisualElement();
                row.AddToClassList("sheet-option");
                row.EnableInClassList("sheet-option--selected", selected);
                row.EnableInClassList("sheet-option--locked", item.Locked);
                var body = new VisualElement();
                body.AddToClassList("sheet-option__body");
                body.Add(UiKit.Text(item.Title, "sheet-option__title"));
                if (!string.IsNullOrEmpty(item.Description)) body.Add(UiKit.Text(item.Description, "caption"));
                row.Add(body);
                if (item.Locked) row.Add(new Icon(IconKind.Lock, 16f, Palette.Gold));
                else if (selected) row.Add(new Icon(IconKind.Check, 20f, Palette.Accent));
                row.Query<VisualElement>().ForEach(child =>
                {
                    if (child != row) child.pickingMode = PickingMode.Ignore;
                });
                var extra = accessory?.Invoke(item); // interactive (e.g. a play button): added after picking is set
                if (extra != null)
                {
                    extra.style.marginRight = 12;
                    row.Insert(0, extra);
                }
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    CloseDialog();
                    onPick?.Invoke(item);
                });
                list.Add(row);
                estimated += string.IsNullOrEmpty(item.Description) ? 54f : 76f;
            }
            // A ScrollView needs a definite height (it collapses inside an auto-height parent).
            float layerHeight = _layer.resolvedStyle.height > 0 ? _layer.resolvedStyle.height : 800f;
            list.style.height = Mathf.Min(estimated + 8f, Mathf.Max(220f, layerHeight * 0.62f));
            sheet.Add(list);

            scrim.Add(sheet);
            scrim.RegisterCallback<ClickEvent>(_ => CloseDialog());
            _layer.Add(scrim);
            _dialogScrim = scrim;
            _dialogDismiss = CloseDialog;
            scrim.schedule.Execute(() => scrim.AddToClassList("scrim--visible")).StartingIn(16);
            if (selectedIndex > 3)
            {
                list.schedule.Execute(() =>
                {
                    if (selectedIndex < list.contentContainer.childCount)
                        list.ScrollTo(list.contentContainer[selectedIndex]);
                }).StartingIn(60);
            }
        }

        public void Toast(string text, IconKind icon = IconKind.Info, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toasts.Enqueue((text, icon, color ?? Palette.Accent));
            if (!_toastShowing) ShowNextToast();
        }

        public void Success(string text) => Toast(text, IconKind.Check, Palette.Success);

        public void Error(string text) => Toast(text, IconKind.Warning, Palette.Danger);

        private void ShowNextToast()
        {
            if (_toasts.Count == 0)
            {
                _toastShowing = false;
                return;
            }
            _toastShowing = true;
            var (text, icon, color) = _toasts.Dequeue();
            var toast = new VisualElement();
            toast.AddToClassList("toast");
            toast.pickingMode = PickingMode.Ignore;
            toast.Add(new Icon(icon, 18f, color));
            toast.Add(UiKit.Text(text, "toast__text"));
            _toastHost.Add(toast);
            toast.schedule.Execute(() => toast.AddToClassList("toast--visible")).StartingIn(16);
            long visibleMs = Math.Min(5000, 1800 + text.Length * 35);
            toast.schedule.Execute(() => toast.RemoveFromClassList("toast--visible")).StartingIn(visibleMs);
            toast.schedule.Execute(() =>
            {
                toast.RemoveFromHierarchy();
                ShowNextToast();
            }).StartingIn(visibleMs + 280);
        }

        /// <summary>Shows a modal dialog. Tapping the scrim dismisses unless <paramref name="dismissible"/> is false.</summary>
        public void Dialog(string title, string body, IList<DialogAction> actions, bool dismissible = true,
            VisualElement extraContent = null)
        {
            CloseDialog();
            var scrim = new VisualElement();
            scrim.AddToClassList("scrim");
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dialog.Add(UiKit.Text(title, "dialog__title"));
            if (!string.IsNullOrEmpty(body)) dialog.Add(UiKit.Text(body, "dialog__body"));
            if (extraContent != null) dialog.Add(extraContent);
            var row = new VisualElement();
            row.AddToClassList("dialog__actions");
            foreach (var a in actions)
            {
                var action = a;
                var b = UiKit.Button(action.Text, () =>
                {
                    CloseDialog();
                    action.OnClick?.Invoke();
                }, action.Style);
                b.AddToClassList("btn--block");
                row.Add(b);
            }
            dialog.Add(row);
            scrim.Add(dialog);
            if (dismissible) scrim.RegisterCallback<ClickEvent>(_ => CloseDialog());
            _layer.Add(scrim);
            _dialogScrim = scrim;
            _dialogDismiss = dismissible ? (Action)CloseDialog : null;
            scrim.schedule.Execute(() => scrim.AddToClassList("scrim--visible")).StartingIn(16);
        }

        public Task<bool> ConfirmAsync(string title, string body, string confirmText, string cancelText = "Cancel",
            bool destructive = false)
        {
            var tcs = new TaskCompletionSource<bool>();
            Dialog(title, body, new[]
            {
                new DialogAction(confirmText, destructive ? ButtonStyle.Danger : ButtonStyle.Primary, () => tcs.TrySetResult(true)),
                new DialogAction(cancelText, ButtonStyle.Secondary, () => tcs.TrySetResult(false))
            }, dismissible: false);
            return tcs.Task;
        }

        /// <summary>Handles Android back for an open dialog. Returns true if a dialog consumed it.</summary>
        public bool HandleBack()
        {
            if (_dialogScrim == null) return false;
            _dialogDismiss?.Invoke();
            return true;
        }

        public void CloseDialog()
        {
            var scrim = _dialogScrim;
            if (scrim == null) return;
            _dialogScrim = null;
            _dialogDismiss = null;
            scrim.RemoveFromClassList("scrim--visible");
            scrim.pickingMode = PickingMode.Ignore;
            scrim.schedule.Execute(scrim.RemoveFromHierarchy).StartingIn(220);
        }

        public void ShowVeil(string message = null)
        {
            HideVeil();
            _veil = new VisualElement();
            _veil.AddToClassList("scrim");
            _veil.AddToClassList("scrim--visible");
            _veil.Add(new Spinner(40f));
            if (!string.IsNullOrEmpty(message))
            {
                var l = UiKit.Text(message, "body");
                l.style.marginTop = 14;
                _veil.Add(l);
            }
            _layer.Add(_veil);
        }

        public void HideVeil()
        {
            _veil?.RemoveFromHierarchy();
            _veil = null;
        }
    }
}
