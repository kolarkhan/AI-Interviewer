using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AIInterviewer.EditorTools
{
    /// <summary>
    /// Command-line builds (CI friendly). Examples:
    ///   Unity -batchmode -quit -projectPath unity/AIInterviewer -executeMethod AIInterviewer.EditorTools.BuildScripts.BuildAndroid
    ///   Unity -batchmode -quit -projectPath unity/AIInterviewer -executeMethod AIInterviewer.EditorTools.BuildScripts.BuildIOS
    /// Signing secrets come from environment variables, never from the repository:
    ///   AII_KEYSTORE_PATH, AII_KEYSTORE_PASS, AII_KEY_ALIAS, AII_KEY_PASS
    /// </summary>
    public static class BuildScripts
    {
        [MenuItem("AI Interviewer/Build/Android App Bundle (release)")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = true;
            string keystore = Environment.GetEnvironmentVariable("AII_KEYSTORE_PATH");
            if (!string.IsNullOrEmpty(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("AII_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("AII_KEY_ALIAS");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("AII_KEY_PASS");
            }
            else
            {
                Debug.LogWarning("[Build] AII_KEYSTORE_PATH not set: the bundle is signed with the debug key (not uploadable).");
            }
            Build(BuildTarget.Android, "Builds/Android/AIInterviewer.aab", BuildOptions.None);
        }

        [MenuItem("AI Interviewer/Build/Android APK (development)")]
        public static void BuildAndroidDev()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/AIInterviewer-dev.apk", BuildOptions.Development);
        }

        [MenuItem("AI Interviewer/Build/iOS Xcode Project")]
        public static void BuildIOS()
        {
            Build(BuildTarget.iOS, "Builds/iOS", BuildOptions.None);
        }

        private static void Build(BuildTarget target, string path, BuildOptions options)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                ProjectSetup.CreateMainScene();
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                options = options
            });
            var summary = report.summary;
            Debug.Log($"[Build] {target}: {summary.result} in {summary.totalTime}, size {summary.totalSize / (1024 * 1024)} MB -> {path}");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
