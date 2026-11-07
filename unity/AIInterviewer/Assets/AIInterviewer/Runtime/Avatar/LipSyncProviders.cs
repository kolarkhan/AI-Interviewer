using System;
using UnityEngine;

namespace AIInterviewer.Avatar
{
    public enum LipSyncProviderKind
    {
        BuiltInSpectral,
        ULipSync
    }

    /// <summary>Creates lip-sync providers. Optional providers live in separate assemblies and are found by name.</summary>
    public static class LipSyncProviders
    {
        private const string ULipSyncType = "AIInterviewer.LipSync.ULipSyncProvider, AIInterviewer.LipSync.ULipSync";

        public static bool IsULipSyncInstalled => Type.GetType(ULipSyncType) != null;

        public static IAvatarLipSync Create(LipSyncProviderKind kind)
        {
            if (kind == LipSyncProviderKind.ULipSync)
            {
                var type = Type.GetType(ULipSyncType);
                if (type != null) return (IAvatarLipSync)Activator.CreateInstance(type);
                Debug.LogWarning("[LipSync] uLipSync is not installed; using the built-in provider.");
            }
            return new SpectralLipSync();
        }
    }
}
