using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if AII_URP
using UnityEngine.Rendering.Universal;
#endif

namespace AIInterviewer.EditorTools
{
    /// <summary>
    /// One-click project configuration: "AI Interviewer > Configure Project".
    /// Idempotent — safe to run again after upgrading Unity or pulling changes.
    /// </summary>
    public static class ProjectSetup
    {
        private const string Root = "Assets/AIInterviewer";
        private const string SettingsDir = Root + "/Settings";
        private const string MaterialsDir = Root + "/Resources/Materials";
        private const string ScenePath = Root + "/Scenes/Main.unity";

        public const string DefaultBundleId = "com.yourcompany.aiinterviewer";

        [MenuItem("AI Interviewer/Configure Project", priority = 0)]
        public static void ConfigureAll()
        {
            ConfigureRenderPipeline();
            CreateBaseMaterials();
            ConfigurePlayer();
            ConfigureAudio();
            CreateMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[AI Interviewer] Project configured. Open Assets/AIInterviewer/Scenes/Main.unity and press Play. " +
                      "Remember to set your own bundle identifier in Player Settings.");
        }

        [MenuItem("AI Interviewer/Setup/Render Pipeline (URP)")]
        public static void ConfigureRenderPipeline()
        {
#if !AII_URP
            Debug.Log("[Setup] URP package not installed: using the Built-in Render Pipeline (Standard shader). " +
                      "Install com.unity.render-pipelines.universal and run this again to switch to URP.");
#else
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset)
            {
                Debug.Log("[Setup] URP already active.");
                return;
            }
            EnsureFolder(SettingsDir);
            try
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsDir + "/AII_MobileRenderer.asset");
                var asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.name = "AII_MobileURP";
                // Mobile-friendly defaults: modest MSAA, no HDR, soft shadows off.
                asset.msaaSampleCount = 2;
                asset.supportsHDR = false;
                asset.renderScale = 1f;
                AssetDatabase.CreateAsset(asset, SettingsDir + "/AII_MobileURP.asset");
                GraphicsSettings.defaultRenderPipeline = asset;
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = asset;
                }
                Debug.Log("[Setup] Created and assigned URP asset at " + SettingsDir);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Setup] Could not create a URP asset automatically (" + ex.Message + "). Create one via " +
                               "Assets > Create > Rendering > URP Asset (with Universal Renderer) and assign it in " +
                               "Project Settings > Graphics and Quality.");
            }
#endif
        }

        [MenuItem("AI Interviewer/Setup/Base Materials")]
        public static void CreateBaseMaterials()
        {
            EnsureFolder(MaterialsDir);
#if AII_URP
            bool urp = GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;
#else
            bool urp = false;
#endif
            CreateMaterial("AII_Lit", urp ? "Universal Render Pipeline/Lit" : "Standard");
            CreateMaterial("AII_Unlit", urp ? "Universal Render Pipeline/Unlit" : "Unlit/Texture");
        }

        private static void CreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[Setup] Shader '{shaderName}' not found.");
                return;
            }
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                EditorUtility.SetDirty(existing);
                return;
            }
            AssetDatabase.CreateAsset(new Material(shader) { name = name }, path);
        }

        [MenuItem("AI Interviewer/Setup/Player Settings")]
        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany"
                ? "YourCompany" : PlayerSettings.companyName;
            PlayerSettings.productName = "Avintera";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly; // http:// only for local dev builds
            // Native launch splash: brand background, no engine logo (the animated Avintera splash follows).
            PlayerSettings.SplashScreen.backgroundColor = new Color32(0x0B, 0x10, 0x20, 0xFF);
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;

            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                string id = PlayerSettings.GetApplicationIdentifier(target);
                if (string.IsNullOrEmpty(id) || id.StartsWith("com.DefaultCompany") || id.StartsWith("com.Company"))
                {
                    PlayerSettings.SetApplicationIdentifier(target, DefaultBundleId);
                }
            }

            // Android
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.forceInternetPermission = true;

            // iOS
            PlayerSettings.iOS.microphoneUsageDescription =
                "Avintera uses the microphone so you can answer the interviewer's questions by voice. Audio is " +
                "streamed only during an interview and is never recorded.";
            PlayerSettings.iOS.targetOSVersionString = "15.0";

            // iOS audio routing: play through the loudspeaker (not the earpiece) while the microphone is active.
            SetPlayerSettingsBool("forceIOSSpeakersWhenRecording", true);
            SetPlayerSettingsBool("Prepare IOS For Recording", true);
            SetPlayerSettingsBool("prepareIOSForRecording", true);
            Debug.Log("[Setup] Player settings applied (portrait, IL2CPP ARM64, Android API 26+, iOS 15+, mic description).");
        }

        [MenuItem("AI Interviewer/Setup/Audio (low latency)")]
        public static void ConfigureAudio()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var dsp = so.FindProperty("m_DSPBufferSize");
            if (dsp != null)
            {
                dsp.intValue = 256; // "Best latency"
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("[Setup] Set Project Settings > Audio > DSP Buffer Size to 'Best latency' manually.");
            }
        }

        [MenuItem("AI Interviewer/Setup/Main Scene")]
        public static void CreateMainScene()
        {
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[Setup] Main scene ready and set as the only build scene. The app boots itself at runtime.");
        }

        private static void SetPlayerSettingsBool(string property, bool value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var p = so.FindProperty(property);
            if (p == null) return;
            p.boolValue = value;
            so.ApplyModifiedProperties();
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
