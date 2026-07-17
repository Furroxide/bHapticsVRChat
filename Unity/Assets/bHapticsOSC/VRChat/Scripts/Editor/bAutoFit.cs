#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    public static class bAutoFit
    {
        private const float MinMeasurement = 0.01f;
        private const float MinReferenceSize = 0.05f;
        private const float MinScale = 0.1f;
        private const float MaxScale = 4f;

        private static readonly HumanBodyBones[] VestAnchorBones = new HumanBodyBones[]
        {
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Spine,
        };

        public static bool Supports(bDeviceType deviceType)
            => deviceType == bDeviceType.VEST;

        public static bool TryApply(bHapticsOSCIntegration editorComp, bDeviceType deviceType, bUserSettings userSettings, out string message)
        {
            if (!Supports(deviceType))
            {
                message = "Auto-fit is not available for this device yet.";
                return false;
            }

            if (editorComp == null || editorComp.avatarAnimator == null)
            {
                message = "Auto-fit needs a valid avatar Animator.";
                return false;
            }

            if (userSettings == null || userSettings.CurrentPrefab == null)
            {
                message = "Auto-fit needs an added vest prefab.";
                return false;
            }

            return TryApplyVest(editorComp, userSettings, out message);
        }

        private static bool TryApplyVest(bHapticsOSCIntegration editorComp, bUserSettings userSettings, out string message)
        {
            Animator animator = editorComp.avatarAnimator;
            if (!TryGetBone(animator, VestAnchorBones, out HumanBodyBones anchorBone, out Transform anchorTransform))
            {
                message = "Auto-fit needs a Chest, Upper Chest, or Spine humanoid bone.";
                return false;
            }

            Transform prefabTransform = userSettings.CurrentPrefab.transform;
            if (!TryGetPrefabReferenceBounds(prefabTransform, out Bounds referenceBounds))
            {
                message = "Auto-fit could not measure the vest prefab bounds.";
                return false;
            }

            bool hasAvatarBounds = TryGetAvatarLocalBounds(editorComp, prefabTransform, out Bounds avatarBounds);
            Vector3 anchorLocal = editorComp.transform.InverseTransformPoint(anchorTransform.position);

            if (!TryMeasureVestWidth(editorComp, animator, hasAvatarBounds, avatarBounds, out float targetWidth))
            {
                message = "Auto-fit could not measure torso width from avatar bones or bounds.";
                return false;
            }

            if (!TryMeasureVestHeight(editorComp, animator, anchorLocal, hasAvatarBounds, avatarBounds, out float targetHeight, out float centerY))
            {
                message = "Auto-fit could not measure torso height from avatar bones or bounds.";
                return false;
            }

            Vector3 targetCenterLocal = anchorLocal;
            targetCenterLocal.y = centerY;
            if (TryGetShoulderMidpoint(editorComp, animator, out Vector3 shoulderMidpoint))
                targetCenterLocal.x = shoulderMidpoint.x;
            else if (hasAvatarBounds)
                targetCenterLocal.x = Mathf.Lerp(anchorLocal.x, avatarBounds.center.x, 0.35f);

            if (hasAvatarBounds)
                targetCenterLocal.z = Mathf.Lerp(anchorLocal.z, avatarBounds.center.z, 0.25f);

            float targetDepth = MeasureVestDepth(targetWidth, hasAvatarBounds, avatarBounds);
            Vector3 referenceSize = GetSafeReferenceSize(referenceBounds.size);
            Vector3 targetScale = ClampScale(new Vector3(
                targetWidth / referenceSize.x,
                targetHeight / referenceSize.y,
                targetDepth / referenceSize.z));

            Quaternion localRotation = GetDefaultLocalRotation(userSettings.CurrentPrefab);
            Quaternion targetWorldRotation = anchorTransform.rotation * localRotation;
            Vector3 targetWorldScale = Vector3.Scale(anchorTransform.lossyScale, targetScale);
            Vector3 targetCenterWorld = editorComp.transform.TransformPoint(targetCenterLocal);
            Vector3 targetRootWorldPosition = targetCenterWorld - (targetWorldRotation * Vector3.Scale(referenceBounds.center, targetWorldScale));

            Vector3 anchorLocalPosition = anchorTransform.InverseTransformPoint(targetRootWorldPosition);
            Vector3 anchorLocalEulerAngles = localRotation.eulerAngles;

            Undo.RecordObject(prefabTransform, $"[{bHapticsOSCIntegration.SystemName}] Auto-Fit Vest");

            HumanBodyBones originalBone = userSettings.Bone;
            try
            {
                userSettings.Bone = anchorBone;
                userSettings.SetBoneLocalTransform(animator, anchorLocalPosition, anchorLocalEulerAngles, targetScale);
            }
            finally
            {
                userSettings.Bone = originalBone;
            }

            EditorUtility.SetDirty(prefabTransform);
            message = "Vest auto-fit applied. Use the manual transform fields for final tweaks.";
            return true;
        }

        private static bool TryMeasureVestWidth(bHapticsOSCIntegration editorComp, Animator animator, bool hasAvatarBounds, Bounds avatarBounds, out float width)
        {
            if (TryGetBonePairLocal(editorComp, animator, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, out Vector3 left, out Vector3 right)
                || TryGetBonePairLocal(editorComp, animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, out left, out right))
            {
                width = Mathf.Abs(left.x - right.x);
                if (width <= MinMeasurement)
                    width = Vector3.Distance(left, right);

                width *= 1.15f;
                if (width > MinMeasurement)
                    return true;
            }

            if (hasAvatarBounds && avatarBounds.size.y > MinMeasurement)
            {
                width = Mathf.Min(avatarBounds.size.x * 0.45f, avatarBounds.size.y * 0.28f);
                return width > MinMeasurement;
            }

            width = 0f;
            return false;
        }

        private static bool TryMeasureVestHeight(
            bHapticsOSCIntegration editorComp,
            Animator animator,
            Vector3 anchorLocal,
            bool hasAvatarBounds,
            Bounds avatarBounds,
            out float height,
            out float centerY)
        {
            bool hasUpper = TryGetFirstBoneLocal(editorComp, animator, out Vector3 upper,
                HumanBodyBones.Neck,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Chest);
            bool hasLower = TryGetFirstBoneLocal(editorComp, animator, out Vector3 lower,
                HumanBodyBones.Hips,
                HumanBodyBones.Spine);

            if (hasUpper && hasLower)
            {
                float torsoHeight = Mathf.Abs(upper.y - lower.y);
                if (torsoHeight > MinMeasurement)
                {
                    height = torsoHeight * 0.92f;
                    centerY = Mathf.Lerp(lower.y, upper.y, 0.56f);
                    return true;
                }
            }

            if (hasAvatarBounds && avatarBounds.size.y > MinMeasurement)
            {
                height = avatarBounds.size.y * 0.32f;
                centerY = anchorLocal.y - (height * 0.12f);
                return true;
            }

            height = 0f;
            centerY = 0f;
            return false;
        }

        private static float MeasureVestDepth(float width, bool hasAvatarBounds, Bounds avatarBounds)
        {
            if (!hasAvatarBounds || avatarBounds.size.z <= MinMeasurement)
                return width * 0.45f;

            return Mathf.Clamp(avatarBounds.size.z * 0.72f, width * 0.28f, width * 0.72f);
        }

        private static bool TryGetShoulderMidpoint(bHapticsOSCIntegration editorComp, Animator animator, out Vector3 midpoint)
        {
            if (TryGetBonePairLocal(editorComp, animator, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, out Vector3 left, out Vector3 right)
                || TryGetBonePairLocal(editorComp, animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, out left, out right))
            {
                midpoint = (left + right) * 0.5f;
                return true;
            }

            midpoint = Vector3.zero;
            return false;
        }

        private static bool TryGetBonePairLocal(
            bHapticsOSCIntegration editorComp,
            Animator animator,
            HumanBodyBones leftBone,
            HumanBodyBones rightBone,
            out Vector3 left,
            out Vector3 right)
        {
            if (TryGetBone(animator, leftBone, out Transform leftTransform) && TryGetBone(animator, rightBone, out Transform rightTransform))
            {
                left = editorComp.transform.InverseTransformPoint(leftTransform.position);
                right = editorComp.transform.InverseTransformPoint(rightTransform.position);
                return true;
            }

            left = Vector3.zero;
            right = Vector3.zero;
            return false;
        }

        private static bool TryGetFirstBoneLocal(bHapticsOSCIntegration editorComp, Animator animator, out Vector3 localPosition, params HumanBodyBones[] bones)
        {
            HumanBodyBones foundBone;
            if (TryGetBone(animator, bones, out foundBone, out Transform transform))
            {
                localPosition = editorComp.transform.InverseTransformPoint(transform.position);
                return true;
            }

            localPosition = Vector3.zero;
            return false;
        }

        private static bool TryGetBone(Animator animator, HumanBodyBones[] bones, out HumanBodyBones foundBone, out Transform transform)
        {
            foreach (HumanBodyBones bone in bones)
            {
                if (TryGetBone(animator, bone, out transform))
                {
                    foundBone = bone;
                    return true;
                }
            }

            foundBone = HumanBodyBones.LastBone;
            transform = null;
            return false;
        }

        private static bool TryGetBone(Animator animator, HumanBodyBones bone, out Transform transform)
        {
            transform = animator == null ? null : animator.GetBoneTransform(bone);
            return transform != null;
        }

        private static bool TryGetAvatarLocalBounds(bHapticsOSCIntegration editorComp, Transform currentPrefab, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = new Bounds();
            Transform stagingRoot = editorComp.transform.Find(bHapticsOSCIntegration.VrcFuryRootName);

            foreach (Renderer renderer in editorComp.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                Transform rendererTransform = renderer.transform;
                if (currentPrefab != null && rendererTransform.IsChildOf(currentPrefab))
                    continue;

                if (stagingRoot != null && rendererTransform.IsChildOf(stagingRoot))
                    continue;

                AddTransformedBounds(ref bounds, ref hasBounds, editorComp.transform.worldToLocalMatrix, renderer.bounds);
            }

            return hasBounds && bounds.size.sqrMagnitude > MinMeasurement;
        }

        private static bool TryGetPrefabReferenceBounds(Transform root, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = new Bounds();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                AddRendererReferenceBounds(root, renderer, ref bounds, ref hasBounds);

            if (hasBounds && bounds.size.x > MinMeasurement && bounds.size.y > MinMeasurement)
                return true;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root)
                    continue;

                AddPoint(ref bounds, ref hasBounds, root.InverseTransformPoint(child.position));
            }

            return hasBounds && bounds.size.x > MinMeasurement && bounds.size.y > MinMeasurement;
        }

        private static void AddRendererReferenceBounds(Transform root, Renderer renderer, ref Bounds bounds, ref bool hasBounds)
        {
            if (renderer == null)
                return;

            SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            if (skinnedMeshRenderer != null)
            {
                Matrix4x4 rendererToRoot = root.worldToLocalMatrix * skinnedMeshRenderer.transform.localToWorldMatrix;
                AddTransformedBounds(ref bounds, ref hasBounds, rendererToRoot, skinnedMeshRenderer.localBounds);
                return;
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Matrix4x4 rendererToRoot = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                AddTransformedBounds(ref bounds, ref hasBounds, rendererToRoot, meshFilter.sharedMesh.bounds);
                return;
            }

            AddTransformedBounds(ref bounds, ref hasBounds, root.worldToLocalMatrix, renderer.bounds);
        }

        private static void AddTransformedBounds(ref Bounds bounds, ref bool hasBounds, Matrix4x4 matrix, Bounds sourceBounds)
        {
            Vector3 center = sourceBounds.center;
            Vector3 extents = sourceBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        AddPoint(ref bounds, ref hasBounds, matrix.MultiplyPoint3x4(corner));
                    }
                }
            }
        }

        private static void AddPoint(ref Bounds bounds, ref bool hasBounds, Vector3 point)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private static Vector3 GetSafeReferenceSize(Vector3 size)
        {
            float width = Mathf.Max(size.x, MinReferenceSize);
            float height = Mathf.Max(size.y, MinReferenceSize);
            float depth = Mathf.Max(size.z, Mathf.Max(width * 0.45f, MinReferenceSize));
            return new Vector3(width, height, depth);
        }

        private static Vector3 ClampScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, MinScale, MaxScale),
                Mathf.Clamp(scale.y, MinScale, MaxScale),
                Mathf.Clamp(scale.z, MinScale, MaxScale));
        }

        private static Quaternion GetDefaultLocalRotation(GameObject currentPrefab)
        {
            UnityEngine.Object prefabObject = PrefabUtility.GetCorrespondingObjectFromOriginalSource(currentPrefab);
            GameObject prefab = prefabObject as GameObject;
            return prefab == null ? Quaternion.identity : prefab.transform.localRotation;
        }
    }
}
#endif
