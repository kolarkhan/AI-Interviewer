using System;
using Newtonsoft.Json;
using UnityEngine;

namespace AIInterviewer.App
{
    /// <summary>
    /// Non-secret client configuration loaded from Resources/AppConfig.json.
    /// An optional Resources/AppConfig.Local.json (git-ignored) overrides it for local development.
    /// Never put API keys or other secrets here: everything in the app bundle is public.
    /// </summary>
    [Serializable]
    public sealed class AppConfig
    {
        [JsonProperty("apiBaseUrl")] public string ApiBaseUrl = "http://127.0.0.1:8000";
        [JsonProperty("androidEmulatorApiBaseUrl")] public string AndroidEmulatorApiBaseUrl;
        [JsonProperty("environment")] public string Environment = "development";
        [JsonProperty("privacyPolicyUrl")] public string PrivacyPolicyUrl;
        [JsonProperty("termsUrl")] public string TermsUrl;
        [JsonProperty("supportEmail")] public string SupportEmail;

        public bool IsProduction => Environment == "production";

        /// <summary>Base URL with the emulator alias applied on Android emulators.</summary>
        public string ResolvedApiBaseUrl
        {
            get
            {
                string url = ApiBaseUrl;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!string.IsNullOrEmpty(AndroidEmulatorApiBaseUrl) && IsAndroidEmulator() &&
                    (url.Contains("127.0.0.1") || url.Contains("localhost")))
                {
                    url = AndroidEmulatorApiBaseUrl;
                }
#endif
                return url.TrimEnd('/');
            }
        }

        public string WebSocketBaseUrl
        {
            get
            {
                string b = ResolvedApiBaseUrl;
                if (b.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "wss://" + b.Substring(8);
                if (b.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return "ws://" + b.Substring(7);
                return b;
            }
        }

        public static AppConfig Load()
        {
            var config = new AppConfig();
            var main = Resources.Load<TextAsset>("AppConfig");
            if (main != null) JsonConvert.PopulateObject(main.text, config);
            var local = Resources.Load<TextAsset>("AppConfig.Local");
            if (local != null) JsonConvert.PopulateObject(local.text, config);
            if (config.IsProduction && !config.ApiBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[AppConfig] Production builds must use an https:// API URL.");
            }
            return config;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool IsAndroidEmulator()
        {
            try
            {
                using (var build = new AndroidJavaClass("android.os.Build"))
                {
                    string fingerprint = build.GetStatic<string>("FINGERPRINT") ?? string.Empty;
                    string model = build.GetStatic<string>("MODEL") ?? string.Empty;
                    return fingerprint.Contains("generic") || fingerprint.Contains("emulator") ||
                           model.Contains("Emulator") || model.Contains("Android SDK built for");
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif
    }
}
