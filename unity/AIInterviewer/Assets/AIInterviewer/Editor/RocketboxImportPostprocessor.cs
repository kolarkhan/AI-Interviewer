// Import rules for the Microsoft Rocketbox avatars (MIT licence) under Assets/ThirdParty/Rocketbox.
// Material and texture fixes are adapted from Rocketbox's FixRocketboxMaxImport.cs (MIT, (c) 2020 Microsoft),
// but models are imported as Humanoid (the original forces Generic) so the app's animator and gaze work.

using UnityEditor;
using UnityEngine;

namespace AIInterviewer.EditorTools
{
    public sealed class RocketboxImportPostprocessor : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdParty/Rocketbox/";

        private static bool IsRocketbox(string path) => path.Replace('\\', '/').StartsWith(Root);

        private static bool IsAnimation(string path) => path.Replace('\\', '/').Contains("/Animations/");

        private void OnPreprocessModel()
        {
            if (!IsRocketbox(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false; // the app needs head/eye bone transforms
            importer.importCameras = false;
            importer.importLights = false;
            if (IsAnimation(assetPath))
            {
                importer.importAnimation = true;
                importer.importBlendShapes = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            else
            {
                importer.importAnimation = false;
                importer.importBlendShapes = true;
                // Legacy import finds the 3ds Max textures in ../Textures and builds Standard materials.
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsRocketbox(assetPath) || !IsAnimation(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
                // Bake root motion into the pose so the character stays in place behind the desk.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!IsRocketbox(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            string p = assetPath.ToLowerInvariant();
            if (p.Contains("normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.convertToNormalmap = false;
            }
            // Mobile budget: full resolution for the face, half for body/clothes.
            importer.maxTextureSize = p.Contains("_head_") ? 2048 : 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.mipmapEnabled = true;
        }

        private void OnPostprocessMeshHierarchy(GameObject go)
        {
            if (!IsRocketbox(assetPath)) return;
            // Keep only the highest LOD active (Rocketbox ships hipoly/midpoly/lowpoly/ultralowpoly variants).
            string n = go.name.ToLowerInvariant();
            if (n.Contains("poly") && !n.Contains("hipoly")) go.SetActive(false);
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsRocketbox(assetPath)) return;
            // 3ds Max diffuse comes from the texture; Unity multiplies by the colour, so make it white.
            material.color = Color.white;
            // Hair/eyelashes use "Transparent" mode, which keeps specular highlights (glassy hair). Use Fade.
            if (material.HasProperty("_Mode") && Mathf.Approximately(material.GetFloat("_Mode"), 3f))
            {
                material.SetFloat("_Mode", 2f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
        }
    }
}
