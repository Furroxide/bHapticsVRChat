#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("bHapticsOSC/VRCFury Setup")]
    public class bVrcFurySetup : MonoBehaviour
    {
        [SerializeField]
        private string generatedAssetFolderPath;
        [SerializeField]
        private string sourceGlobalObjectId;
        [SerializeField]
        private int sourceInstanceId;
        [SerializeField]
        private bool cleanupGeneratedAssets = true;

        public string GeneratedAssetFolderPath => generatedAssetFolderPath;

        public void Configure(string folderPath)
        {
            generatedAssetFolderPath = NormalizeAssetPath(folderPath);
            sourceGlobalObjectId = GetGlobalObjectId(gameObject);
            sourceInstanceId = gameObject.GetInstanceID();
            EditorUtility.SetDirty(this);
        }

        private void OnValidate()
            => PromoteToStableObjectId();

        private void OnEnable()
            => PromoteToStableObjectId();

        private void OnDestroy()
        {
            string folderPath = generatedAssetFolderPath;
            if (!cleanupGeneratedAssets || !ShouldCleanup(folderPath) || !IsConfiguredSourceObject())
                return;

            EditorApplication.delayCall += () => DeleteGeneratedAssetsIfSetupMissing(folderPath);
        }

        private void PromoteToStableObjectId()
        {
            if (HasStableObjectId(sourceGlobalObjectId) || sourceInstanceId != gameObject.GetInstanceID())
                return;

            string currentGlobalObjectId = GetGlobalObjectId(gameObject);
            if (!HasStableObjectId(currentGlobalObjectId))
                return;

            sourceGlobalObjectId = currentGlobalObjectId;
            EditorUtility.SetDirty(this);
        }

        private bool IsConfiguredSourceObject()
        {
            string currentGlobalObjectId = GetGlobalObjectId(gameObject);
            if (HasStableObjectId(sourceGlobalObjectId))
                return currentGlobalObjectId == sourceGlobalObjectId;

            return sourceInstanceId == gameObject.GetInstanceID();
        }

        private static bool ShouldCleanup(string folderPath)
        {
            return !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating
                   && !EditorApplication.isPlayingOrWillChangePlaymode
                   && !BuildPipeline.isBuildingPlayer
                   && IsSafeGeneratedFolder(folderPath);
        }

        private static void DeleteGeneratedAssets(string folderPath)
        {
            if (!ShouldCleanup(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return;

            AssetDatabase.DeleteAsset(folderPath);
            DeleteGeneratedRootIfEmpty();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void DeleteGeneratedAssetsIfSetupMissing(string folderPath)
        {
            if (ConfiguredSetupExists(folderPath))
                return;

            DeleteGeneratedAssets(folderPath);
        }

        private static bool ConfiguredSetupExists(string folderPath)
        {
            folderPath = NormalizeAssetPath(folderPath);
            foreach (bVrcFurySetup setup in Resources.FindObjectsOfTypeAll<bVrcFurySetup>())
            {
                if (setup == null)
                    continue;

                if (NormalizeAssetPath(setup.generatedAssetFolderPath) != folderPath)
                    continue;

                if (setup.IsConfiguredSourceObject())
                    return true;
            }

            return false;
        }

        private static void DeleteGeneratedRootIfEmpty()
        {
            string generatedRoot = bHapticsOSCIntegration.GeneratedAssetsRoot;
            if (!AssetDatabase.IsValidFolder(generatedRoot))
                return;

            string[] remainingAssets = AssetDatabase.FindAssets(string.Empty, new[] { generatedRoot });
            if (remainingAssets.Length == 0)
                AssetDatabase.DeleteAsset(generatedRoot);
        }

        private static bool IsSafeGeneratedFolder(string folderPath)
        {
            folderPath = NormalizeAssetPath(folderPath);
            string generatedRoot = bHapticsOSCIntegration.GeneratedAssetsRoot;
            return !string.IsNullOrWhiteSpace(folderPath)
                   && !folderPath.Equals(generatedRoot, StringComparison.Ordinal)
                   && folderPath.StartsWith($"{generatedRoot}/", StringComparison.Ordinal);
        }

        private static string NormalizeAssetPath(string path)
            => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');

        private static string GetGlobalObjectId(GameObject obj)
            => GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();

        private static bool HasStableObjectId(string objectId)
            => !string.IsNullOrWhiteSpace(objectId) && !objectId.Contains("Null");
    }
}
#endif
