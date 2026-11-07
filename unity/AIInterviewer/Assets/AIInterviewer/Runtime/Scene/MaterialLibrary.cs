using System.Collections.Generic;
using UnityEngine;

namespace AIInterviewer.Scene
{
    /// <summary>
    /// Creates tinted material instances from base materials in Resources/Materials (created by the editor
    /// menu "AI Interviewer/Configure Project", which guarantees the shaders are included in builds).
    /// Falls back to Shader.Find in the editor if the base materials are missing.
    /// </summary>
    public static class MaterialLibrary
    {
        private static Material _lit;
        private static Material _unlit;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static Material Lit(Color color, float smoothness = 0.25f, float metallic = 0f)
        {
            string key = $"lit:{ColorUtility.ToHtmlStringRGBA(color)}:{smoothness:F2}:{metallic:F2}";
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(BaseLit()) { name = key };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            Cache[key] = m;
            return m;
        }

        public static Material Unlit(Color color, Texture texture = null)
        {
            var m = new Material(BaseUnlit()) { name = "unlit" };
            SetColor(m, color);
            if (texture != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            }
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static Material BaseLit()
        {
            if (_lit != null) return _lit;
            _lit = Resources.Load<Material>("Materials/AII_Lit");
            if (_lit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Debug.LogWarning("[MaterialLibrary] Resources/Materials/AII_Lit missing. Run 'AI Interviewer > Configure Project'.");
                _lit = new Material(shader);
            }
            return _lit;
        }

        private static Material BaseUnlit()
        {
            if (_unlit != null) return _unlit;
            _unlit = Resources.Load<Material>("Materials/AII_Unlit");
            if (_unlit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                _unlit = new Material(shader);
            }
            return _unlit;
        }

        /// <summary>Vertical gradient texture (bottom to top).</summary>
        public static Texture2D VerticalGradient(Color bottom, Color top, int height = 128)
        {
            var tex = new Texture2D(2, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "gradient"
            };
            for (int y = 0; y < height; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(height - 1));
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply(false, true);
            return tex;
        }
    }
}
