#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace bHapticsOSC.VRChat
{
    internal sealed class bCompanionSetupWindow : EditorWindow
    {
        private const string WindowTitle = "bHapticsOSC Setup";
        private const string AvatarSetupCompleteSessionKey = bCompanionRequirements.PackageId + ".avatar-setup-complete";

        private Vector2 scrollPosition;
        private bCompanionStatusResult companionStatus;
        private string actionMessage = string.Empty;
        private MessageType actionMessageType = MessageType.None;
        private bool avatarSetupJustCompleted;

        internal static void ShowWindow()
        {
            bCompanionSetupWindow window = GetWindow<bCompanionSetupWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(460f, 520f);
            window.ConsumeAvatarSetupCompleteFlag();
            window.Show();
            window.Recheck();
        }

        internal static void ShowAvatarSetupComplete()
        {
            SessionState.SetBool(AvatarSetupCompleteSessionKey, true);
            ShowWindow();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(460f, 520f);
            ConsumeAvatarSetupCompleteFlag();
            Recheck();
        }

        private void OnDisable()
            => avatarSetupJustCompleted = false;

        private void OnFocus()
            => Recheck();

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("bHapticsOSC setup assistant", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The Unity integration creates the avatar setup. The portable Windows companion app and bHaptics Player are separate requirements for using haptics in VRChat.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(8f);

            if (avatarSetupJustCompleted)
            {
                EditorGUILayout.HelpBox(
                    "Avatar setup complete. The generated VRCFury setup is ready; finish any companion-app actions shown below before playing VRChat.",
                    MessageType.Info);
                GUILayout.Space(6f);
            }

            DrawPackageChecklist();
            GUILayout.Space(8f);
            DrawCompanionSection();
            GUILayout.Space(8f);
            DrawManualChecklist();

            if (!string.IsNullOrWhiteSpace(actionMessage))
            {
                GUILayout.Space(8f);
                EditorGUILayout.HelpBox(actionMessage, actionMessageType);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPackageChecklist()
        {
            EditorGUILayout.LabelField("Unity project", EditorStyles.boldLabel);

            PackageInfo avatars = FindPackage(bCompanionRequirements.VrchatAvatarsPackageId);
            DrawChecklistItem(
                avatars != null,
                "VRChat Avatars SDK",
                avatars == null ? "Resolve the Avatars SDK in VCC." : $"Installed: {avatars.version}");

            PackageInfo vrcFury = FindPackage(bCompanionRequirements.VrcFuryPackageId);
            bool vrcFurySupported = IsSupportedVrcFury(vrcFury, out string vrcFuryDetails);
            DrawChecklistItem(vrcFurySupported, "VRCFury", vrcFuryDetails);
        }

        private void DrawCompanionSection()
        {
            EditorGUILayout.LabelField("Windows companion app", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"{bCompanionStatusGUI.GetSummary(companionStatus)}\n{bCompanionStatusGUI.GetDetails(companionStatus)}",
                bCompanionStatusGUI.GetMessageType(companionStatus));

            if (!string.IsNullOrWhiteSpace(companionStatus.ExecutablePath))
                EditorGUILayout.SelectableLabel(companionStatus.ExecutablePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.LabelField($"Required version: {companionStatus.RequiredVersion}");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Download matching version"))
                    Application.OpenURL(bCompanionRequirements.GetMatchingDownloadUrl(companionStatus.RequiredVersion));

                if (GUILayout.Button("Latest release"))
                    Application.OpenURL(bCompanionRequirements.LatestReleaseUrl);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
                using (new EditorGUI.DisabledScope(!isWindows))
                {
                    if (GUILayout.Button("Locate existing app"))
                        LocateExistingApp();
                }

                using (new EditorGUI.DisabledScope(companionStatus.Status != bCompanionStatus.ReadyStopped))
                {
                    if (GUILayout.Button("Launch"))
                        LaunchCompanion();
                }

                if (GUILayout.Button("Recheck"))
                    Recheck();
            }
        }

        private static void DrawManualChecklist()
        {
            EditorGUILayout.LabelField("Before playing", EditorStyles.boldLabel);
            DrawChecklistItem(
                false,
                "bHaptics Player",
                "Install bHaptics Player, pair your devices, and leave it running. This must be confirmed manually.");
            if (GUILayout.Button("Open bHaptics Player downloads"))
                Application.OpenURL(bCompanionRequirements.BHapticsPlayerUrl);

            GUILayout.Space(4f);
            DrawChecklistItem(
                false,
                "VRChat OSC",
                "Enable OSC in VRChat's Action Menu under OSC > Enabled. Leave bHapticsOSC running while you play; this must be confirmed manually.");
            if (GUILayout.Button("Open VRChat OSC guidance"))
                Application.OpenURL(bCompanionRequirements.VrchatOscGuideUrl);
        }

        private static void DrawChecklistItem(bool ready, string title, string details)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{(ready ? "Ready" : "Action")} — {title}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(details, EditorStyles.wordWrappedLabel);
            }
        }

        private void LocateExistingApp()
        {
            string rememberedPath = bCompanionStatusDetector.RememberedExecutablePath;
            string initialDirectory = string.Empty;
            if (!string.IsNullOrWhiteSpace(rememberedPath))
            {
                string directory = Path.GetDirectoryName(rememberedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    initialDirectory = directory;
            }

            string selectedPath = EditorUtility.OpenFilePanel(
                "Locate bHapticsOSC.exe",
                initialDirectory,
                "exe");
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            bCompanionStatusDetector.SetRememberedExecutablePath(selectedPath);
            Recheck();
            actionMessage = bCompanionStatusGUI.GetDetails(companionStatus);
            actionMessageType = bCompanionStatusGUI.GetMessageType(companionStatus);
        }

        private void LaunchCompanion()
        {
            if (bCompanionStatusDetector.TryLaunch(companionStatus, out string error))
            {
                actionMessage = "bHapticsOSC launch requested. Use Recheck after the app starts.";
                actionMessageType = MessageType.Info;
            }
            else
            {
                actionMessage = $"Unable to launch bHapticsOSC: {error}";
                actionMessageType = MessageType.Error;
            }
        }

        private void Recheck()
        {
            companionStatus = bCompanionStatusDetector.Detect(true);
            Repaint();
        }

        private void ConsumeAvatarSetupCompleteFlag()
        {
            if (!SessionState.GetBool(AvatarSetupCompleteSessionKey, false))
                return;

            SessionState.SetBool(AvatarSetupCompleteSessionKey, false);
            avatarSetupJustCompleted = true;
        }

        private static PackageInfo FindPackage(string packageId)
        {
            try
            {
                foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
                {
                    if (package.name == packageId)
                        return package;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSupportedVrcFury(PackageInfo package, out string details)
        {
            if (package == null)
            {
                details = "Resolve VRCFury in VCC.";
                return false;
            }

            if (!bCompanionStatusDetector.TryNormalizeVersion(package.version, out _, out string version))
            {
                details = $"Installed version could not be evaluated: {package.version}";
                return false;
            }

            bool supported = bCompanionStatusDetector.CompareVersions(version, bCompanionRequirements.MinimumVrcFuryVersion) >= 0
                             && bCompanionStatusDetector.CompareVersions(version, bCompanionRequirements.MaximumVrcFuryVersion) < 0;
            details = supported
                ? $"Installed: {version}"
                : $"Installed: {version}; supported range is >= {bCompanionRequirements.MinimumVrcFuryVersion} and < {bCompanionRequirements.MaximumVrcFuryVersion}.";
            return supported;
        }
    }

    [InitializeOnLoad]
    internal static class bCompanionOnboarding
    {
        internal const string OnboardingPreferencePrefix = bCompanionRequirements.PackageId + ".onboarding.";
        private static bool scheduled;

        static bCompanionOnboarding()
            => Schedule();

        private static void Schedule()
        {
            if (scheduled)
                return;

            scheduled = true;
            EditorApplication.delayCall += ShowIfNeeded;
        }

        private static void ShowIfNeeded()
        {
            scheduled = false;
            if (Application.isBatchMode)
                return;

            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Schedule();
                return;
            }

            string requiredVersion = bCompanionRequirements.RequiredVersion;
            if (IsDismissed(requiredVersion))
                return;

            // Mark before opening so an assembly reload cannot create a loop.
            Dismiss(requiredVersion);
            bCompanionSetupWindow.ShowWindow();
        }

        internal static string GetPreferenceKey(string version)
            => OnboardingPreferencePrefix + version;

        internal static bool IsDismissed(string version)
            => EditorPrefs.GetBool(GetPreferenceKey(version), false);

        internal static void Dismiss(string version)
            => EditorPrefs.SetBool(GetPreferenceKey(version), true);
    }

    internal static class bCompanionStatusGUI
    {
        internal static void DrawInspectorCard()
        {
            bCompanionStatusResult status = bCompanionStatusDetector.Detect();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("bHapticsOSC companion", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    $"{GetSummary(status)}\n{GetDetails(status)}",
                    GetMessageType(status));

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Setup Assistant"))
                        bCompanionSetupWindow.ShowWindow();
                    if (GUILayout.Button("Recheck"))
                        bCompanionStatusDetector.Detect(true);
                }
            }

            GUILayout.Space(6f);
        }

        internal static string GetSummary(bCompanionStatusResult result)
        {
            switch (result.Status)
            {
                case bCompanionStatus.UnsupportedPlatform:
                    return "Check on the Windows VRChat PC";
                case bCompanionStatus.NotLocated:
                    return "Companion app not located";
                case bCompanionStatus.MissingPath:
                    return "Remembered companion app is missing";
                case bCompanionStatus.InvalidProduct:
                    return "Selected executable is not bHapticsOSC";
                case bCompanionStatus.UnknownVersion:
                    return "Companion app version is unknown";
                case bCompanionStatus.Outdated:
                    return "Companion app update required";
                case bCompanionStatus.ReadyStopped:
                    return "Companion app ready — stopped";
                case bCompanionStatus.ReadyRunning:
                    return "Companion app ready — running";
                default:
                    return "Companion app status unavailable";
            }
        }

        internal static string GetDetails(bCompanionStatusResult result)
        {
            switch (result.Status)
            {
                case bCompanionStatus.UnsupportedPlatform:
                    return "bHapticsOSC is a portable Windows app. Download, locate, and run it on the Windows PC used for VRChat.";
                case bCompanionStatus.NotLocated:
                    return "No running bHapticsOSC process or remembered executable was found. Download it or locate an existing copy.";
                case bCompanionStatus.MissingPath:
                    return "The remembered executable path no longer exists. Locate the app again or download the matching version.";
                case bCompanionStatus.InvalidProduct:
                    return string.IsNullOrWhiteSpace(result.DetectedProductName)
                        ? $"The selected file does not identify itself as {bCompanionRequirements.ProductName}."
                        : $"The selected file identifies itself as '{result.DetectedProductName}', not {bCompanionRequirements.ProductName}.";
                case bCompanionStatus.UnknownVersion:
                    return $"The app version could not be read. Version {result.RequiredVersion} or newer is required.";
                case bCompanionStatus.Outdated:
                    return $"Detected version {result.DetectedVersion}; version {result.RequiredVersion} or newer is required.";
                case bCompanionStatus.ReadyStopped:
                    return $"Version {result.DetectedVersion} meets the {result.RequiredVersion} requirement. Launch it before using haptics in VRChat.";
                case bCompanionStatus.ReadyRunning:
                    return $"Version {result.DetectedVersion} meets the {result.RequiredVersion} requirement and is currently running.";
                default:
                    return "Open Setup Assistant to review the companion requirements.";
            }
        }

        internal static MessageType GetMessageType(bCompanionStatusResult result)
        {
            switch (result.Status)
            {
                case bCompanionStatus.InvalidProduct:
                case bCompanionStatus.Outdated:
                    return MessageType.Error;
                case bCompanionStatus.NotLocated:
                case bCompanionStatus.MissingPath:
                case bCompanionStatus.UnknownVersion:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
#endif
