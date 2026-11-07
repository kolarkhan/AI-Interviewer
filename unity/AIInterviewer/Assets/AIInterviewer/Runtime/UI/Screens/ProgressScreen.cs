using System.Collections.Generic;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class ProgressScreen : AppScreen
    {
        private ScrollView _scroll;

        public ProgressScreen(AppServices app) : base(app)
        {
        }

        public override TabId Tab => TabId.Progress;

        protected override void Build()
        {
            var title = T("Progress", "title");
            title.style.marginTop = 16;
            title.style.marginBottom = 12;
            Content.Add(title);
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Progress");
        }

        private async Task LoadAsync()
        {
            if (_scroll.childCount == 0) _scroll.Add(new Spinner(30f));
            ProgressResponse p;
            try
            {
                p = await App.Api.GetProgressAsync();
            }
            catch (ApiException ex)
            {
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.WifiOff, "Couldn't load your progress", ex.Message, "Retry",
                    () => MainThread.Run(LoadAsync, "Progress.Retry"), iconColor: Palette.Warning));
                return;
            }
            Render(p);
        }

        private void Render(ProgressResponse p)
        {
            _scroll.Clear();
            var s = p.Summary;
            if (s.Reports == 0)
            {
                _scroll.Add(UiKit.StatusView(IconKind.Chart, "Your progress starts here",
                    "Complete an interview to get your first scores. We'll chart your improvement over time.",
                    "Start an interview", () => App.Nav.Push(new SetupScreen(App))));
                return;
            }

            var metrics = UiKit.Row(
                UiKit.Metric(s.AverageScore?.ToString() ?? "–", "Average score"),
                UiKit.Metric(s.InterviewsCompleted.ToString(), "Interviews"),
                UiKit.Metric($"{Mathf.RoundToInt(s.PracticeMinutes)}", "Minutes practised", last: true));
            metrics.style.alignItems = Align.Stretch;
            _scroll.Add(metrics);
            _scroll.Add(UiKit.Spacer(12));

            if (s.ProgressTrackingLocked)
            {
                var locked = UiKit.Card("card--accent");
                locked.Add(UiKit.Row(new Icon(IconKind.Crown, 20f, Palette.Gold), UiKit.HSpacer(10), T("Track your improvement", "h3")));
                locked.Add(UiKit.Spacer(6));
                locked.Add(T("Premium unlocks score trends, per-skill progress and your focus areas across every interview.", "body"));
                locked.Add(UiKit.Spacer(12));
                var b = UiKit.Button("See Premium", App.OpenSubscription, ButtonStyle.Gold, IconKind.Crown);
                b.AddToClassList("btn--block");
                locked.Add(b);
                _scroll.Add(locked);
                return;
            }

            var trendCard = UiKit.Card();
            var change = s.Improvement ?? 0;
            trendCard.Add(UiKit.Row(T("Overall score", "h3"), UiKit.Spacer(0, grow: true),
                T((change >= 0 ? "+" : "") + change + " since first", change >= 0 ? "caption text-success" : "caption text-danger")));
            trendCard.Add(UiKit.Spacer(10));
            var chart = new LineChart(170f);
            var values = new List<int>();
            foreach (var t in p.Trend) values.Add(t.OverallScore);
            chart.SetValues(values);
            trendCard.Add(chart);
            _scroll.Add(trendCard);

            if (!string.IsNullOrEmpty(s.FocusCategory))
            {
                var focus = UiKit.Card("card--accent");
                focus.Add(T("FOCUS AREA", "overline"));
                focus.Add(UiKit.Spacer(4));
                focus.Add(T(InterviewReport.CategoryTitle(s.FocusCategory), "h2"));
                focus.Add(UiKit.Spacer(4));
                focus.Add(T("This is your lowest recent score. Target it in your next session.", "body"));
                _scroll.Add(focus);
            }

            var catTitle = T("Skills", "h2");
            catTitle.AddToClassList("section-title");
            _scroll.Add(catTitle);
            var cats = UiKit.Card();
            foreach (var key in InterviewReport.CategoryKeys)
            {
                if (!p.Categories.TryGetValue(key, out var c)) continue;
                string delta = c.Change == 0 ? "±0" : (c.Change > 0 ? "+" : "") + c.Change;
                cats.Add(UiKit.Row(T(InterviewReport.CategoryTitle(key), "h3"), UiKit.Spacer(0, grow: true),
                    T($"{c.RecentAverage}  ({delta})", "caption")));
                cats.Add(UiKit.Spacer(6));
                cats.Add(new ProgressTrack(c.RecentAverage / 100f, c.RecentAverage >= 75 ? "success" : c.RecentAverage >= 55 ? null : "warning"));
                cats.Add(UiKit.Spacer(14));
            }
            _scroll.Add(cats);
            var note = T("Scores are practice estimates and vary between sessions. Look at the trend, not single numbers.", "caption");
            note.style.marginTop = 6;
            _scroll.Add(note);
        }
    }
}
