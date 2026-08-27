#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The whole avatar side in one guarded press.
    ///
    /// Everything here is also reachable one control at a time from the bHapticsOSC Integration
    /// inspector; this exists because doing it by hand is a dozen or more clicks that a first-time
    /// user has no way to get right in order. The confirmation dialog is the entire safety story -
    /// without it this would be a silent bulk edit of somebody's avatar - so it shows exactly what
    /// is about to happen and nothing proceeds until it is accepted.
    /// </summary>
    internal static class bAvatarSetup
    {
        /// <summary>
        /// What a fresh setup starts with. The vest, head and forearms are what almost everyone
        /// owns and where the haptics actually read as haptics; hands and feet are offered but
        /// left unticked, because every added device is contact receivers the user has to pay for
        /// in VRChat's performance rating, and under-selecting is recoverable by running again.
        /// </summary>
        private static readonly bDeviceType[] DefaultDevices =
        {
            bDeviceType.VEST,
            bDeviceType.HEAD,
            bDeviceType.ARM_LEFT,
            bDeviceType.ARM_RIGHT,
        };

        private static readonly bDeviceType[] OptionalDevices =
        {
            bDeviceType.HAND_LEFT,
            bDeviceType.HAND_RIGHT,
            bDeviceType.FOOT_LEFT,
            bDeviceType.FOOT_RIGHT,
        };

        internal enum bReadiness
        {
            Ready,

            /// <summary>Nothing selected, or the selection is not on an avatar.</summary>
            NoAvatar,

            /// <summary>Not a humanoid rig, so there are no bones to attach devices to.</summary>
            NotHumanoid,

            /// <summary>Already carries a generated setup.</summary>
            AlreadySetUp,
        }

        // ------------------------------------------------------------------ entry points

        [MenuItem("GameObject/bHapticsOSC/Set up this avatar", false, 20)]
        private static void SetUpFromHierarchy()
            => Run(Selection.activeGameObject);

        [MenuItem("GameObject/bHapticsOSC/Set up this avatar", true)]
        private static bool SetUpFromHierarchyValidate()
            => FindAvatar(Selection.activeGameObject) != null;

        /// <summary>The avatar the given object belongs to, or null.</summary>
        internal static VRCAvatarDescriptor FindAvatar(GameObject candidate)
            => candidate == null ? null : candidate.GetComponentInParent<VRCAvatarDescriptor>();

        /// <summary>What, if anything, stands in the way - for the window to show before the click.</summary>
        internal static bReadiness Inspect(VRCAvatarDescriptor avatar, out string detail)
        {
            detail = string.Empty;

            if (avatar == null)
            {
                detail = "Select your avatar in the Hierarchy.";
                return bReadiness.NoAvatar;
            }

            Animator animator = avatar.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                detail = $"'{avatar.name}' is not a humanoid rig, so there are no bones to attach devices to.";
                return bReadiness.NotHumanoid;
            }

            if (avatar.transform.Find(bHapticsOSCIntegration.VrcFuryRootName) != null)
            {
                detail = $"'{avatar.name}' already has a bHapticsOSC setup.";
                return bReadiness.AlreadySetUp;
            }

            detail = $"Ready to set up '{avatar.name}'.";
            return bReadiness.Ready;
        }

        // ------------------------------------------------------------------ the one click

        /// <summary>
        /// Confirms, then does the whole avatar side. Returns true when a setup was created.
        /// </summary>
        internal static bool Run(GameObject selection)
        {
            VRCAvatarDescriptor avatar = FindAvatar(selection);
            bReadiness readiness = Inspect(avatar, out string detail);

            if (readiness == bReadiness.NoAvatar || readiness == bReadiness.NotHumanoid)
            {
                EditorUtility.DisplayDialog(bHapticsOSCIntegration.SystemName, detail, "OK");
                return false;
            }

            if (readiness == bReadiness.AlreadySetUp
                && !EditorUtility.DisplayDialog(
                    bHapticsOSCIntegration.SystemName,
                    detail + "\n\nSetting it up again replaces the generated assets. Your device "
                           + "positions are kept.",
                    "Set up again",
                    "Cancel"))
            {
                return false;
            }

            bHapticsOSCIntegration integration = avatar.GetComponentInChildren<bHapticsOSCIntegration>(true);
            bool adopting = integration != null;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set up bHapticsOSC on " + avatar.name);

            if (!adopting)
                integration = Undo.AddComponent<bHapticsOSCIntegration>(avatar.gameObject);

            if (integration.TryValidate() != bHapticsOSCIntegration.bSetupProblem.Ok)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                EditorUtility.DisplayDialog(
                    bHapticsOSCIntegration.SystemName,
                    $"'{avatar.name}' cannot take the component. Add it by hand to see why.",
                    "OK");
                return false;
            }

            bEditorGUI.EnsureUserSettings(integration);

            // Someone who already picked devices and spent time positioning them keeps every one
            // of those choices; only a genuinely fresh avatar gets the defaults.
            integration.FindExistingPrefabs(bDevice.AllTemplates);
            bool hasExistingChoices = HasAnyDevice(integration);

            var plan = BuildPlan(integration, hasExistingChoices, out string[] unavailable);
            if (plan.Count == 0)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                EditorUtility.DisplayDialog(
                    bHapticsOSCIntegration.SystemName,
                    $"None of the haptic devices can be placed on '{avatar.name}' - its rig is missing the "
                    + "bones they attach to.",
                    "OK");
                return false;
            }

            if (!EditorUtility.DisplayDialog(
                    bHapticsOSCIntegration.SystemName,
                    BuildConfirmation(avatar, plan, hasExistingChoices, unavailable),
                    hasExistingChoices ? "Create the setup" : "Add these and create the setup",
                    "Cancel"))
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return false;
            }

            try
            {
                if (!hasExistingChoices)
                    SeedDevices(integration, plan);

                AutoFit(integration, plan);
                bEditorGUI.RunSetupPipeline(integration);
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch (Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(exception);
                Undo.RevertAllDownToGroup(undoGroup);
                EditorUtility.DisplayDialog(
                    bHapticsOSCIntegration.SystemName,
                    $"Setting up '{avatar.name}' failed, so it was put back as it was.\n\n{exception.Message}\n\n"
                    + "The Console has the full details.",
                    "OK");
                return false;
            }
        }

        // ------------------------------------------------------------------ planning

        private static bool HasAnyDevice(bHapticsOSCIntegration integration)
            => integration.AllUserSettings != null
               && integration.AllUserSettings.Values.Any(settings => settings != null && settings.CurrentPrefab != null);

        /// <summary>
        /// The devices to end up with. When the avatar already has some, that IS the plan - a user
        /// who spent twenty minutes placing devices must not have them replaced by defaults.
        /// </summary>
        private static List<bDeviceType> BuildPlan(
            bHapticsOSCIntegration integration,
            bool hasExistingChoices,
            out string[] unavailable)
        {
            var plan = new List<bDeviceType>();
            var missing = new List<string>();

            IEnumerable<bDeviceType> wanted = hasExistingChoices
                ? bDevice.AllTemplates.Keys.Where(type => HasDevice(integration, type))
                : DefaultDevices;

            foreach (bDeviceType type in wanted)
            {
                if (!bDevice.AllTemplates.TryGetValue(type, out bDeviceTemplate template) || !template.HasBone)
                    continue;

                if (integration.avatarAnimator.GetBoneTransform(template.Bone) == null)
                {
                    missing.Add(template.Name);
                    continue;
                }

                plan.Add(type);
            }

            unavailable = missing.ToArray();
            return plan;
        }

        private static bool HasDevice(bHapticsOSCIntegration integration, bDeviceType type)
            => bDevice.AllTemplates.TryGetValue(type, out bDeviceTemplate template)
               && integration.AllUserSettings != null
               && integration.AllUserSettings.TryGetValue(template, out bUserSettings settings)
               && settings != null
               && settings.CurrentPrefab != null;

        /// <summary>
        /// Everything the press is about to do, in the user's words. This is the safety story, so
        /// it names the platform, the devices, the receiver cost, and anything being skipped.
        /// </summary>
        private static string BuildConfirmation(
            VRCAvatarDescriptor avatar,
            List<bDeviceType> plan,
            bool hasExistingChoices,
            string[] unavailable)
        {
            bool mobile = IsMobileBuildTarget();
            var text = new StringBuilder();

            text.AppendLine(hasExistingChoices
                ? $"Create the bHapticsOSC setup on '{avatar.name}' using the devices already on it:"
                : $"Add these haptic devices to '{avatar.name}' and create its bHapticsOSC setup:");
            text.AppendLine();

            foreach (bDeviceType type in plan)
                text.AppendLine("    " + bDevice.AllTemplates[type].Name);

            text.AppendLine();
            if (!hasExistingChoices)
            {
                text.AppendLine(mobile
                    ? "Quest versions, because this project is currently building for Android. You can "
                      + "switch any device to the PC version afterwards."
                    : "PC versions, because this project is currently building for Windows. You can "
                      + "switch any device to the Quest version afterwards.");
                text.AppendLine();
                text.AppendLine("Only you know which devices you actually own - untick nothing here, but do "
                                + "add or remove devices afterwards from the component's inspector.");
                text.AppendLine();
            }

            if (unavailable.Length > 0)
            {
                text.AppendLine("Skipped, because this rig has no bone for them: "
                                + string.Join(", ", unavailable) + ".");
                text.AppendLine();
            }

            text.AppendLine("Each device is scaled to this avatar automatically. The whole thing is one "
                            + "undo step, so Ctrl+Z puts the avatar back.");

            return text.ToString();
        }

        private static bool IsMobileBuildTarget()
            => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
               || EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;

        // ------------------------------------------------------------------ doing it

        /// <summary>
        /// Adds the planned devices. Sets the backing state directly rather than replaying the
        /// inspector's ADD DEVICE, which spawns the desktop prefab and immediately swaps it.
        /// </summary>
        private static void SeedDevices(bHapticsOSCIntegration integration, List<bDeviceType> plan)
        {
            bool mobile = IsMobileBuildTarget();

            foreach (bDeviceType type in plan)
            {
                bDeviceTemplate template = bDevice.AllTemplates[type];
                if (!integration.AllUserSettings.TryGetValue(template, out bUserSettings settings) || settings == null)
                    continue;

                // No Quest prefab for this device: fall back to the PC one rather than adding
                // nothing, which would silently drop the device from a Quest setup.
                bool useMobile = mobile && template.PrefabMeshMobile != null;

                settings.IsMobile = useMobile;
                settings.Reset();
            }
        }

        private static void AutoFit(bHapticsOSCIntegration integration, List<bDeviceType> plan)
        {
            foreach (bDeviceType type in plan)
            {
                if (!bAutoFit.Supports(type))
                    continue;

                bDeviceTemplate template = bDevice.AllTemplates[type];
                if (!integration.AllUserSettings.TryGetValue(template, out bUserSettings settings)
                    || settings == null
                    || settings.CurrentPrefab == null)
                {
                    continue;
                }

                // A device that will not fit is not a reason to abandon the run; it just keeps the
                // transform it was authored with, which is what the user would have got anyway.
                if (!bAutoFit.TryApply(integration, type, settings, out string message))
                    Debug.Log($"[{bHapticsOSCIntegration.SystemName}] {template.Name}: {message}");
            }
        }
    }
}
#endif
