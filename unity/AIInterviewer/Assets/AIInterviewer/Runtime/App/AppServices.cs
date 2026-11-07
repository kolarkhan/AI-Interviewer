using System.Threading.Tasks;
using AIInterviewer.Core.Api;
using AIInterviewer.Net;
using AIInterviewer.Platform;
using AIInterviewer.Realtime;
using AIInterviewer.Scene;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using AIInterviewer.UI.Screens;
using UnityEngine;

namespace AIInterviewer.App
{
    /// <summary>Facade passed to every screen: services, caches and cross-screen navigation helpers.</summary>
    public sealed class AppServices
    {
        private HomeScreen _home;
        private HistoryScreen _history;
        private ProgressScreen _progress;
        private ProfileScreen _profile;

        public AppConfig Config { get; internal set; }
        public AuthSession Auth { get; internal set; }
        public BackendApi Api { get; internal set; }
        public UiRoot Ui { get; internal set; }
        public InterviewRoom Room { get; internal set; }
        public LiveInterviewController Interview { get; internal set; }
        public NetworkMonitor Network { get; internal set; }
        public LocalSettings Settings { get; internal set; }
        public MonoBehaviour Host { get; internal set; }
        public AIInterviewer.Audio.VoicePreviewPlayer VoicePreview { get; internal set; }

        public Navigator Nav => Ui.Navigator;
        public Overlay Overlay => Ui.Overlay;

        public Catalog Catalog { get; private set; }
        public SubscriptionResponse Subscription { get; private set; }

        public bool IsPremium => Subscription?.Plan?.Id == "premium";

        public async Task<Catalog> GetCatalogAsync(bool refresh = false)
        {
            if (Catalog == null || refresh) Catalog = await Api.GetCatalogAsync();
            return Catalog;
        }

        public async Task<SubscriptionResponse> GetSubscriptionAsync(bool refresh = false)
        {
            if (Subscription == null || refresh) Subscription = await Api.GetSubscriptionAsync();
            return Subscription;
        }

        /// <summary>Called after plan changes so gated UI refreshes.</summary>
        public void InvalidatePlanCaches()
        {
            Catalog = null;
            Subscription = null;
        }

        public void OpenTab(TabId tab)
        {
            AppScreen screen;
            switch (tab)
            {
                case TabId.History: screen = _history ?? (_history = new HistoryScreen(this)); break;
                case TabId.Progress: screen = _progress ?? (_progress = new ProgressScreen(this)); break;
                case TabId.Profile: screen = _profile ?? (_profile = new ProfileScreen(this)); break;
                default: screen = _home ?? (_home = new HomeScreen(this)); break;
            }
            if (Nav.Current == screen) return;
            Nav.ResetTo(screen, Transition.Fade);
        }

        public void GoHome() => OpenTab(TabId.Home);

        public void OnSignedIn()
        {
            InvalidatePlanCaches();
            _home = null;
            _history = null;
            _progress = null;
            _profile = null;
            GoHome();
        }

        public void OnSignedOut(string message = null)
        {
            if (Interview.Session != null) Interview.Abort();
            InvalidatePlanCaches();
            Settings.ClearUserScoped();
            _home = null;
            _history = null;
            _progress = null;
            _profile = null;
            Nav.ResetTo(new LoginScreen(this), Transition.Fade);
            if (!string.IsNullOrEmpty(message)) Overlay.Toast(message);
        }

        public void OpenSubscription() => Nav.Push(new SubscriptionScreen(this), Transition.Up);

        /// <summary>Standard presentation for API failures.</summary>
        public void HandleApiError(ApiException ex, string context = null)
        {
            switch (ex.Kind)
            {
                case ApiFailureKind.Network:
                    Overlay.Error(Network.IsOnline
                        ? "Can't reach the server. Please try again."
                        : "You're offline. Check your connection.");
                    break;
                case ApiFailureKind.Unauthorized:
                    if (ex.Code == "invalid_credentials") Overlay.Error(ex.Message);
                    else Auth.ForgetLocally();
                    break;
                case ApiFailureKind.PlanLimit:
                    Overlay.Dialog("Upgrade to continue", ex.Message, new[]
                    {
                        new DialogAction("See Premium", ButtonStyle.Gold, OpenSubscription),
                        new DialogAction("Not now", ButtonStyle.Secondary)
                    });
                    break;
                default:
                    Overlay.Error(ex.Message);
                    break;
            }
            Debug.LogWarning($"[Api] {context ?? "request"} failed: {ex.StatusCode} {ex.Code}");
        }
    }
}
