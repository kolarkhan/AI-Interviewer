using UnityEngine;

namespace AIInterviewer.Avatar
{
    /// <summary>
    /// Describes an importable interviewer character. Create via Assets > Create > AI Interviewer > Avatar Definition
    /// and save it at Resources/Avatars/{id}.asset where {id} matches the backend avatar id (e.g. "jordan").
    /// </summary>
    [CreateAssetMenu(menuName = "AI Interviewer/Avatar Definition", fileName = "AvatarDefinition")]
    public sealed class AvatarDefinition : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Prefab with a BlendShapeAvatarRig on its root (rigged humanoid with facial blendshapes).")]
        public GameObject prefab;

        [Tooltip("Uniform scale applied to the prefab instance.")]
        public float scale = 1f;

        [Tooltip("Local position offset so the character sits behind the desk.")]
        public Vector3 positionOffset;

        [Tooltip("Asset licence / source, shown in the credits screen.")]
        [TextArea] public string licenseNotice;
    }

    public static class AvatarFactory
    {
        public const string PlaceholderId = "alex_placeholder";
        public const string DefaultId = "alex";

        /// <summary>Instantiates the requested avatar, or the labeled placeholder if it is not available.</summary>
        public static IAvatarRig Create(string avatarId, Transform parent, out string notice)
        {
            notice = null;
            if (!string.IsNullOrEmpty(avatarId) && avatarId != PlaceholderId)
            {
                var def = Resources.Load<AvatarDefinition>("Avatars/" + avatarId);
                if (def != null && def.prefab != null)
                {
                    var go = Object.Instantiate(def.prefab, parent, false);
                    go.transform.localScale = Vector3.one * def.scale;
                    go.transform.localPosition = def.positionOffset;
                    var rig = go.GetComponent<BlendShapeAvatarRig>();
                    if (rig != null) return rig;
                    Debug.LogError($"[Avatar] Prefab for '{avatarId}' has no BlendShapeAvatarRig; using placeholder.");
                    Object.Destroy(go);
                }
                notice = $"Avatar '{avatarId}' is not installed in this build. Showing the placeholder avatar.";
            }
            return PlaceholderAvatar.Build(parent);
        }
    }
}
