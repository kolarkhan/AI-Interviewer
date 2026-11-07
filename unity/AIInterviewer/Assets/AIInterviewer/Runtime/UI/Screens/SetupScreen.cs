using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Audio;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    /// <summary>Interview customisation. Locked (premium) options open the subscription screen.</summary>
    public sealed class SetupScreen : AppScreen
    {
        private InterviewConfig _config;
        private Catalog _catalog;
        private List<ResumeDocument> _documents = new List<ResumeDocument>();
        private ScrollView _scroll;
        private TextField _customRole;
        private Label _customRoleError;

        public SetupScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("New interview", () => App.Nav.Pop()));
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
            _scroll.Add(UiKit.Spacer(40));
            _scroll.Add(new Spinner(32f));
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Setup");
        }

        private async Task LoadAsync()
        {
            _catalog = await Guard(App.GetCatalogAsync(), "catalog");
            if (_catalog == null)
            {
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.WifiOff, "Couldn't load interview options", "Please try again.",
                    "Retry", () => MainThread.Run(LoadAsync, "Setup.Retry"), iconColor: Palette.Warning));
                return;
            }
            if (!_catalog.ResumeQuestions.Locked)
            {
                try
                {
                    _documents = (await App.Api.ListResumesAsync()).Documents;
                }
                catch (ApiException)
                {
                    _documents = new List<ResumeDocument>(); // offline etc.: treated as "no documents yet"
                }
            }
            _config = _config ?? Sanitize(App.Settings.LastConfig);
            Render();
        }

        /// <summary>Makes a remembered config valid for the current plan.</summary>
        private InterviewConfig Sanitize(InterviewConfig c)
        {
            T Pick<T>(List<T> options, Func<T, bool> isCurrent, Func<T, bool> locked, T fallback) where T : class
            {
                foreach (var o in options) if (isCurrent(o) && !locked(o)) return o;
                foreach (var o in options) if (!locked(o)) return o;
                return fallback;
            }

            c.RoleId = Pick(_catalog.Roles, r => r.Id == c.RoleId, r => r.Locked, null)?.Id ?? "software_engineer";
            c.InterviewType = Pick(_catalog.InterviewTypes, r => r.Id == c.InterviewType, r => r.Locked, null)?.Id ?? "technical";
            c.Difficulty = Pick(_catalog.Difficulties, r => r.Id == c.Difficulty, r => r.Locked, null)?.Id ?? "beginner";
            c.AvatarId = Pick(_catalog.Avatars, r => r.Id == c.AvatarId, r => r.Locked || !r.Available, null)?.Id ?? "alex";
            c.Voice = MatchingVoice(c.AvatarId, c.Voice);
            var duration = _catalog.Durations.Find(d => d.Minutes == c.DurationMinutes && !d.Locked) ??
                           _catalog.Durations.Find(d => !d.Locked);
            c.DurationMinutes = duration?.Minutes ?? 5;
            if (_catalog.Languages.Count > 0 && _catalog.Languages.Find(l => l.Id == c.Language) == null) c.Language = "en";
            if (string.IsNullOrEmpty(c.ExperienceLevel)) c.ExperienceLevel = App.Auth.User?.Preferences?.ExperienceLevel ?? "junior";
            if (_catalog.ResumeQuestions.Locked) c.UseResume = false;
            c.LiveTranscript = App.Auth.User?.Preferences?.ShowLiveTranscript ?? true;
            return c;
        }

        private void Render()
        {
            float scrollY = _scroll.scrollOffset.y;
            _scroll.Clear();
            _scroll.Add(T("Choose your interviewer, then tailor the interview to the job you want.", "body"));

            Section("Your interviewer");
            var avatars = _catalog.Avatars.FindAll(x => x.Available);
            var selected = avatars.Find(x => x.Id == _config.AvatarId);
            Dropdown("Choose your interviewer", selected?.Title ?? "Choose", AvatarDescription(selected),
                avatars.ConvertAll(x => new PickerItem(x.Id, x.Title, AvatarDescription(x), x.Locked)), _config.AvatarId,
                id =>
                {
                    _config.AvatarId = id;
                    _config.Voice = MatchingVoice(id, _config.Voice);
                });

            string interviewer = selected?.InterviewerName ?? selected?.Title ?? "Interviewer";
            Section($"{interviewer}'s voice");
            var voiceOptions = VoicesFor(_config.AvatarId);
            if (App.VoicePreview != null && !_catalog.DemoMode)
            {
                // Download this interviewer's samples now (chosen voice first) so play taps start instantly.
                App.VoicePreview.SetCacheVersion(_catalog.VoicePreviewVersion);
                var ids = new List<string> { _config.Voice };
                ids.AddRange(voiceOptions.ConvertAll(v => v.Id));
                App.VoicePreview.Prefetch(ids);
            }
            var chosenVoice = voiceOptions.Find(v => v.Id == _config.Voice);
            string voiceKind = selected?.Gender == "female" ? "Female voices" : selected?.Gender == "male" ? "Male voices" : "Voices";
            Dropdown(voiceKind, chosenVoice?.LabelWithTag ?? "Choose a voice", VoiceDescription(chosenVoice),
                voiceOptions.ConvertAll(v => new PickerItem(v.Id, v.LabelWithTag, VoiceDescription(v), v.Locked)),
                _config.Voice, id => _config.Voice = id,
                accessory: item => PreviewButton(item.Id), fieldAccessory: chosenVoice != null ? PreviewButton(chosenVoice.Id) : null);
            var hint = T("Tap the play button to hear a voice before you choose.", "caption");
            hint.style.marginTop = 6;
            _scroll.Add(hint);

            if (_catalog.Languages.Count > 0)
            {
                Section("Interview language");
                var language = _catalog.Languages.Find(l => l.Id == _config.Language);
                Dropdown("Interview language", language?.Title ?? "English",
                    language?.Description ?? "The interviewer speaks this language",
                    _catalog.Languages.ConvertAll(l => new PickerItem(l.Id, l.Title, l.Description, l.Locked)),
                    _config.Language, id => _config.Language = id);
            }

            Section("Target role");
            var role = _catalog.Roles.Find(r => r.Id == _config.RoleId);
            Dropdown("Target role", role?.Title ?? "Choose a role", null,
                _catalog.Roles.ConvertAll(r => new PickerItem(r.Id, r.Title, null, r.Locked)), _config.RoleId,
                id => _config.RoleId = id);
            if (_config.RoleId == "custom")
            {
                var f = UiKit.Field(null, "e.g. Product Manager, DevOps Engineer");
                _customRole = f.input;
                _customRoleError = f.error;
                _customRole.maxLength = 80;
                _customRole.value = _config.CustomRoleTitle ?? string.Empty;
                _customRole.RegisterValueChangedCallback(e => _config.CustomRoleTitle = e.newValue);
                f.container.style.marginTop = 10;
                _scroll.Add(f.container);
            }

            Section("Interview type");
            var type = _catalog.InterviewTypes.Find(t => t.Id == _config.InterviewType);
            Dropdown("Interview type", type?.Title ?? "Choose a type", type?.Description,
                _catalog.InterviewTypes.ConvertAll(t => new PickerItem(t.Id, t.Title, t.Description, t.Locked)),
                _config.InterviewType, id => _config.InterviewType = id);

            Section("Difficulty");
            var difficulty = _catalog.Difficulties.Find(d => d.Id == _config.Difficulty);
            Dropdown("Difficulty", difficulty?.Title ?? "Choose", difficulty?.Description,
                _catalog.Difficulties.ConvertAll(d => new PickerItem(d.Id, d.Title, d.Description, d.Locked)),
                _config.Difficulty, id => _config.Difficulty = id);

            Section("Your experience level");
            var experience = _catalog.ExperienceLevels.Find(e => e.Id == _config.ExperienceLevel);
            Dropdown("Your experience level", experience?.Title ?? "Choose", experience?.Description,
                _catalog.ExperienceLevels.ConvertAll(e => new PickerItem(e.Id, e.Title, e.Description)),
                _config.ExperienceLevel, id => _config.ExperienceLevel = id);

            Section("Duration");
            Dropdown("Duration", _config.DurationMinutes + " min", null,
                _catalog.Durations.ConvertAll(d => new PickerItem(d.Minutes.ToString(), d.Minutes + " min",
                    d.Locked ? "Premium" : null, d.Locked)),
                _config.DurationMinutes.ToString(), id => _config.DurationMinutes = int.Parse(id));

            Section("Options");
            string resumeSubtitle;
            if (_catalog.ResumeQuestions.Locked) resumeSubtitle = "Premium feature";
            else if (_documents.Count == 0) resumeSubtitle = "Upload your resume, cover letter and more";
            else resumeSubtitle = (_documents.Count == 1 ? "Using " : "Using " + _documents.Count + " documents: ") +
                                  string.Join(", ", _documents.ConvertAll(d => d.Filename)) + " · tap to manage";
            var resumeSwitch = new Switch(_config.UseResume && _documents.Count > 0, on =>
            {
                if (_catalog.ResumeQuestions.Locked)
                {
                    App.OpenSubscription();
                    Render();
                    return;
                }
                if (on && _documents.Count == 0)
                {
                    OpenDocuments(enableOnReturn: true);
                    return;
                }
                _config.UseResume = on;
            });
            var transcriptSwitch = new Switch(_config.LiveTranscript, on => _config.LiveTranscript = on);
            _scroll.Add(UiKit.SettingsGroup(
                UiKit.SettingsRow("Questions from my resume", resumeSubtitle, resumeSwitch,
                    _catalog.ResumeQuestions.Locked ? (Action)App.OpenSubscription : () => OpenDocuments(enableOnReturn: false)),
                UiKit.SettingsRow("Show live transcript", "Captions of the conversation during the interview", transcriptSwitch)));

            _scroll.Add(UiKit.Spacer(8));
            var next = UiKit.Button("Continue", Continue);
            next.AddToClassList("btn--block");
            _scroll.Add(next);
            _scroll.Add(UiKit.Spacer(24));
            _scroll.schedule.Execute(() => _scroll.scrollOffset = new Vector2(0, scrollY)).StartingIn(1);
        }

        /// <summary>Dropdown field; picking a locked option opens the subscription screen instead.</summary>
        private void Dropdown(string sheetTitle, string value, string caption, List<PickerItem> items, string selectedId,
            Action<string> apply, Func<PickerItem, VisualElement> accessory = null, VisualElement fieldAccessory = null)
        {
            _scroll.Add(UiKit.SelectField(value, caption, () =>
                App.Overlay.OptionSheet(sheetTitle, items, selectedId,
                    item => Select(item.Locked, () => apply(item.Id)), accessory), fieldAccessory));
        }

        public override void OnHide()
        {
            App.VoicePreview?.Stop();
        }

        /// <summary>Round play/stop button that previews a voice; shows a spinner while the sample downloads.</summary>
        private VisualElement PreviewButton(string voiceId)
        {
            var player = App.VoicePreview;
            var button = new VisualElement();
            button.AddToClassList("preview-btn");
            if (player == null) return button;

            void Refresh()
            {
                button.Clear();
                bool loading = player.LoadingVoice == voiceId;
                bool playing = player.PlayingVoice == voiceId;
                button.EnableInClassList("preview-btn--active", loading || playing);
                if (loading) button.Add(new Spinner(18f, Palette.Accent));
                else button.Add(new Icon(playing ? IconKind.Stop : IconKind.Play, 16f, playing ? Palette.Bg : Palette.Accent));
                foreach (var child in button.Children()) child.pickingMode = PickingMode.Ignore;
            }

            Action changed = Refresh;
            button.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                player.Changed -= changed;
                player.Changed += changed;
                Refresh();
            });
            button.RegisterCallback<DetachFromPanelEvent>(_ => player.Changed -= changed);
            // Pointer events too, so the row/field underneath doesn't open or select on a play tap.
            button.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            button.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            button.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                MainThread.Run(async () =>
                {
                    try
                    {
                        await player.ToggleAsync(voiceId);
                    }
                    catch (ApiException ex)
                    {
                        App.Overlay.Error(ex.Kind == ApiFailureKind.Network
                            ? "Couldn't load the voice sample. Check your connection."
                            : ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        App.Overlay.Error(ex.Message);
                    }
                }, "VoicePreview");
            });
            Refresh();
            return button;
        }

        private static string AvatarDescription(CatalogOption avatar)
        {
            if (avatar == null) return null;
            return avatar.Gender == "female" ? "Professional female interviewer" :
                avatar.Gender == "male" ? "Professional male interviewer" : avatar.Description;
        }

        private static string VoiceDescription(CatalogOption voice)
        {
            if (voice == null) return null;
            string text = voice.Description ?? string.Empty;
            if (voice.Language != null && voice.Language != "English")
                text += (text.Length > 0 ? " · " : string.Empty) + voice.Language + " accent";
            return text.Length > 0 ? text : null;
        }

        /// <summary>Voices that suit the interviewer (same gender); all voices if the avatar has no gender.</summary>
        private List<CatalogOption> VoicesFor(string avatarId)
        {
            var avatar = _catalog.Avatars.Find(a => a.Id == avatarId);
            if (avatar == null || string.IsNullOrEmpty(avatar.Gender)) return _catalog.Voices;
            return _catalog.Voices.FindAll(v => v.Gender == avatar.Gender);
        }

        /// <summary>Keeps the current voice if it suits the interviewer, otherwise the interviewer's default voice.</summary>
        private string MatchingVoice(string avatarId, string currentVoice)
        {
            var options = VoicesFor(avatarId);
            var current = options.Find(v => v.Id == currentVoice && !v.Locked);
            if (current != null) return current.Id;
            var avatar = _catalog.Avatars.Find(a => a.Id == avatarId);
            var preferred = options.Find(v => v.Id == avatar?.DefaultVoice && !v.Locked);
            if (preferred != null) return preferred.Id;
            var firstUnlocked = options.Find(v => !v.Locked);
            return firstUnlocked?.Id ?? avatar?.DefaultVoice ?? currentVoice;
        }

        /// <summary>Opens the document upload screen; documents added there switch resume questions on.</summary>
        private void OpenDocuments(bool enableOnReturn)
        {
            App.Nav.Push(new ResumeScreen(App, count =>
            {
                if (count > 0 && (enableOnReturn || _config.UseResume)) _config.UseResume = true;
                if (count == 0) _config.UseResume = false;
            }), Transition.Up);
        }

        private void Select(bool locked, Action apply)
        {
            if (locked)
            {
                App.OpenSubscription();
                return;
            }
            apply();
            Render();
        }

        private void Continue()
        {
            if (_config.RoleId == "custom")
            {
                string title = (_config.CustomRoleTitle ?? string.Empty).Trim();
                if (title.Length < 2)
                {
                    if (_customRole != null) UiKit.SetFieldError(_customRole, _customRoleError, "Enter the role title");
                    return;
                }
                _config.CustomRoleTitle = title;
            }
            if (_config.UseResume && _documents.Count == 0) _config.UseResume = false;
            App.Settings.LastConfig = _config;
            App.Nav.Push(new LobbyScreen(App, _config), Transition.Up);
        }

        private void Section(string title)
        {
            var l = T(title, "h3");
            l.AddToClassList("section-title");
            _scroll.Add(l);
        }

    }
}
