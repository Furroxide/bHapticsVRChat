#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace bHapticsOSC.VRChat
{
    internal enum bAvatarUploadTargetStatus
    {
        None,
        Incomplete,
        Configured,
    }

    internal sealed class bAvatarUploadDiagnostics : IVRCSDKPreprocessAvatarCallback
    {
        private const int CallbackOrder = -10001;

        public int callbackOrder => CallbackOrder;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (Application.isBatchMode || avatarGameObject == null)
                return true;

            bAvatarUploadTargetStatus targetStatus = ClassifyTarget(avatarGameObject);
            if (targetStatus == bAvatarUploadTargetStatus.None)
                return true;

            if (targetStatus == bAvatarUploadTargetStatus.Incomplete)
            {
                Debug.LogWarning(
                    "[bHapticsOSC] This avatar still has a bHapticsOSC Integration component. " +
                    "Create the VRCFury setup from its inspector before uploading. The upload will continue.",
                    avatarGameObject);
                return true;
            }

            if (!ShouldRunCompanionDiagnostics(
                    Application.isBatchMode,
                    Application.platform == RuntimePlatform.WindowsEditor,
                    targetStatus))
                return true;

            try
            {
                bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

                // A second, unsupported companion holding the OSC port is worth flagging even
                // when the supported build itself is healthy.
                if (ShouldWarnCompanionStatus(result.Status) || result.HasConflictingProcess)
                    Debug.LogWarning(BuildCompanionWarning(result), avatarGameObject);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[bHapticsOSC] Companion app diagnostics could not be completed: {exception.Message}\n" +
                    "The avatar upload will continue.",
                    avatarGameObject);
            }

            return true;
        }

        internal static bAvatarUploadTargetStatus ClassifyTarget(GameObject avatarGameObject)
        {
            if (avatarGameObject == null)
                return bAvatarUploadTargetStatus.None;

            if (avatarGameObject.GetComponentInChildren<bHapticsOSCIntegration>(true) != null)
                return bAvatarUploadTargetStatus.Incomplete;

            return avatarGameObject.GetComponentInChildren<bVrcFurySetup>(true) != null
                ? bAvatarUploadTargetStatus.Configured
                : bAvatarUploadTargetStatus.None;
        }

        internal static bool ShouldWarnCompanionStatus(bCompanionStatus status)
        {
            switch (status)
            {
                case bCompanionStatus.NotLocated:
                case bCompanionStatus.MissingPath:
                case bCompanionStatus.InvalidProduct:
                case bCompanionStatus.UnknownVersion:
                case bCompanionStatus.Outdated:
                case bCompanionStatus.ForeignBuild:
                case bCompanionStatus.RunningUninspectable:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool ShouldRunCompanionDiagnostics(
            bool isBatchMode,
            bool isWindowsEditor,
            bAvatarUploadTargetStatus targetStatus)
            => !isBatchMode
               && isWindowsEditor
               && targetStatus == bAvatarUploadTargetStatus.Configured;

        private static string BuildCompanionWarning(bCompanionStatusResult result)
        {
            // The same wording the setup window shows, from the same place - this used to be a
            // second copy of the status string table that could drift away from it.
            bSetupStep step = bSetupModel.DescribeCompanion(result);

            // Keep the title even when there is a detail sentence to go with it. In the window the
            // two are drawn together and the row is under a heading, so the detail alone is enough;
            // here the line lands in a console among VRCFury's build output, where "Installed, but
            // not running." on its own does not say what is not running.
            string summary = string.IsNullOrWhiteSpace(step.Detail)
                ? step.Title
                : $"{step.Title} - {step.Detail}";
            string details = step.Explanation;
            string message = string.IsNullOrWhiteSpace(details)
                ? summary
                : $"{summary}\n{details}";

            if (result.HasConflictingProcess)
            {
                message += $"\n'{result.ConflictingProcessName}' is also running and competes for the VRChat OSC port.";
            }

            return $"[bHapticsOSC] {message}\n" +
                   "Open bHapticsOSC > Setup Assistant to resolve this.\n" +
                   "The avatar upload will continue.";
        }
    }
}
#endif
