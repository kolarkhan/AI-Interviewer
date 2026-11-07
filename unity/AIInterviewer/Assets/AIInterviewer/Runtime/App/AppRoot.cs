using System;
using AIInterviewer.Audio;
using AIInterviewer.Avatar;
using AIInterviewer.Net;
using AIInterviewer.Platform;
using AIInterviewer.Realtime;
using AIInterviewer.Scene;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Screens;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIInterviewer.App
{
    /// <summary>Composition root. Created automatically when the "Main" scene loads (see <see cref="Boot"/>).</summary>
    public sealed class AppRoot : MonoBehaviour
    {
        private AppServices _services;
        private bool _legacyInputAvailable = true;

        public static AppRoot Instance { get; private set; }
        public AppServices Services => _services;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            // Only auto-start in the app scene so other scenes (tests, sandboxes) are unaffected.
            if (SceneManager.GetActiveScene().name != "Main" && FindAnyObjectByType<AppBootstrapMarker>() == null) return;
            var go = new GameObject("AIInterviewerApp");
            go.AddComponent<AppRoot>();
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            MainThread.Initialize(gameObject);
            Input.multiTouchEnabled = false;

            var config = AppConfig.Load();
            var apiClient = new ApiClient(config.ResolvedApiBaseUrl);
            var storage = SecureStorage.Create();
            if (!storage.IsHardwareBacked && config.IsProduction && !Application.isEditor)
            {
                Debug.LogError("[Security] Production build without hardware-backed token storage.");
            }
            var auth = new AuthSession(apiClient, storage);
            var network = gameObject.AddComponent<NetworkMonitor>();
            var mic = gameObject.AddComponent<MicrophoneCapture>();
            var settings = new LocalSettings();

            var room = InterviewRoom.Create(transform);
            room.SetAvatar(AvatarFactory.DefaultId, LipSyncProviders.Create(settings.LipSyncProvider));
            room.SetActive(false);

            var interview = gameObject.AddComponent<LiveInterviewController>();
            interview.Initialize(config, auth, new BackendApi(apiClient), room, mic, network);

            var ui = gameObject.AddComponent<UiRoot>();
            var backend = new BackendApi(apiClient);
            var voicePreview = gameObject.AddComponent<AIInterviewer.Audio.VoicePreviewPlayer>();
            voicePreview.Fetch = backend.GetVoicePreviewAsync;
            _services = new AppServices
            {
                VoicePreview = voicePreview,
                Config = config,
                Auth = auth,
                Api = backend,
                Room = room,
                Interview = interview,
                Network = network,
                Settings = settings,
                Host = this,
            };
            ui.Initialize(tab => _services.OpenTab(tab));
            _services.Ui = ui;

            ui.Navigator.CurrentChanged += screen => room.SetActive(screen != null && screen.ShowsInterviewRoom);
            network.OnlineChanged += online => ui.SetOffline(!online);
            auth.SignedOut += () => MainThread.Post(() =>
                _services.OnSignedOut(auth.HasStoredSession ? null : "You've been signed out."));

            Debug.Log($"[App] Started ({config.Environment}) API={config.ResolvedApiBaseUrl}");
        }

        private void Start()
        {
            _services.Nav.ResetTo(new SplashScreen(_services), Transition.None);
            _services.Ui.SetOffline(!_services.Network.IsOnline);
        }

        private void Update()
        {
            if (!_legacyInputAvailable) return;
            bool back;
            try
            {
                back = Input.GetKeyDown(KeyCode.Escape); // Android back button
            }
            catch (InvalidOperationException)
            {
                // Project uses the Input System package only; back is then handled by the platform default.
                _legacyInputAvailable = false;
                return;
            }
            if (!back) return;
            if (_services.Overlay.HandleBack()) return;
            if (_services.Nav.HandleBack()) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call<bool>("moveTaskToBack", true);
            }
#endif
        }
    }
}
