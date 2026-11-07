using AIInterviewer.Audio;
using AIInterviewer.Avatar;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIInterviewer.Scene
{
    /// <summary>
    /// Builds the virtual interview room at runtime: camera framing, three-point lighting, backdrop with depth,
    /// desk and the interviewer. Geometry is intentionally simple (mobile budget: &lt; 30 draw calls, no realtime
    /// shadows on low tier). Replace with an authored environment prefab for production art if desired.
    /// </summary>
    public sealed class InterviewRoom : MonoBehaviour
    {
        private Camera _camera;
        private Transform _avatarAnchor;
        private Transform _environment;
        private Vector2Int _lastScreen;
        private float _bottomUiFraction = 0.28f;
        private float _headY = 1.63f;

        public Camera Camera => _camera;
        public AvatarController Avatar { get; private set; }
        public StreamingAudioPlayer Voice { get; private set; }
        public string AvatarNotice { get; private set; }
        public bool AvatarIsPlaceholder => Avatar != null && Avatar.Rig.IsPlaceholder;

        /// <summary>Id requested by the last <see cref="SetAvatar"/> call (the rig may be the placeholder fallback).</summary>
        public string CurrentAvatarId { get; private set; }

        public static InterviewRoom Create(Transform parent)
        {
            var go = new GameObject("InterviewRoom");
            go.transform.SetParent(parent, false);
            var room = go.AddComponent<InterviewRoom>();
            room.Build();
            return room;
        }

        private void Build()
        {
            ConfigureQuality();

            var camGo = new GameObject("InterviewCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color32(0x0B, 0x11, 0x20, 0xFF);
            _camera.fieldOfView = CameraFov;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;
            _camera.allowHDR = false;
            _camera.allowMSAA = SystemInfo.systemMemorySize > 3500;
            camGo.AddComponent<AudioListener>();

            _environment = new GameObject("Environment").transform;
            _environment.SetParent(transform, false);
            BuildEnvironment();
            BuildLights();

            _avatarAnchor = new GameObject("AvatarAnchor").transform;
            _avatarAnchor.SetParent(transform, false);

            // Interviewer voice: positioned at the avatar's mouth (2D audio, the position only aids debugging).
            // Kept outside the avatar anchor so hiding the room never stops the audio pipeline.
            var voiceGo = new GameObject("InterviewerVoice");
            voiceGo.transform.SetParent(transform, false);
            voiceGo.transform.localPosition = new Vector3(0f, 1.57f, 0.1f);
            voiceGo.AddComponent<AudioSource>();
            Voice = voiceGo.AddComponent<StreamingAudioPlayer>();

            Frame();
        }

        /// <summary>Spawns (or replaces) the interviewer avatar.</summary>
        public void SetAvatar(string avatarId, IAvatarLipSync lipSync)
        {
            if (Avatar != null)
            {
                Avatar.ResetFace();
                Destroy(Avatar.Rig.Root.gameObject);
                Destroy(Avatar);
            }
            // Spawn with the anchor active so the character's Awake/Animator run now (the room may still be hidden
            // when a screen calls this from OnShow); restore the previous state afterwards.
            bool anchorWasActive = _avatarAnchor.gameObject.activeSelf;
            _avatarAnchor.gameObject.SetActive(true);
            var rig = AvatarFactory.Create(avatarId, _avatarAnchor, out string notice);
            CurrentAvatarId = avatarId;
            AvatarNotice = notice;
            if (rig is BlendShapeAvatarRig real)
            {
                // Imported characters may face another axis; turn them towards the camera (+Z).
                real.FaceDirection(transform.forward);
            }
            Avatar = rig.Root.gameObject.AddComponent<AvatarController>();
            Avatar.Initialize(rig, lipSync ?? new SpectralLipSync(), Voice);
            RefreshHeadHeight(force: true);
            _avatarAnchor.gameObject.SetActive(anchorWasActive);
            Frame();
        }

        /// <summary>Fraction of the screen covered by bottom controls; the camera keeps the face above it.</summary>
        public void SetBottomUiFraction(float fraction)
        {
            _bottomUiFraction = Mathf.Clamp(fraction, 0f, 0.6f);
            Frame();
        }

        public void SetActive(bool active)
        {
            _camera.enabled = active;
            _environment.gameObject.SetActive(active);
            _avatarAnchor.gameObject.SetActive(active);
            if (active) _measureFrames = 30; // re-measure once the idle animation has posed the character
        }

        private int _measureFrames;

        /// <summary>Frames the camera on the avatar's real eye height (eyes sit slightly above the head bone).</summary>
        private void RefreshHeadHeight(bool force)
        {
            if (Avatar == null) return;
            var head = Avatar.Rig.Head;
            if (head == null || head == Avatar.Rig.Root) return;
            float eye = head.position.y - transform.position.y + (Avatar.Rig.IsPlaceholder ? 0.13f : 0.09f);
            eye = Mathf.Clamp(eye, 1.0f, 2.2f);
            if (force || Mathf.Abs(eye - _headY) > 0.02f)
            {
                _headY = eye;
                Frame();
            }
        }

        private void Update()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != _lastScreen) Frame();
            if (_measureFrames > 0)
            {
                _measureFrames--;
                RefreshHeadHeight(force: false);
            }
        }

        // Close-up "video call" framing: from just above the hair down to the chest pocket.
        private const float CameraFov = 25f;          // portrait-lens feel, little facial distortion
        private const float AboveEyes = 0.14f;        // top of the hair above eye level (m)
        private const float BelowEyes = 0.28f;        // upper chest below eye level (m)
        private const float MinHalfWidth = 0.14f;     // face plus the start of the shoulders
        private const float BottomOverlap = 0.15f;    // the chest may sit slightly under the bottom fade
        private const float TopHudFraction = 0.08f;   // timer/status chips at the top of the screen

        private void Frame()
        {
            if (_camera == null) return;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            _camera.fieldOfView = CameraFov;
            float aspect = Mathf.Max(0.3f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float tanHalf = Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad);
            float eyeY = _headY;

            // The avatar must fit in the part of the screen not covered by the HUD and bottom controls.
            float freeFraction = Mathf.Clamp(1f - _bottomUiFraction * (1f - BottomOverlap) - TopHudFraction, 0.35f, 1f);
            float visibleHeight = (AboveEyes + BelowEyes) / (freeFraction * 0.95f);
            visibleHeight = Mathf.Max(visibleHeight, 2f * MinHalfWidth / aspect);
            float distance = Mathf.Clamp(visibleHeight * 0.5f / tanHalf, 0.6f, 3.5f);
            visibleHeight = 2f * tanHalf * distance;

            // Place the top of the hair just under the top HUD; everything below the pocket is behind the controls.
            float topY = eyeY + AboveEyes + visibleHeight * (TopHudFraction + 0.02f);
            float centreY = topY - visibleHeight * 0.5f;
            _camera.transform.localPosition = new Vector3(0f, eyeY, distance); // eye-level camera
            _camera.transform.LookAt(transform.TransformPoint(new Vector3(0f, centreY, 0f)));
        }

        private void BuildEnvironment()
        {
            // Backdrop: gradient wall with soft panels for depth.
            var wallTex = MaterialLibrary.VerticalGradient(new Color32(0x0C, 0x13, 0x24, 0xFF), new Color32(0x1B, 0x26, 0x40, 0xFF));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Strip(wall, "Backdrop", new Vector3(0f, 1.6f, -2.2f), new Vector3(7f, 4.5f, 1f), MaterialLibrary.Unlit(Color.white, wallTex));
            wall.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads face -Z; the camera looks from +Z
            for (int i = -3; i <= 3; i++)
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                float x = i * 0.9f;
                Strip(panel, "Panel", new Vector3(x, 1.7f, -2.1f), new Vector3(0.04f, 3.2f, 0.06f),
                    MaterialLibrary.Lit(new Color32(0x22, 0x2F, 0x4D, 0xFF), 0.2f));
            }
            // Warm "window" glow to the side for colour contrast.
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Strip(glow, "WindowGlow", new Vector3(-1.9f, 1.9f, -2.15f), new Vector3(1.1f, 1.8f, 1f),
                MaterialLibrary.Unlit(new Color(1f, 0.78f, 0.55f, 1f) * 0.35f));
            glow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Plant silhouette for depth.
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(pot, "PlantPot", new Vector3(1.25f, 0.55f, -1.2f), new Vector3(0.28f, 0.25f, 0.28f),
                MaterialLibrary.Lit(new Color32(0x2C, 0x2C, 0x34, 0xFF), 0.3f));
            for (int i = 0; i < 4; i++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Strip(leaf, "Leaves", new Vector3(1.25f + (i % 2 - 0.5f) * 0.22f, 1.0f + i * 0.18f, -1.2f + (i - 1.5f) * 0.05f),
                    new Vector3(0.4f, 0.32f, 0.35f) * (1f - i * 0.12f),
                    MaterialLibrary.Lit(new Color32(0x24, 0x4A, 0x3A, 0xFF), 0.15f));
            }
            // Desk in front of the interviewer (hides the lower body, frames the shot).
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(desk, "Desk", new Vector3(0f, 0.74f, 0.42f), new Vector3(1.8f, 0.05f, 0.7f),
                MaterialLibrary.Lit(new Color32(0x3A, 0x2C, 0x25, 0xFF), 0.55f));
            var laptop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(laptop, "Laptop", new Vector3(-0.42f, 0.86f, 0.36f), new Vector3(0.34f, 0.22f, 0.012f),
                MaterialLibrary.Lit(new Color32(0x9A, 0xA3, 0xB3, 0xFF), 0.7f, 0.6f));
            laptop.transform.localRotation = Quaternion.Euler(-15f, 20f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Strip(floor, "Floor", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f),
                MaterialLibrary.Lit(new Color32(0x10, 0x16, 0x26, 0xFF), 0.1f));
        }

        private void Strip(GameObject go, string name, Vector3 pos, Vector3 scale, Material material)
        {
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(_environment, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void BuildLights()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color32(0x3A, 0x48, 0x6B, 0xFF);
            RenderSettings.ambientEquatorColor = new Color32(0x24, 0x2C, 0x42, 0xFF);
            RenderSettings.ambientGroundColor = new Color32(0x12, 0x14, 0x1C, 0xFF);

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(_environment, false);
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.93f, 0.85f);
            key.intensity = 1.15f;
            key.transform.localRotation = Quaternion.Euler(28f, 150f, 0f); // from front-left, above
            key.shadows = LightShadows.None;

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.transform.SetParent(_environment, false);
            fill.type = LightType.Point;
            fill.color = new Color(0.6f, 0.72f, 1f);
            fill.intensity = 0.8f;
            fill.range = 4f;
            fill.transform.localPosition = new Vector3(1.2f, 1.7f, 1.4f);

            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.transform.SetParent(_environment, false);
            rim.type = LightType.Spot;
            rim.color = new Color(0.55f, 0.75f, 1f);
            rim.intensity = 2.2f;
            rim.range = 4f;
            rim.spotAngle = 50f;
            rim.transform.localPosition = new Vector3(-0.6f, 2.3f, -1.2f);
            rim.transform.LookAt(transform.TransformPoint(new Vector3(0f, 1.55f, 0f)));
        }

        private static void ConfigureQuality()
        {
            // Mid-range Android target: 60 fps where possible, 30 on low-memory devices.
            bool lowEnd = SystemInfo.systemMemorySize < 3000 || SystemInfo.processorCount <= 4;
            Application.targetFrameRate = lowEnd ? 30 : 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = lowEnd ? 0 : 2;
        }
    }
}
