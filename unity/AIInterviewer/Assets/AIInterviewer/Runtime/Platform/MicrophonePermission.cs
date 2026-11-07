using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace AIInterviewer.Platform
{
    public enum PermissionResult
    {
        Granted,
        Denied
    }

    /// <summary>Cross-platform microphone permission flow (Android runtime permission / iOS AVAudioSession).</summary>
    public static class MicrophonePermission
    {
        public static bool IsGranted
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#elif UNITY_IOS && !UNITY_EDITOR
                return Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
                return Microphone.devices.Length > 0;
#endif
            }
        }

        /// <summary>Requests the permission if needed. Must be called from the main thread.</summary>
        public static Task<PermissionResult> RequestAsync(MonoBehaviour host)
        {
            var tcs = new TaskCompletionSource<PermissionResult>();
            if (IsGranted)
            {
                tcs.SetResult(PermissionResult.Granted);
                return tcs.Task;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => tcs.TrySetResult(PermissionResult.Granted);
            callbacks.PermissionDenied += _ => tcs.TrySetResult(PermissionResult.Denied);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#elif UNITY_IOS && !UNITY_EDITOR
            host.StartCoroutine(RequestIos(tcs));
#else
            tcs.SetResult(Microphone.devices.Length > 0 ? PermissionResult.Granted : PermissionResult.Denied);
#endif
            return tcs.Task;
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static IEnumerator RequestIos(TaskCompletionSource<PermissionResult> tcs)
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            tcs.TrySetResult(Application.HasUserAuthorization(UserAuthorization.Microphone)
                ? PermissionResult.Granted
                : PermissionResult.Denied);
        }
#endif

        /// <summary>Opens the OS settings page for this app so the user can enable the microphone.</summary>
        public static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string package = activity.Call<string>("getPackageName");
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + package))
                    using (var intent = new AndroidJavaObject("android.content.Intent",
                               "android.settings.APPLICATION_DETAILS_SETTINGS", uri))
                    {
                        intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                        activity.Call("startActivity", intent);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permission] Could not open settings: " + ex.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#else
            Debug.Log("[Permission] Open the OS privacy settings to allow microphone access.");
#endif
        }
    }
}
