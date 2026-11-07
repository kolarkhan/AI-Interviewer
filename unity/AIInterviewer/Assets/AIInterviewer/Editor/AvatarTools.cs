using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIInterviewer.Avatar;
using AIInterviewer.Core.LipSync;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AIInterviewer.EditorTools
{
    /// <summary>Avatar integration helpers (see docs/AVATAR_ASSETS.md).</summary>
    public static class AvatarTools
    {
        private const string AnimatorPath = "Assets/AIInterviewer/Avatars/InterviewerAnimator.controller";

        /// <summary>
        /// Creates an Animator Controller with the states the app drives: Idle, Listening, Thinking, Speaking
        /// (parameter "Mood": 0..3) and a float "Talk" for speaking intensity. Assign your animation clips
        /// (e.g. from a licensed mocap pack) to the states afterwards.
        /// </summary>
        [MenuItem("AI Interviewer/Avatar/Create Interviewer Animator Controller")]
        public static void CreateAnimatorController()
        {
            ProjectSetup.EnsureFolder("Assets/AIInterviewer/Avatars");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath) != null)
            {
                Debug.Log("[Avatar] Animator controller already exists: " + AnimatorPath);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
                return;
            }
            var controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);
            controller.AddParameter("Mood", AnimatorControllerParameterType.Int);
            controller.AddParameter("Talk", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            string[] names = { "Idle", "Listening", "Thinking", "Speaking" };
            for (int i = 0; i < names.Length; i++)
            {
                var state = sm.AddState(names[i], new Vector3(300, 60 + i * 70, 0));
                if (i == 0) sm.defaultState = state;
                var t = sm.AddAnyStateTransition(state);
                t.AddCondition(AnimatorConditionMode.Equals, i, "Mood");
                t.hasExitTime = false;
                t.duration = 0.3f;
                t.canTransitionToSelf = false;
            }
            AssetDatabase.SaveAssets();
            Selection.activeObject = controller;
            Debug.Log("[Avatar] Created " + AnimatorPath + ". Assign idle/listening/thinking/talking clips to its states.");
        }

        /// <summary>Checks a selected avatar prefab against the asset contract and logs a report.</summary>
        [MenuItem("AI Interviewer/Avatar/Validate Selected Avatar Prefab")]
        public static void ValidateSelected()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Validate avatar", "Select an avatar prefab or scene object first.", "OK");
                return;
            }
            var report = new StringBuilder();
            int errors = 0, warnings = 0;
            void Err(string m) { errors++; report.AppendLine("ERROR: " + m); }
            void Warn(string m) { warnings++; report.AppendLine("WARN:  " + m); }
            void Ok(string m) => report.AppendLine("OK:    " + m);

            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null) Err("No Animator with an Avatar. Import the model with Rig > Animation Type = Humanoid.");
            else if (!animator.avatar.isHuman) Err("Avatar is not Humanoid.");
            else
            {
                Ok("Humanoid avatar configured.");
                foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest })
                {
                    if (animator.GetBoneTransform(bone) == null) Err($"Missing humanoid bone mapping: {bone}");
                }
                if (animator.GetBoneTransform(HumanBodyBones.LeftEye) == null) Warn("No eye bones mapped: gaze will use head motion only.");
                if (animator.GetBoneTransform(HumanBodyBones.Jaw) == null) Warn("No jaw bone mapped (fine if a jawOpen blendshape exists).");
            }

            var rig = go.GetComponent<BlendShapeAvatarRig>();
            if (rig == null) Err("Root has no BlendShapeAvatarRig component.");

            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var shapes = new HashSet<string>();
            int triangles = 0;
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                triangles += smr.sharedMesh.triangles.Length / 3;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) shapes.Add(smr.sharedMesh.GetBlendShapeName(i));
            }
            Ok($"{smrs.Length} skinned meshes, {triangles:N0} triangles, {shapes.Count} blendshapes.");
            if (triangles > 60000) Warn("Over 60k triangles: consider a LOD for mid-range Android (target 20-50k).");

            if (rig != null)
            {
                var mapped = rig.visemeMap.Where(v => v.viseme != Viseme.Sil).ToList();
                var missing = mapped.Where(v => !shapes.Contains(v.blendShape)).Select(v => v.blendShape).Distinct().ToList();
                if (mapped.Count == 0 && !shapes.Contains(rig.jawOpenBlendShape)) Err("Viseme map is empty and no jawOpen blendshape: no lip-sync possible.");
                else if (missing.Count > 0) Warn("Mapped blendshapes not found: " + string.Join(", ", missing));
                else Ok("All mapped viseme blendshapes found.");
                if (!rig.blinkBlendShapes.Any(shapes.Contains)) Warn("No blink blendshape found: the avatar will not blink.");
                if (rig.faceRenderers.Length == 0) Err("BlendShapeAvatarRig.faceRenderers is empty.");
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var tex = m.mainTexture;
                    if (tex != null && (tex.width > 2048 || tex.height > 2048)) Warn($"Texture {tex.name} is {tex.width}x{tex.height}; use ≤ 2048 on mobile.");
                }
            }

            string summary = $"{errors} error(s), {warnings} warning(s)";
            Debug.Log($"[Avatar] Validation of '{go.name}': {summary}\n{report}");
            EditorUtility.DisplayDialog("Avatar validation", summary + "\n\nDetails are in the Console.", "OK");
        }
    }
}
