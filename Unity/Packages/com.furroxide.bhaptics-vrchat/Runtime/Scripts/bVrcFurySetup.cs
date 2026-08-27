#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace bHapticsOSC.VRChat
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("bHapticsOSC/VRCFury Setup")]
    public class bVrcFurySetup : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [SerializeField]
        private string generatedAssetFolderPath;
        [SerializeField]
        private bool cleanupGeneratedAssets = true;

        public string GeneratedAssetFolderPath => generatedAssetFolderPath;

        public void Configure(string folderPath)
        {
            generatedAssetFolderPath = NormalizeAssetPath(folderPath);
            EditorUtility.SetDirty(this);
        }

        private void OnDestroy()
        {
            string folderPath = generatedAssetFolderPath;
            if (!cleanupGeneratedAssets || !ShouldCleanup(folderPath))
                return;

            PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
            bVrcFurySetupCleanup.Request(
                gameObject.scene,
                folderPath,
                prefabStage != null);
        }

        internal static bool ShouldCleanup(string folderPath)
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
        }

        internal static void DeleteGeneratedAssetsIfSetupMissing(string folderPath)
        {
            DeleteGeneratedAssetsIfSetupsMissing(new[] { folderPath });
        }

        internal static void DeleteGeneratedAssetsIfSetupsMissing(IEnumerable<string> folderPaths)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            if (folderPaths == null)
                return;

            foreach (string folderPathValue in folderPaths)
            {
                string folderPath = NormalizeAssetPath(folderPathValue);
                if (ShouldCleanup(folderPath) && AssetDatabase.IsValidFolder(folderPath))
                    candidates.Add(folderPath);
            }

            RemoveConfiguredFoldersAndOverlaps(candidates);
            if (candidates.Count == 0)
                return;

            candidates.ExceptWith(GetReferencedFolders(candidates));
            RemoveConfiguredFoldersAndOverlaps(candidates);
            foreach (string folderPath in candidates)
                DeleteGeneratedAssets(folderPath);

            DeleteGeneratedRootIfEmpty();
        }

        internal static bool ConfiguredSetupExists(string folderPath)
            => GetConfiguredSetupFolders().Contains(NormalizeAssetPath(folderPath));

        private static void RemoveConfiguredFoldersAndOverlaps(HashSet<string> candidates)
        {
            HashSet<string> configuredFolders = GetConfiguredSetupFolders();
            foreach (string candidate in new List<string>(candidates))
            {
                foreach (string configuredFolder in configuredFolders)
                {
                    if (!IsPathInsideFolder(candidate, configuredFolder)
                        && !IsPathInsideFolder(configuredFolder, candidate))
                    {
                        continue;
                    }

                    candidates.Remove(candidate);
                    break;
                }
            }
        }

        private static HashSet<string> GetConfiguredSetupFolders()
        {
            var configuredFolders = new HashSet<string>(StringComparer.Ordinal);
            foreach (bVrcFurySetup setup in Resources.FindObjectsOfTypeAll<bVrcFurySetup>())
            {
                if (setup == null
                    || EditorUtility.IsPersistent(setup)
                    || !setup.gameObject.scene.IsValid()
                    || !setup.gameObject.scene.isLoaded)
                    continue;

                string configuredFolder = NormalizeAssetPath(setup.generatedAssetFolderPath);
                if (!string.IsNullOrEmpty(configuredFolder))
                    configuredFolders.Add(configuredFolder);
            }

            return configuredFolders;
        }

        internal static bool PersistentReferenceExists(string folderPath)
        {
            folderPath = NormalizeAssetPath(folderPath);
            if (!IsSafeGeneratedFolder(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return false;

            var candidates = new HashSet<string>(StringComparer.Ordinal) { folderPath };
            return GetReferencedFolders(candidates).Contains(folderPath);
        }

        public static bool GeneratedFolderIsClaimed(string folderPath)
        {
            folderPath = NormalizeAssetPath(folderPath);
            foreach (string configuredFolder in GetConfiguredSetupFolders())
            {
                if (IsPathInsideFolder(folderPath, configuredFolder)
                    || IsPathInsideFolder(configuredFolder, folderPath))
                {
                    return true;
                }
            }

            return PersistentReferenceExists(folderPath);
        }

        private static HashSet<string> GetReferencedFolders(HashSet<string> candidates)
        {
            var referencedFolders = new HashSet<string>(StringComparer.Ordinal);
            var generatedAssetOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string folderPath in candidates)
            {
                generatedAssetOwners[folderPath] = folderPath;
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folderPath }))
                {
                    string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (!string.IsNullOrEmpty(assetPath))
                        generatedAssetOwners[assetPath] = folderPath;
                }
            }

            AddLoadedObjectReferences(generatedAssetOwners, candidates, referencedFolders);

            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            AddSerializedSetupReferences(allAssetPaths, candidates, referencedFolders);
            ExpandOverlappingReferencedFolders(candidates, referencedFolders);

            var externalAssetPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string assetPathValue in allAssetPaths)
            {
                string normalizedPath = NormalizeAssetPath(assetPathValue);
                if ((!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                     && !normalizedPath.StartsWith("Packages/", StringComparison.Ordinal))
                    || IsPathInsideAnyFolder(normalizedPath, candidates)
                    || AssetDatabase.IsValidFolder(normalizedPath))
                {
                    continue;
                }

                externalAssetPaths.Add(normalizedPath);
            }

            // A serialized setup marker can protect a folder without directly
            // referencing one of its assets. Treat assets in any already-protected
            // folder as dependency roots so cross-folder generated references are
            // retained transitively as well.
            foreach (KeyValuePair<string, string> generatedAssetOwner in generatedAssetOwners)
            {
                if (referencedFolders.Contains(generatedAssetOwner.Value)
                    && !AssetDatabase.IsValidFolder(generatedAssetOwner.Key))
                {
                    externalAssetPaths.Add(generatedAssetOwner.Key);
                }
            }

            if (externalAssetPaths.Count > 0)
            {
                try
                {
                    foreach (string dependencyPathValue in
                             AssetDatabase.GetDependencies(
                                 new List<string>(externalAssetPaths).ToArray(),
                                 true))
                    {
                        string dependencyPath = NormalizeAssetPath(dependencyPathValue);
                        if (generatedAssetOwners.TryGetValue(dependencyPath, out string ownerFolder))
                            referencedFolders.Add(ownerFolder);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Unable to verify generated-asset dependencies; cleanup was skipped. {exception.Message}");
                    referencedFolders.UnionWith(candidates);
                }
            }

            ExpandOverlappingReferencedFolders(candidates, referencedFolders);
            return referencedFolders;
        }

        private static void ExpandOverlappingReferencedFolders(
            HashSet<string> candidates,
            HashSet<string> referencedFolders)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (string candidate in candidates)
                {
                    if (referencedFolders.Contains(candidate))
                        continue;

                    foreach (string referencedFolder in new List<string>(referencedFolders))
                    {
                        if (!IsPathInsideFolder(candidate, referencedFolder)
                            && !IsPathInsideFolder(referencedFolder, candidate))
                        {
                            continue;
                        }

                        changed |= referencedFolders.Add(candidate);
                        break;
                    }
                }
            } while (changed);
        }

        private static void AddLoadedObjectReferences(
            Dictionary<string, string> generatedAssetOwners,
            HashSet<string> candidates,
            HashSet<string> referencedFolders)
        {
            var sources = new List<UnityEngine.Object>();
            var sourceInstanceIds = new HashSet<int>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root != null && sourceInstanceIds.Add(root.GetInstanceID()))
                        sources.Add(root);
                }
            }

            PrefabStage currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject prefabContentsRoot = currentPrefabStage?.prefabContentsRoot;
            if (prefabContentsRoot != null
                && sourceInstanceIds.Add(prefabContentsRoot.GetInstanceID()))
            {
                sources.Add(prefabContentsRoot);
            }

            // Also cover loaded prefab contents and other nonpersistent preview
            // scenes that are not part of SceneManager's normal scene list.
            foreach (GameObject loadedObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (loadedObject == null
                    || EditorUtility.IsPersistent(loadedObject)
                    || loadedObject.transform.parent != null
                    || !loadedObject.scene.IsValid()
                    || !loadedObject.scene.isLoaded
                    || !sourceInstanceIds.Add(loadedObject.GetInstanceID()))
                {
                    continue;
                }

                sources.Add(loadedObject);
            }

            foreach (UnityEngine.Object loadedObject in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (loadedObject == null
                    || !EditorUtility.IsPersistent(loadedObject)
                    || !EditorUtility.IsDirty(loadedObject)
                    || !sourceInstanceIds.Add(loadedObject.GetInstanceID()))
                {
                    continue;
                }

                string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(loadedObject));
                if (IsPathInsideAnyFolder(assetPath, candidates))
                    continue;

                sources.Add(loadedObject);
            }

            if (sources.Count == 0)
                return;

            try
            {
                foreach (UnityEngine.Object dependency in EditorUtility.CollectDependencies(sources.ToArray()))
                {
                    if (dependency == null)
                        continue;

                    string dependencyPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(dependency));
                    if (generatedAssetOwners.TryGetValue(dependencyPath, out string ownerFolder))
                        referencedFolders.Add(ownerFolder);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Unable to verify loaded generated-asset references; cleanup was skipped. {exception.Message}");
                referencedFolders.UnionWith(candidates);
            }
        }

        private static void AddSerializedSetupReferences(
            IEnumerable<string> allAssetPaths,
            HashSet<string> candidates,
            HashSet<string> referencedFolders)
        {
            foreach (string assetPath in allAssetPaths)
            {
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    || (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                        && !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!TryAddSerializedSetupReferences(assetPath, candidates, referencedFolders))
                {
                    // Fail closed when a serialized asset cannot be inspected.
                    referencedFolders.UnionWith(candidates);
                    return;
                }
            }
        }

        private static bool TryAddSerializedSetupReferences(
            string assetPath,
            HashSet<string> candidates,
            HashSet<string> referencedFolders)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(projectRoot))
                    return false;

                using (StreamReader reader = File.OpenText(Path.Combine(projectRoot, assetPath)))
                {
                    string line = reader.ReadLine();
                    if (line == null
                        || !line.TrimStart('\uFEFF').StartsWith("%YAML", StringComparison.Ordinal))
                    {
                        // Binary scenes and prefabs are covered by the dependency graph.
                        return true;
                    }

                    AddSerializedSetupReferencesFromLine(line, candidates, referencedFolders);
                    while ((line = reader.ReadLine()) != null)
                        AddSerializedSetupReferencesFromLine(line, candidates, referencedFolders);
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void AddSerializedSetupReferencesFromLine(
            string line,
            HashSet<string> candidates,
            HashSet<string> referencedFolders)
        {
            const string fieldName = "generatedAssetFolderPath:";
            string trimmedLine = line.TrimStart();
            if (!trimmedLine.StartsWith(fieldName, StringComparison.Ordinal))
                return;

            string serializedPath = trimmedLine.Substring(fieldName.Length).Trim();
            if (serializedPath.Length >= 2
                && serializedPath[0] == '"'
                && serializedPath[serializedPath.Length - 1] == '"')
            {
                serializedPath = serializedPath.Substring(1, serializedPath.Length - 2);
            }

            string normalizedSerializedPath = NormalizeAssetPath(serializedPath);
            foreach (string folderPath in candidates)
            {
                if (IsPathInsideFolder(normalizedSerializedPath, folderPath)
                    || IsPathInsideFolder(folderPath, normalizedSerializedPath))
                {
                    referencedFolders.Add(folderPath);
                }
            }
        }

        private static bool IsPathInsideAnyFolder(
            string assetPath,
            IEnumerable<string> folderPaths)
        {
            foreach (string folderPath in folderPaths)
            {
                if (IsPathInsideFolder(assetPath, folderPath))
                    return true;
            }

            return false;
        }

        private static bool IsPathInsideFolder(string assetPath, string folderPath)
        {
            return assetPath.Equals(folderPath, StringComparison.Ordinal)
                   || assetPath.StartsWith($"{folderPath}/", StringComparison.Ordinal);
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

        internal static string NormalizeAssetPath(string path)
            => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    [InitializeOnLoad]
    internal static class bVrcFurySetupCleanup
    {
        private const int MaxPrefabCloseChecks = 120;

        private sealed class PendingSceneCleanup
        {
            internal readonly HashSet<string> Requested = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> ConfirmedBySave = new HashSet<string>(StringComparer.Ordinal);
            internal bool IsPrefabStage;
        }

        private static readonly Dictionary<int, PendingSceneCleanup> PendingByScene =
            new Dictionary<int, PendingSceneCleanup>();
        private static readonly HashSet<int> ClosingScenes = new HashSet<int>();
        private static readonly HashSet<int> MonitoredPrefabScenes = new HashSet<int>();
        private static readonly Dictionary<int, int> PrefabMonitorMissingChecks =
            new Dictionary<int, int>();
        private static readonly Dictionary<int, int> PrefabMonitorOpenChecks =
            new Dictionary<int, int>();
        private static readonly Dictionary<int, string> SavingDestinationsByScene =
            new Dictionary<int, string>();
        private static readonly Dictionary<string, int> PendingFolderDeletes =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static bool isTearingDown;

        static bVrcFurySetupCleanup()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            PrefabStage.prefabSaved += OnPrefabSaved;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        internal static void Request(Scene scene, string folderPath, bool isPrefabStage)
        {
            folderPath = bVrcFurySetup.NormalizeAssetPath(folderPath);
            if (isTearingDown
                || !scene.IsValid()
                || !scene.isLoaded
                || ClosingScenes.Contains(scene.handle)
                || !bVrcFurySetup.ShouldCleanup(folderPath))
            {
                return;
            }

            if (!PendingByScene.TryGetValue(scene.handle, out PendingSceneCleanup pending))
            {
                pending = new PendingSceneCleanup();
                PendingByScene.Add(scene.handle, pending);
            }

            PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            pending.IsPrefabStage |= isPrefabStage
                                     || (currentStage != null && currentStage.scene == scene);
            pending.Requested.Add(folderPath);
        }

        private static void OnSceneSaving(Scene scene, string destinationPath)
        {
            if (scene.IsValid())
                SavingDestinationsByScene[scene.handle] = bVrcFurySetup.NormalizeAssetPath(destinationPath);
        }

        private static void OnSceneSaved(Scene scene)
        {
            bool savedCurrentState = !scene.isDirty
                                     && SavingDestinationsByScene.TryGetValue(
                                         scene.handle,
                                         out string destinationPath)
                                     && destinationPath == bVrcFurySetup.NormalizeAssetPath(scene.path);
            SavingDestinationsByScene.Remove(scene.handle);
            if (!savedCurrentState)
                return;

            ProcessSavedScene(scene);
        }

        private static bool ConfiguredSetupExistsInScene(Scene scene, string folderPath)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            folderPath = bVrcFurySetup.NormalizeAssetPath(folderPath);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (bVrcFurySetup setup in root.GetComponentsInChildren<bVrcFurySetup>(true))
                {
                    if (setup != null
                        && bVrcFurySetup.NormalizeAssetPath(setup.GeneratedAssetFolderPath) == folderPath)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void OnPrefabSaved(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return;

            PrefabStage stage = PrefabStageUtility.GetPrefabStage(prefabRoot);
            if (stage == null)
            {
                PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage != null && currentStage.scene == prefabRoot.scene)
                    stage = currentStage;
            }

            if (stage == null || stage.scene.isDirty)
                return;

            ProcessSavedScene(stage.scene);
        }

        private static void ScheduleSavedPrefabStageMonitor(int sceneHandle)
        {
            if (PendingByScene.TryGetValue(sceneHandle, out PendingSceneCleanup pending)
                && pending.IsPrefabStage
                && pending.ConfirmedBySave.Count > 0
                && MonitoredPrefabScenes.Add(sceneHandle))
            {
                PrefabMonitorMissingChecks[sceneHandle] = 0;
                PrefabMonitorOpenChecks[sceneHandle] = 0;
            }
        }

        private static void MonitorSavedPrefabStages()
        {
            foreach (int sceneHandle in new List<int>(MonitoredPrefabScenes))
            {
                if (!PendingByScene.TryGetValue(sceneHandle, out PendingSceneCleanup pending)
                    || pending.ConfirmedBySave.Count == 0)
                {
                    StopMonitoringPrefabStage(sceneHandle);
                    continue;
                }

                PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage == null || currentStage.scene.handle != sceneHandle)
                {
                    int missingChecks = PrefabMonitorMissingChecks[sceneHandle] + 1;
                    PrefabMonitorMissingChecks[sceneHandle] = missingChecks;
                    if (missingChecks >= 2)
                        CompleteSceneClose(sceneHandle);
                    continue;
                }

                PrefabMonitorMissingChecks[sceneHandle] = 0;
                int openChecks = PrefabMonitorOpenChecks[sceneHandle] + 1;
                PrefabMonitorOpenChecks[sceneHandle] = openChecks;
                if (openChecks >= MaxPrefabCloseChecks)
                    StopMonitoringPrefabStage(sceneHandle);
            }
        }

        private static void OnEditorUpdate()
        {
            MonitorSavedPrefabStages();
            ProcessPendingFolderDeletes();
        }

        private static void ProcessPendingFolderDeletes()
        {
            if (isTearingDown || PendingFolderDeletes.Count == 0)
                return;

            var readyFolders = new List<string>();
            foreach (string folderPath in new List<string>(PendingFolderDeletes.Keys))
            {
                int remainingUpdates = PendingFolderDeletes[folderPath];
                if (remainingUpdates > 0)
                {
                    PendingFolderDeletes[folderPath] = remainingUpdates - 1;
                    continue;
                }

                PendingFolderDeletes.Remove(folderPath);
                readyFolders.Add(folderPath);
            }

            if (readyFolders.Count > 0)
                bVrcFurySetup.DeleteGeneratedAssetsIfSetupsMissing(readyFolders);
        }

        private static void StopMonitoringPrefabStage(int sceneHandle)
        {
            MonitoredPrefabScenes.Remove(sceneHandle);
            PrefabMonitorMissingChecks.Remove(sceneHandle);
            PrefabMonitorOpenChecks.Remove(sceneHandle);
        }

        private static void ProcessSavedScene(Scene scene)
        {
            if (isTearingDown || !PendingByScene.TryGetValue(scene.handle, out PendingSceneCleanup pending))
                return;

            foreach (string folderPath in new List<string>(pending.Requested))
            {
                if (ConfiguredSetupExistsInScene(scene, folderPath))
                {
                    pending.Requested.Remove(folderPath);
                    pending.ConfirmedBySave.Remove(folderPath);
                    continue;
                }

                pending.ConfirmedBySave.Add(folderPath);
            }

            if (pending.Requested.Count == 0)
                PendingByScene.Remove(scene.handle);
            else
                ScheduleSavedPrefabStageMonitor(scene.handle);
        }

        private static void OnPrefabStageClosing(PrefabStage stage)
        {
            if (stage == null)
                return;

            int sceneHandle = stage.scene.handle;
            OnSceneClosing(stage.scene, true);
            EditorApplication.delayCall += () => CompletePrefabStageClose(sceneHandle, 0);
        }

        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            SavingDestinationsByScene.Remove(scene.handle);
            ClosingScenes.Add(scene.handle);
            if (!PendingByScene.TryGetValue(scene.handle, out PendingSceneCleanup pending))
                return;

            if (pending.ConfirmedBySave.Count == 0)
                PendingByScene.Remove(scene.handle);
        }

        private static void OnSceneClosed(Scene scene)
            => CompleteSceneClose(scene.handle);

        private static void CompletePrefabStageClose(int sceneHandle, int checkCount)
        {
            PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage != null && currentStage.scene.handle == sceneHandle)
            {
                if (checkCount >= MaxPrefabCloseChecks)
                {
                    ClosingScenes.Remove(sceneHandle);
                    return;
                }

                EditorApplication.delayCall += () => CompletePrefabStageClose(
                    sceneHandle,
                    checkCount + 1);
                return;
            }

            CompleteSceneClose(sceneHandle);
        }

        private static void CompleteSceneClose(int sceneHandle)
        {
            ClosingScenes.Remove(sceneHandle);
            StopMonitoringPrefabStage(sceneHandle);
            if (!PendingByScene.TryGetValue(sceneHandle, out PendingSceneCleanup pending))
                return;

            var confirmedFolders = new List<string>(pending.ConfirmedBySave);
            PendingByScene.Remove(sceneHandle);
            if (confirmedFolders.Count == 0)
                return;

            foreach (string folderPath in confirmedFolders)
                PendingFolderDeletes[folderPath] = 1;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                DiscardPending();
        }

        private static void OnBeforeAssemblyReload()
        {
            isTearingDown = true;
            DiscardPending();
        }

        private static void OnEditorQuitting()
        {
            isTearingDown = true;
            DiscardPending();
        }

        private static void DiscardPending()
        {
            PendingByScene.Clear();
            ClosingScenes.Clear();
            MonitoredPrefabScenes.Clear();
            PrefabMonitorMissingChecks.Clear();
            PrefabMonitorOpenChecks.Clear();
            SavingDestinationsByScene.Clear();
            PendingFolderDeletes.Clear();
        }
    }
}
#endif
