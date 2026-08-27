#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using System.Collections.Generic;
using System.Linq;

namespace bHapticsOSC.VRChat
{
    [AddComponentMenu("bHapticsOSC Integration")]
    [ExecuteInEditMode]
    [System.Serializable]
    public class bHapticsOSCIntegration : MonoBehaviour
    {
        public static string SystemName = "bHapticsOSC";
        public const string VrcFuryRootName = "bHapticsOSC VRCFury";
        public const string GeneratedAssetsRoot = "Assets/bHapticsOSC/VRChat/Generated";

        [SerializeField]
        public VRCAvatarDescriptor avatar;
        [SerializeField]
        public Animator avatarAnimator;
        [SerializeField]
        public string assetKey;

        [SerializeField]
        public bDeviceType CurrentDevice = bDeviceType.VEST;

        [Tooltip("Replace the per-motor contact receivers with a positional encoder at build time. "
                 + "Far fewer contacts, and the touch position is continuous rather than one motor at a time.")]
        public bool ConsolidateContacts = false;
        [SerializeField]
        public Dictionary<bDeviceTemplate, bUserSettings> AllUserSettings;
        [SerializeField]
        public Dictionary<bUserSettings, bReorderableListContainer<string>> AllCustomContactTagsContainers;

        //private static int AudioLinkCost = 8;
        //[SerializeField]
        //public bool AudioLink = false;

        /// <summary>
        /// Why this component cannot be used where it currently sits.
        /// </summary>
        public enum bSetupProblem
        {
            Ok,

            /// <summary>Not on an avatar root - there is no VRC Avatar Descriptor here.</summary>
            NoAvatarDescriptor,

            /// <summary>The avatar has no Animator, so no bones to attach devices to.</summary>
            NoAnimator,

            /// <summary>Another bHapticsOSC Integration already exists under this object.</summary>
            DuplicateComponent,
        }

        /// <summary>
        /// Caches the avatar references and reports what is wrong, without changing anything.
        ///
        /// This deliberately does not destroy the component or log. It used to do both, from
        /// inside OnInspectorGUI: dropping the component on the wrong object made it vanish with
        /// only a console line to explain, which reads as a broken package rather than as a
        /// misplaced component. The inspector now says what is wrong and offers to fix it.
        /// </summary>
        public bSetupProblem TryValidate()
        {
            avatar = gameObject.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null)
                return bSetupProblem.NoAvatarDescriptor;

            if (gameObject.GetComponentsInChildren<bHapticsOSCIntegration>(true).Length > 1)
                return bSetupProblem.DuplicateComponent;

            avatarAnimator = gameObject.GetComponent<Animator>();
            if (avatarAnimator == null)
                return bSetupProblem.NoAnimator;

            EnsureUniqueAssetKey();
            return bSetupProblem.Ok;
        }

        /// <summary>Caches the avatar references, ignoring any problem. Kept for existing callers.</summary>
        public void Validate() => TryValidate();

        /// <summary>The nearest ancestor that could host this component, or null if there is none.</summary>
        public GameObject FindAvatarRoot()
        {
            for (Transform current = transform.parent; current != null; current = current.parent)
            {
                if (current.GetComponent<VRCAvatarDescriptor>() != null)
                    return current.gameObject;
            }

            return null;
        }

        public void EnsureUniqueAssetKey()
        {
            if (!NeedsNewAssetKey(assetKey))
                return;

            SetAssetKey(CreateStableAssetKey());
        }

        public void AssignFreshFallbackAssetKey()
        {
            SetAssetKey(System.Guid.NewGuid().ToString("N"));
        }

        private void SetAssetKey(string value)
        {
            Undo.RecordObject(this, $"[{SystemName}] Repair Generated Asset Ownership");
            assetKey = value;
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        private string CreateStableAssetKey()
        {
            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (HasStableGlobalObjectId(globalId))
                return globalId.ToString();

            return System.Guid.NewGuid().ToString("N");
        }

        private bool NeedsNewAssetKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            GlobalObjectId currentGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (HasStableGlobalObjectId(currentGlobalId))
            {
                return !GlobalObjectId.TryParse(value, out GlobalObjectId storedGlobalId)
                       || !HasStableGlobalObjectId(storedGlobalId)
                       || !storedGlobalId.ToString().Equals(
                           currentGlobalId.ToString(),
                           System.StringComparison.Ordinal);
            }

            if (GlobalObjectId.TryParse(value, out _)
                || value.StartsWith("GlobalObjectId_V1-", System.StringComparison.Ordinal))
            {
                return true;
            }

            foreach (bHapticsOSCIntegration integration in
                     Resources.FindObjectsOfTypeAll<bHapticsOSCIntegration>())
            {
                if (integration != null
                    && integration != this
                    && value.Equals(integration.assetKey, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStableGlobalObjectId(GlobalObjectId globalId)
        {
            return globalId.identifierType != 0
                   && !globalId.assetGUID.Empty()
                   && globalId.targetObjectId != 0;
        }

        public Transform GetOrCreateVrcFuryRoot(bool registerUndo = false)
        {
            Transform existing = transform.Find(VrcFuryRootName);
            if (existing != null)
            {
                EnsureVrcFurySetup(existing, registerUndo);
                return existing;
            }

            GameObject root = new GameObject(VrcFuryRootName);
            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(root, $"[{SystemName}] Created VRCFury Root");
                Undo.SetTransformParent(root.transform, transform, $"[{SystemName}] Created VRCFury Root");
            }
            else
            {
                root.transform.SetParent(transform, false);
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            EnsureVrcFurySetup(root.transform, registerUndo);
            return root.transform;
        }

        public bVrcFurySetup ConfigureVrcFurySetup(string generatedAssetFolderPath, bool registerUndo = false)
        {
            Transform root = GetOrCreateVrcFuryRoot(registerUndo);
            bVrcFurySetup setup = EnsureVrcFurySetup(root, registerUndo);
            setup.Configure(generatedAssetFolderPath);
            return setup;
        }

        private static bVrcFurySetup EnsureVrcFurySetup(Transform root, bool registerUndo)
        {
            bVrcFurySetup setup = root.GetComponent<bVrcFurySetup>();
            if (setup != null)
                return setup;

            return registerUndo
                ? Undo.AddComponent<bVrcFurySetup>(root.gameObject)
                : root.gameObject.AddComponent<bVrcFurySetup>();
        }

        public bool IsReadyToApply()
        {
            //if (AudioLink)
            //    return true;
            foreach (bUserSettings settings in AllUserSettings.Values)
                if (settings.CurrentPrefab != null)
                    return true;
            return false;
        }

        //public void ResetExtras()
        //{
        //}

        public void FindExistingPrefabs(Dictionary<bDeviceType, bDeviceTemplate> deviceTemplates)
        {
            for (int i = 0; i < deviceTemplates.Count; i++)
            {
                bDeviceTemplate template = deviceTemplates.Values.ElementAt(i);
                if (!template.HasBone)
                    continue;
                AllUserSettings[template].FindExistingPrefab(this, template);
            }
        }
    }
}
#endif
