using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class ReportScreen : AppScreen
    {
        private readonly string _sessionId;
        private ScrollView _scroll;

        public ReportScreen(AppServices app, string sessionId) : base(app)
        {
            _sessionId = sessionId;
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("Feedback report", Back,
                UiKit.IconButton(IconKind.Document, () => App.Nav.Push(new TranscriptScreen(App, _sessionId)), 20f)));
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
            _scroll.Add(UiKit.Spacer(60));
            _scroll.Add(new Spinner(34f));
        }

        private void Back()
        {
            if (!App.Nav.Pop()) App.GoHome();
        }

        public override bool OnBack()
        {
            Back();
            return true;
        }

        public override void OnShow()
        {
            if (_scroll.childCount <= 2) MainThread.Run(LoadAsync, "Report");
        }

        private async Task LoadAsync()
        {
            ReportEnvelope env;
            InterviewSession session;
            try
            {
                var envTask = App.Api.GetReportAsync(_sessionId);
                var sessionTask = App.Api.GetInterviewAsync(_sessionId);
                await Task.WhenAll(envTask, sessionTask);
                env = envTask.Result;
                session = sessionTask.Result;
            }
            catch (ApiException ex)
            {
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.Warning, "Couldn't load the report", ex.Message, "Retry",
                    () => MainThread.Run(LoadAsync, "Report.Retry"), iconColor: Palette.Warning));
                return;
            }
            _scroll.Clear();
            if (env.Report == null)
            {
                _scroll.Add(UiKit.StatusView(IconKind.Clock, "Report not available yet",
                    env.Status == "insufficient_data" ? "There wasn't enough conversation to evaluate." :
                    env.Status == "failed" ? "Report generation failed. Open the interview from History to retry." :
                    "Your report is still being prepared. Check back shortly.",
                    "Back", Back));
                return;
            }
            Render(env.Report, session);
        }

        private void Render(InterviewReport r, InterviewSession session)
        {
            if (r.IsDemo)
            {
                _scroll.Add(UiKit.Banner("Demo report with sample content — no AI evaluated this interview.", "warning", IconKind.Info));
            }

            // Overview
            var hero = UiKit.Card();
            hero.style.alignItems = Align.Center;
            var ring = new ScoreRing(156f, "Overall");
            hero.Add(ring);
            ring.SetScore(r.OverallScore);
            hero.Add(UiKit.Spacer(10));
            var role = T($"{session.Config?.RoleTitle} · {Cap(session.Config?.InterviewType)} · {Cap(session.Config?.Difficulty)}", "caption");
            role.style.unityTextAlign = TextAnchor.MiddleCenter;
            hero.Add(role);
            hero.Add(UiKit.Spacer(12));
            hero.Add(T(r.Summary, "body body--strong"));
            _scroll.Add(hero);

            // Categories
            Section("Breakdown");
            var cats = UiKit.Card();
            foreach (var key in InterviewReport.CategoryKeys)
            {
                if (!r.Categories.TryGetValue(key, out var c)) continue;
                var head = UiKit.Row(T(InterviewReport.CategoryTitle(key), "h3"), UiKit.Spacer(0, grow: true),
                    T(c.Assessed && c.Score.HasValue ? c.Score.Value.ToString() : "n/a", "h3"));
                cats.Add(head);
                cats.Add(UiKit.Spacer(6));
                if (c.Assessed && c.Score.HasValue)
                {
                    int score = c.Score.Value;
                    cats.Add(new ProgressTrack(score / 100f, score >= 75 ? "success" : score >= 55 ? null : score >= 40 ? "warning" : "danger"));
                }
                var why = T(c.Assessed ? c.Rationale : "Not assessed in this interview.", "caption");
                why.style.marginTop = 6;
                why.style.marginBottom = 14;
                cats.Add(why);
            }
            _scroll.Add(cats);

            if (r.Strengths.Count > 0)
            {
                Section("What went well");
                var card = UiKit.Card();
                foreach (var s in r.Strengths) card.Add(UiKit.Bullet(s, IconKind.Check, Palette.Success));
                _scroll.Add(card);
            }
            if (r.Improvements.Count > 0)
            {
                Section("Focus on next");
                var card = UiKit.Card();
                foreach (var s in r.Improvements) card.Add(UiKit.Bullet(s, IconKind.ChevronRight, Palette.Warning));
                _scroll.Add(card);
            }

            if (r.ConfidenceIndicators.Count > 0)
            {
                Section("Delivery patterns");
                var note = T("Based only on patterns visible in your answers — not on your voice or emotions.", "caption");
                note.style.marginBottom = 8;
                _scroll.Add(note);
                foreach (var ci in r.ConfidenceIndicators)
                {
                    var card = UiKit.Card();
                    card.Add(T(ci.Observation, "h3"));
                    card.Add(UiKit.Spacer(4));
                    card.Add(T("“" + ci.Evidence + "”", "caption"));
                    card.Add(UiKit.Spacer(8));
                    card.Add(UiKit.Bullet(ci.Suggestion, IconKind.Sparkle, Palette.Accent));
                    _scroll.Add(card);
                }
            }

            if (r.AnswerFeedback.Count > 0)
            {
                Section("Question by question");
                foreach (var a in r.AnswerFeedback) _scroll.Add(AnswerCard(a));
            }

            if (r.RecommendedTopics.Count > 0)
            {
                Section("Recommended topics");
                var wrap = UiKit.Row();
                wrap.AddToClassList("row--wrap");
                foreach (var t in r.RecommendedTopics) wrap.Add(UiKit.Chip(t, false, false, null));
                _scroll.Add(wrap);
            }

            if (r.PracticePlan.Count > 0)
            {
                Section("Your practice plan");
                var card = UiKit.Card();
                for (int i = 0; i < r.PracticePlan.Count; i++)
                {
                    var step = r.PracticePlan[i];
                    var num = T((i + 1).ToString(), "h3 text-accent");
                    num.style.width = 26;
                    var body = UiKit.Column(T(step.Title, "h3"), T($"{step.Activity} · {step.DurationMinutes} min", "caption"));
                    body.style.flexShrink = 1;
                    var row = UiKit.Row(num, body);
                    row.style.alignItems = Align.FlexStart;
                    row.style.marginBottom = 12;
                    card.Add(row);
                }
                _scroll.Add(card);
            }

            if (r.Tier == "basic")
            {
                var upsell = UiKit.Card("card--accent");
                upsell.Add(UiKit.Row(new Icon(IconKind.Crown, 20f, Palette.Gold), UiKit.HSpacer(10), T("Unlock detailed feedback", "h3")));
                upsell.Add(UiKit.Spacer(6));
                upsell.Add(T("Premium reports add question-by-question feedback, improved answer structures, delivery " +
                             "patterns and a personalised practice plan.", "body"));
                upsell.Add(UiKit.Spacer(12));
                var b = UiKit.Button("See Premium", App.OpenSubscription, ButtonStyle.Gold, IconKind.Crown);
                b.AddToClassList("btn--block");
                upsell.Add(b);
                _scroll.Add(UiKit.Spacer(8));
                _scroll.Add(upsell);
            }

            var disclaimer = T(r.Disclaimer, "caption");
            disclaimer.style.marginTop = 16;
            _scroll.Add(disclaimer);
            _scroll.Add(UiKit.Spacer(12));
            var again = UiKit.Button("Practise again", () => App.Nav.Push(new SetupScreen(App)), icon: IconKind.Refresh);
            again.AddToClassList("btn--block");
            _scroll.Add(again);
        }

        private static VisualElement AnswerCard(AnswerFeedback a)
        {
            var card = UiKit.Card();
            string variant = a.Assessment == "strong" ? "success" : a.Assessment == "adequate" ? "accent" : "warning";
            string label = a.Assessment == "strong" ? "STRONG" : a.Assessment == "adequate" ? "ADEQUATE" : "NEEDS WORK";
            card.Add(UiKit.Badge(label, variant));
            card.Add(UiKit.Spacer(8));
            card.Add(T(a.Question, "h3"));
            card.Add(UiKit.Spacer(6));
            card.Add(T("Your answer: " + a.AnswerSummary, "caption"));
            card.Add(UiKit.Spacer(8));
            card.Add(T(a.Feedback, "body"));

            var improved = UiKit.Column(UiKit.Spacer(10), T("A stronger answer structure", "overline"), UiKit.Spacer(4),
                T(a.ImprovedAnswer, "body body--strong"));
            improved.style.display = DisplayStyle.None;
            card.Add(improved);
            var toggle = UiKit.Button("Show a stronger answer", null, ButtonStyle.Ghost, small: true);
            toggle.style.alignSelf = Align.FlexStart;
            toggle.style.marginTop = 6;
            toggle.clicked += () =>
            {
                bool show = improved.style.display == DisplayStyle.None;
                improved.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                UiKit.SetButtonText(toggle, show ? "Hide" : "Show a stronger answer");
            };
            card.Add(toggle);
            return card;
        }

        private void Section(string title)
        {
            var l = T(title, "h2");
            l.AddToClassList("section-title");
            _scroll.Add(l);
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    /// <summary>Saved transcript (only available when the user opted in to transcript retention).</summary>
    public sealed class TranscriptScreen : AppScreen
    {
        private readonly string _sessionId;
        private ScrollView _scroll;

        public TranscriptScreen(AppServices app, string sessionId) : base(app)
        {
            _sessionId = sessionId;
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("Transcript", () => App.Nav.Pop()));
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Transcript");
        }

        private async Task LoadAsync()
        {
            var t = await Guard(App.Api.GetTranscriptAsync(_sessionId), "transcript");
            _scroll.Clear();
            if (t == null) return;
            if (t.Messages.Count == 0)
            {
                _scroll.Add(UiKit.StatusView(IconKind.Shield, "Transcript not kept",
                    "Transcripts are deleted after your report is generated. You can choose to keep future transcripts " +
                    "in Profile → Privacy.", "Open privacy settings", () => App.OpenTab(TabId.Profile)));
                return;
            }
            foreach (var m in t.Messages)
            {
                bool you = m.Role == "candidate";
                var row = new VisualElement();
                row.AddToClassList("transcript-line");
                if (you) row.AddToClassList("transcript-line--candidate");
                row.Add(T(you ? "YOU" : "INTERVIEWER", "transcript-line__speaker"));
                row.Add(T(m.Text + (m.Truncated ? " …" : string.Empty), "transcript-line__text"));
                row.style.marginBottom = 14;
                _scroll.Add(row);
            }
        }
    }
}
