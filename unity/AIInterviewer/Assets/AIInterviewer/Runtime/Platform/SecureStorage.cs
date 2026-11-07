using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AIInterviewer.Platform
{
    /// <summary>Key/value storage for secrets (refresh tokens).</summary>
    public interface ISecureStorage
    {
        bool IsHardwareBacked { get; }
        void Set(string key, string value);
        string Get(string key);
        void Delete(string key);
    }

    public static class SecureStorage
    {
        public static ISecureStorage Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidKeystoreStorage();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosKeychainStorage();
#else
            return new DevelopmentPlayerPrefsStorage();
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// AES-256-GCM with a non-exportable key in the Android Keystore; ciphertext in private SharedPreferences.
    /// Implemented in Assets/Plugins/Android/SecureStore.java.
    /// </summary>
    internal sealed class AndroidKeystoreStorage : ISecureStorage
    {
        private const string JavaClass = "com.aiinterviewer.security.SecureStore";

        public bool IsHardwareBacked => true;

        public void Set(string key, string value) => Call<bool>("put", key, value);

        public string Get(string key) => Call<string>("get", key);

        public void Delete(string key) => Call<bool>("remove", key);

        private static T Call<T>(string method, params object[] args)
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var store = new AndroidJavaClass(JavaClass))
            {
                var all = new object[args.Length + 1];
                all[0] = activity;
                Array.Copy(args, 0, all, 1, args.Length);
                return store.CallStatic<T>(method, all);
            }
        }
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>iOS Keychain (kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly). See Assets/Plugins/iOS/SecureStore.mm.</summary>
    internal sealed class IosKeychainStorage : ISecureStorage
    {
        // C++ `bool` is one byte: marshal explicitly (the default for bool is a 4-byte Win32 BOOL).
        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool AIISecureStoreSet(string key, string value);

        [DllImport("__Internal")] private static extern IntPtr AIISecureStoreGet(string key);

        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool AIISecureStoreDelete(string key);

        [DllImport("__Internal")] private static extern void AIISecureStoreFree(IntPtr ptr);

        public bool IsHardwareBacked => true;

        public void Set(string key, string value)
        {
            if (!AIISecureStoreSet(key, value)) Debug.LogError("[SecureStorage] Keychain write failed");
        }

        public string Get(string key)
        {
            IntPtr ptr = AIISecureStoreGet(key);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                AIISecureStoreFree(ptr);
            }
        }

        public void Delete(string key) => AIISecureStoreDelete(key);
    }
#endif

    /// <summary>
    /// Editor/desktop fallback. NOT secure — PlayerPrefs is plain text. Used only so the app can be
    /// developed and demoed in the Unity Editor. Mobile builds always use the platform keystore.
    /// </summary>
    internal sealed class DevelopmentPlayerPrefsStorage : ISecureStorage
    {
        private const string Prefix = "aii.dev.secure.";
        private static bool _warned;

        public bool IsHardwareBacked => false;

        public void Set(string key, string value)
        {
            Warn();
            PlayerPrefs.SetString(Prefix + key, value);
            PlayerPrefs.Save();
        }

        public string Get(string key)
        {
            string v = PlayerPrefs.GetString(Prefix + key, null);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }

        private static void Warn()
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[SecureStorage] Using PlayerPrefs (development only, not encrypted).");
        }
    }
}
