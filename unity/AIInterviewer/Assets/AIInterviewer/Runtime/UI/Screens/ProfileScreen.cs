using System;
using System.Collections.Generic;
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
    public sealed class ProfileScreen : AppScreen
    {
        private ScrollView _scroll;

        public ProfileScreen(AppServices app) : base(app)
        {
        }

        public override TabId Tab => TabId.Profile;

        protected override void Build()
        {
            var title = T("Profile", "title");
            title.style.marginTop = 16;
            title.style.marginBottom = 12;
            Content.Add(title);
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Profile");
        }

        private async Task LoadAsync()
        {
            var sub = await Guard(App.GetSubscriptionAsync(refresh: true), "subscription");
            Render(sub);
        }

        private void Render(SubscriptionResponse sub)
        {
            _scroll.Clear();
            var user = App.Auth.User;
            if (user == null) return;

            // Account card
            var card = UiKit.Card();
            var avatar = new VisualElement();
            avatar.style.width = 52;
            avatar.style.height = 52;
            avatar.style.borderTopLeftRadius = 26;
            avatar.style.borderTopRightRadius = 26;
            avatar.style.borderBottomLeftRadius = 26;
            avatar.style.borderBottomRightRadius = 26;
            avatar.style.alignItems = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            UiKit.HeroGradient(avatar);
            avatar.style.overflow = Overflow.Hidden;
            var initial = T(string.IsNullOrEmpty(user.DisplayName) ? "?" : user.DisplayName.Substring(0, 1).ToUpperInvariant(), "h2");
            initial.style.color = Color.white;
            avatar.Add(initial);
            var info = UiKit.Column(T(user.DisplayName, "h2"), T(user.Email, "caption"));
            info.style.marginLeft = 14;
            info.style.flexShrink = 1;
            card.Add(UiKit.Row(avatar, info));
            _scroll.Add(card);

            // Plan
            if (sub != null)
            {
                bool premium = sub.Plan?.Id == "premium";
                var plan = UiKit.Card(premium ? "card--accent" : null);
                plan.Add(UiKit.Row(T(premium ? "Premium" : "Free plan", "h3"), UiKit.Spacer(0, grow: true),
                    premium ? UiKit.Badge(sub.Subscription.IsTestGrant ? "TEST" : "ACTIVE", "gold", IconKind.Crown) :
                        UiKit.Button("Upgrade", App.OpenSubscription, ButtonStyle.Gold, small: true)));
                if (sub.Usage != null)
                {
                    plan.Add(UiKit.Spacer(10));
                    plan.Add(T($"{sub.Usage.InterviewsUsed} of {sub.Usage.InterviewsLimit} interviews used this month", "caption"));
                    plan.Add(UiKit.Spacer(6));
                    plan.Add(new ProgressTrack(sub.Usage.InterviewsLimit == 0 ? 0f : sub.Usage.InterviewsUsed / (float)sub.Usage.InterviewsLimit));
                }
                plan.RegisterCallback<ClickEvent>(_ => App.OpenSubscription());
                _scroll.Add(plan);
            }

            Section("Interview preferences");
            var headphones = new Switch(App.Settings.UseHeadphones, on => App.Settings.UseHeadphones = on);
            var captions = new Switch(user.Preferences.ShowLiveTranscript, on =>
                MainThread.Run(() => UpdatePrefsAsync(new PreferencesUpdate { ShowLiveTranscript = on }), "Prefs"));
            bool uLipSync = LipSyncProviders.IsULipSyncInstalled;
            var lipSync = new Switch(App.Settings.LipSyncProvider == LipSyncProviderKind.ULipSync && uLipSync, on =>
            {
                if (!uLipSync)
                {
                    App.Overlay.Toast("uLipSync is not installed in this build.");
                    return;
                }
                App.Settings.LipSyncProvider = on ? LipSyncProviderKind.ULipSync : LipSyncProviderKind.BuiltInSpectral;
                App.Room.SetAvatar(App.Room.CurrentAvatarId ?? AvatarFactory.DefaultId, LipSyncProviders.Create(App.Settings.LipSyncProvider));
            });
            _scroll.Add(UiKit.SettingsGroup(
                UiKit.SettingsRow("Headphones by default", "Enables natural interruptions by voice", headphones),
                UiKit.SettingsRow("Live captions", "Show the transcript during interviews", captions),
                UiKit.SettingsRow("High-fidelity lip-sync", uLipSync ? "uLipSync phoneme analysis" : "Not installed (built-in lip-sync active)", lipSync),
                UiKit.SettingsRow("Resume & documents", "Resume, cover letter and more: tailors questions (Premium)",
                    new Icon(IconKind.ChevronRight, 18f, Palette.TextMuted),
                    () => App.Nav.Push(new ResumeScreen(App)))));

            Section("Privacy & data");
            var retain = new Switch(user.Preferences.RetainTranscripts, on =>
                MainThread.Run(() => UpdatePrefsAsync(new PreferencesUpdate { RetainTranscripts = on }), "Prefs"));
            _scroll.Add(UiKit.SettingsGroup(
                UiKit.SettingsRow("Keep transcripts", "Off: transcripts are deleted after your report. On: kept for up to 90 days.", retain),
                UiKit.SettingsRow("Delete interview data", "Sessions, transcripts, reports and resume", new Icon(IconKind.Trash, 18f, Palette.Danger),
                    () => MainThread.Run(DeleteDataAsync, "DeleteData"), danger: true),
                UiKit.SettingsRow("Delete account", "Permanently remove your account and all data", new Icon(IconKind.Trash, 18f, Palette.Danger),
                    DeleteAccount, danger: true)));

            Section("About");
            _scroll.Add(UiKit.SettingsGroup(
                UiKit.SettingsRow("Privacy policy", null, new Icon(IconKind.ChevronRight, 18f, Palette.TextMuted),
                    () => Application.OpenURL(App.Config.PrivacyPolicyUrl)),
                UiKit.SettingsRow("Terms of service", null, new Icon(IconKind.ChevronRight, 18f, Palette.TextMuted),
                    () => Application.OpenURL(App.Config.TermsUrl)),
                UiKit.SettingsRow("Credits & licences", null, new Icon(IconKind.ChevronRight, 18f, Palette.TextMuted),
                    () => App.Nav.Push(new CreditsScreen(App))),
                UiKit.SettingsRow("Version", $"{Application.version} · {App.Config.Environment}", null)));

            if (!App.Config.IsProduction)
            {
                Section("Developer");
                _scroll.Add(UiKit.SettingsGroup(
                    UiKit.SettingsRow("Grant test Premium (30 days)", "Development builds only — not a real purchase", null,
                        () => MainThread.Run(DevPremiumAsync, "DevPremium")),
                    UiKit.SettingsRow("Reset to Free plan", "Development builds only", null,
                        () => MainThread.Run(DevFreeAsync, "DevFree"))));
            }

            var signOut = UiKit.Button("Sign out", () => MainThread.Run(App.Auth.LogoutAsync, "Logout"), ButtonStyle.Secondary, IconKind.Logout);
            signOut.AddToClassList("btn--block");
            signOut.style.marginTop = 8;
            _scroll.Add(signOut);
        }

        private async Task UpdatePrefsAsync(PreferencesUpdate update)
        {
            var user = await Guard(App.Api.UpdateMeAsync(new ProfileUpdate { Preferences = update }), "preferences");
            if (user != null)
            {
                App.Auth.SetUser(user);
                App.Overlay.Success("Saved");
            }
        }

        private async Task DeleteDataAsync()
        {
            bool ok = await App.Overlay.ConfirmAsync("Delete all interview data?",
                "All sessions, transcripts, reports and your resume will be permanently deleted. Your account and plan stay.",
                "Delete data", destructive: true);
            if (!ok) return;
            var result = await Guard(App.Api.DeleteMyDataAsync(), "delete data");
            if (result != null) App.Overlay.Success($"Deleted {result.SessionsDeleted} interviews");
        }

        private void DeleteAccount()
        {
            var field = UiKit.Field("Confirm with your password", "Password", password: true);
            App.Overlay.Dialog("Delete your account?",
                "This permanently deletes your account, interviews, reports and resume. This cannot be undone.",
                new[]
                {
                    new DialogAction("Delete account", ButtonStyle.Danger,
                        () => MainThread.Run(() => DeleteAccountAsync(field.input.value), "DeleteAccount")),
                    new DialogAction("Cancel", ButtonStyle.Secondary)
                }, dismissible: false, extraContent: field.container);
        }

        private async Task DeleteAccountAsync(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                App.Overlay.Error("Password is required to delete your account.");
                return;
            }
            App.Overlay.ShowVeil("Deleting account…");
            try
            {
                await App.Api.DeleteAccountAsync(password);
                App.Overlay.HideVeil();
                App.Auth.ForgetLocally();
                App.Overlay.Success("Your account has been deleted.");
            }
            catch (ApiException ex)
            {
                App.Overlay.HideVeil();
                if (ex.Kind == ApiFailureKind.Unauthorized) App.Overlay.Error("Incorrect password.");
                else App.HandleApiError(ex, "delete account");
            }
        }

        private async Task DevPremiumAsync()
        {
            if (await Guard(App.Api.DevGrantPremiumAsync(), "dev premium") == null) return;
            App.InvalidatePlanCaches();
            App.Overlay.Success("Test Premium activated");
            await LoadAsync();
        }

        private async Task DevFreeAsync()
        {
            if (await Guard(App.Api.DevResetFreeAsync(), "dev free") == null) return;
            App.InvalidatePlanCaches();
            App.Overlay.Success("Back on the Free plan");
            await LoadAsync();
        }

        private void Section(string title)
        {
            var l = T(title, "h3");
            l.AddToClassList("section-title");
            _scroll.Add(l);
        }
    }

    /// <summary>
    /// Resume and related documents (up to 5: resume, cover letter, LinkedIn export ...). Files are picked with the
    /// native document picker (several at once where the OS supports it) or text can be pasted.
    /// </summary>
    public sealed class ResumeScreen : AppScreen
    {
        private readonly Action<int> _onUseForInterview;
        private readonly IFilePicker _picker = FilePicker.Create();
        private ScrollView _scroll;
        private TextField _text;
        private ResumeDocumentList _docs;
        private bool _busy;

        /// <param name="onUseForInterview">Set when opened from interview setup: called with the document count.</param>
        public ResumeScreen(AppServices app, Action<int> onUseForInterview = null) : base(app)
        {
            _onUseForInterview = onUseForInterview;
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("Resume & documents", Back));
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
        }

        private void Back()
        {
            _onUseForInterview?.Invoke(_docs?.Documents.Count ?? 0);
            App.Nav.Pop();
        }

        public override bool OnBack()
        {
            Back();
            return true;
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Resume");
        }

        private async Task LoadAsync()
        {
            _scroll.Clear();
            _scroll.Add(UiKit.Spacer(30));
            _scroll.Add(new Spinner(28f));
            var catalog = await Guard(App.GetCatalogAsync(), "catalog");
            if (catalog != null && catalog.ResumeQuestions.Locked)
            {
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.Crown, "Resume-based questions",
                    "Premium tailors questions to your experience by reading your resume and related documents.",
                    "See Premium", App.OpenSubscription, iconColor: Palette.Gold));
                return;
            }
            _docs = await Guard(App.Api.ListResumesAsync(), "documents") ?? new ResumeDocumentList();
            Render();
        }

        private void Render()
        {
            _scroll.Clear();
            int count = _docs.Documents.Count;
            int max = Math.Max(1, _docs.MaxDocuments);
            _scroll.Add(T("Add your resume and, if you like, a cover letter or LinkedIn profile. The interviewer uses " +
                          "them to ask about your real experience.", "body"));
            _scroll.Add(UiKit.Spacer(10));
            _scroll.Add(UiKit.Banner("Only the extracted text is stored, never the file. Delete documents any time.",
                "info", IconKind.Shield));

            var header = UiKit.Row(T("Your documents", "h3"), UiKit.Spacer(0, grow: true), T(count + " of " + max, "caption"));
            header.style.marginTop = 8;
            header.style.marginBottom = 8;
            _scroll.Add(header);

            if (count == 0)
            {
                var empty = UiKit.Card("card--flat");
                empty.Add(T("No documents yet", "h3"));
                empty.Add(UiKit.Spacer(4));
                empty.Add(T("PDF, DOCX or TXT - up to 2 MB each - up to " + max + " documents", "caption"));
                _scroll.Add(empty);
            }
            foreach (var doc in _docs.Documents)
            {
                var d = doc;
                var remove = UiKit.IconButton(IconKind.Trash, () => MainThread.Run(() => DeleteAsync(d), "Resume.Delete"),
                    18f, Palette.Danger);
                string meta = (d.Kind ?? "").ToUpperInvariant() + " · " + d.CharCount.ToString("N0") + " characters · " +
                              UiKit.FormatDate(d.UploadedAt);
                _scroll.Add(UiKit.ListItem(d.Filename, meta, IconKind.Document, remove, null));
            }

            if (count < max)
            {
                bool multiple = _picker.CanPickMultiple;
                string label = multiple ? "Upload files" : (count == 0 ? "Upload a file" : "Add another file");
                var pick = UiKit.Button(label, () => MainThread.Run(PickAndUploadAsync, "Resume.Pick"), icon: IconKind.Document);
                pick.AddToClassList("btn--block");
                pick.style.marginTop = 6;
                pick.SetEnabled(_picker.IsSupported && !_busy);
                _scroll.Add(pick);
                var hint = T(_picker.IsSupported
                        ? (multiple ? "You can select several files at once." : "Choose one file at a time.")
                        : "File upload isn't available on this device. Paste the text below instead.", "caption");
                hint.style.marginTop = 6;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                _scroll.Add(hint);

                var pasteTitle = T("Or paste text", "h3");
                pasteTitle.AddToClassList("section-title");
                _scroll.Add(pasteTitle);
                var field = UiKit.Field(null, "Experience, projects, skills...", multiline: true);
                _text = field.input;
                _text.maxLength = 12000;
                _scroll.Add(field.container);
                var save = UiKit.Button("Save text", null, ButtonStyle.Secondary);
                save.clicked += () => MainThread.Run(() => SaveTextAsync(save), "Resume.Save");
                save.AddToClassList("btn--block");
                _scroll.Add(save);
            }

            if (_onUseForInterview != null && count > 0)
            {
                string useLabel = count == 1 ? "Use this document for my interview" : "Use these " + count + " documents for my interview";
                var use = UiKit.Button(useLabel, Back, icon: IconKind.Check);
                use.AddToClassList("btn--block");
                use.style.marginTop = 16;
                _scroll.Add(use);
            }

            var consent = T("By uploading, you agree that the text is processed by our AI provider to tailor your " +
                            "interview questions.", "caption");
            consent.style.marginTop = 10;
            _scroll.Add(consent);
        }

        private async Task PickAndUploadAsync()
        {
            if (_busy) return;
            List<PickedDocument> picked;
            try
            {
                picked = await FilePicker.PickDocumentsAsync(_picker, multiple: true);
            }
            catch (System.IO.IOException ex)
            {
                App.Overlay.Error(ex.Message);
                return;
            }
            if (picked == null || picked.Count == 0) return;
            int room = _docs.MaxDocuments - _docs.Documents.Count;
            if (picked.Count > room)
            {
                App.Overlay.Error("You can add " + room + " more document" + (room == 1 ? "." : "s."));
                return;
            }
            _busy = true;
            App.Overlay.ShowVeil(picked.Count == 1 ? "Reading your document..." : "Reading " + picked.Count + " documents...");
            var files = new List<(string, byte[])>();
            foreach (var p in picked) files.Add((p.FileName, p.Data));
            var result = await Guard(App.Api.UploadResumeFilesAsync(files), "upload documents");
            App.Overlay.HideVeil();
            _busy = false;
            if (result == null) return;
            _docs = result;
            App.Overlay.Success(picked.Count == 1 ? "Document added" : picked.Count + " documents added");
            Render();
        }

        private async Task SaveTextAsync(Button save)
        {
            string text = (_text?.value ?? string.Empty).Trim();
            if (text.Length < 100)
            {
                App.Overlay.Error("Please paste at least 100 characters.");
                return;
            }
            var restore = UiKit.SetLoading(save, "Saving...");
            var result = await Guard(App.Api.UploadResumeTextAsync(text), "paste resume");
            restore();
            if (result == null) return;
            _docs = result;
            App.Overlay.Success("Text saved");
            Render();
        }

        private async Task DeleteAsync(ResumeDocument doc)
        {
            if (!await App.Overlay.ConfirmAsync("Remove " + doc.Filename + "?", "Its text will be deleted.", "Remove",
                    destructive: true)) return;
            if (!await Guard(App.Api.DeleteResumeAsync(doc.Id), "delete document")) return;
            _docs.Documents.RemoveAll(x => x.Id == doc.Id);
            App.Overlay.Success("Document removed");
            Render();
        }
    }

    public sealed class CreditsScreen : AppScreen
    {
        public CreditsScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("Credits & licences", () => App.Nav.Pop()));
            var scroll = UiKit.Scroll();
            Content.Add(scroll);
            var card = UiKit.Card();
            card.Add(T("Voice AI", "h3"));
            card.Add(T("Conversations and feedback are powered by the OpenAI API. AI output can be inaccurate.", "body"));
            card.Add(UiKit.Spacer(12));
            card.Add(T("Lip-sync", "h3"));
            card.Add(T("Built-in spectral analysis. Optional: uLipSync by hecomi (MIT licence).", "body"));
            card.Add(UiKit.Spacer(12));
            card.Add(T("Interviewer avatar", "h3"));
            card.Add(T("Interviewers Alex and Maya: Microsoft Rocketbox Avatar Library, MIT License, (c) 2020 Microsoft. " +
                       "A simple built-in avatar is used if a character is not installed.", "body"));
            card.Add(UiKit.Spacer(12));
            card.Add(T("Open-source", "h3"));
            card.Add(T("Newtonsoft.Json (MIT) · Unity Technologies packages under the Unity Companion License.", "body"));
            scroll.Add(card);
        }
    }

    public sealed class PermissionDeniedScreen : AppScreen
    {
        public PermissionDeniedScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar(string.Empty, () => { if (!App.Nav.Pop()) App.GoHome(); }));
            Content.Add(UiKit.Spacer(0, grow: true));
            Content.Add(UiKit.StatusView(IconKind.MicOff, "Microphone access is off",
                Brand.AppName + " needs your microphone to hear your answers. Your voice is streamed only during the interview " +
                "and is never recorded.",
                "Open settings", Platform.MicrophonePermission.OpenAppSettings,
                "Back to home", App.GoHome, Palette.Warning));
            Content.Add(UiKit.Spacer(0, grow: true));
        }

        public override void OnShow()
        {
            // Returning from OS settings with the permission granted: go back automatically.
            Root.schedule.Execute(() =>
            {
                if (App.Nav.Current == this && Platform.MicrophonePermission.IsGranted)
                {
                    App.Overlay.Success("Microphone enabled");
                    if (!App.Nav.Pop()) App.GoHome();
                }
            }).Every(1000);
        }
    }
}
