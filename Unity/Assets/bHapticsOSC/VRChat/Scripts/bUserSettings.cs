#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    [System.Serializable]
    public class bUserSettings : ScriptableObject
    {
        [SerializeField] public HumanBodyBones Bone;
        [SerializeField] public bool ApplyParentConstraints = true;
        [SerializeField] public GameObject CurrentPrefab;
        [SerializeField] public List<string> CustomContactTags = new List<string>();

        [SerializeField] public Color TouchView_Default = new Color(0, 0, 0, 0);
        private Color touchView_Default = new Color(0, 0, 0, 0);
        [SerializeField] public Color TouchView_Triggered = new Color(0, 1, 1, 0.5f);
        private Color touchView_Triggered = new Color(0, 1, 1, 0.5f);

        [SerializeField] private bool _showMesh = true;
        [SerializeField] private bool _isMobile = false;
        public System.Action<bUserSettings> OnShowMeshChange;
        public System.Action<bUserSettings> OnIsMobileChange;

        public bool ShowMesh
        {
            get => _showMesh;
            set
            {
                if (_showMesh == value)
                    return;
                _showMesh = value;
                OnShowMeshChange?.Invoke(this);
            }
        }

        public bool IsMobile
        {
            get => _isMobile;
            set
            {
                if (_isMobile == value)
                {
                    return;
                }

                _isMobile = value;
                OnIsMobileChange?.Invoke(this);
            }
        }

        public void FindExistingPrefab(bHapticsOSCIntegration editorComp, bDeviceTemplate device)
        {
            if (CurrentPrefab != null)
                return;
            foreach (GameObject obj in (GameObject[])FindObjectsOfType(typeof(GameObject)))
            {
                if (!obj.transform.IsChildOf(editorComp.transform))
                    continue;

                if (!PrefabUtility.IsPartOfAnyPrefab(obj))
                    continue;

                Object objPrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(obj);
                if (!TryGetPrefabMode(objPrefab, device, out bool showMesh, out bool isMobile))
                    continue;

                _showMesh = showMesh;
                _isMobile = isMobile;
                //if (_showMesh)
                //    bShader.GetTouchViewColors(device.ShaderIndex, obj, ref TouchView_Default, ref TouchView_Triggered);

                CurrentPrefab = obj;
                CustomContactTags.Clear();
                bContacts.ScanForExistingTags(this);

                break;
            }
        }

        public void SwapPrefabs(bHapticsOSCIntegration editorComp, GameObject newPrefab, bool resetTransform = false)
        {
            if (newPrefab == null)
                return;

            if (CurrentPrefab != null)
                Undo.RecordObject(CurrentPrefab, $"[{bHapticsOSCIntegration.SystemName}] Swapped Prefabs");

            Transform stagingRoot = editorComp.GetOrCreateVrcFuryRoot(true);
            GameObject spawnedPrefab = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);

            Undo.RegisterCreatedObjectUndo(spawnedPrefab, $"[{bHapticsOSCIntegration.SystemName}] Swapped Prefabs");
            Undo.SetTransformParent(spawnedPrefab.transform, stagingRoot, $"[{bHapticsOSCIntegration.SystemName}] Swapped Prefabs");

            Vector3 localPosition = newPrefab.transform.localPosition;
            Vector3 localEulerAngles = newPrefab.transform.localEulerAngles;
            Vector3 localScale = newPrefab.transform.localScale;
            if (!resetTransform && (CurrentPrefab != null))
            {
                localPosition = GetBoneLocalPosition(editorComp.avatarAnimator);
                localEulerAngles = GetBoneLocalEulerAngles(editorComp.avatarAnimator);
                localScale = GetBoneLocalScale(editorComp.avatarAnimator);
            }

            ApplyBoneLocalTransform(editorComp.avatarAnimator, spawnedPrefab.transform, localPosition, localEulerAngles, localScale);

            string[] currentTags = CustomContactTags.ToArray();

            Color currentTouchViewDefault = TouchView_Default;
            Color currentTouchViewTriggered = TouchView_Triggered;

            if (CurrentPrefab != null)
                Undo.DestroyObjectImmediate(CurrentPrefab);

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            CustomContactTags.Clear();
            CustomContactTags.AddRange(currentTags);

            TouchView_Default = currentTouchViewDefault;
            TouchView_Triggered = currentTouchViewTriggered;

            CurrentPrefab = spawnedPrefab;
        }

        public void MoveToStagingRoot(bHapticsOSCIntegration editorComp, bool registerUndo)
        {
            if (CurrentPrefab == null)
                return;

            Transform stagingRoot = editorComp.GetOrCreateVrcFuryRoot(registerUndo);
            if (CurrentPrefab.transform.parent == stagingRoot)
                return;

            Vector3 worldPosition = CurrentPrefab.transform.position;
            Quaternion worldRotation = CurrentPrefab.transform.rotation;
            Vector3 worldScale = CurrentPrefab.transform.lossyScale;

            if (registerUndo)
            {
                Undo.SetTransformParent(CurrentPrefab.transform, stagingRoot, $"[{bHapticsOSCIntegration.SystemName}] Moved Device to VRCFury Root");
            }
            else
            {
                CurrentPrefab.transform.SetParent(stagingRoot, true);
            }

            CurrentPrefab.transform.position = worldPosition;
            CurrentPrefab.transform.rotation = worldRotation;
            SetWorldScale(CurrentPrefab.transform, worldScale);
        }

        public Vector3 GetBoneLocalPosition(Animator animator)
        {
            if (CurrentPrefab == null)
                return Vector3.zero;

            Transform bone = animator == null ? null : animator.GetBoneTransform(Bone);
            return bone == null ? CurrentPrefab.transform.localPosition : bone.InverseTransformPoint(CurrentPrefab.transform.position);
        }

        public Vector3 GetBoneLocalEulerAngles(Animator animator)
        {
            if (CurrentPrefab == null)
                return Vector3.zero;

            Transform bone = animator == null ? null : animator.GetBoneTransform(Bone);
            Quaternion localRotation = bone == null
                ? CurrentPrefab.transform.localRotation
                : Quaternion.Inverse(bone.rotation) * CurrentPrefab.transform.rotation;
            return localRotation.eulerAngles;
        }

        public Vector3 GetBoneLocalScale(Animator animator)
        {
            if (CurrentPrefab == null)
                return Vector3.one;

            Transform bone = animator == null ? null : animator.GetBoneTransform(Bone);
            return bone == null
                ? CurrentPrefab.transform.localScale
                : Divide(CurrentPrefab.transform.lossyScale, bone.lossyScale);
        }

        public void SetBoneLocalPosition(Animator animator, Vector3 localPosition)
            => SetBoneLocalTransform(animator, localPosition, GetBoneLocalEulerAngles(animator), GetBoneLocalScale(animator));

        public void SetBoneLocalEulerAngles(Animator animator, Vector3 localEulerAngles)
            => SetBoneLocalTransform(animator, GetBoneLocalPosition(animator), localEulerAngles, GetBoneLocalScale(animator));

        public void SetBoneLocalScale(Animator animator, Vector3 localScale)
            => SetBoneLocalTransform(animator, GetBoneLocalPosition(animator), GetBoneLocalEulerAngles(animator), localScale);

        public void SetBoneLocalTransform(Animator animator, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            if (CurrentPrefab == null)
                return;

            ApplyBoneLocalTransform(animator, CurrentPrefab.transform, localPosition, localEulerAngles, localScale);
        }

        private void ApplyBoneLocalTransform(Animator animator, Transform target, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            Transform bone = animator == null ? null : animator.GetBoneTransform(Bone);
            if (bone == null)
            {
                target.localPosition = localPosition;
                target.localEulerAngles = localEulerAngles;
                target.localScale = localScale;
                return;
            }

            target.position = bone.TransformPoint(localPosition);
            target.rotation = bone.rotation * Quaternion.Euler(localEulerAngles);
            SetWorldScale(target, Vector3.Scale(bone.lossyScale, localScale));
        }

        private static void SetWorldScale(Transform target, Vector3 worldScale)
        {
            Transform parent = target.parent;
            target.localScale = parent == null ? worldScale : Divide(worldScale, parent.lossyScale);
        }

        private static Vector3 Divide(Vector3 left, Vector3 right)
        {
            return new Vector3(
                right.x == 0 ? 0 : left.x / right.x,
                right.y == 0 ? 0 : left.y / right.y,
                right.z == 0 ? 0 : left.z / right.z);
        }

        private static bool TryGetPrefabMode(Object objPrefab, bDeviceTemplate device, out bool showMesh, out bool isMobile)
        {
            showMesh = false;
            isMobile = false;

            if (objPrefab == device.Prefab)
                return true;

            if (objPrefab == device.PrefabMesh)
            {
                showMesh = true;
                return true;
            }

            if (objPrefab == device.PrefabMobile)
            {
                isMobile = true;
                return true;
            }

            if (objPrefab == device.PrefabMeshMobile)
            {
                showMesh = true;
                isMobile = true;
                return true;
            }

            return false;
        }

        public void SelectCurrentPrefab()
            => Selection.activeGameObject = CurrentPrefab;

        public void DestroyCurrentPrefab()
        {
            if (CurrentPrefab == null)
                return;
            Undo.DestroyObjectImmediate(CurrentPrefab);
            CurrentPrefab = null;
            CustomContactTags.Clear();
            TouchView_Default = touchView_Default;
            TouchView_Triggered = touchView_Triggered;
        }

        public void Reset()
        {
            DestroyCurrentPrefab();
            _showMesh = false;
            ShowMesh = true;
            ApplyParentConstraints = true;
            CustomContactTags.Clear();
            TouchView_Default = touchView_Default;
            TouchView_Triggered = touchView_Triggered;
        }
    }
}
#endif
