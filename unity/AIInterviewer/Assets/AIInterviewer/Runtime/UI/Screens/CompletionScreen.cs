using System.Linq;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.Core.Interview;
using AIInterviewer.Realtime;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class CompletionScreen : AppScreen
    {
        private readonly InterviewSession _session;
        private readonly InterviewCompletionInfo _info;
        private readonly int _answers;
        private VisualElement _reportArea;
        private bool _polling;

        public CompletionScreen(AppServices app, InterviewSession session, InterviewCompletionInfo info, TranscriptLog transcript)
            : base(app)
        {
            _session = session;
            _info = info;
            _answers = transcript?.Lines.Count(l => l.Speaker == Speaker.Candidate) ?? 0;
        }

        protected override void Build()
        {
            var scroll = UiKit.Scroll();
            Content.Add(scroll);
            scroll.Add(UiKit.Spacer(56));

            var badge = new VisualElement();
            badge.style.width = 96;
            badge.style.height = 96;
            badge.style.borderTopLeftRadius = 48;
            badge.style.borderTopRightRadius = 48;
            badge.style.borderBottomLeftRadius = 48;
            badge.style.borderBottomRightRadius = 48;
            badge.style.backgroundColor = new Color(0.2f, 0.83f, 0.6f, 0.15f);
            badge.style.alignSelf = Align.Center;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.Add(new Icon(IconKind.Check, 48f, Palette.Success));
            badge.AddToClassList("splash-fade");
            scroll.Add(badge);
            badge.schedule.Execute(() => badge.AddToClassList("splash-fade--in")).StartingIn(50);

            scroll.Add(UiKit.Spacer(20));
            var title = T(_info.Reason == "time_limit" ? "Time's up — great work!" : "Interview complete", "display");
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            scroll.Add(title);
            scroll.Add(UiKit.Spacer(8));
            var sub = T("Taking time to practise out loud is the fastest way to improve.", "body");
            sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            scroll.Add(sub);
            scroll.Add(UiKit.Spacer(24));

            var role = _session.Config?.RoleTitle ?? "Interview";
            var metrics = UiKit.Row(
                UiKit.Metric(role, "Role"),
                UiKit.Metric(_answers.ToString(), "Answers given"),
                UiKit.Metric($"{_session.Config?.DurationMinutes ?? 0} min", "Planned length", last: true));
            metrics.style.alignItems = Align.Stretch;
            scroll.Add(metrics);
            scroll.Add(UiKit.Spacer(20));

            _reportArea = new VisualElement();
            scroll.Add(_reportArea);

            scroll.Add(UiKit.Spacer(12));
            var home = UiKit.Button("Back to home", App.GoHome, ButtonStyle.Secondary);
            home.AddToClassList("btn--block");
            scroll.Add(home);
        }

        public override void OnShow()
        {
            RenderStatus(_info.ReportStatus);
            if (_info.ReportStatus == "pending" || _info.ReportStatus == "generating")
            {
                MainThread.Run(PollAsync, "Completion.Poll");
            }
        }

        public override void OnHide()
        {
            _polling = false;
        }

        public override bool OnBack()
        {
            App.GoHome();
            return true;
        }

        private async Task PollAsync()
        {
            if (_polling) return;
            _polling = true;
            float delay = 1.5f;
            for (int attempt = 0; attempt < 40 && _polling; attempt++)
            {
                await Task.Delay((int)(delay * 1000));
                if (!_polling) return;
                try
                {
                    var r = await App.Api.GetReportAsync(_session.Id);
                    if (r.Status != "pending" && r.Status != "generating")
                    {
                        RenderStatus(r.Status);
                        _polling = false;
                        return;
                    }
                }
                catch (ApiException ex) when (ex.Kind == ApiFailureKind.Network || ex.Kind == ApiFailureKind.Server)
                {
                    // Keep polling; transient.
                }
                delay = Mathf.Min(delay * 1.3f, 5f);
            }
            _polling = false;
            RenderStatus("timeout");
        }

        private void RenderStatus(string status)
        {
            _reportArea.Clear();
            var card = UiKit.Card("card--accent");
            switch (status)
            {
                case "ready":
                    card.Add(UiKit.Row(new Icon(IconKind.Sparkle, 20f, Palette.Gold), UiKit.HSpacer(10), T("Your report is ready", "h3")));
                    card.Add(UiKit.Spacer(6));
                    card.Add(T("See your scores, strengths and what to practise next.", "body"));
                    card.Add(UiKit.Spacer(14));
                    var view = UiKit.Button("View report", () => App.Nav.Replace(new ReportScreen(App, _session.Id), Transition.Up),
                        icon: IconKind.Chart);
                    view.AddToClassList("btn--block");
                    card.Add(view);
                    break;
                case "pending":
                case "generating":
                    card.Add(UiKit.Row(new Spinner(22f), UiKit.HSpacer(12), T("Analysing your interview…", "h3")));
                    card.Add(UiKit.Spacer(6));
                    card.Add(T("This usually takes less than a minute. You can leave — the report will be in History.", "body"));
                    break;
                case "insufficient_data":
                    card.Add(T("Not enough conversation for a report", "h3"));
                    card.Add(UiKit.Spacer(6));
                    card.Add(T("Answer at least a couple of questions in full sentences so we can give meaningful feedback. " +
                               "Try another session when you're ready.", "body"));
                    break;
                case "failed":
                case "timeout":
                    card.Add(T(status == "failed" ? "We couldn't generate your report" : "Your report is taking longer than usual", "h3"));
                    card.Add(UiKit.Spacer(6));
                    card.Add(T(status == "failed" ? "This is usually temporary. Try again in a moment."
                        : "Check History in a few minutes.", "body"));
                    if (status == "failed")
                    {
                        card.Add(UiKit.Spacer(14));
                        var retry = UiKit.Button("Try again", () => MainThread.Run(RetryAsync, "Completion.Retry"),
                            icon: IconKind.Refresh);
                        retry.AddToClassList("btn--block");
                        card.Add(retry);
                    }
                    break;
                default:
                    card.Add(T("No report for this session", "h3"));
                    card.Add(UiKit.Spacer(6));
                    card.Add(T("The interview ended before any questions were answered.", "body"));
                    break;
            }
            _reportArea.Add(card);
        }

        private async Task RetryAsync()
        {
            if (await Guard(App.Api.RegenerateReportAsync(_session.Id), "regenerate") == null) return;
            RenderStatus("generating");
            await PollAsync();
        }
    }
}
