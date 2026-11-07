using AIInterviewer.Core.LipSync;
using AIInterviewer.Scene;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    /// <summary>
    /// PLACEHOLDER interviewer built from primitives at runtime.
    ///
    /// This is NOT a production character. It exists so the full pipeline (animation states, blinking,
    /// gaze, head motion and audio-driven lip-sync) can be developed and demonstrated before a licensed,
    /// rigged humanoid with facial blendshapes is imported. The UI labels it "Placeholder avatar".
    /// Replace it by adding an AvatarDefinition (docs/AVATAR_ASSETS.md).
    /// </summary>
    public sealed class PlaceholderAvatar : MonoBehaviour, IAvatarRig
    {
        private static readonly Color Skin = new Color32(0xC8, 0x9B, 0x7E, 0xFF);
        private static readonly Color SkinShade = new Color32(0xB9, 0x8B, 0x6F, 0xFF);
        private static readonly Color Hair = new Color32(0x2A, 0x21, 0x1C, 0xFF);
        private static readonly Color Suit = new Color32(0x1D, 0x27, 0x3E, 0xFF);
        private static readonly Color SuitDark = new Color32(0x16, 0x1E, 0x31, 0xFF);
        private static readonly Color Shirt = new Color32(0xEE, 0xF1, 0xF5, 0xFF);
        private static readonly Color Tie = new Color32(0x7A, 0x2E, 0x3A, 0xFF);
        private static readonly Color Lips = new Color32(0xA8, 0x6A, 0x62, 0xFF);
        private static readonly Color MouthInside = new Color32(0x3E, 0x16, 0x1B, 0xFF);
        private static readonly Color EyeWhite = new Color32(0xF4, 0xF1, 0xEC, 0xFF);
        private static readonly Color Iris = new Color32(0x3B, 0x2B, 0x22, 0xFF);

        private Transform _headPivot, _torso, _shoulders, _mouthInner, _lips, _leftEye, _rightEye, _leftBrow, _rightBrow;
        private Quaternion _headRest;
        private Vector3 _torsoScale, _shouldersPos, _leftBrowPos, _rightBrowPos, _mouthPos;
        private float _smile, _brow, _jaw;
        private float _width = 0.055f, _height = 0.006f;

        public Transform Root => transform;
        public Transform Head => _headPivot;
        public bool IsPlaceholder => true;

        public static PlaceholderAvatar Build(Transform parent)
        {
            var root = new GameObject("PlaceholderInterviewer");
            root.transform.SetParent(parent, false);
            var avatar = root.AddComponent<PlaceholderAvatar>();
            avatar.Construct();
            return avatar;
        }

        private void Construct()
        {
            var suit = MaterialLibrary.Lit(Suit, 0.35f);
            var suitDark = MaterialLibrary.Lit(SuitDark, 0.3f);
            var skin = MaterialLibrary.Lit(Skin, 0.35f);
            var skinShade = MaterialLibrary.Lit(SkinShade, 0.3f);

            _torso = Part("Torso", PrimitiveType.Capsule, transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.5f, 0.34f, 0.27f), suit);
            _shoulders = Part("Shoulders", PrimitiveType.Capsule, transform, new Vector3(0f, 1.36f, -0.01f),
                new Vector3(0.17f, 0.3f, 0.17f), suit, Quaternion.Euler(0f, 0f, 90f));
            Part("LapelL", PrimitiveType.Cube, _torso, new Vector3(-0.17f, 0.3f, 0.42f), new Vector3(0.18f, 0.55f, 0.05f), suitDark,
                Quaternion.Euler(0f, 0f, -24f));
            Part("LapelR", PrimitiveType.Cube, _torso, new Vector3(0.17f, 0.3f, 0.42f), new Vector3(0.18f, 0.55f, 0.05f), suitDark,
                Quaternion.Euler(0f, 0f, 24f));
            Part("Shirt", PrimitiveType.Cube, transform, new Vector3(0f, 1.36f, 0.105f), new Vector3(0.12f, 0.12f, 0.04f),
                MaterialLibrary.Lit(Shirt, 0.2f), Quaternion.Euler(0f, 0f, 45f));
            Part("Tie", PrimitiveType.Cube, transform, new Vector3(0f, 1.24f, 0.13f), new Vector3(0.045f, 0.2f, 0.02f),
                MaterialLibrary.Lit(Tie, 0.4f));
            Part("Neck", PrimitiveType.Cylinder, transform, new Vector3(0f, 1.46f, 0f), new Vector3(0.1f, 0.06f, 0.1f), skinShade);

            _headPivot = new GameObject("HeadPivot").transform;
            _headPivot.SetParent(transform, false);
            _headPivot.localPosition = new Vector3(0f, 1.5f, 0f);
            _headRest = _headPivot.localRotation;

            Part("Head", PrimitiveType.Sphere, _headPivot, new Vector3(0f, 0.13f, 0f), new Vector3(0.2f, 0.25f, 0.22f), skin);
            Part("Hair", PrimitiveType.Sphere, _headPivot, new Vector3(0f, 0.19f, -0.015f), new Vector3(0.215f, 0.17f, 0.225f),
                MaterialLibrary.Lit(Hair, 0.45f));
            Part("Nose", PrimitiveType.Sphere, _headPivot, new Vector3(0f, 0.115f, 0.108f), new Vector3(0.03f, 0.045f, 0.035f), skinShade);
            Part("EarL", PrimitiveType.Sphere, _headPivot, new Vector3(-0.1f, 0.13f, 0f), new Vector3(0.025f, 0.05f, 0.035f), skinShade);
            Part("EarR", PrimitiveType.Sphere, _headPivot, new Vector3(0.1f, 0.13f, 0f), new Vector3(0.025f, 0.05f, 0.035f), skinShade);

            _leftEye = Eye("EyeL", new Vector3(-0.038f, 0.145f, 0.093f));
            _rightEye = Eye("EyeR", new Vector3(0.038f, 0.145f, 0.093f));
            var browMat = MaterialLibrary.Lit(Hair, 0.2f);
            _leftBrow = Part("BrowL", PrimitiveType.Cube, _headPivot, new Vector3(-0.04f, 0.183f, 0.1f),
                new Vector3(0.045f, 0.008f, 0.012f), browMat, Quaternion.Euler(0f, 0f, 6f));
            _rightBrow = Part("BrowR", PrimitiveType.Cube, _headPivot, new Vector3(0.04f, 0.183f, 0.1f),
                new Vector3(0.045f, 0.008f, 0.012f), browMat, Quaternion.Euler(0f, 0f, -6f));
            _leftBrowPos = _leftBrow.localPosition;
            _rightBrowPos = _rightBrow.localPosition;

            _mouthPos = new Vector3(0f, 0.066f, 0.101f);
            _lips = Part("Lips", PrimitiveType.Sphere, _headPivot, _mouthPos + new Vector3(0f, 0f, -0.002f),
                new Vector3(0.065f, 0.016f, 0.02f), MaterialLibrary.Lit(Lips, 0.5f));
            _mouthInner = Part("MouthInner", PrimitiveType.Sphere, _headPivot, _mouthPos + new Vector3(0f, 0f, 0.004f),
                new Vector3(0.055f, 0.006f, 0.018f), MaterialLibrary.Lit(MouthInside, 0.1f));

            _torsoScale = _torso.localScale;
            _shouldersPos = _shoulders.localPosition;
        }

        private Transform Eye(string name, Vector3 position)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(_headPivot, false);
            pivot.localPosition = position;
            Part("White", PrimitiveType.Sphere, pivot, Vector3.zero, Vector3.one * 0.032f, MaterialLibrary.Lit(EyeWhite, 0.6f));
            Part("Iris", PrimitiveType.Sphere, pivot, new Vector3(0f, 0f, 0.012f), new Vector3(0.018f, 0.018f, 0.012f),
                MaterialLibrary.Lit(Iris, 0.8f));
            return pivot;
        }

        private static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale,
            Material material, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rotation ?? Quaternion.identity;
            t.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }

        public void ApplyVisemes(VisemeFrame f)
        {
            _jaw = f.JawOpen;
            float wide = f[Viseme.E] + f[Viseme.IH] * 0.9f + f[Viseme.SS] * 0.6f;
            float round = f[Viseme.OU] + f[Viseme.OH] * 0.6f;
            float targetWidth = 0.055f + wide * 0.02f + f[Viseme.AA] * 0.006f - round * 0.022f + _smile * 0.008f;
            float targetHeight = 0.004f + _jaw * 0.05f + round * 0.006f;
            _width = Mathf.Lerp(_width, targetWidth, 0.5f);
            _height = Mathf.Lerp(_height, targetHeight, 0.6f);
            _mouthInner.localScale = new Vector3(_width, _height, 0.018f);
            _lips.localScale = new Vector3(_width + 0.012f, _height + 0.012f, 0.02f);
            var mouth = _mouthPos + new Vector3(0f, -_jaw * 0.008f + _smile * 0.002f, 0f);
            _mouthInner.localPosition = mouth + new Vector3(0f, 0f, 0.004f);
            _lips.localPosition = mouth + new Vector3(0f, 0f, -0.002f);
        }

        public void SetBlink(float closed)
        {
            float y = Mathf.Lerp(1f, 0.08f, closed);
            _leftEye.localScale = new Vector3(1f, y, 1f);
            _rightEye.localScale = new Vector3(1f, y, 1f);
        }

        public void SetGaze(Vector2 yawPitch)
        {
            var rot = Quaternion.Euler(-yawPitch.y, yawPitch.x, 0f);
            _leftEye.localRotation = rot;
            _rightEye.localRotation = rot;
        }

        public void SetMood(AvatarMood mood, float speakingIntensity)
        {
            // No Animator on the placeholder: mood is expressed through procedural pose and expression.
        }

        public void SetExpression(float smile, float browRaise)
        {
            _smile = smile;
            _brow = browRaise;
            var up = new Vector3(0f, _brow * 0.012f, 0f);
            _leftBrow.localPosition = _leftBrowPos + up;
            _rightBrow.localPosition = _rightBrowPos + up;
        }

        public void ApplyProceduralPose(Quaternion headOffset, float breath)
        {
            _headPivot.localRotation = _headRest * headOffset;
            _torso.localScale = new Vector3(_torsoScale.x * (1f + breath * 0.012f), _torsoScale.y, _torsoScale.z * (1f + breath * 0.02f));
            _shoulders.localPosition = _shouldersPos + new Vector3(0f, breath * 0.004f, 0f);
        }
    }
}
