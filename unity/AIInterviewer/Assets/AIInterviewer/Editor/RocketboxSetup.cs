using System.Linq;
using AIInterviewer.Avatar;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AIInterviewer.EditorTools
{
    /// <summary>
    /// Turns the downloaded Microsoft Rocketbox characters (Assets/ThirdParty/Rocketbox, MIT licence) into app avatars:
    /// importer settings → animator controller (Idle / Listening / Thinking / Speaking) → prefab with
    /// <see cref="BlendShapeAvatarRig"/> → <see cref="AvatarDefinition"/> in Resources/Avatars/{id}.
    /// Runs automatically once after the files are added; re-run any time from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class RocketboxSetup
    {
        private const string RocketboxRoot = "Assets/ThirdParty/Rocketbox";
        private const string OutputDir = "Assets/AIInterviewer/Avatars/Rocketbox";
        private const string DefinitionsDir = "Assets/AIInterviewer/Resources/Avatars";

        private sealed class Spec
        {
            public string Id, DisplayName, Folder, Gender;
        }

        private static readonly Spec[] Avatars =
        {
            new Spec { Id = "alex", DisplayName = "Alex", Folder = "Business_Male_01", Gender = "m" },
            new Spec { Id = "maya", DisplayName = "Maya", Folder = "Business_Female_01", Gender = "f" },
        };

        static RocketboxSetup()
        {
            EditorApplication.delayCall += AutoRunOnce;
        }

        private static void AutoRunOnce()
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!AssetDatabase.IsValidFolder(RocketboxRoot)) return;
            if (AssetDatabase.LoadAssetAtPath<AvatarDefinition>($"{DefinitionsDir}/alex.asset") != null) return;
            Debug.Log("[Rocketbox] First-time setup of the realistic interviewer avatars…");
            Run();
        }

        [MenuItem("AI Interviewer/Avatar/Set Up Rocketbox Interviewers (Alex, Maya)")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(RocketboxRoot))
            {
                EditorUtility.DisplayDialog("Rocketbox avatars", $"Folder {RocketboxRoot} not found.", "OK");
                return;
            }
            try
            {
                EditorUtility.DisplayProgressBar("Rocketbox avatars", "Re-importing models and textures…", 0.1f);
                Reimport();
                ProjectSetup.EnsureFolder(OutputDir);
                ProjectSetup.EnsureFolder(DefinitionsDir);
                int done = 0;
                foreach (var spec in Avatars)
                {
                    EditorUtility.DisplayProgressBar("Rocketbox avatars", $"Creating {spec.DisplayName}…", 0.4f + 0.25f * done++);
                    CreateAvatar(spec);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[Rocketbox] Avatars ready: alex, maya. Press Play — the Lobby shows the selected interviewer.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Reimport()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D t:Model", new[] { RocketboxRoot }))
                {
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            // Textures first, then models again so the imported materials pick up the final textures.
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { RocketboxRoot }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
        }

        private static AnimationClip LoadClip(string name)
        {
            string path = $"{RocketboxRoot}/Animations/{name}.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogWarning($"[Rocketbox] Animation not found: {path}");
            return clip;
        }

        private static AnimatorController CreateController(Spec spec)
        {
            string path = $"{OutputDir}/{spec.Id}_Animator.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Mood", AnimatorControllerParameterType.Int);
            controller.AddParameter("Talk", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            string g = spec.Gender;
            var states = new (string name, string clip)[]
            {
                ("Idle", $"{g}_idle_neutral_01"),
                ("Listening", $"{g}_gestic_listen_neutral_01"),
                ("Thinking", $"{g}_gestic_listen_neutral_02"),
                ("Speaking", $"{g}_gestic_talk_neutral_01"),
            };
            for (int i = 0; i < states.Length; i++)
            {
                var state = sm.AddState(states[i].name, new Vector3(320, 40 + i * 70, 0));
                state.motion = LoadClip(states[i].clip);
                if (i == 0) sm.defaultState = state;
                var t = sm.AddAnyStateTransition(state);
                t.AddCondition(AnimatorConditionMode.Equals, i, "Mood");
                t.hasExitTime = false;
                t.duration = 0.45f;
                t.canTransitionToSelf = false;
            }
            return controller;
        }

        private static void CreateAvatar(Spec spec)
        {
            string fbxPath = $"{RocketboxRoot}/Avatars/{spec.Folder}/Export/{spec.Folder}_facial.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null)
            {
                Debug.LogError($"[Rocketbox] Model not found: {fbxPath}");
                return;
            }
            var controller = CreateController(spec);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            instance.name = spec.DisplayName;

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                Debug.LogError($"[Rocketbox] {spec.Folder}: Humanoid avatar could not be created automatically. Select " +
                               $"{fbxPath} → Rig → Configure… and fix the bone mapping, then run this menu again.");
            }

            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true; // bounds stay correct while the face deforms
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var rig = instance.AddComponent<BlendShapeAvatarRig>();
            rig.animator = animator;
            rig.faceRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0).ToArray();
            rig.ApplyRocketboxPreset();
            if (rig.faceRenderers.Length == 0)
            {
                Debug.LogError($"[Rocketbox] {spec.Folder}: no blendshapes imported (check Import BlendShapes).");
            }

            string prefabPath = $"{OutputDir}/{spec.Id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            string defPath = $"{DefinitionsDir}/{spec.Id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<AvatarDefinition>(defPath);
            bool created = def == null;
            if (created) def = ScriptableObject.CreateInstance<AvatarDefinition>();
            def.id = spec.Id;
            def.displayName = spec.DisplayName;
            def.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            def.scale = 1f;
            def.positionOffset = new Vector3(0f, 0f, -0.15f); // stands just behind the desk
            def.licenseNotice = "Character from the Microsoft Rocketbox Avatar Library, MIT License, (c) 2020 Microsoft.";
            if (created) AssetDatabase.CreateAsset(def, defPath);
            else EditorUtility.SetDirty(def);
            Debug.Log($"[Rocketbox] {spec.DisplayName}: {rig.faceRenderers.Length} face mesh(es), prefab {prefabPath}");
        }
    }
}
