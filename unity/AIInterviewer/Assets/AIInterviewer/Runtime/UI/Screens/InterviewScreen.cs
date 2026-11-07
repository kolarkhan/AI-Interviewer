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
    /// <summary>Full-screen interview room HUD layered over the 3D interviewer.</summary>
    public sealed class InterviewScreen : AppScreen
    {
        private const int TranscriptLines = 6;

        private readonly InterviewSession _session;
        private LiveInterviewController _ctl;
        private Label _timer;
        private ProgressTrack _progress;
        private VisualElement _statusPill;
        private Label _statusText;
        private VoiceMeter _meter;
        private Icon _netIcon;
        private VisualElement _transcriptPanel;
        private ScrollView _transcriptScroll;
        private bool _transcriptVisible;
        private Button _muteBtn, _interruptBtn, _transcriptBtn;
        private VisualElement _interruptControl;
        private VisualElement _failure;
        private VisualElement _bottom;
        private IVisualElementScheduledItem _tick;
        private bool _started;

        public InterviewScreen(AppServices app, InterviewSession session) : base(app)
        {
            _session = session;
        }

        public override bool ShowsInterviewRoom => true;

        protected override void Build()
        {
            _ctl = App.Interview;
            _transcriptVisible = _session.Config?.LiveTranscript ?? true;

            // Top: timer, progress, connection and demo indicators.
            var top = new VisualElement();
            top.AddToClassList("hud-top");
            var timerChip = new VisualElement();
            timerChip.AddToClassList("hud-chip");
            timerChip.Add(new Icon(IconKind.Clock, 16f, Palette.TextSecondary));
            _timer = T("--:--", "hud-chip__text mono");
            timerChip.Add(_timer);
            top.Add(timerChip);
            var right = UiKit.Row();
            if (_session.IsDemo)
            {
                var demo = UiKit.Badge("DEMO", "warning");
                demo.style.marginRight = 8;
                right.Add(demo);
            }
            var netChip = new VisualElement();
            netChip.AddToClassList("hud-chip");
            _netIcon = new Icon(IconKind.Wifi, 16f, Palette.Success);
            netChip.Add(_netIcon);
            right.Add(netChip);
            top.Add(right);
            Content.Add(top);
            _progress = new ProgressTrack(0f, null, thin: true);
            _progress.AddToClassList("hud-progress");
            Content.Add(_progress);

            Content.Add(UiKit.Spacer(0, grow: true));

            // Bottom HUD over a gradient for legibility.
            var fade = new VisualElement();
            fade.AddToClassList("hud-gradient");
            fade.pickingMode = PickingMode.Ignore;
            UiKit.BottomFade(fade);
            Root.Insert(0, fade);

            _bottom = new VisualElement();
            Content.Add(_bottom);

            _statusPill = new VisualElement();
            _statusPill.AddToClassList("status-pill");
            var dot = new VisualElement();
            dot.AddToClassList("status-pill__dot");
            _statusPill.Add(dot);
            _statusText = T("Connecting…", "status-pill__text");
            _statusPill.Add(_statusText);
            _meter = new VoiceMeter(MeterLevel, Palette.Accent, 34f, 18f);
            _meter.style.marginLeft = 10;
            _statusPill.Add(_meter);
            _statusPill.style.marginBottom = 14;

            _transcriptPanel = new VisualElement();
            _transcriptPanel.AddToClassList("transcript-panel");
            _transcriptScroll = UiKit.Scroll();
            _transcriptPanel.Add(_transcriptScroll);
            _bottom.Add(_transcriptPanel);
            _bottom.Add(_statusPill);

            var controls = new VisualElement();
            controls.AddToClassList("controls");
            _muteBtn = CallButton(IconKind.Mic, "Mute", ToggleMute, out var muteControl);
            _transcriptBtn = CallButton(IconKind.Document, "Captions", ToggleTranscript, out var transcriptControl);
            var endBtn = CallButton(IconKind.CallEnd, "End", ConfirmEnd, out var endControl);
            endBtn.AddToClassList("call-btn--end");
            _interruptBtn = CallButton(IconKind.Hand, "Interrupt", () => _ctl.InterruptAi(), out _interruptControl);
            controls.Add(muteControl);
            controls.Add(_interruptControl);
            controls.Add(endControl);
            controls.Add(transcriptControl);
            _bottom.Add(controls);
            _bottom.style.paddingBottom = 10;

            UpdateTranscriptVisibility();
        }

        private float MeterLevel()
        {
            if (_ctl == null) return 0f;
            var s = _ctl.StateMachine.State;
            if (s == InterviewState.Speaking) return App.Room.Avatar != null ? App.Room.Avatar.SpeakingVolume * 1.4f : 0f;
            if (s == InterviewState.Listening || s == InterviewState.Interrupted) return _ctl.IsMuted ? 0f : _ctl.MicLevel;
            return 0.05f;
        }

        private static Button CallButton(IconKind icon, string caption, System.Action onClick, out VisualElement control)
        {
            var b = new Button(onClick) { text = string.Empty };
            b.AddToClassList("call-btn");
            b.Add(new Icon(icon, 26f, Palette.Text));
            control = new VisualElement();
            control.AddToClassList("control");
            control.Add(b);
            control.Add(UiKit.Text(caption, "call-btn__caption"));
            return b;
        }

        public override void OnShow()
        {
            App.Room.SetBottomUiFraction(_transcriptVisible ? 0.48f : 0.3f);
            _ctl.StateChanged += OnState;
            _ctl.Notice += OnNotice;
            _ctl.Completed += OnCompleted;
            _ctl.Failed += OnFailed;
            _ctl.Transcript.Changed += OnTranscript;
            App.Network.OnlineChanged += OnOnline;
            _tick = Root.schedule.Execute(Tick).Every(250);
            if (!_started)
            {
                _started = true;
                MainThread.Run(() => _ctl.StartAsync(_session, App.Settings.UseHeadphones), "Interview.Start");
            }
            OnState(_ctl.StateMachine.State);
        }

        public override void OnHide()
        {
            _ctl.StateChanged -= OnState;
            _ctl.Notice -= OnNotice;
            _ctl.Completed -= OnCompleted;
            _ctl.Failed -= OnFailed;
            _ctl.Transcript.Changed -= OnTranscript;
            App.Network.OnlineChanged -= OnOnline;
            _tick?.Pause();
        }

        public override bool OnBack()
        {
            ConfirmEnd();
            return true;
        }

        private void Tick()
        {
            int remaining = _ctl.RemainingSeconds;
            _timer.text = UiKit.FormatDuration(remaining);
            int total = Mathf.Max(1, _ctl.TotalSeconds);
            _progress.SetValue(1f - remaining / (float)total);
            _timer.style.color = remaining <= 60 && remaining > 0 ? Palette.Warning : Palette.Text;
            _interruptControl.style.opacity = _ctl.StateMachine.State == InterviewState.Speaking ? 1f : 0.35f;
            _interruptBtn.SetEnabled(_ctl.StateMachine.State == InterviewState.Speaking);
        }

        private void OnState(InterviewState state)
        {
            string text;
            string cls;
            string name = _ctl.InterviewerName;
            switch (state)
            {
                case InterviewState.Connecting: text = "Connecting…"; cls = "thinking"; break;
                case InterviewState.Ready: text = $"{name} is getting ready…"; cls = "thinking"; break;
                case InterviewState.Listening: text = _ctl.IsUserSpeaking ? "Listening…" : "Your turn — speak when ready"; cls = "listening"; break;
                case InterviewState.Processing: text = $"{name} is thinking…"; cls = "thinking"; break;
                case InterviewState.Speaking: text = $"{name} is speaking"; cls = "speaking"; break;
                case InterviewState.Interrupted: text = "Go ahead — you have the floor"; cls = "listening"; break;
                case InterviewState.Reconnecting: text = "Reconnecting…"; cls = "offline"; break;
                case InterviewState.Completed: text = "Interview complete"; cls = "listening"; break;
                case InterviewState.Error: text = "Connection problem"; cls = "offline"; break;
                default: text = string.Empty; cls = "thinking"; break;
            }
            if (_ctl.IsWrappingUp && state != InterviewState.Completed) text = "Wrapping up…";
            _statusText.text = text;
            foreach (var c in new[] { "speaking", "listening", "thinking", "offline" })
            {
                _statusPill.EnableInClassList("status-pill--" + c, c == cls);
            }
            _meter.Color = cls == "speaking" ? Palette.Accent : cls == "listening" ? Palette.Success : Palette.TextMuted;
            bool online = state != InterviewState.Reconnecting && state != InterviewState.Error;
            _netIcon.Kind = online ? IconKind.Wifi : IconKind.WifiOff;
            _netIcon.Color = online ? Palette.Success : Palette.Danger;
        }

        private void OnOnline(bool online)
        {
            if (!online) App.Overlay.Error("You're offline. We'll reconnect automatically.");
        }

        private void OnNotice(string message)
        {
            App.Overlay.Toast(message);
            OnState(_ctl.StateMachine.State);
        }

        private void ToggleMute()
        {
            _ctl.ToggleMute();
            bool muted = _ctl.IsMuted;
            var icon = _muteBtn.Q<Icon>();
            icon.Kind = muted ? IconKind.MicOff : IconKind.Mic;
            icon.Color = muted ? Palette.Bg : Palette.Text;
            _muteBtn.EnableInClassList("call-btn--active", muted);
            _muteBtn.parent.Q<Label>().text = muted ? "Unmute" : "Mute";
        }

        private void ToggleTranscript()
        {
            _transcriptVisible = !_transcriptVisible;
            UpdateTranscriptVisibility();
            App.Room.SetBottomUiFraction(_transcriptVisible ? 0.48f : 0.3f);
        }

        private void UpdateTranscriptVisibility()
        {
            _transcriptPanel.style.display = _transcriptVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _transcriptBtn.EnableInClassList("call-btn--active", _transcriptVisible);
            _transcriptBtn.Q<Icon>().Color = _transcriptVisible ? Palette.Bg : Palette.Text;
            if (_transcriptVisible) RenderTranscript();
        }

        private void OnTranscript(TranscriptLine line)
        {
            if (_transcriptVisible) RenderTranscript();
        }

        private void RenderTranscript()
        {
            _transcriptScroll.Clear();
            var lines = _ctl.Transcript.Lines;
            int start = Mathf.Max(0, lines.Count - TranscriptLines);
            if (lines.Count == 0)
            {
                _transcriptScroll.Add(T("Captions will appear here.", "caption"));
                return;
            }
            for (int i = start; i < lines.Count; i++)
            {
                var l = lines[i];
                var row = new VisualElement();
                row.AddToClassList("transcript-line");
                bool candidate = l.Speaker == Speaker.Candidate;
                if (candidate) row.AddToClassList("transcript-line--candidate");
                if (!l.IsFinal) row.AddToClassList("transcript-line--partial");
                row.Add(T(candidate ? "YOU" : _ctl.InterviewerName.ToUpperInvariant(), "transcript-line__speaker"));
                row.Add(T(l.Text + (l.Interrupted ? " …" : string.Empty), "transcript-line__text"));
                _transcriptScroll.Add(row);
            }
            _transcriptScroll.schedule.Execute(() =>
                _transcriptScroll.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(16);
        }

        private void ConfirmEnd()
        {
            App.Overlay.Dialog("End the interview?",
                "Your feedback report will be generated from the conversation so far.",
                new[]
                {
                    new DialogAction("End interview", ButtonStyle.Danger, () => MainThread.Run(_ctl.EndAsync, "Interview.End")),
                    new DialogAction("Keep going", ButtonStyle.Secondary)
                });
        }

        private void OnCompleted(InterviewCompletionInfo info)
        {
            App.Nav.Replace(new CompletionScreen(App, _session, info, _ctl.Transcript), Transition.Up);
        }

        private void OnFailed(InterviewFailureInfo failure)
        {
            _failure?.RemoveFromHierarchy();
            if (failure.Kind == InterviewFailure.Microphone && !Platform.MicrophonePermission.IsGranted)
            {
                App.Nav.Replace(new PermissionDeniedScreen(App), Transition.Fade);
                return;
            }
            if (failure.Kind == InterviewFailure.Unauthorized)
            {
                App.OnSignedOut(failure.Message);
                return;
            }
            var card = UiKit.Card();
            card.style.backgroundColor = new Color(0.086f, 0.122f, 0.208f, 0.97f);
            card.Add(UiKit.Row(new Icon(IconKind.Warning, 20f, Palette.Warning), UiKit.HSpacer(10),
                T(failure.Kind == InterviewFailure.PlanLimit ? "Plan limit reached" : "We lost the interviewer", "h3")));
            card.Add(UiKit.Spacer(6));
            card.Add(T(failure.Message, "body"));
            card.Add(UiKit.Spacer(14));
            if (failure.CanRetry)
            {
                var retry = UiKit.Button("Try again", () =>
                {
                    _failure?.RemoveFromHierarchy();
                    _ctl.RetryAfterFailure();
                }, icon: IconKind.Refresh);
                retry.AddToClassList("btn--block");
                card.Add(retry);
                card.Add(UiKit.Spacer(8));
            }
            if (failure.Kind == InterviewFailure.PlanLimit)
            {
                var upgrade = UiKit.Button("See Premium", App.OpenSubscription, ButtonStyle.Gold, IconKind.Crown);
                upgrade.AddToClassList("btn--block");
                card.Add(upgrade);
                card.Add(UiKit.Spacer(8));
            }
            var leave = UiKit.Button("End and leave", () =>
            {
                _ctl.Abort();
                MainThread.Run(async () =>
                {
                    try
                    {
                        await App.Api.CompleteInterviewAsync(_session.Id, "connection_failed");
                    }
                    catch (ApiException)
                    {
                        // The server expires the session on its own.
                    }
                }, "Interview.Leave");
                App.GoHome();
            }, ButtonStyle.Secondary);
            leave.AddToClassList("btn--block");
            card.Add(leave);
            _failure = card;
            _bottom.Insert(0, card);
        }
    }
}
