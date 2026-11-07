using System;
using System.Collections.Generic;
using AIInterviewer.Core.LipSync;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    [Serializable]
    public sealed class VisemeBlendShape
    {
        public Viseme viseme;
        [Tooltip("Blendshape name on the face mesh(es).")] public string blendShape;
        [Range(0f, 1.5f)] public float weight = 1f;
    }

    /// <summary>
    /// Rig for a production character: humanoid skeleton + facial blendshapes.
    /// Supports two common conventions out of the box (use the context menu to fill presets):
    /// - Oculus/Meta visemes (`viseme_sil`, `viseme_PP`, ... `viseme_U`) e.g. Ready Player Me-style exports,
    /// - ARKit-style shapes (`jawOpen`, `mouthFunnel`, `mouthPucker`, `mouthSmileLeft`, ...) approximated per viseme.
    /// See docs/AVATAR_ASSETS.md for the full asset contract.
    /// </summary>
    public sealed class BlendShapeAvatarRig : MonoBehaviour, IAvatarRig
    {
        [Header("Meshes")]
        [Tooltip("Face/teeth/tongue meshes that share the facial blendshapes.")]
        public SkinnedMeshRenderer[] faceRenderers = Array.Empty<SkinnedMeshRenderer>();

        [Header("Lip-sync")]
        public List<VisemeBlendShape> visemeMap = new List<VisemeBlendShape>();
        [Tooltip("Optional jaw-open blendshape driven by overall mouth opening (e.g. 'jawOpen').")]
        public string jawOpenBlendShape = "jawOpen";
        [Range(0f, 1.5f)] public float jawOpenScale = 0.7f;
        [Tooltip("Optional jaw bone rotated by mouth opening when no jaw blendshape exists.")]
        public Transform jawBone;
        public float jawBoneMaxDegrees = 12f;

        [Header("Eyes & expression")]
        public string[] blinkBlendShapes = { "eyeBlinkLeft", "eyeBlinkRight", "eyesClosed" };
        public string[] smileBlendShapes = { "mouthSmileLeft", "mouthSmileRight", "mouthSmile" };
        public string[] browRaiseBlendShapes = { "browInnerUp", "browOuterUpLeft", "browOuterUpRight" };
        public float maxGazeDegrees = 15f;

        [Header("Animator (optional)")]
        public Animator animator;
        public string moodParameter = "Mood";
        public string talkParameter = "Talk";

        private readonly List<(SkinnedMeshRenderer smr, int index, Viseme viseme, float weight)> _visemeTargets =
            new List<(SkinnedMeshRenderer, int, Viseme, float)>();
        private readonly List<(SkinnedMeshRenderer, int)> _jaw = new List<(SkinnedMeshRenderer, int)>();
        private readonly List<(SkinnedMeshRenderer, int)> _blink = new List<(SkinnedMeshRenderer, int)>();
        private readonly List<(SkinnedMeshRenderer, int)> _smile = new List<(SkinnedMeshRenderer, int)>();
        private readonly List<(SkinnedMeshRenderer, int)> _brow = new List<(SkinnedMeshRenderer, int)>();
        private Transform _head, _leftEye, _rightEye, _chest;
        private Quaternion _leftEyeRest, _rightEyeRest, _jawRest, _headRest;
        private Vector3 _chestRestScale;
        private Vector2 _gaze;
        private int _moodHash, _talkHash;
        private bool _hasMoodParam, _hasTalkParam;

        public Transform Root => transform;
        public Transform Head
        {
            get
            {
                if (_head == null && animator != null && animator.isHuman) _head = animator.GetBoneTransform(HumanBodyBones.Head);
                return _head != null ? _head : transform;
            }
        }
        public bool IsPlaceholder => false;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                _rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
                _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (jawBone == null) jawBone = animator.GetBoneTransform(HumanBodyBones.Jaw);
            }
            if (_head) _headRest = _head.localRotation;
            if (_leftEye) _leftEyeRest = _leftEye.localRotation;
            if (_rightEye) _rightEyeRest = _rightEye.localRotation;
            if (jawBone) _jawRest = jawBone.localRotation;
            if (_chest) _chestRestScale = _chest.localScale;
            ResolveBlendShapes();

            if (animator != null)
            {
                _moodHash = Animator.StringToHash(moodParameter);
                _talkHash = Animator.StringToHash(talkParameter);
                foreach (var p in animator.parameters)
                {
                    if (p.nameHash == _moodHash) _hasMoodParam = true;
                    if (p.nameHash == _talkHash) _hasTalkParam = true;
                }
            }
        }

        public void ResolveBlendShapes()
        {
            _visemeTargets.Clear();
            _jaw.Clear();
            _blink.Clear();
            _smile.Clear();
            _brow.Clear();
            int missing = 0;
            foreach (var smr in faceRenderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh;
                foreach (var entry in visemeMap)
                {
                    int idx = FindBlendShape(mesh, entry.blendShape);
                    if (idx >= 0) _visemeTargets.Add((smr, idx, entry.viseme, entry.weight));
                    else missing++;
                }
                Collect(mesh, smr, new[] { jawOpenBlendShape }, _jaw);
                Collect(mesh, smr, blinkBlendShapes, _blink);
                Collect(mesh, smr, smileBlendShapes, _smile);
                Collect(mesh, smr, browRaiseBlendShapes, _brow);
            }
            if (_visemeTargets.Count == 0 && _jaw.Count == 0 && jawBone == null)
            {
                Debug.LogError($"[Avatar] '{name}' has no usable mouth blendshapes or jaw bone. Lip-sync will not be " +
                               "visible. Check the viseme map (docs/AVATAR_ASSETS.md).");
            }
            else if (missing > 0)
            {
                Debug.Log($"[Avatar] {missing} mapped viseme blendshape(s) not found on some meshes (this is normal " +
                          "when teeth/tongue meshes only have a subset).");
            }
        }

        private static void Collect(Mesh mesh, SkinnedMeshRenderer smr, string[] names, List<(SkinnedMeshRenderer, int)> into)
        {
            if (names == null) return;
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                int idx = FindBlendShape(mesh, n);
                if (idx >= 0) into.Add((smr, idx));
            }
        }

        /// <summary>
        /// Finds a blendshape by exact name, or by name after the deformer prefix Unity adds for some FBX exports
        /// (e.g. "blendShape1.AA_VI_10_aa" matches "AA_VI_10_aa"). Case-insensitive.
        /// </summary>
        public static int FindBlendShape(Mesh mesh, string name)
        {
            if (mesh == null || string.IsNullOrEmpty(name)) return -1;
            int exact = mesh.GetBlendShapeIndex(name);
            if (exact >= 0) return exact;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                int dot = n.LastIndexOf('.');
                string bare = dot >= 0 ? n.Substring(dot + 1) : n;
                if (string.Equals(bare, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Turns the character so its humanoid body faces <paramref name="worldForward"/> (models exported from other
        /// tools often face another axis). Requires a humanoid Animator.
        /// </summary>
        public void FaceDirection(Vector3 worldForward)
        {
            if (animator == null || !animator.isHuman || animator.avatar == null) return;
            animator.Update(0f);
            Vector3 body = animator.bodyRotation * Vector3.forward;
            body.y = 0f;
            worldForward.y = 0f;
            if (body.sqrMagnitude < 1e-4f || worldForward.sqrMagnitude < 1e-4f) return;
            float angle = Vector3.SignedAngle(body, worldForward, Vector3.up);
            if (Mathf.Abs(angle) > 1f) transform.Rotate(0f, angle, 0f, Space.World);
        }

        public void ApplyVisemes(VisemeFrame frame)
        {
            // Reset mapped shapes first because several visemes may map to the same blendshape.
            foreach (var t in _visemeTargets) t.smr.SetBlendShapeWeight(t.index, 0f);
            foreach (var t in _visemeTargets)
            {
                float w = frame.Weights[(int)t.viseme] * t.weight;
                if (w <= 0f) continue;
                float current = t.smr.GetBlendShapeWeight(t.index);
                t.smr.SetBlendShapeWeight(t.index, Mathf.Min(100f, current + w * 100f));
            }
            foreach (var (smr, idx) in _jaw) smr.SetBlendShapeWeight(idx, frame.JawOpen * jawOpenScale * 100f);
            if (jawBone != null && _jaw.Count == 0)
            {
                jawBone.localRotation = _jawRest * Quaternion.Euler(frame.JawOpen * jawBoneMaxDegrees, 0f, 0f);
            }
        }

        public void SetBlink(float closed)
        {
            foreach (var (smr, idx) in _blink) smr.SetBlendShapeWeight(idx, closed * 100f);
        }

        public void SetGaze(Vector2 yawPitch)
        {
            _gaze = Vector2.ClampMagnitude(yawPitch, maxGazeDegrees);
        }

        public void SetMood(AvatarMood mood, float speakingIntensity)
        {
            if (animator == null) return;
            if (_hasMoodParam) animator.SetInteger(_moodHash, (int)mood);
            if (_hasTalkParam) animator.SetFloat(_talkHash, speakingIntensity, 0.1f, Time.deltaTime);
        }

        public void SetExpression(float smile, float browRaise)
        {
            foreach (var (smr, idx) in _smile) smr.SetBlendShapeWeight(idx, smile * 100f);
            foreach (var (smr, idx) in _brow) smr.SetBlendShapeWeight(idx, browRaise * 100f);
        }

        public void ApplyProceduralPose(Quaternion headOffset, float breath)
        {
            // Applied in LateUpdate, i.e. after the Animator has written the pose for this frame. Without an
            // active Animator nothing resets the bone each frame, so start from the rest pose to avoid drift.
            if (_head != null)
            {
                bool animated = animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
                _head.localRotation = (animated ? _head.localRotation : _headRest) * headOffset;
            }
            var eyeRot = Quaternion.Euler(-_gaze.y, _gaze.x, 0f);
            if (_leftEye) _leftEye.localRotation = _leftEyeRest * eyeRot;
            if (_rightEye) _rightEye.localRotation = _rightEyeRest * eyeRot;
            if (_chest) _chest.localScale = _chestRestScale * (1f + breath * 0.012f);
        }

        [ContextMenu("Fill viseme map: Oculus/Meta visemes (viseme_*)")]
        private void FillOculusPreset()
        {
            visemeMap = new List<VisemeBlendShape>();
            string[] names = { "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH", "viseme_DD", "viseme_kk", "viseme_CH",
                               "viseme_SS", "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E", "viseme_I", "viseme_O", "viseme_U" };
            for (int i = 0; i < names.Length; i++)
            {
                visemeMap.Add(new VisemeBlendShape { viseme = (Viseme)i, blendShape = names[i], weight = i == 0 ? 0f : 1f });
            }
            jawOpenScale = 0.35f;
        }

        /// <summary>Microsoft Rocketbox "_facial" avatars: 15 Oculus-order visemes (AA_VI_*) + ARKit shapes (AK_*).</summary>
        [ContextMenu("Fill viseme map: Microsoft Rocketbox")]
        public void ApplyRocketboxPreset()
        {
            string[] names = { "AA_VI_00_Sil", "AA_VI_01_PP", "AA_VI_02_FF", "AA_VI_03_TH", "AA_VI_04_DD", "AA_VI_05_KK",
                               "AA_VI_06_CH", "AA_VI_07_SS", "AA_VI_08_nn", "AA_VI_09_RR", "AA_VI_10_aa", "AA_VI_11_E",
                               "AA_VI_12_I", "AA_VI_13_O", "AA_VI_14_U" };
            visemeMap = new List<VisemeBlendShape>();
            for (int i = 0; i < names.Length; i++)
            {
                visemeMap.Add(new VisemeBlendShape { viseme = (Viseme)i, blendShape = names[i], weight = i == 0 ? 0f : 0.85f });
            }
            jawOpenBlendShape = "AK_25_JawOpen";
            jawOpenScale = 0.25f; // visemes already open the mouth; add a little jaw for energy
            blinkBlendShapes = new[] { "AK_09_EyeBlinkLeft", "AK_10_EyeBlinkRight" };
            smileBlendShapes = new[] { "AK_44_MouthSmileLeft", "AK_45_MouthSmileRight" };
            browRaiseBlendShapes = new[] { "AK_03_BrowInnerUp" };
            maxGazeDegrees = 12f;
        }

        [ContextMenu("Fill viseme map: ARKit approximation")]
        private void FillArkitPreset()
        {
            visemeMap = new List<VisemeBlendShape>
            {
                new VisemeBlendShape { viseme = Viseme.PP, blendShape = "mouthClose", weight = 0.8f },
                new VisemeBlendShape { viseme = Viseme.PP, blendShape = "mouthPressLeft", weight = 0.5f },
                new VisemeBlendShape { viseme = Viseme.PP, blendShape = "mouthPressRight", weight = 0.5f },
                new VisemeBlendShape { viseme = Viseme.FF, blendShape = "mouthRollLower", weight = 0.7f },
                new VisemeBlendShape { viseme = Viseme.SS, blendShape = "mouthStretchLeft", weight = 0.5f },
                new VisemeBlendShape { viseme = Viseme.SS, blendShape = "mouthStretchRight", weight = 0.5f },
                new VisemeBlendShape { viseme = Viseme.CH, blendShape = "mouthFunnel", weight = 0.6f },
                new VisemeBlendShape { viseme = Viseme.E, blendShape = "mouthStretchLeft", weight = 0.6f },
                new VisemeBlendShape { viseme = Viseme.E, blendShape = "mouthStretchRight", weight = 0.6f },
                new VisemeBlendShape { viseme = Viseme.IH, blendShape = "mouthSmileLeft", weight = 0.4f },
                new VisemeBlendShape { viseme = Viseme.IH, blendShape = "mouthSmileRight", weight = 0.4f },
                new VisemeBlendShape { viseme = Viseme.OH, blendShape = "mouthFunnel", weight = 0.8f },
                new VisemeBlendShape { viseme = Viseme.OU, blendShape = "mouthPucker", weight = 1f },
                new VisemeBlendShape { viseme = Viseme.AA, blendShape = "mouthLowerDownLeft", weight = 0.4f },
                new VisemeBlendShape { viseme = Viseme.AA, blendShape = "mouthLowerDownRight", weight = 0.4f },
            };
            jawOpenBlendShape = "jawOpen";
            jawOpenScale = 0.7f;
        }
    }
}
