using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Avatar;
using AIInterviewer.Core.Api;
using AIInterviewer.Platform;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    /// <summary>Avatar preview, microphone check and connection — the "green room" before the interview.</summary>
    public sealed class LobbyScreen : AppScreen
    {
        private readonly InterviewConfig _config;
        private VisualElement _sheet;
        private VisualElement _micRow;
        private Button _start;
        private bool _testingMic;

        public LobbyScreen(AppServices app, InterviewConfig config) : base(app)
        {
            _config = config;
        }

        public override bool ShowsInterviewRoom => true;

        protected override void Build()
        {
            Content.Add(UiKit.TopBar(string.Empty, () => App.Nav.Pop()));
            Content.Add(UiKit.Spacer(0, grow: true));

            _sheet = UiKit.Card();
            _sheet.style.marginBottom = 8;
            _sheet.style.backgroundColor = new Color(0.086f, 0.122f, 0.208f, 0.94f);
            Content.Add(_sheet);
        }

        public override void OnShow()
        {
            if (App.Room.Avatar == null || App.Room.CurrentAvatarId != _config.AvatarId)
            {
                App.Room.SetAvatar(_config.AvatarId, LipSyncProviders.Create(App.Settings.LipSyncProvider));
            }
            App.Room.Avatar?.SetMood(AvatarMood.Idle);
            App.Room.SetBottomUiFraction(0.45f);
            Render();
        }

        public override void OnHide()
        {
            StopMicTest();
        }

        private void Render()
        {
            _sheet.Clear();
            string roleTitle = _config.RoleId == "custom" ? _config.CustomRoleTitle : RoleTitle(_config.RoleId);
            var badges = UiKit.Row();
            badges.AddToClassList("row--wrap");
            if (App.Catalog != null && App.Catalog.DemoMode) badges.Add(Tag(UiKit.Badge("DEMO MODE", "warning", IconKind.Info)));
            if (App.Room.AvatarIsPlaceholder) badges.Add(Tag(UiKit.Badge("PLACEHOLDER AVATAR", null, IconKind.User)));
            if (badges.childCount > 0) _sheet.Add(badges);

            _sheet.Add(T($"Meet {InterviewerName()}, your interviewer", "h2"));
            _sheet.Add(UiKit.Spacer(4));
            _sheet.Add(T($"{roleTitle} · {Cap(_config.InterviewType)} · {Cap(_config.Difficulty)} · {_config.DurationMinutes} min" +
                  (string.IsNullOrEmpty(_config.Language) || _config.Language == "en" ? string.Empty : " · " + LanguageName(_config.Language)),
                "body"));
            if (App.Room.AvatarNotice != null)
            {
                _sheet.Add(UiKit.Spacer(8));
                _sheet.Add(T(App.Room.AvatarNotice, "caption"));
            }
            _sheet.Add(UiKit.Spacer(14));

            _micRow = new VisualElement();
            _sheet.Add(_micRow);
            RenderMicRow();

            var headphones = new Switch(App.Settings.UseHeadphones, on =>
            {
                App.Settings.UseHeadphones = on;
                App.Overlay.Toast(on ? "Full-duplex: just start talking to interrupt." :
                    "Speakerphone: speak up clearly to interrupt, or tap the hand button.");
            });
            _sheet.Add(UiKit.SettingsGroup(
                UiKit.SettingsRow("I'm using headphones", "Best quality and natural interruptions", headphones)));

            _start = UiKit.Button("Start interview", () => MainThread.Run(StartAsync, "Lobby.Start"), icon: IconKind.Play);
            _start.AddToClassList("btn--block");
            _sheet.Add(_start);
            var note = T("Your voice is streamed for the live conversation and is never recorded.", "caption");
            note.style.marginTop = 10;
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sheet.Add(note);
        }

        private void RenderMicRow()
        {
            _micRow.Clear();
            bool granted = MicrophonePermission.IsGranted;
            VisualElement trailing;
            if (!granted)
            {
                trailing = UiKit.Button("Allow", () => MainThread.Run(RequestMicAsync, "Lobby.Mic"), small: true);
            }
            else if (_testingMic)
            {
                trailing = UiKit.Row(new VoiceMeter(() => App.Interview.MicLevel, Palette.Success),
                    UiKit.HSpacer(8), UiKit.Button("Stop", StopMicTest, ButtonStyle.Secondary, small: true));
            }
            else
            {
                trailing = UiKit.Button("Test", StartMicTest, ButtonStyle.Secondary, small: true);
            }
            _micRow.Add(UiKit.SettingsGroup(UiKit.SettingsRow(
                granted ? "Microphone ready" : "Microphone access needed",
                granted ? (_testingMic ? "Say something — the bars should move" : "Check your input level") :
                    "Required to talk with the interviewer",
                trailing)));
        }

        private async Task RequestMicAsync()
        {
            var result = await MicrophonePermission.RequestAsync(App.Host);
            if (result == PermissionResult.Denied)
            {
                App.Nav.Push(new PermissionDeniedScreen(App));
                return;
            }
            RenderMicRow();
        }

        private void StartMicTest()
        {
            var mic = App.Host.GetComponent<Audio.MicrophoneCapture>();
            if (mic != null && mic.StartCapture())
            {
                _testingMic = true;
                RenderMicRow();
            }
        }

        private void StopMicTest()
        {
            if (!_testingMic) return;
            _testingMic = false;
            App.Host.GetComponent<Audio.MicrophoneCapture>()?.StopCapture();
            if (_micRow != null) RenderMicRow();
        }

        private async Task StartAsync()
        {
            StopMicTest();
            if (!MicrophonePermission.IsGranted &&
                await MicrophonePermission.RequestAsync(App.Host) != PermissionResult.Granted)
            {
                App.Nav.Push(new PermissionDeniedScreen(App));
                return;
            }
            var restore = UiKit.SetLoading(_start, "Preparing your interview…");
            InterviewSession session;
            try
            {
                session = await App.Api.CreateInterviewAsync(_config);
            }
            catch (ApiException ex)
            {
                restore();
                App.HandleApiError(ex, "create interview");
                return;
            }
            restore();
            App.InvalidatePlanCaches();
            App.Nav.Replace(new InterviewScreen(App, session), Transition.Fade);
        }

        private static VisualElement Tag(VisualElement badge)
        {
            badge.style.marginRight = 6;
            badge.style.marginBottom = 10;
            return badge;
        }

        private string RoleTitle(string id)
        {
            if (App.Catalog != null)
            {
                var r = App.Catalog.Roles.Find(x => x.Id == id);
                if (r != null) return r.Title;
            }
            return id;
        }

        private string InterviewerName()
        {
            var avatar = App.Catalog?.Avatars.Find(a => a.Id == _config.AvatarId);
            return !string.IsNullOrEmpty(avatar?.InterviewerName) ? avatar.InterviewerName : "Alex";
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private string LanguageName(string code)
        {
            var language = App.Catalog?.Languages?.Find(l => l.Id == code);
            return language?.Title ?? code.ToUpperInvariant();
        }
    }
}
