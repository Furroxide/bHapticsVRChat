#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The setup assistant.
    ///
    /// It used to draw every row through one shared help-box - a bold title over a full paragraph,
    /// identical whether the row was satisfied or blocking - so a working install read exactly
    /// like a broken one and the whole window was about three hundred words of equal weight. All
    /// of that wording still exists, in bSetupModel; what changed is that a satisfied step is now
    /// a single line with its prose on hover, and only what actually needs doing is expanded and
    /// coloured.
    ///
    /// Guarded on UNITY_EDITOR alone, deliberately: telling the user to install VRCFury is one of
    /// this window's jobs, so it has to build and open on a project that has not got it yet.
    /// </summary>
    internal sealed class bCompanionSetupWindow : EditorWindow
    {
        private const string WindowTitle = "bHapticsOSC Setup";
        private const string AvatarSetupCompleteSessionKey = bCompanionRequirements.PackageId + ".avatar-setup-complete";
        private const string AutoLocateSessionKey = bCompanionRequirements.PackageId + ".auto-locate-attempted";
        private const string LayoutPath = "UI/bSetupWindow.uxml";

        private bCompanionStatusResult companionStatus;
        private bEnvironment environment;
        private string actionMessage = string.Empty;
        private bStepState actionMessageState = bStepState.Ok;
        private bool avatarSetupJustCompleted;

        private VisualElement headerIcon;
        private Label headerPill;
        private VisualElement bannerRoot;
        private VisualElement bannerIcon;
        private Label bannerTitle;
        private Label bannerDetail;
        private Button bannerAction;
        private VisualElement toastRoot;
        private VisualElement installerRoot;
        private VisualElement groupsRoot;
        private Label rememberedPathLabel;
        private readonly Dictionary<string, Button> optionButtons = new Dictionary<string, Button>();

        /// <summary>
        /// The installer phase the window last drew. It exists because bCompanionInstaller is a
        /// static pump with nothing to subscribe to: comparing its phase against what is on screen
        /// is the only way to notice that a download has finished, failed or been cancelled.
        /// Everything gated on IsBusy goes stale on that transition - the banner's primary action,
        /// the secondary option buttons, and the whole terminal panel that replaces the progress
        /// bar - so a change here asks for a rebuild rather than a repaint of the bar alone.
        /// </summary>
        private bCompanionInstaller.bInstallPhase renderedInstallerPhase = bCompanionInstaller.bInstallPhase.Idle;

        /// <summary>
        /// The live progress bar and its Cancel button, held so the busy panel is built once per
        /// phase and only updated afterwards. UI Toolkit delivers a click to the same element
        /// instance that captured the pointer down, so replacing these on every editor update -
        /// which is how often a download refreshes - would quietly eat the click that was meant to
        /// cancel it.
        /// </summary>
        private ProgressBar installerProgress;
        private Button installerCancel;

        /// <summary>
        /// Set while the window is being opened by something other than the user, so the
        /// automatic disk search never runs behind an unasked-for window.
        /// </summary>
        private static bool suppressAutoLocateOnce;

        internal static void ShowWindow()
        {
            bCompanionSetupWindow window = GetWindow<bCompanionSetupWindow>();
            window.ApplyChrome();
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

        private void ApplyChrome()
        {
            titleContent = new GUIContent(
                WindowTitle,
                bPackageAssetResolver.LoadAsset<Texture2D>("Textures/UI/bhaptics_icon.png"));
            minSize = new Vector2(460f, 560f);
        }

        private void OnEnable()
        {
            ApplyChrome();
            ConsumeAvatarSetupCompleteFlag();
            Recheck();
            ScheduleAutoLocate();

            // Subtracted before it is added, for the reason bCompanionInstaller does the same
            // around its own pump: a second subscription would outlive the single unsubscribe in
            // OnDisable and leave the phase comparison running twice per update.
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            avatarSetupJustCompleted = false;

            // The visual tree does not survive being disabled, so nothing that points into it may
            // either. Dropping the phase back to Idle alongside the references is what lets a
            // second install in the same session draw at all: a window re-enabled still believing
            // it had rendered Downloading would see no change on the next update and would never
            // build the panel these fields are meant to hold.
            installerProgress = null;
            installerCancel = null;
            renderedInstallerPhase = bCompanionInstaller.bInstallPhase.Idle;
        }

        private void OnFocus() => Recheck();

        /// <summary>The avatar step describes whatever is selected, so a new selection is new state.</summary>
        private void OnSelectionChange() => Rebuild();

        /// <summary>
        /// Only the installer needs a clock. Everything else on this window is repainted by the
        /// events that change it, which is the point of moving off an every-frame OnGUI.
        ///
        /// Two different jobs, deliberately kept apart. A phase change is a change to the whole
        /// window - the banner's action and the secondary options are all disabled while the
        /// installer is busy, and the Done or Failed panel only exists on the structural path - so
        /// it asks for a full rebuild. Watching for that here, rather than only while busy, is what
        /// makes the finished state appear at all: the pump leaves IsBusy false the moment it is
        /// done, and a condition that only looked while busy stopped looking on exactly that frame,
        /// leaving a completed download drawn as a progress bar until something else happened to
        /// refocus the window. Every other tick only moves the bar, and must not touch the
        /// hierarchy the user is trying to click.
        /// </summary>
        private void OnEditorUpdate()
        {
            if (bCompanionInstaller.Phase != renderedInstallerPhase)
            {
                Rebuild();
                return;
            }

            if (bCompanionInstaller.IsBusy)
                UpdateInstallerProgress();
        }

        // ------------------------------------------------------------------ construction

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            bUI.ApplyTheme(root);

            VisualTreeAsset layout = bPackageAssetResolver.LoadAsset<VisualTreeAsset>(LayoutPath);
            if (layout == null)
            {
                // Without the layout there is no window at all, so say why rather than showing a
                // blank panel that looks like the package failed to install.
                root.Add(new HelpBox(
                    "The setup window layout could not be loaded from " + LayoutPath
                    + ". Reimport the bHapticsOSC package.",
                    HelpBoxMessageType.Error));
                return;
            }

            layout.CloneTree(root);
            BindChrome(root);
            Rebuild();
        }

        private void BindChrome(VisualElement root)
        {
            // CloneTree made a fresh set of buttons; the old entries point at orphans. The
            // installer's progress bar and Cancel button are cached for the same reason and are
            // just as orphaned, and the phase has to be forgotten with them so the RefreshInstaller
            // that follows treats this as a transition and rebuilds into the new tree.
            optionButtons.Clear();
            installerProgress = null;
            installerCancel = null;
            renderedInstallerPhase = bCompanionInstaller.bInstallPhase.Idle;

            headerIcon = root.Q<VisualElement>("header-icon");
            headerPill = root.Q<Label>("header-pill");
            bannerRoot = root.Q<VisualElement>("banner");
            bannerIcon = root.Q<VisualElement>("banner-icon");
            bannerTitle = root.Q<Label>("banner-title");
            bannerDetail = root.Q<Label>("banner-detail");
            bannerAction = root.Q<Button>("banner-action");
            toastRoot = root.Q<VisualElement>("toast");
            installerRoot = root.Q<VisualElement>("installer");
            groupsRoot = root.Q<VisualElement>("groups");
            rememberedPathLabel = root.Q<Label>("opt-path");

            Texture2D brand = bPackageAssetResolver.LoadAsset<Texture2D>("Textures/UI/bhaptics_icon.png");
            if (headerIcon != null && brand != null)
                headerIcon.style.backgroundImage = brand;

            // The two-sentence standing paragraph the window used to open with. It answers a
            // question the user asks once, so it is on hover rather than on screen.
            Label title = root.Q<Label>("header-title");
            if (title != null)
            {
                title.tooltip =
                    "The Unity integration creates the avatar setup. The portable Windows companion app "
                    + "and bHaptics Player are separate requirements for using haptics in VRChat.";
            }

            Bind(root, "header-recheck", Recheck);
            Bind(root, "opt-releases", () => Application.OpenURL(bCompanionRequirements.ReleasesUrl));
            Bind(root, "opt-download", () => Application.OpenURL(
                bCompanionRequirements.GetMatchingDownloadUrl(companionStatus.RequiredVersion)));
            Bind(root, "opt-find", () => RunAutoLocate(false));
            Bind(root, "opt-locate", LocateExistingApp);
            Bind(root, "opt-launch", LaunchCompanion);
            Bind(root, "opt-stop", StopUnsupportedCompanion);
            Bind(root, "opt-forget", ForgetRememberedApp);
            Bind(root, "opt-recheck", Recheck);

            Foldout otherOptions = root.Q<Foldout>("other-options");
            if (otherOptions != null)
            {
                const string key = "window.other-options";
                otherOptions.value = bUI.GetFlag(key, false);
                otherOptions.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == otherOptions)
                        bUI.SetFlag(key, evt.newValue);
                });
            }
        }

        private void Bind(VisualElement root, string name, Action action)
        {
            Button button = root.Q<Button>(name);
            if (button == null)
                return;

            button.clicked += action;
            optionButtons[name] = button;
        }

        // ------------------------------------------------------------------ rendering

        private void Rebuild()
        {
            if (groupsRoot == null)
                return;

            IReadOnlyList<bSetupGroup> groups = bSetupModel.Build(
                companionStatus,
                environment,
                BuildAvatarStep(),
                BuildActions());

            RefreshHeader(groups);
            RefreshBanner(groups);
            RefreshToast();
            RefreshInstaller();
            RefreshOptions();

            groupsRoot.Clear();
            foreach (bSetupGroup group in groups)
                groupsRoot.Add(new bStepGroupElement(group));
        }

        private bSetupActions BuildActions() => new bSetupActions
        {
            Recheck = Recheck,
            InstallOrLocate = InstallOrLocate,
            Launch = LaunchCompanion,
            LocateExisting = LocateExistingApp,
            StopUnsupported = StopUnsupportedCompanion,
            OpenReleases = () => Application.OpenURL(bCompanionRequirements.ReleasesUrl),
            OpenPlayerDownloads = () => Application.OpenURL(bCompanionRequirements.BHapticsPlayerUrl),
            OpenOscGuide = () => Application.OpenURL(bCompanionRequirements.VrchatOscGuideUrl),
        };

        private void RefreshHeader(IReadOnlyList<bSetupGroup> groups)
        {
            if (headerPill == null)
                return;

            bStepState worst = bSetupModel.WorstState(groups);
            headerPill.text = bSetupModel.DescribeOverall(groups);
            bUI.SetStateClass(headerPill, "b-pill", worst);
        }

        /// <summary>
        /// The single next thing, promoted above everything that merely wants reading. When there
        /// is nothing to do the banner shrinks to one green line rather than disappearing - an
        /// empty space would not say "you are done".
        ///
        /// The third case is nothing to do but something that could not be checked, and it gets
        /// its own wording: a green "everything is ready" over a probe that came back empty is a
        /// claim the window is in no position to make.
        /// </summary>
        private void RefreshBanner(IReadOnlyList<bSetupGroup> groups)
        {
            if (bannerRoot == null)
                return;

            bSetupStep? next = bSetupModel.FirstActionable(groups);

            if (!next.HasValue)
            {
                bUI.SetStateClass(bannerRoot, "b-banner", bStepState.Ok);
                SetMarker(bannerIcon, bStepState.Ok);
                bannerTitle.text = avatarSetupJustCompleted
                    ? "Avatar setup complete - everything is ready"
                    : "Everything is ready";
                Display(bannerDetail, false);
                Display(bannerAction, false);
                return;
            }

            bSetupStep step = next.Value;
            bUI.SetStateClass(bannerRoot, "b-banner", step.State);
            SetMarker(bannerIcon, step.State);

            // Something that could not be checked is reported, never promoted. Naming the one step
            // and putting its button in the banner would read as "you must fix this", when all the
            // window knows is that it could not look. Nothing becomes unreachable by doing it this
            // way: an unchecked step now keeps its group open, so whatever that row offers is
            // already on screen underneath.
            if (step.State == bStepState.Unknown)
            {
                int notChecked = bSetupModel.CountUnchecked(groups);
                string tail = notChecked == 1
                    ? "1 check could not be run"
                    : notChecked + " checks could not be run";
                bannerTitle.text = avatarSetupJustCompleted
                    ? "Avatar setup complete - " + tail
                    : "Nothing left to do - " + tail;
                bannerDetail.text = step.Detail;
                Display(bannerDetail, !string.IsNullOrEmpty(step.Detail));
                Display(bannerAction, false);
                return;
            }

            bannerTitle.text = step.Title;
            bannerDetail.text = step.Detail;
            Display(bannerDetail, !string.IsNullOrEmpty(step.Detail));

            bStepAction primary = default;
            bool hasPrimary = false;
            foreach (bStepAction action in step.Actions)
            {
                if (!action.Enabled)
                    continue;

                if (!hasPrimary || action.IsPrimary)
                {
                    primary = action;
                    hasPrimary = true;
                }

                if (action.IsPrimary)
                    break;
            }

            Display(bannerAction, hasPrimary);
            if (!hasPrimary)
                return;

            bannerAction.text = primary.Label;
            bannerAction.SetEnabled(!bCompanionInstaller.IsBusy);
            bannerAction.clickable = new Clickable(() => primary.Run?.Invoke());
        }

        private void RefreshToast()
        {
            if (toastRoot == null)
                return;

            toastRoot.Clear();
            if (string.IsNullOrWhiteSpace(actionMessage))
            {
                Display(toastRoot, false);
                return;
            }

            Display(toastRoot, true);
            toastRoot.Add(new HelpBox(actionMessage, ToHelpBoxType(actionMessageState)));
        }

        /// <summary>
        /// The structural pass, and the one place that records which phase is on screen. It stamps
        /// that before the early return on purpose: a window whose layout failed to load would
        /// otherwise report a phase change on every editor update for as long as it stayed open.
        /// </summary>
        private void RefreshInstaller()
        {
            bCompanionInstaller.bInstallPhase phase = bCompanionInstaller.Phase;
            bool phaseChanged = phase != renderedInstallerPhase;
            renderedInstallerPhase = phase;

            if (installerRoot == null)
                return;

            if (bCompanionInstaller.IsBusy)
            {
                Display(installerRoot, true);

                // Kept across every tick of one phase. The parent check covers what the phase
                // cannot: CreateGUI clones a new tree, and the cached bar would then be an orphan
                // that is updated forever without ever being seen.
                if (phaseChanged
                    || installerProgress == null
                    || installerCancel == null
                    || installerProgress.parent != installerRoot)
                {
                    BuildBusyInstaller();
                }

                UpdateInstallerProgress();
                return;
            }

            // Nothing below is reused, so the cached references are dropped before the clear
            // rather than left pointing at elements that have been removed.
            installerProgress = null;
            installerCancel = null;
            installerRoot.Clear();

            if (phase == bCompanionInstaller.bInstallPhase.Idle)
            {
                Display(installerRoot, false);
                return;
            }

            Display(installerRoot, true);

            bool failed = phase == bCompanionInstaller.bInstallPhase.Failed;
            installerRoot.Add(new HelpBox(
                bCompanionInstaller.Message,
                failed ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info));

            var row = new VisualElement();
            row.AddToClassList("b-installer__row");

            if (!failed)
            {
                row.Add(new Button(() =>
                {
                    Recheck();
                    LaunchCompanion();
                })
                { text = "Start it now" });
            }

            row.Add(new Button(() =>
            {
                bCompanionInstaller.Dismiss();
                Recheck();
            })
            { text = "Dismiss" });

            installerRoot.Add(row);
        }

        /// <summary>
        /// Built when the installer enters a busy phase, not once per frame. Cancel ends the
        /// download, which un-busies everything the window disabled on the way in, so it goes
        /// through Rebuild rather than refreshing this panel on its own.
        /// </summary>
        private void BuildBusyInstaller()
        {
            installerRoot.Clear();

            installerProgress = new ProgressBar
            {
                lowValue = 0f,
                highValue = 1f,
                value = bCompanionInstaller.Progress,
                title = bCompanionInstaller.Message,
            };
            installerRoot.Add(installerProgress);

            installerCancel = new Button(() =>
            {
                bCompanionInstaller.Cancel();
                Rebuild();
            })
            { text = "Cancel" };
            installerRoot.Add(installerCancel);
        }

        /// <summary>
        /// The per-tick pass, and the whole reason the busy panel is cached: it may write to the
        /// bar and nothing else. Adding or removing anything here would put a download back to
        /// replacing the Cancel button underneath the pointer that is trying to press it.
        /// </summary>
        private void UpdateInstallerProgress()
        {
            if (installerProgress == null)
                return;

            installerProgress.value = bCompanionInstaller.Progress;
            installerProgress.title = bCompanionInstaller.Message;
        }

        /// <summary>
        /// The secondary controls are always present but should not look available when they
        /// cannot do anything - a disabled button still says the option exists.
        /// </summary>
        private void RefreshOptions()
        {
            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            string remembered = bCompanionStatusDetector.RememberedExecutablePath;

            SetOptionEnabled("opt-find", isWindows);
            SetOptionEnabled("opt-locate", isWindows);
            SetOptionEnabled("opt-launch", companionStatus.Status == bCompanionStatus.ReadyStopped);
            SetOptionEnabled("opt-stop", isWindows && companionStatus.HasUnsupportedProcessRunning);
            SetOptionEnabled("opt-forget", !string.IsNullOrWhiteSpace(remembered));

            if (rememberedPathLabel == null)
                return;

            // Diagnostics, not a decision. It used to sit in the main flow above the primary
            // action; it belongs with the controls that act on it.
            bool hasPath = !string.IsNullOrWhiteSpace(companionStatus.ExecutablePath);
            Display(rememberedPathLabel, hasPath);
            if (hasPath)
            {
                rememberedPathLabel.text = companionStatus.ExecutablePath;
                rememberedPathLabel.tooltip =
                    "Required version: " + companionStatus.RequiredVersion;
            }
        }

        private void SetOptionEnabled(string name, bool enabled)
        {
            if (optionButtons.TryGetValue(name, out Button button))
                button.SetEnabled(enabled && !bCompanionInstaller.IsBusy);
        }

        private static void SetMarker(VisualElement marker, bStepState state)
        {
            if (marker == null)
                return;

            Texture2D icon = bUI.StateIcon(state);
            marker.style.backgroundImage = icon;
            marker.EnableInClassList("b-step__icon--dot", icon == null);
            bUI.SetStateClass(marker, "b-step__icon", state);
        }

        private static void Display(VisualElement element, bool visible)
        {
            if (element != null)
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static HelpBoxMessageType ToHelpBoxType(bStepState state)
        {
            switch (state)
            {
                case bStepState.Blocked: return HelpBoxMessageType.Error;
                case bStepState.Attention: return HelpBoxMessageType.Warning;
                default: return HelpBoxMessageType.Info;
            }
        }

        // ------------------------------------------------------------------ the avatar step

        /// <summary>
        /// The half of the journey the assistant used to leave out entirely. Everything else here
        /// is about software; this is about the user's avatar, which is what they came to do.
        ///
        /// Built here rather than in bSetupModel because inspecting an avatar needs the VRChat SDK
        /// and VRCFury, and the model has to compile without either.
        /// </summary>
        private bSetupStep? BuildAvatarStep()
        {
#if VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
            var avatar = bAvatarSetup.FindAvatar(Selection.activeGameObject);
            bAvatarSetup.bReadiness readiness = bAvatarSetup.Inspect(avatar, out string detail);

            switch (readiness)
            {
                case bAvatarSetup.bReadiness.NoAvatar:
                    return new bSetupStep(
                        bSetupModel.StepAvatar,
                        "Avatar",
                        bStepState.Attention,
                        null,
                        "Select your avatar in the Hierarchy.",
                        "Select the avatar root - the object carrying the VRC Avatar Descriptor - and this "
                        + "window will offer to set it up in one press.");

                case bAvatarSetup.bReadiness.NotHumanoid:
                    return new bSetupStep(
                        bSetupModel.StepAvatar,
                        avatar.name,
                        bStepState.Blocked,
                        null,
                        "This rig is not humanoid.",
                        detail + " bHaptics devices attach to humanoid bones, so the rig has to be set to "
                        + "Humanoid in its import settings before anything can be placed on it.");

                case bAvatarSetup.bReadiness.AlreadySetUp:
                    return new bSetupStep(
                        bSetupModel.StepAvatar,
                        avatar.name,
                        bStepState.Ok,
                        "Set up",
                        null,
                        "Already set up. Upload it with the VRChat SDK to use it. Setting it up again "
                        + "replaces the generated assets and keeps your device positions.",
                        new bStepAction("Set up again", () => RunAvatarSetup(avatar.gameObject), true));

                default:
                    return new bSetupStep(
                        bSetupModel.StepAvatar,
                        avatar.name,
                        bStepState.Attention,
                        null,
                        "Ready to set up.",
                        "One press adds the devices, fits them to this avatar, and builds the VRCFury "
                        + "setup. It is a single undo step, so Ctrl+Z puts the avatar back.",
                        new bStepAction("Set up " + avatar.name, () => RunAvatarSetup(avatar.gameObject), true));
            }
#else
            return new bSetupStep(
                bSetupModel.StepAvatar,
                "Avatar",
                bStepState.Unknown,
                "Waiting on the project",
                "The Unity project is not ready yet.",
                "The VRChat Avatars SDK and VRCFury need to be installed before an avatar can be set up.");
#endif
        }

#if VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
        private void RunAvatarSetup(GameObject avatar)
        {
            if (bAvatarSetup.Run(avatar))
                avatarSetupJustCompleted = true;

            Recheck();
        }
#endif

        // ------------------------------------------------------------------ actions

        /// <summary>
        /// What the companion app's blocked states all want: use what is already on disk if there
        /// is any, and only reach for the network when there is not.
        /// </summary>
        private void InstallOrLocate()
        {
            if (!bCompanionInstaller.IsSupportedPlatform)
            {
                Application.OpenURL(bCompanionRequirements.ReleasesUrl);
                return;
            }

            RunAutoLocate(false);
            if (!companionStatus.IsReady)
                bCompanionInstaller.Begin(companionStatus.RequiredVersion);

            Rebuild();
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
            bSetupStep verdict = bSetupModel.DescribeCompanion(selected);

            if (selected.Lineage == bCompanionBuildLineage.Unrelated)
            {
                SetToast(
                    Path.GetFileName(selectedPath) + " is not a bHapticsOSC app, so it was not remembered. "
                    + verdict.Explanation,
                    bStepState.Blocked);
                Recheck();
                return;
            }

            bCompanionStatusDetector.SetRememberedExecutablePath(selectedPath);
            Recheck();
            SetToast(selectedPath + "\n" + verdict.Explanation, verdict.State);
        }

        private void ForgetRememberedApp()
        {
            bCompanionStatusDetector.SetRememberedExecutablePath(null);
            SessionState.SetBool(AutoLocateSessionKey, false);
            Recheck();
            SetToast("Forgot the remembered companion app.", bStepState.Ok);
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
                SetToast("The search could not be completed: " + exception.Message, bStepState.Attention);
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
                bSetupStep verdict = bSetupModel.DescribeCompanion(found);
                SetToast("Found " + located.ExecutablePath + "\n" + verdict.Explanation, verdict.State);
                return;
            }

            Recheck();
            if (located.Cancelled)
            {
                SetToast("Search cancelled.", bStepState.Ok);
                return;
            }

            if (automatic)
                return;

            SetToast(
                "No bHapticsOSC executable was found in Downloads, on the Desktop, or under the usual "
                + "install folders. Download the matching version, or use Locate existing app if you keep "
                + "it elsewhere.",
                bStepState.Attention);
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
                SetToast("bHapticsOSC launch requested. Use Recheck after the app starts.", bStepState.Ok);
            else
                SetToast("Unable to launch bHapticsOSC: " + error, bStepState.Blocked);

            Recheck();
        }

        private void StopUnsupportedCompanion()
        {
            string label = string.IsNullOrWhiteSpace(companionStatus.ConflictingProcessName)
                ? "the running bHapticsOSC app"
                : "'" + companionStatus.ConflictingProcessName + "'";

            if (!EditorUtility.DisplayDialog(
                    "Stop the unsupported companion app",
                    "Close " + label + "?\n\nIt holds the VRChat OSC port, so the supported build receives "
                    + "nothing while it runs. Any unsaved companion settings may be lost.",
                    "Stop it",
                    "Cancel"))
                return;

            if (bCompanionStatusDetector.TryStopUnsupported(out int stoppedCount, out string error))
            {
                SetToast(
                    stoppedCount == 1
                        ? "Closed the unsupported companion app."
                        : "Closed " + stoppedCount + " unsupported companion processes.",
                    bStepState.Ok);
            }
            else
            {
                SetToast("Unable to close the companion app: " + error, bStepState.Blocked);
            }

            Recheck();
        }

        private void SetToast(string message, bStepState state)
        {
            actionMessage = message;
            actionMessageState = state;
            RefreshToast();
        }

        private void Recheck()
        {
            companionStatus = bCompanionStatusDetector.Detect(true);
            environment = bEnvironmentProbes.Probe(true);
            Rebuild();
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
}
#endif
