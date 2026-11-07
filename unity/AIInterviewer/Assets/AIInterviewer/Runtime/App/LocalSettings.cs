using AIInterviewer.Avatar;
using AIInterviewer.Core.Api;
using Newtonsoft.Json;
using UnityEngine;

namespace AIInterviewer.App
{
    /// <summary>Non-sensitive device preferences (PlayerPrefs). Secrets never go here — see SecureStorage.</summary>
    public sealed class LocalSettings
    {
        private const string Prefix = "aii.";

        public bool OnboardingSeen
        {
            get => PlayerPrefs.GetInt(Prefix + "onboarding_seen", 0) == 1;
            set => SetInt("onboarding_seen", value ? 1 : 0);
        }

        /// <summary>Headphones disable the speakerphone echo gate (full-duplex barge-in).</summary>
        public bool UseHeadphones
        {
            get => PlayerPrefs.GetInt(Prefix + "use_headphones", 0) == 1;
            set => SetInt("use_headphones", value ? 1 : 0);
        }

        public LipSyncProviderKind LipSyncProvider
        {
            get => (LipSyncProviderKind)PlayerPrefs.GetInt(Prefix + "lipsync_provider", 0);
            set => SetInt("lipsync_provider", (int)value);
        }

        public InterviewConfig LastConfig
        {
            get
            {
                string json = PlayerPrefs.GetString(Prefix + "last_config", null);
                if (string.IsNullOrEmpty(json)) return new InterviewConfig();
                try
                {
                    return JsonConvert.DeserializeObject<InterviewConfig>(json) ?? new InterviewConfig();
                }
                catch (JsonException)
                {
                    return new InterviewConfig();
                }
            }
            set
            {
                PlayerPrefs.SetString(Prefix + "last_config", JsonConvert.SerializeObject(value));
                PlayerPrefs.Save();
            }
        }

        public void ClearUserScoped()
        {
            PlayerPrefs.DeleteKey(Prefix + "last_config");
            PlayerPrefs.Save();
        }

        private static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(Prefix + key, value);
            PlayerPrefs.Save();
        }
    }
}
