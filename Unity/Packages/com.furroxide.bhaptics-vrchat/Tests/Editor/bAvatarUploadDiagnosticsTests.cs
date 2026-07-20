#if VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace bHapticsOSC.VRChat.Tests
{
    public class bAvatarUploadDiagnosticsTests
    {
        private GameObject avatar;

        [TearDown]
        public void TearDown()
        {
            if (avatar != null)
                Object.DestroyImmediate(avatar);
        }

        [Test]
        public void Callback_RunsImmediatelyBeforeVrcFury()
        {
            var diagnostics = new bAvatarUploadDiagnostics();

            Assert.That(diagnostics.callbackOrder, Is.EqualTo(-10001));
        }

        [Test]
        public void OnPreprocessAvatar_NullTarget_DoesNotBlockUpload()
        {
            var diagnostics = new bAvatarUploadDiagnostics();

            Assert.That(diagnostics.OnPreprocessAvatar(null), Is.True);
        }

        [Test]
        public void ClassifyTarget_UnrelatedAvatar_IsNone()
        {
            avatar = new GameObject("Unrelated Avatar");

            Assert.That(
                bAvatarUploadDiagnostics.ClassifyTarget(avatar),
                Is.EqualTo(bAvatarUploadTargetStatus.None));
        }

        [Test]
        public void ClassifyTarget_IntegrationOnInactiveChild_IsIncomplete()
        {
            avatar = new GameObject("Avatar");
            var child = new GameObject("Inactive Integration");
            child.transform.SetParent(avatar.transform);
            child.AddComponent<bHapticsOSCIntegration>();
            child.SetActive(false);

            Assert.That(
                bAvatarUploadDiagnostics.ClassifyTarget(avatar),
                Is.EqualTo(bAvatarUploadTargetStatus.Incomplete));
        }

        [Test]
        public void ClassifyTarget_SetupMarkerOnInactiveChild_IsConfigured()
        {
            avatar = new GameObject("Avatar");
            var child = new GameObject("Inactive Setup Marker");
            child.transform.SetParent(avatar.transform);
            child.AddComponent<bVrcFurySetup>();
            child.SetActive(false);

            Assert.That(
                bAvatarUploadDiagnostics.ClassifyTarget(avatar),
                Is.EqualTo(bAvatarUploadTargetStatus.Configured));
        }

        [Test]
        public void SetupMarker_IsEditorOnlyAndAllowedByAvatarValidation()
        {
            avatar = new GameObject("Avatar");
            bVrcFurySetup setup = avatar.AddComponent<bVrcFurySetup>();

            Assert.That(setup.GetType().GetInterface("VRC.SDKBase.IEditorOnly"), Is.Not.Null);
            foreach (Component illegalComponent in VRC.SDK3.Validation.AvatarValidation.FindIllegalComponents(avatar))
                Assert.That(illegalComponent, Is.Not.SameAs(setup));
        }

        [TestCase("", true)]
        [TestCase("  ", true)]
        [TestCase("GlobalObjectId_V1-0-00000000000000000000000000000000-0-0", true)]
        [TestCase("GlobalObjectId_V1-2-00000000000000000000000000000000-12345-0", true)]
        [TestCase("legacy-scene-avatar", false)]
        public void NeedsNewAssetKey_RejectsNullGlobalObjectIds(string assetKey, bool expected)
        {
            avatar = new GameObject("Asset Key Owner");
            bHapticsOSCIntegration integration = avatar.AddComponent<bHapticsOSCIntegration>();
            System.Reflection.MethodInfo needsNewAssetKey = typeof(bHapticsOSCIntegration).GetMethod(
                "NeedsNewAssetKey",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(needsNewAssetKey, Is.Not.Null);
            Assert.That(needsNewAssetKey.Invoke(integration, new object[] { assetKey }), Is.EqualTo(expected));
        }

        [Test]
        public void ClassifyTarget_IntegrationTakesPriorityOverSetupMarker()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<bHapticsOSCIntegration>();
            avatar.AddComponent<bVrcFurySetup>();

            Assert.That(
                bAvatarUploadDiagnostics.ClassifyTarget(avatar),
                Is.EqualTo(bAvatarUploadTargetStatus.Incomplete));
        }

        [TestCase((int)bCompanionStatus.UnsupportedPlatform, false)]
        [TestCase((int)bCompanionStatus.NotLocated, true)]
        [TestCase((int)bCompanionStatus.MissingPath, true)]
        [TestCase((int)bCompanionStatus.InvalidProduct, true)]
        [TestCase((int)bCompanionStatus.UnknownVersion, true)]
        [TestCase((int)bCompanionStatus.Outdated, true)]
        [TestCase((int)bCompanionStatus.ReadyStopped, false)]
        [TestCase((int)bCompanionStatus.ReadyRunning, false)]
        public void ShouldWarnCompanionStatus_OnlyWarnsForActionableFailures(
            int statusValue,
            bool expected)
        {
            Assert.That(
                bAvatarUploadDiagnostics.ShouldWarnCompanionStatus((bCompanionStatus)statusValue),
                Is.EqualTo(expected));
        }

        [TestCase(false, true, (int)bAvatarUploadTargetStatus.Configured, true)]
        [TestCase(true, true, (int)bAvatarUploadTargetStatus.Configured, false)]
        [TestCase(false, false, (int)bAvatarUploadTargetStatus.Configured, false)]
        [TestCase(false, true, (int)bAvatarUploadTargetStatus.Incomplete, false)]
        [TestCase(false, true, (int)bAvatarUploadTargetStatus.None, false)]
        public void ShouldRunCompanionDiagnostics_RequiresInteractiveWindowsConfiguredAvatar(
            bool isBatchMode,
            bool isWindowsEditor,
            int targetStatusValue,
            bool expected)
        {
            Assert.That(
                bAvatarUploadDiagnostics.ShouldRunCompanionDiagnostics(
                    isBatchMode,
                    isWindowsEditor,
                    (bAvatarUploadTargetStatus)targetStatusValue),
                Is.EqualTo(expected));
        }
    }

    public class bIntegrationAssetKeyTests
    {
        private string duplicatePrefabPath;
        private string sourcePrefabPath;

        [Test]
        public void EnsureUniqueAssetKey_CopiedUnsavedKey_RegeneratesOnlyOnce()
        {
            string copiedKey = System.Guid.NewGuid().ToString("N");
            Assert.That(SceneManager.GetActiveScene().path, Is.Empty);
            GameObject firstObject = null;
            GameObject duplicateObject = null;
            try
            {
                firstObject = new GameObject("First Asset Key Owner");
                duplicateObject = new GameObject("Duplicate Asset Key Owner");
                bHapticsOSCIntegration first = firstObject.AddComponent<bHapticsOSCIntegration>();
                bHapticsOSCIntegration duplicate =
                    duplicateObject.AddComponent<bHapticsOSCIntegration>();
                first.assetKey = copiedKey;
                duplicate.assetKey = copiedKey;

                duplicate.EnsureUniqueAssetKey();
                string repairedKey = duplicate.assetKey;
                duplicate.EnsureUniqueAssetKey();

                Assert.That(repairedKey, Is.Not.EqualTo(copiedKey));
                Assert.That(duplicate.assetKey, Is.EqualTo(repairedKey));
                Assert.That(duplicate.assetKey, Is.Not.EqualTo(first.assetKey));
            }
            finally
            {
                if (duplicateObject != null)
                    Object.DestroyImmediate(duplicateObject);
                if (firstObject != null)
                    Object.DestroyImmediate(firstObject);
            }
        }

        [Test]
        public void PrepareGeneratedFolder_ClaimedFallbackKey_PreservesExistingSetup()
        {
            string copiedKey = System.Guid.NewGuid().ToString("N");
            string oldFolder =
                $"{bHapticsOSCIntegration.GeneratedAssetsRoot}/Duplicate Avatar_{copiedKey}";
            string newFolder = null;
            string legacyRoot = bHapticsOSCIntegration.GeneratedAssetsRoot.Substring(
                0,
                bHapticsOSCIntegration.GeneratedAssetsRoot.LastIndexOf('/'));
            bool legacyRootExisted = AssetDatabase.IsValidFolder(legacyRoot);
            bool generatedRootExisted = AssetDatabase.IsValidFolder(
                bHapticsOSCIntegration.GeneratedAssetsRoot);
            Assert.That(SceneManager.GetActiveScene().path, Is.Empty);
            GameObject setupObject = null;
            GameObject duplicateObject = null;
            try
            {
                EnsureAssetFolder(oldFolder);
                setupObject = new GameObject("Existing Generated Setup");
                setupObject.AddComponent<bVrcFurySetup>().Configure(oldFolder);

                duplicateObject = new GameObject("Duplicate Avatar");
                bHapticsOSCIntegration duplicate =
                    duplicateObject.AddComponent<bHapticsOSCIntegration>();
                duplicate.assetKey = copiedKey;
                System.Reflection.MethodInfo prepareGeneratedFolder = typeof(bAnimator).GetMethod(
                    "PrepareGeneratedFolder",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.That(prepareGeneratedFolder, Is.Not.Null);

                newFolder = (string)prepareGeneratedFolder.Invoke(null, new object[] { duplicate });

                Assert.That(newFolder, Is.Not.EqualTo(oldFolder));
                Assert.That(duplicate.assetKey, Is.Not.EqualTo(copiedKey));
                Assert.That(AssetDatabase.IsValidFolder(oldFolder), Is.True);
                Assert.That(AssetDatabase.IsValidFolder(newFolder), Is.True);
            }
            finally
            {
                if (duplicateObject != null)
                    Object.DestroyImmediate(duplicateObject);
                if (setupObject != null)
                    Object.DestroyImmediate(setupObject);
                if (!string.IsNullOrEmpty(newFolder))
                    AssetDatabase.DeleteAsset(newFolder);
                AssetDatabase.DeleteAsset(oldFolder);
                if (!generatedRootExisted
                    && AssetDatabase.IsValidFolder(bHapticsOSCIntegration.GeneratedAssetsRoot)
                    && AssetDatabase.FindAssets(
                        string.Empty,
                        new[] { bHapticsOSCIntegration.GeneratedAssetsRoot }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(bHapticsOSCIntegration.GeneratedAssetsRoot);
                }
                if (!legacyRootExisted
                    && AssetDatabase.IsValidFolder(legacyRoot)
                    && AssetDatabase.FindAssets(string.Empty, new[] { legacyRoot }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(legacyRoot);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(duplicatePrefabPath))
                AssetDatabase.DeleteAsset(duplicatePrefabPath);
            if (!string.IsNullOrEmpty(sourcePrefabPath))
                AssetDatabase.DeleteAsset(sourcePrefabPath);
        }

        [Test]
        public void Validate_CopiedPrefabKey_RebindsToDuplicateOwner()
        {
            string suffix = System.Guid.NewGuid().ToString("N");
            sourcePrefabPath = $"Assets/__bHapticsOSC_asset_key_source_{suffix}.prefab";
            duplicatePrefabPath = $"Assets/__bHapticsOSC_asset_key_duplicate_{suffix}.prefab";

            var sourceObject = new GameObject("Asset Key Avatar");
            sourceObject.AddComponent<Animator>();
            System.Type avatarDescriptorType = System.Type.GetType(
                "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor, VRCSDK3A");
            Assert.That(avatarDescriptorType, Is.Not.Null);
            sourceObject.AddComponent(avatarDescriptorType);
            sourceObject.AddComponent<bHapticsOSCIntegration>();
            PrefabUtility.SaveAsPrefabAsset(sourceObject, sourcePrefabPath);
            Object.DestroyImmediate(sourceObject);

            GameObject sourceContents = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
            try
            {
                bHapticsOSCIntegration sourceIntegration =
                    sourceContents.GetComponent<bHapticsOSCIntegration>();
                sourceIntegration.assetKey = GlobalObjectId.GetGlobalObjectIdSlow(sourceContents).ToString();
                PrefabUtility.SaveAsPrefabAsset(sourceContents, sourcePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(sourceContents);
            }

            Assert.That(AssetDatabase.CopyAsset(sourcePrefabPath, duplicatePrefabPath), Is.True);
            GameObject duplicateContents = PrefabUtility.LoadPrefabContents(duplicatePrefabPath);
            try
            {
                bHapticsOSCIntegration duplicateIntegration =
                    duplicateContents.GetComponent<bHapticsOSCIntegration>();
                string copiedKey = duplicateIntegration.assetKey;
                string duplicateOwnerKey = GlobalObjectId.GetGlobalObjectIdSlow(duplicateContents).ToString();
                Assert.That(copiedKey, Is.Not.EqualTo(duplicateOwnerKey));

                duplicateIntegration.Validate();
                string repairedKey = duplicateIntegration.assetKey;
                duplicateIntegration.Validate();

                Assert.That(repairedKey, Is.EqualTo(duplicateOwnerKey));
                Assert.That(duplicateIntegration.assetKey, Is.EqualTo(repairedKey));
                Assert.That(duplicateIntegration.assetKey, Is.Not.EqualTo(copiedKey));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(duplicateContents);
            }
        }


        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }

    public class bVrcFurySetupCleanupTests
    {
        private string duplicateScenePath;
        private string generatedAssetPath;
        private string generatedFolderPath;
        private bool generatedRootExistedBeforeTest;
        private string hostScenePath;
        private bool legacyRootExistedBeforeTest;
        private string legacyRootPath;
        private string prefabPath;
        private string scenePath;
        private string secondaryGeneratedAssetPath;
        private string secondaryGeneratedFolderPath;
        private PrefabStage testPrefabStage;
        private Scene testScene;
        private string unrelatedAssetPath;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PrefabStage currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (IsTestPrefabStage(currentPrefabStage))
            {
                if (currentPrefabStage.scene.isDirty)
                    SavePrefabStage(currentPrefabStage);
                StageUtility.GoToMainStage();
                for (int frame = 0; frame < 120 && IsTestPrefabStage(
                         PrefabStageUtility.GetCurrentPrefabStage()); frame++)
                {
                    yield return null;
                }
            }

            if (testScene.IsValid() && testScene.isLoaded)
                EditorSceneManager.CloseScene(testScene, true);

            if (!string.IsNullOrEmpty(hostScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(scenePath))
                AssetDatabase.DeleteAsset(scenePath);
            if (!string.IsNullOrEmpty(duplicateScenePath))
                AssetDatabase.DeleteAsset(duplicateScenePath);
            if (!string.IsNullOrEmpty(hostScenePath))
                AssetDatabase.DeleteAsset(hostScenePath);
            if (!string.IsNullOrEmpty(prefabPath))
                AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(unrelatedAssetPath))
                AssetDatabase.DeleteAsset(unrelatedAssetPath);
            if (!string.IsNullOrEmpty(generatedFolderPath))
                AssetDatabase.DeleteAsset(generatedFolderPath);
            if (!string.IsNullOrEmpty(secondaryGeneratedFolderPath))
                AssetDatabase.DeleteAsset(secondaryGeneratedFolderPath);

            string generatedRoot = bHapticsOSCIntegration.GeneratedAssetsRoot;
            if (!generatedRootExistedBeforeTest
                && AssetDatabase.IsValidFolder(generatedRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { generatedRoot }).Length == 0)
            {
                AssetDatabase.DeleteAsset(generatedRoot);
            }

            if (!legacyRootExistedBeforeTest
                && !string.IsNullOrEmpty(legacyRootPath)
                && AssetDatabase.IsValidFolder(legacyRootPath)
                && AssetDatabase.FindAssets(string.Empty, new[] { legacyRootPath }).Length == 0)
            {
                AssetDatabase.DeleteAsset(legacyRootPath);
            }

            AssetDatabase.Refresh();
        }

        [UnityTest]
        public IEnumerator ClosingIntactSavedScene_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out _);

            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator DeletingSetupThenClosingWithoutSave_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            Undo.DestroyObjectImmediate(setupObject);

            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator DeletingThenUndoingAndSaving_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            Undo.IncrementCurrentGroup();
            Undo.DestroyObjectImmediate(setupObject);
            Undo.PerformUndo();
            yield return null;

            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator DeletingSavingAndClosing_RemovesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            Undo.DestroyObjectImmediate(setupObject);

            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.False);
        }

        [UnityTest]
        public IEnumerator Cleanup_DoesNotSaveUnrelatedDirtyAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            Undo.DestroyObjectImmediate(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);

            var unrelatedClip = new AnimationClip();
            AssetDatabase.CreateAsset(unrelatedClip, unrelatedAssetPath);
            AssetDatabase.SaveAssets();
            unrelatedClip.wrapMode = WrapMode.Loop;
            EditorUtility.SetDirty(unrelatedClip);
            Assert.That(EditorUtility.IsDirty(unrelatedClip), Is.True);

            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.False);
            Assert.That(EditorUtility.IsDirty(unrelatedClip), Is.True);
        }

        [UnityTest]
        public IEnumerator BatchedCleanup_PreservesReferencedFolderAndDeletesUnreferencedFolder()
        {
            CreateConfiguredSetup(out GameObject referencedSetupObject);
            EnsureAssetFolder(secondaryGeneratedFolderPath);
            var unreferencedClip = new AnimationClip { legacy = true };
            AssetDatabase.CreateAsset(unreferencedClip, secondaryGeneratedAssetPath);
            var unreferencedSetupObject = new GameObject("Unreferenced generated setup");
            unreferencedSetupObject.AddComponent<bVrcFurySetup>()
                .Configure(secondaryGeneratedFolderPath);
            var setupAnimation = unreferencedSetupObject.AddComponent<Animation>();
            setupAnimation.AddClip(unreferencedClip, "Generated");
            setupAnimation.clip = unreferencedClip;
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);

            AnimationClip referencedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedAssetPath);
            Scene hostScene = SceneManager.GetSceneByPath(hostScenePath);
            var referenceObject = new GameObject("Persisted generated reference");
            SceneManager.MoveGameObjectToScene(referenceObject, hostScene);
            var referenceAnimation = referenceObject.AddComponent<Animation>();
            referenceAnimation.AddClip(referencedClip, "Generated");
            referenceAnimation.clip = referencedClip;
            Assert.That(EditorSceneManager.SaveScene(hostScene), Is.True);

            Undo.DestroyObjectImmediate(referencedSetupObject);
            Undo.DestroyObjectImmediate(unreferencedSetupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(secondaryGeneratedFolderPath), Is.False);
        }

        [UnityTest]
        public IEnumerator BatchedCleanup_FollowsTransitiveReferencesAcrossGeneratedFolders()
        {
            CreateConfiguredSetup(out GameObject firstSetupObject);
            EnsureAssetFolder(secondaryGeneratedFolderPath);
            string texturePath = $"{secondaryGeneratedFolderPath}/transitive.asset";
            var generatedTexture = new Texture2D(1, 1);
            AssetDatabase.CreateAsset(generatedTexture, texturePath);
            var secondSetupObject = new GameObject("Transitive generated setup");
            secondSetupObject.AddComponent<bVrcFurySetup>()
                .Configure(secondaryGeneratedFolderPath);

            Shader standardShader = Shader.Find("Standard");
            Assert.That(standardShader, Is.Not.Null);
            var generatedMaterial = new Material(standardShader) { mainTexture = generatedTexture };
            string materialPath = $"{generatedFolderPath}/transitive.mat";
            AssetDatabase.CreateAsset(generatedMaterial, materialPath);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);

            Scene hostScene = SceneManager.GetSceneByPath(hostScenePath);
            var referenceObject = new GameObject("Transitive generated reference");
            SceneManager.MoveGameObjectToScene(referenceObject, hostScene);
            referenceObject.AddComponent<MeshRenderer>().sharedMaterial = generatedMaterial;
            Assert.That(EditorSceneManager.SaveScene(hostScene), Is.True);

            Undo.DestroyObjectImmediate(firstSetupObject);
            Undo.DestroyObjectImmediate(secondSetupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(secondaryGeneratedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator BatchedCleanup_ProtectsReferencedNestedFolderAndItsParent()
        {
            CreateConfiguredSetup(out GameObject parentSetupObject);
            secondaryGeneratedFolderPath = $"{generatedFolderPath}/Nested";
            secondaryGeneratedAssetPath = $"{secondaryGeneratedFolderPath}/generated.anim";
            EnsureAssetFolder(secondaryGeneratedFolderPath);
            var nestedClip = new AnimationClip { legacy = true };
            AssetDatabase.CreateAsset(nestedClip, secondaryGeneratedAssetPath);
            var nestedSetupObject = new GameObject("Nested generated setup");
            nestedSetupObject.AddComponent<bVrcFurySetup>()
                .Configure(secondaryGeneratedFolderPath);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);

            Scene hostScene = SceneManager.GetSceneByPath(hostScenePath);
            var referenceObject = new GameObject("Nested generated reference");
            SceneManager.MoveGameObjectToScene(referenceObject, hostScene);
            var animation = referenceObject.AddComponent<Animation>();
            animation.AddClip(nestedClip, "Generated");
            animation.clip = nestedClip;
            Assert.That(EditorSceneManager.SaveScene(hostScene), Is.True);

            Undo.DestroyObjectImmediate(parentSetupObject);
            Undo.DestroyObjectImmediate(nestedSetupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(secondaryGeneratedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator SerializedParentSetup_PreservesNestedGeneratedFolder()
        {
            CreateConfiguredSetup(out _);
            secondaryGeneratedFolderPath = $"{generatedFolderPath}/Nested";
            secondaryGeneratedAssetPath = $"{secondaryGeneratedFolderPath}/generated.anim";
            EnsureAssetFolder(secondaryGeneratedFolderPath);
            var nestedClip = new AnimationClip { legacy = true };
            AssetDatabase.CreateAsset(nestedClip, secondaryGeneratedAssetPath);
            var nestedSetupObject = new GameObject("Nested generated setup");
            nestedSetupObject.AddComponent<bVrcFurySetup>()
                .Configure(secondaryGeneratedFolderPath);
            var animation = nestedSetupObject.AddComponent<Animation>();
            animation.AddClip(nestedClip, "Generated");
            animation.clip = nestedClip;
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);

            Undo.DestroyObjectImmediate(nestedSetupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(secondaryGeneratedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator SavingDeletedSetupAsCopy_DoesNotConfirmCleanupOfOriginalScene()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            string originalScenePath = testScene.path;
            Undo.DestroyObjectImmediate(setupObject);

            Assert.That(
                EditorSceneManager.SaveScene(testScene, duplicateScenePath, true),
                Is.True);
            Assert.That(testScene.path, Is.EqualTo(originalScenePath));
            Assert.That(GetConfirmedFolderCount(testScene.handle), Is.Zero);

            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator UnopenedDuplicateScene_PreservesSharedGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            Assert.That(AssetDatabase.CopyAsset(scenePath, duplicateScenePath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(generatedAssetPath), Is.True);
            AssetDatabase.Refresh();

            Undo.DestroyObjectImmediate(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator UnopenedSceneAssetReferenceWithoutSetup_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            AnimationClip generatedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedAssetPath);
            Assert.That(generatedClip, Is.Not.Null);

            Scene referenceScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            var referenceObject = new GameObject("Generated asset reference");
            var animation = referenceObject.AddComponent<Animation>();
            animation.AddClip(generatedClip, "Generated");
            animation.clip = generatedClip;
            Assert.That(EditorSceneManager.SaveScene(referenceScene, duplicateScenePath), Is.True);
            EditorSceneManager.CloseScene(referenceScene, true);
            EditorSceneManager.SetActiveScene(testScene);

            Undo.DestroyObjectImmediate(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator UnsavedLoadedSceneAssetReference_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            AnimationClip generatedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedAssetPath);
            Assert.That(generatedClip, Is.Not.Null);

            Scene hostScene = SceneManager.GetSceneByPath(hostScenePath);
            Assert.That(hostScene.IsValid() && hostScene.isLoaded, Is.True);
            var referenceObject = new GameObject("Unsaved generated asset reference");
            SceneManager.MoveGameObjectToScene(referenceObject, hostScene);
            var animation = referenceObject.AddComponent<Animation>();
            animation.AddClip(generatedClip, "Generated");
            animation.clip = generatedClip;
            EditorSceneManager.MarkSceneDirty(hostScene);
            Assert.That(hostScene.isDirty, Is.True);

            Undo.DestroyObjectImmediate(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator UnsavedPrefabStageAssetReference_PreservesGeneratedAssets()
        {
            CreateConfiguredSetup(out GameObject setupObject);
            AnimationClip generatedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedAssetPath);
            Assert.That(generatedClip, Is.Not.Null);

            var prefabObject = new GameObject("Unsaved reference prefab");
            PrefabUtility.SaveAsPrefabAsset(prefabObject, prefabPath);
            Object.DestroyImmediate(prefabObject);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(stage, Is.Not.Null);
            testPrefabStage = stage;
            var animation = stage.prefabContentsRoot.AddComponent<Animation>();
            animation.AddClip(generatedClip, "Generated");
            animation.clip = generatedClip;
            EditorSceneManager.MarkSceneDirty(stage.scene);
            Assert.That(stage.scene.isDirty, Is.True);

            Undo.DestroyObjectImmediate(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
            EditorSceneManager.CloseScene(testScene, true);
            yield return null;
            yield return null;

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator ClosingIntactSavedPrefabStage_PreservesGeneratedAssets()
        {
            CreateConfiguredPrefabStage(out _);

            yield return CloseTestPrefabStage();

            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.True);
        }

        [UnityTest]
        public IEnumerator DeletingAndSavingPrefabStage_ConfirmsCleanupForClose()
        {
            CreateConfiguredPrefabStage(out GameObject setupObject);
            yield return null;
            Undo.DestroyObjectImmediate(setupObject);

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            Assert.That(stage, Is.Not.Null);
            SavePrefabStage(stage);

            Assert.That(GetConfirmedFolderCount(stage.scene.handle), Is.EqualTo(1));
            Assert.That(GetPendingPrefabStageFlag(stage.scene.handle), Is.True);
            Assert.That(IsPrefabStageMonitored(stage.scene.handle), Is.True);
            int stageSceneHandle = stage.scene.handle;
            yield return CloseTestPrefabStage();

            Assert.That(GetPendingSceneCleanup(stageSceneHandle), Is.Null,
                "Confirmed cleanup remained pending after the Prefab Stage closed.");
            Assert.That(AssetDatabase.IsValidFolder(generatedFolderPath), Is.False);
        }

        private void CreateConfiguredSetup(out GameObject setupObject)
        {
            InitializeTestPaths();

            Scene hostScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(hostScene, hostScenePath), Is.True);
            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(testScene);
            setupObject = new GameObject("bHapticsOSC VRCFury");
            var setup = setupObject.AddComponent<bVrcFurySetup>();

            Assert.That(EditorSceneManager.SaveScene(testScene, scenePath), Is.True);
            setup.Configure(generatedFolderPath);
            CreateGeneratedAssetReference(setupObject);
            Assert.That(EditorSceneManager.SaveScene(testScene), Is.True);
        }

        private void CreateConfiguredPrefabStage(out GameObject setupObject)
        {
            InitializeTestPaths();

            Scene hostScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(hostScene, hostScenePath), Is.True);

            var avatar = new GameObject("Avatar");
            PrefabUtility.SaveAsPrefabAsset(avatar, prefabPath);
            Object.DestroyImmediate(avatar);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(stage, Is.Not.Null);
            setupObject = new GameObject("bHapticsOSC VRCFury");
            setupObject.transform.SetParent(stage.prefabContentsRoot.transform);
            var setup = setupObject.AddComponent<bVrcFurySetup>();
            setup.Configure(generatedFolderPath);
            CreateGeneratedAssetReference(setupObject);
            SavePrefabStage(stage);
            testPrefabStage = stage;
        }

        private static void SavePrefabStage(PrefabStage stage)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo savePrefab = typeof(PrefabStage).GetMethod(
                "Save",
                flags,
                null,
                System.Type.EmptyTypes,
                null);

            Assert.That(savePrefab, Is.Not.Null, "Unity's stage save method was not found.");
            Assert.That(savePrefab.Invoke(stage, null), Is.EqualTo(true));
        }

        private IEnumerator CloseTestPrefabStage()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            Assert.That(IsTestPrefabStage(stage), Is.True);
            int sceneHandle = stage.scene.handle;
            StageUtility.GoToMainStage();
            for (int frame = 0; frame < 240; frame++)
            {
                bool stageIsOpen = IsTestPrefabStage(PrefabStageUtility.GetCurrentPrefabStage());
                bool cleanupIsPending = GetPendingSceneCleanup(sceneHandle) != null;
                if (!stageIsOpen && !cleanupIsPending)
                    break;

                yield return null;
            }

            Assert.That(IsTestPrefabStage(PrefabStageUtility.GetCurrentPrefabStage()), Is.False,
                "The test-created Prefab Stage did not close within 240 editor frames.");
            Assert.That(GetPendingSceneCleanup(sceneHandle), Is.Null,
                "Prefab cleanup did not finish within 240 editor frames.");
            yield return null;
            yield return null;
            yield return null;
        }

        private bool IsTestPrefabStage(PrefabStage stage)
            => stage != null
               && (ReferenceEquals(stage, testPrefabStage)
                   || (!string.IsNullOrEmpty(prefabPath) && stage.assetPath == prefabPath));

        private static object GetPendingSceneCleanup(int sceneHandle)
        {
            System.Type cleanupType = typeof(bVrcFurySetup).Assembly.GetType(
                "bHapticsOSC.VRChat.bVrcFurySetupCleanup");
            System.Reflection.FieldInfo pendingByScene = cleanupType?.GetField(
                "PendingByScene",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(pendingByScene, Is.Not.Null);

            var pending = (System.Collections.IDictionary)pendingByScene.GetValue(null);
            return pending.Contains(sceneHandle) ? pending[sceneHandle] : null;
        }

        private static int GetConfirmedFolderCount(int sceneHandle)
        {
            object pendingCleanup = GetPendingSceneCleanup(sceneHandle);
            Assert.That(pendingCleanup, Is.Not.Null);
            System.Reflection.FieldInfo confirmedBySave = pendingCleanup.GetType().GetField(
                "ConfirmedBySave",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            Assert.That(confirmedBySave, Is.Not.Null);
            object confirmedFolders = confirmedBySave.GetValue(pendingCleanup);
            System.Reflection.PropertyInfo confirmedCount = confirmedFolders.GetType().GetProperty("Count");
            Assert.That(confirmedCount, Is.Not.Null);
            return (int)confirmedCount.GetValue(confirmedFolders);
        }

        private static bool GetPendingPrefabStageFlag(int sceneHandle)
        {
            object pendingCleanup = GetPendingSceneCleanup(sceneHandle);
            Assert.That(pendingCleanup, Is.Not.Null);
            System.Reflection.FieldInfo isPrefabStage = pendingCleanup.GetType().GetField(
                "IsPrefabStage",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            Assert.That(isPrefabStage, Is.Not.Null);
            return (bool)isPrefabStage.GetValue(pendingCleanup);
        }

        private static bool IsPrefabStageMonitored(int sceneHandle)
        {
            System.Type cleanupType = typeof(bVrcFurySetup).Assembly.GetType(
                "bHapticsOSC.VRChat.bVrcFurySetupCleanup");
            System.Reflection.FieldInfo monitoredScenes = cleanupType?.GetField(
                "MonitoredPrefabScenes",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(monitoredScenes, Is.Not.Null);
            object monitored = monitoredScenes.GetValue(null);
            System.Reflection.MethodInfo contains = monitored.GetType().GetMethod("Contains");
            Assert.That(contains, Is.Not.Null);
            return (bool)contains.Invoke(monitored, new object[] { sceneHandle });
        }

        private void InitializeTestPaths()
        {
            string suffix = System.Guid.NewGuid().ToString("N");
            duplicateScenePath = $"Assets/__bHapticsOSC_cleanup_copy_{suffix}.unity";
            generatedFolderPath = $"{bHapticsOSCIntegration.GeneratedAssetsRoot}/__cleanup_test_{suffix}";
            generatedAssetPath = $"{generatedFolderPath}/generated.anim";
            hostScenePath = $"Assets/__bHapticsOSC_cleanup_host_{suffix}.unity";
            prefabPath = $"Assets/__bHapticsOSC_cleanup_test_{suffix}.prefab";
            scenePath = $"Assets/__bHapticsOSC_cleanup_test_{suffix}.unity";
            secondaryGeneratedFolderPath =
                $"{bHapticsOSCIntegration.GeneratedAssetsRoot}/__cleanup_secondary_{suffix}";
            secondaryGeneratedAssetPath = $"{secondaryGeneratedFolderPath}/generated.anim";
            unrelatedAssetPath = $"Assets/__bHapticsOSC_unrelated_{suffix}.anim";
            legacyRootPath = bHapticsOSCIntegration.GeneratedAssetsRoot.Substring(
                0,
                bHapticsOSCIntegration.GeneratedAssetsRoot.LastIndexOf('/'));
            legacyRootExistedBeforeTest = AssetDatabase.IsValidFolder(legacyRootPath);
            generatedRootExistedBeforeTest = AssetDatabase.IsValidFolder(
                bHapticsOSCIntegration.GeneratedAssetsRoot);
            EnsureAssetFolder(generatedFolderPath);
        }

        private void CreateGeneratedAssetReference(GameObject owner)
        {
            var generatedClip = new AnimationClip { legacy = true };
            AssetDatabase.CreateAsset(generatedClip, generatedAssetPath);
            var animation = owner.AddComponent<Animation>();
            animation.AddClip(generatedClip, "Generated");
            animation.clip = generatedClip;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
#endif
