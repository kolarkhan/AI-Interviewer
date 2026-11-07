using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class HistoryScreen : AppScreen
    {
        private const int PageSize = 20;
        private ScrollView _scroll;
        private VisualElement _list;
        private Button _more;
        private int _loaded;
        private int _total;
        private bool _loading;

        public HistoryScreen(AppServices app) : base(app)
        {
        }

        public override TabId Tab => TabId.History;

        protected override void Build()
        {
            var header = UiKit.Row(T("History", "title"), UiKit.Spacer(0, grow: true),
                UiKit.IconButton(IconKind.Refresh, () => MainThread.Run(() => LoadAsync(reset: true), "History.Refresh"), 20f));
            header.style.marginTop = 16;
            header.style.marginBottom = 12;
            Content.Add(header);
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
            _list = new VisualElement();
            _scroll.Add(_list);
            _more = UiKit.Button("Load more", () => MainThread.Run(() => LoadAsync(reset: false), "History.More"), ButtonStyle.Secondary);
            _more.style.display = DisplayStyle.None;
            _scroll.Add(_more);
        }

        public override void OnShow()
        {
            MainThread.Run(() => LoadAsync(reset: true), "History");
        }

        private async Task LoadAsync(bool reset)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                if (reset && _loaded == 0)
                {
                    _list.Clear();
                    _list.Add(new Spinner(30f));
                }
                var page = await App.Api.ListInterviewsAsync(PageSize, reset ? 0 : _loaded);
                if (reset)
                {
                    _list.Clear();
                    _loaded = 0;
                }
                _total = page.Total;
                foreach (var s in page.Items) _list.Add(SessionItem(App, s, onDeleted: () => MainThread.Run(() => LoadAsync(true), "History.Reload")));
                _loaded += page.Items.Count;
                if (_loaded == 0)
                {
                    _list.Add(UiKit.StatusView(IconKind.History, "No interviews yet",
                        "Complete your first practice interview and your reports will appear here.",
                        "Start an interview", () => App.Nav.Push(new SetupScreen(App))));
                }
                _more.style.display = _loaded < _total ? DisplayStyle.Flex : DisplayStyle.None;
                if (page.HistoryLimited && _loaded > 0 && _loaded >= _total)
                {
                    var upsell = UiKit.Banner("The free plan shows your 5 most recent interviews. Premium keeps your full history.",
                        "info", IconKind.Crown);
                    upsell.RegisterCallback<ClickEvent>(_ => App.OpenSubscription());
                    _list.Add(upsell);
                }
            }
            catch (ApiException ex)
            {
                _list.Clear();
                _list.Add(UiKit.StatusView(IconKind.WifiOff, "Couldn't load your history",
                    ex.Kind == ApiFailureKind.Network ? "Check your connection and try again." : ex.Message,
                    "Retry", () => MainThread.Run(() => LoadAsync(true), "History.Retry"), iconColor: Palette.Warning));
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>A history row (also used on the home dashboard).</summary>
        public static VisualElement SessionItem(AppServices app, InterviewSession s, System.Action onDeleted = null)
        {
            string title = s.Config?.RoleTitle ?? "Interview";
            string subtitle = $"{Cap(s.Config?.InterviewType)} · {s.Config?.DurationMinutes} min · {UiKit.FormatDate(s.CreatedAt)}";
            VisualElement trailing;
            if (s.OverallScore.HasValue)
            {
                var score = UiKit.Text(s.OverallScore.Value.ToString(), "h2");
                score.style.color = Palette.ForScore(s.OverallScore.Value);
                trailing = score;
            }
            else
            {
                trailing = StatusBadge(s);
            }
            var icon = s.Config?.InterviewType == "behavioral" || s.Config?.InterviewType == "hr" ? IconKind.User : IconKind.Document;
            bool pressed = false, longPressed = false;
            Vector2 downPos = Vector2.zero;
            var item = UiKit.ListItem(title, subtitle, icon, trailing, () =>
            {
                if (longPressed)
                {
                    longPressed = false; // the long press already handled this touch
                    return;
                }
                Open(app, s);
            });
            if (onDeleted != null)
            {
                // Long-press (600 ms without moving) offers deletion.
                item.RegisterCallback<PointerDownEvent>(e =>
                {
                    pressed = true;
                    longPressed = false;
                    downPos = e.position;
                    item.schedule.Execute(() =>
                    {
                        if (!pressed) return;
                        pressed = false;
                        longPressed = true;
                        MainThread.Run(() => ConfirmDeleteAsync(app, s, onDeleted), "History.Delete");
                    }).StartingIn(600);
                });
                item.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (pressed && ((Vector2)e.position - downPos).sqrMagnitude > 100f) pressed = false;
                });
                item.RegisterCallback<PointerUpEvent>(_ => pressed = false);
                item.RegisterCallback<PointerLeaveEvent>(_ => pressed = false);
                item.RegisterCallback<PointerCancelEvent>(_ => pressed = false);
            }
            return item;
        }

        private static async Task ConfirmDeleteAsync(AppServices app, InterviewSession s, System.Action onDeleted)
        {
            bool ok = await app.Overlay.ConfirmAsync("Delete this interview?",
                "The session, its transcript and its report will be permanently deleted.", "Delete", destructive: true);
            if (!ok) return;
            try
            {
                await app.Api.DeleteInterviewAsync(s.Id);
                app.Overlay.Success("Interview deleted");
                onDeleted();
            }
            catch (ApiException ex)
            {
                app.HandleApiError(ex, "delete interview");
            }
        }

        private static VisualElement StatusBadge(InterviewSession s)
        {
            switch (s.ReportStatus)
            {
                case "pending":
                case "generating": return UiKit.Badge("ANALYSING", "accent");
                case "failed": return UiKit.Badge("RETRY", "danger");
                case "insufficient_data": return UiKit.Badge("TOO SHORT", "warning");
            }
            switch (s.Status)
            {
                case "created": return UiKit.Badge("NOT STARTED", null);
                case "live":
                case "interrupted": return UiKit.Badge("IN PROGRESS", "accent");
                case "abandoned": return UiKit.Badge("CANCELLED", null);
                default: return UiKit.Badge(s.Status?.ToUpperInvariant() ?? "", null);
            }
        }

        private static void Open(AppServices app, InterviewSession s)
        {
            if (s.ReportStatus == "ready")
            {
                app.Nav.Push(new ReportScreen(app, s.Id));
                return;
            }
            if (s.Status == "live" || s.Status == "interrupted" || s.Status == "created")
            {
                app.Overlay.Dialog("Resume this interview?",
                    s.Status == "created" ? "This interview hasn't started yet." : "You can continue where you left off while time remains.",
                    new[]
                    {
                        new DialogAction(s.Status == "created" ? "Start" : "Resume",
                            ButtonStyle.Primary, () => app.Nav.Push(new InterviewScreen(app, s), Transition.Up)),
                        new DialogAction("Cancel", ButtonStyle.Secondary)
                    });
                return;
            }
            app.Nav.Push(new CompletionScreen(app, s, new Realtime.InterviewCompletionInfo
            {
                SessionId = s.Id, Reason = s.EndReason, ReportStatus = s.ReportStatus
            }, null));
        }

        private static string Cap(string v) => string.IsNullOrEmpty(v) ? v : char.ToUpperInvariant(v[0]) + v.Substring(1);
    }
}
