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
        private const string AutoLocateSessionKey = bCompanionRequirements.PackageId + ".auto-locate-attempted";

        private Vector2 scrollPosition;
        private bCompanionStatusResult companionStatus;
        private bEnvironment environment;
        private string actionMessage = string.Empty;
        private MessageType actionMessageType = MessageType.None;
        private bool avatarSetupJustCompleted;
        private bool showOtherOptions;

        /// <summary>
        /// Set while the window is being opened by something other than the user, so the
        /// automatic disk search never runs behind an unasked-for window.
        /// </summary>
        private static bool suppressAutoLocateOnce;

        internal static void ShowWindow()
        {
            bCompanionSetupWindow window = GetWindow<bCompanionSetupWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(460f, 560f);
            window.ConsumeAvatarSetupCompleteFlag();
            window.Show();
            window.Recheck();
            window.ScheduleAutoLocate();
        }

        internal static void ShowAvatarSetupComplete()
        {
            SessionState.SetBool(AvatarSetupCompleteSessionKey, true);
            ShowWindow();
        }

        /// <summary>Opens the window on the user's behalf, without the automatic disk search.</summary>
        internal static void ShowUnattended()
        {
            suppressAutoLocateOnce = true;
            try
            {
                ShowWindow();
            }
            finally
            {
                suppressAutoLocateOnce = false;
            }
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(460f, 560f);
            ConsumeAvatarSetupCompleteFlag();
            Recheck();
            ScheduleAutoLocate();
        }

        private void OnDisable()
            => avatarSetupJustCompleted = false;

        private void OnFocus()
            => Recheck();

        private void OnSelectionChange()
            => Repaint();

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
            DrawAvatarSection();
            GUILayout.Space(8f);
            DrawCompanionSection();
            GUILayout.Space(8f);
            DrawEnvironmentChecklist();

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

        /// <summary>
        /// The half of the journey the assistant used to leave out entirely. Everything else here
        /// is about software; this is about the user's avatar, which is what they came to do.
        /// </summary>
        private void DrawAvatarSection()
        {
            EditorGUILayout.LabelField("Your avatar", EditorStyles.boldLabel);

#if VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
            var avatar = bAvatarSetup.FindAvatar(Selection.activeGameObject);
            bAvatarSetup.bReadiness readiness = bAvatarSetup.Inspect(avatar, out string detail);

            switch (readiness)
            {
                case bAvatarSetup.bReadiness.NoAvatar:
                    DrawChecklistItem("Action", "Avatar", "Select your avatar in the Hierarchy and this will set it up.");
                    return;

                case bAvatarSetup.bReadiness.NotHumanoid:
                    DrawChecklistItem("Action", "Avatar", detail + " bHaptics devices attach to humanoid bones.");
                    return;

                case bAvatarSetup.bReadiness.AlreadySetUp:
                    DrawChecklistItem("Ready", avatar.name, "Already set up. Upload it with the VRChat SDK to use it.");
                    break;

                default:
                    DrawChecklistItem("Action", avatar.name, detail
                        + " One press adds the devices, fits them to this avatar, and builds the VRCFury setup.");
                    break;
            }

            string label = readiness == bAvatarSetup.bReadiness.AlreadySetUp
                ? "Set up " + avatar.name + " again"
                : "Set up " + avatar.name;

            if (GUILayout.Button(label, GUILayout.Height(26f)))
            {
                if (bAvatarSetup.Run(avatar.gameObject))
                    Recheck();

                GUIUtility.ExitGUI();
            }
#else
            DrawChecklistItem(
                "Action",
                "Avatar",
                "The VRChat Avatars SDK and VRCFury need to be installed before an avatar can be set up.");
#endif
        }

        private void DrawCompanionSection()
        {
            EditorGUILayout.LabelField("Windows companion app", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"{bCompanionStatusGUI.GetSummary(companionStatus)}\n{bCompanionStatusGUI.GetDetails(companionStatus)}",
                bCompanionStatusGUI.GetMessageType(companionStatus));

            if (!string.IsNullOrWhiteSpace(companionStatus.ExecutablePath))
            {
                EditorGUILayout.SelectableLabel(
                    companionStatus.ExecutablePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            EditorGUILayout.LabelField($"Required version: {companionStatus.RequiredVersion}");

            if (companionStatus.HasConflictingProcess)
            {
                EditorGUILayout.HelpBox(
                    $"'{companionStatus.ConflictingProcessName}' is also running. Two companion apps cannot share the "
                    + "VRChat OSC port, so haptics may silently stop working until the unsupported one is closed.",
                    MessageType.Warning);
            }

            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;

            DrawInstallerState();

            // One obvious next step, sized to matter, before the rest. Eight equal buttons made the
            // user work out which one their situation called for.
            using (new EditorGUI.DisabledScope(bCompanionInstaller.IsBusy))
            {
                if (GUILayout.Button(PrimaryActionLabel(), GUILayout.Height(26f)))
                {
                    RunPrimaryAction();
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(2f);
            showOtherOptions = EditorGUILayout.Foldout(showOtherOptions, "Other options", true);
            if (!showOtherOptions)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open the releases page"))
                    Application.OpenURL(bCompanionRequirements.ReleasesUrl);

                if (GUILayout.Button("Download in a browser"))
                    Application.OpenURL(bCompanionRequirements.GetMatchingDownloadUrl(companionStatus.RequiredVersion));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!isWindows))
                {
                    if (GUILayout.Button("Find automatically"))
                        RunAutoLocate(false);

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

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!isWindows || !companionStatus.HasUnsupportedProcessRunning))
                {
                    if (GUILayout.Button("Stop the unsupported app"))
                        StopUnsupportedCompanion();
                }

                using (new EditorGUI.DisabledScope(
                    string.IsNullOrWhiteSpace(bCompanionStatusDetector.RememberedExecutablePath)))
                {
                    if (GUILayout.Button("Forget remembered app"))
                        ForgetRememberedApp();
                }
            }
        }

        /// <summary>
        /// The two things that live outside Unity. Both used to say "this must be confirmed
        /// manually", which asked the user to go and check what the machine already knows - and
        /// said nothing at all to the user whose Player was closed or whose OSC was off, which is
        /// exactly the state that produces silent no-haptics in VRChat.
        /// </summary>
        private void DrawEnvironmentChecklist()
        {
            EditorGUILayout.LabelField("Before playing", EditorStyles.boldLabel);

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                DrawChecklistItem(
                    "Check",
                    "bHaptics Player and VRChat OSC",
                    "Both live on the Windows PC you play on, so they cannot be checked from this editor.");
                return;
            }

            DrawPlayerRow();
            GUILayout.Space(4f);
            DrawOscRow();
        }

        private void DrawPlayerRow()
        {
            if (environment.PlayerRunning == bProbeState.Yes)
            {
                DrawChecklistItem(
                    "Ready",
                    "bHaptics Player",
                    string.IsNullOrEmpty(environment.PlayerVersion)
                        ? "Running. Your devices need to be paired and switched on in it."
                        : $"Running (version {environment.PlayerVersion}). Your devices need to be paired and "
                          + "switched on in it.");
                return;
            }

            if (environment.PlayerInstalled == bProbeState.Yes)
            {
                DrawChecklistItem(
                    "Action",
                    "bHaptics Player",
                    "Installed, but not running. Start it and pair your devices before playing - nothing "
                    + "reaches your gear without it.");
                return;
            }

            DrawChecklistItem(
                "Action",
                "bHaptics Player",
                "Not found on this PC. It is bHaptics' own app, and it is what actually drives your gear.");

            if (GUILayout.Button("Get bHaptics Player"))
                Application.OpenURL(bCompanionRequirements.BHapticsPlayerUrl);
        }

        private void DrawOscRow()
        {
            switch (environment.OscEnabled)
            {
                case bProbeState.Yes:
                    DrawChecklistItem("Ready", "VRChat OSC", DescribeOscEvidence("Turned on in VRChat on this PC."));
                    return;

                case bProbeState.No:
                    DrawChecklistItem(
                        "Action",
                        "VRChat OSC",
                        "Turned off in VRChat on this PC. In VRChat, open the Action Menu and turn on "
                        + "OSC > Enabled - without it your avatar's touches never reach the companion app.");
                    break;

                default:
                    DrawChecklistItem(
                        "Check",
                        "VRChat OSC",
                        DescribeOscEvidence(
                            "Could not be read on this PC. In VRChat, open the Action Menu and make sure "
                            + "OSC > Enabled is on."));
                    break;
            }

            if (GUILayout.Button("How to turn on OSC"))
                Application.OpenURL(bCompanionRequirements.VrchatOscGuideUrl);
        }

        /// <summary>
        /// Adds what VRChat's own files show on top of the setting. Seeing this package's
        /// parameters on an avatar VRChat has loaded is the only proof available inside Unity that
        /// the whole chain works.
        /// </summary>
        private string DescribeOscEvidence(string lead)
        {
            if (environment.HasHapticAvatar)
            {
                return lead + $"\n\nVRChat has loaded '{environment.HapticAvatarName}' with this package's "
                       + "haptic parameters, so the avatar side is working.";
            }

            if (environment.HasSeenOscConfig)
            {
                return lead + $"\n\nVRChat last saved an OSC config on "
                       + $"{environment.OscConfigWritten:d MMM yyyy}, but none of the recent ones carry this "
                       + "package's haptic parameters yet.";
            }

            return lead;
        }

        /// <summary>
        /// The single thing this state calls for. Every other control stays available under Other
        /// options; this is only about which one the user should reach for first.
        /// </summary>
        private string PrimaryActionLabel()
        {
            if (companionStatus.HasUnsupportedProcessRunning)
                return "Stop the unsupported app";

            switch (companionStatus.Status)
            {
                case bCompanionStatus.ReadyStopped:
                    return "Start bHapticsOSC";
                case bCompanionStatus.ReadyRunning:
                    return "Running - nothing to do here";
                case bCompanionStatus.NotLocated:
                case bCompanionStatus.MissingPath:
                    return bCompanionInstaller.IsSupportedPlatform ? "Install the companion app" : "Open the releases page";
                case bCompanionStatus.ForeignBuild:
                case bCompanionStatus.Outdated:
                    return bCompanionInstaller.IsSupportedPlatform ? "Install the supported build" : "Open the releases page";
                case bCompanionStatus.InvalidProduct:
                case bCompanionStatus.RunningUninspectable:
                    return "Locate bHapticsOSC.exe";
                default:
                    return "Open the releases page";
            }
        }

        private void RunPrimaryAction()
        {
            if (companionStatus.HasUnsupportedProcessRunning)
            {
                StopUnsupportedCompanion();
                return;
            }

            switch (companionStatus.Status)
            {
                case bCompanionStatus.ReadyStopped:
                    LaunchCompanion();
                    return;

                case bCompanionStatus.ReadyRunning:
                    Recheck();
                    return;

                case bCompanionStatus.NotLocated:
                case bCompanionStatus.MissingPath:
                case bCompanionStatus.ForeignBuild:
                case bCompanionStatus.Outdated:
                    if (bCompanionInstaller.IsSupportedPlatform)
                    {
                        // Try what is already on disk before reaching for the network.
                        RunAutoLocate(false);
                        if (!companionStatus.IsReady)
                            bCompanionInstaller.Begin(companionStatus.RequiredVersion);

                        return;
                    }

                    Application.OpenURL(bCompanionRequirements.ReleasesUrl);
                    return;

                case bCompanionStatus.InvalidProduct:
                case bCompanionStatus.RunningUninspectable:
                    LocateExistingApp();
                    return;

                default:
                    Application.OpenURL(bCompanionRequirements.ReleasesUrl);
                    return;
            }
        }

        /// <summary>
        /// Progress drawn in the window rather than behind a modal bar, so the editor stays usable
        /// while a several-megabyte download runs.
        /// </summary>
        private void DrawInstallerState()
        {
            if (bCompanionInstaller.Phase == bCompanionInstaller.bInstallPhase.Idle)
                return;

            if (bCompanionInstaller.IsBusy)
            {
                Rect bar = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                EditorGUI.ProgressBar(bar, bCompanionInstaller.Progress, bCompanionInstaller.Message);

                if (GUILayout.Button("Cancel"))
                    bCompanionInstaller.Cancel();

                Repaint();
                return;
            }

            bool failed = bCompanionInstaller.Phase == bCompanionInstaller.bInstallPhase.Failed;
            EditorGUILayout.HelpBox(
                bCompanionInstaller.Message,
                failed ? MessageType.Warning : MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!failed && GUILayout.Button("Start it now"))
                {
                    Recheck();
                    LaunchCompanion();
                }

                if (GUILayout.Button("Dismiss"))
                {
                    bCompanionInstaller.Dismiss();
                    Recheck();
                }
            }

            GUILayout.Space(4f);
        }

        private static void DrawChecklistItem(bool ready, string title, string details)
            => DrawChecklistItem(ready ? "Ready" : "Action", title, details);

        private static void DrawChecklistItem(string state, string title, string details)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{state} — {title}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(details, EditorStyles.wordWrappedLabel);
            }
        }

        private void LocateExistingApp()
        {
            string rememberedPath = bCompanionStatusDetector.RememberedExecutablePath;
            string initialDirectory = string.Empty;
            if (!string.IsNullOrWhiteSpace(rememberedPath))
            {
                string directory = SafeGetDirectoryName(rememberedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    initialDirectory = directory;
            }

            string selectedPath = EditorUtility.OpenFilePanel(
                "Locate bHapticsOSC.exe",
                initialDirectory,
                "exe");
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            // Judge the file the user actually picked. Reading the global detector instead would
            // report on whatever else happens to be running, and a mis-picked file would replace
            // a working remembered path while being told it was fine.
            bCompanionStatusResult selected = bCompanionStatusDetector.InspectExecutable(
                selectedPath,
                false,
                bCompanionRequirements.RequiredVersion);

            if (selected.Lineage == bCompanionBuildLineage.Unrelated)
            {
                actionMessage =
                    $"{Path.GetFileName(selectedPath)} is not a bHapticsOSC app, so it was not remembered. "
                    + bCompanionStatusGUI.GetDetails(selected);
                actionMessageType = MessageType.Error;
                Recheck();
                return;
            }

            bCompanionStatusDetector.SetRememberedExecutablePath(selectedPath);
            Recheck();
            actionMessage = $"{selectedPath}\n{bCompanionStatusGUI.GetDetails(selected)}";
            actionMessageType = bCompanionStatusGUI.GetMessageType(selected);
        }

        private void ForgetRememberedApp()
        {
            bCompanionStatusDetector.SetRememberedExecutablePath(null);
            SessionState.SetBool(AutoLocateSessionKey, false);
            Recheck();
            actionMessage = "Forgot the remembered companion app.";
            actionMessageType = MessageType.Info;
        }

        /// <summary>
        /// Scans the usual download locations so the common case - the portable app sitting in
        /// Downloads - needs no file dialog at all.
        /// </summary>
        private void RunAutoLocate(bool automatic)
        {
            bCompanionLocator.bLocatorResult located;
            try
            {
                located = bCompanionLocator.Locate();
            }
            catch (Exception exception)
            {
                Recheck();
                actionMessage = $"The search could not be completed: {exception.Message}";
                actionMessageType = MessageType.Warning;
                return;
            }

            if (located.Found)
            {
                bCompanionStatusResult found = bCompanionStatusDetector.InspectExecutable(
                    located.ExecutablePath,
                    false,
                    bCompanionRequirements.RequiredVersion);

                // An unattended scan reports what it found but does not adopt it: the remembered
                // path is shared by every project on the machine, and the most likely find on a
                // fresh install is the upstream build this package exists to replace.
                if (found.IsReady || !automatic)
                    bCompanionStatusDetector.SetRememberedExecutablePath(located.ExecutablePath);

                Recheck();
                actionMessage = $"Found {located.ExecutablePath}\n{bCompanionStatusGUI.GetDetails(found)}";
                actionMessageType = bCompanionStatusGUI.GetMessageType(found);
                return;
            }

            Recheck();
            if (located.Cancelled)
            {
                actionMessage = "Search cancelled.";
                actionMessageType = MessageType.Info;
                return;
            }

            if (automatic)
                return;

            actionMessage =
                "No bHapticsOSC executable was found in Downloads, on the Desktop, or under the usual install "
                + "folders. Download the matching version, or use Locate existing app if you keep it elsewhere.";
            actionMessageType = MessageType.Warning;
        }

        /// <summary>
        /// Runs the automatic search once per editor session, and only when nothing was found,
        /// so opening the window never costs a filesystem sweep it does not need.
        /// </summary>
        private void ScheduleAutoLocate()
        {
            if (suppressAutoLocateOnce)
                return;
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return;
            if (Application.isBatchMode)
                return;
            if (SessionState.GetBool(AutoLocateSessionKey, false))
                return;
            if (companionStatus.Status != bCompanionStatus.NotLocated)
                return;

            EditorApplication.delayCall += RunScheduledAutoLocate;
        }

        private void RunScheduledAutoLocate()
        {
            // The window can be closed between scheduling and the delayed call.
            if (this == null)
                return;
            if (SessionState.GetBool(AutoLocateSessionKey, false))
                return;

            Recheck();
            if (companionStatus.Status != bCompanionStatus.NotLocated)
                return;

            SessionState.SetBool(AutoLocateSessionKey, true);
            RunAutoLocate(true);
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

            Recheck();
        }

        private void StopUnsupportedCompanion()
        {
            string label = string.IsNullOrWhiteSpace(companionStatus.ConflictingProcessName)
                ? "the running bHapticsOSC app"
                : $"'{companionStatus.ConflictingProcessName}'";

            if (!EditorUtility.DisplayDialog(
                    "Stop the unsupported companion app",
                    $"Close {label}?\n\nIt holds the VRChat OSC port, so the supported build receives nothing while "
                    + "it runs. Any unsaved companion settings may be lost.",
                    "Stop it",
                    "Cancel"))
                return;

            if (bCompanionStatusDetector.TryStopUnsupported(out int stoppedCount, out string error))
            {
                actionMessage = stoppedCount == 1
                    ? "Closed the unsupported companion app."
                    : $"Closed {stoppedCount} unsupported companion processes.";
                actionMessageType = MessageType.Info;
            }
            else
            {
                actionMessage = $"Unable to close the companion app: {error}";
                actionMessageType = MessageType.Error;
            }

            Recheck();
        }

        private void Recheck()
        {
            companionStatus = bCompanionStatusDetector.Detect(true);
            environment = bEnvironmentProbes.Probe(true);
            Repaint();
        }

        private void ConsumeAvatarSetupCompleteFlag()
        {
            if (!SessionState.GetBool(AvatarSetupCompleteSessionKey, false))
                return;

            SessionState.SetBool(AvatarSetupCompleteSessionKey, false);
            avatarSetupJustCompleted = true;
        }

        private static string SafeGetDirectoryName(string path)
        {
            try
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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
            bCompanionSetupWindow.ShowUnattended();
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

                if (status.HasConflictingProcess)
                {
                    EditorGUILayout.HelpBox(
                        $"'{status.ConflictingProcessName}' is also running and will compete for the VRChat OSC port. "
                        + "Open Setup Assistant to close it.",
                        MessageType.Warning);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Setup Assistant"))
                        bCompanionSetupWindow.ShowWindow();
                    if (GUILayout.Button("Recheck"))
                        bCompanionStatusDetector.InvalidateCache();
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
                case bCompanionStatus.ForeignBuild:
                    return result.IsRunning
                        ? "Unsupported bHapticsOSC build is running"
                        : "Unsupported bHapticsOSC build installed";
                case bCompanionStatus.RunningUninspectable:
                    return "Companion app is running but could not be inspected";
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
                    return "No running bHapticsOSC process or remembered executable was found. Use Find automatically, download it, or locate an existing copy.";
                case bCompanionStatus.MissingPath:
                    return "The remembered executable path no longer exists. Locate the app again or download the matching version.";
                case bCompanionStatus.InvalidProduct:
                    return string.IsNullOrWhiteSpace(result.DetectedProductName)
                        ? $"The selected file does not identify itself as {bCompanionRequirements.ProductName}."
                        : $"The selected file identifies itself as '{result.DetectedProductName}', not {bCompanionRequirements.ProductName}.";
                case bCompanionStatus.ForeignBuild:
                    return BuildForeignDetails(result);
                case bCompanionStatus.RunningUninspectable:
                    return string.IsNullOrWhiteSpace(result.DetectedProcessName)
                        ? "A bHapticsOSC process is running, but Windows would not say which file it came from, so its version could not be checked. This usually means the app was started as administrator while Unity was not. Use Locate existing app to point at its executable, or restart both at the same permission level."
                        : $"'{result.DetectedProcessName}' is running, but Windows would not say which file it came from, so its version could not be checked. This usually means the app was started as administrator while Unity was not. Use Locate existing app to point at its executable, or restart both at the same permission level.";
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

        /// <summary>
        /// The upstream bHaptics release is the app most users already have, and it looks
        /// correct from the outside. Say plainly that it is a different build and that it has
        /// to be replaced, not updated - its version number is not comparable to this fork's.
        /// </summary>
        private static string BuildForeignDetails(bCompanionStatusResult result)
        {
            string identity = string.IsNullOrWhiteSpace(result.DetectedProductName)
                ? "A different bHapticsOSC build"
                : $"'{result.DetectedProductName}'";
            string version = string.IsNullOrWhiteSpace(result.DetectedVersion)
                ? string.Empty
                : $" (version {result.DetectedVersion})";
            string running = result.IsRunning
                ? " Stop it first: it holds the VRChat OSC port."
                : string.Empty;

            return $"{identity}{version} is installed, not the build this package needs. It does not understand the "
                   + $"compressed contact parameters the avatar setup generates, so haptics will not respond. "
                   + $"Replace it with version {result.RequiredVersion} of the maintained build.{running}";
        }

        internal static string GetDownloadButtonLabel(bCompanionStatusResult result)
            => result.Status == bCompanionStatus.ForeignBuild
                ? "Download the supported build"
                : "Download matching version";

        internal static MessageType GetMessageType(bCompanionStatusResult result)
        {
            switch (result.Status)
            {
                case bCompanionStatus.InvalidProduct:
                case bCompanionStatus.Outdated:
                case bCompanionStatus.ForeignBuild:
                    return MessageType.Error;
                case bCompanionStatus.NotLocated:
                case bCompanionStatus.MissingPath:
                case bCompanionStatus.UnknownVersion:
                case bCompanionStatus.RunningUninspectable:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
#endif
