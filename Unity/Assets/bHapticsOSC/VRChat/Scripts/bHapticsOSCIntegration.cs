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
        [SerializeField]
        public Dictionary<bDeviceTemplate, bUserSettings> AllUserSettings;
        [SerializeField]
        public Dictionary<bUserSettings, bReorderableListContainer<string>> AllCustomContactTagsContainers;

        //private static int AudioLinkCost = 8;
        //[SerializeField]
        //public bool AudioLink = false;

        public void Validate()
	    {
            avatar = gameObject.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null)
            {
                Debug.LogError("No VRCAvatarDescriptor Detected!");
                DestroyImmediate(this);
                return;
            }
            
            if (gameObject.GetComponentsInChildren<bHapticsOSCIntegration>(true).Length > 1)
            {
                Debug.LogError("Only 1 bHapticsOSC Integration component can be used at a time!");
                DestroyImmediate(this);
                return;
            }

            avatarAnimator = gameObject.GetComponent<Animator>();
            if (avatarAnimator == null)
            {
                Debug.LogError("Avatar must have an Animator!");
                DestroyImmediate(this);
                return;
            }

            if (string.IsNullOrEmpty(assetKey) || string.IsNullOrEmpty(assetKey.Trim()))
                assetKey = CreateStableAssetKey();
        }

        private string CreateStableAssetKey()
        {
            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
            if (!string.IsNullOrWhiteSpace(globalId) && !globalId.Contains("Null"))
                return globalId;

            return $"{gameObject.scene.name}_{gameObject.name}";
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
