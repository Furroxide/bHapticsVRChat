#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// How much a single setup step is asking of the user, ordered so that the worst wins.
    ///
    /// The panel used to draw every row identically and leave the state as the word "Ready" or
    /// "Action" inside a bold line, so a working install read exactly like a broken one. This is
    /// the value the presentation layer colours, sizes and orders by; nothing else decides how
    /// loud a row is.
    /// </summary>
    internal enum bStepState
    {
        /// <summary>Nothing to do. Collapses to a single line.</summary>
        Ok,

        /// <summary>Not checkable from here - off-platform, or a probe that came back empty.</summary>
        Unknown,

        /// <summary>Works, but something is still needed before playing.</summary>
        Attention,

        /// <summary>Haptics will not work until this is dealt with.</summary>
        Blocked,
    }

    /// <summary>One button a step offers. The primary action is what the panel promotes.</summary>
    internal readonly struct bStepAction
    {
        internal bStepAction(string label, Action run, bool isPrimary = false)
        {
            Label = label ?? string.Empty;
            Run = run;
            IsPrimary = isPrimary;
        }

        internal string Label { get; }
        internal Action Run { get; }
        internal bool IsPrimary { get; }
        internal bool Enabled => Run != null;
    }

    /// <summary>
    /// One row of the setup panel, already reduced to what the UI needs.
    ///
    /// The split between <see cref="Detail"/> and <see cref="Explanation"/> is the whole point:
    /// Detail is one short sentence shown inline when something is wrong, Explanation is the long
    /// prose that used to be shown unconditionally and now lives in a tooltip and a disclosure.
    /// A satisfied row shows neither - only <see cref="Value"/>.
    /// </summary>
    internal readonly struct bSetupStep
    {
        internal bSetupStep(
            string id,
            string title,
            bStepState state,
            string value = null,
            string detail = null,
            string explanation = null,
            params bStepAction[] actions)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            State = state;
            Value = value ?? string.Empty;
            Detail = detail ?? string.Empty;
            Explanation = explanation ?? string.Empty;
            Actions = actions ?? Array.Empty<bStepAction>();
        }

        /// <summary>Stable key. Used for USS classes and for remembering disclosure state.</summary>
        internal string Id { get; }

        internal string Title { get; }
        internal bStepState State { get; }

        /// <summary>The one-line answer, shown dimmed on the right of a satisfied row.</summary>
        internal string Value { get; }

        /// <summary>One short sentence. Shown only when the step is not satisfied.</summary>
        internal string Detail { get; }

        /// <summary>The long form. Tooltip, plus the "Why this matters" disclosure.</summary>
        internal string Explanation { get; }

        internal bStepAction[] Actions { get; }

        /// <summary>
        /// Whether this step is asking the user for something. This is the predicate that decides
        /// how loud a row is drawn - a step that needs attention opens into a tinted card with its
        /// sentence and its buttons, anything else stays a single quiet line - so Unknown is
        /// deliberately left out of it. "We could not check this" is not a fault to shout about,
        /// and folding it in here would also throw away the one useful thing those rows carry,
        /// because only a row that needs nothing shows its <see cref="Value"/>.
        /// </summary>
        internal bool NeedsAttention => State == bStepState.Attention || State == bStepState.Blocked;

        /// <summary>
        /// Whether this step was actually checked and came back fine.
        ///
        /// This is deliberately not the negation of <see cref="NeedsAttention"/>, and the gap
        /// between them is the whole point: an Unknown step asks nothing of the user, so it stays
        /// quiet, but it was never verified either, so nothing may count it towards a group or a
        /// summary claiming that everything is in order.
        /// </summary>
        internal bool IsSatisfied => State == bStepState.Ok;
    }

    /// <summary>A titled run of steps. Collapses to its header once every step in it has passed.</summary>
    internal sealed class bSetupGroup
    {
        internal bSetupGroup(string id, string title, IReadOnlyList<bSetupStep> steps)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Steps = steps ?? Array.Empty<bSetupStep>();
        }

        internal string Id { get; }
        internal string Title { get; }
        internal IReadOnlyList<bSetupStep> Steps { get; }

        /// <summary>
        /// True only when every step in the group was checked and came back fine. This drives the
        /// automatic collapse, so it asks about <see cref="bSetupStep.IsSatisfied"/> rather than
        /// about <see cref="bSetupStep.NeedsAttention"/>: a step whose probe came back empty is
        /// not asking for anything, but folding it away behind an "all set" header would present a
        /// check that never ran as one that passed. An empty group stays clean - there is nothing
        /// in it to be unsure about, and it is not drawn at all.
        /// </summary>
        internal bool IsClean
        {
            get
            {
                foreach (bSetupStep step in Steps)
                {
                    if (!step.IsSatisfied)
                        return false;
                }

                return true;
            }
        }

        internal bStepState WorstState
        {
            get
            {
                bStepState worst = bStepState.Ok;
                foreach (bSetupStep step in Steps)
                {
                    if (step.State > worst)
                        worst = step.State;
                }

                return worst;
            }
        }
    }

    /// <summary>
    /// The callbacks a step's buttons invoke. Supplied by whichever surface is drawing, because
    /// the model knows what should be offered but not how this window performs it.
    /// </summary>
    internal sealed class bSetupActions
    {
        internal Action Recheck;
        internal Action InstallOrLocate;
        internal Action Launch;
        internal Action LocateExisting;
        internal Action StopUnsupported;
        internal Action OpenReleases;
        internal Action OpenPlayerDownloads;
        internal Action OpenOscGuide;
    }

    /// <summary>
    /// Turns everything the editor has observed into the rows the panel draws.
    ///
    /// This is deliberately free of GUI calls and of the VRCFury compile guard, so it can be
    /// exercised directly in tests through the seams the detectors already expose
    /// (<see cref="bCompanionStatusDetector.RunningProcessProvider"/> and
    /// <see cref="bEnvironmentProbes.OverrideProvider"/>), and so the setup window can still be
    /// built on a project that has not installed VRCFury yet - telling the user to install it is
    /// one of that window's jobs.
    /// </summary>
    internal static class bSetupModel
    {
        internal const string GroupPc = "pc";
        internal const string GroupAvatar = "avatar";
        internal const string GroupProject = "project";

        internal const string StepCompanion = "companion";
        internal const string StepConflict = "conflict";
        internal const string StepPlayer = "player";
        internal const string StepOsc = "osc";
        internal const string StepChain = "chain";
        internal const string StepAvatar = "avatar";
        internal const string StepAvatarsSdk = "avatars-sdk";
        internal const string StepVrcFury = "vrcfury";

        /// <summary>
        /// Builds every group, in the order the user needs them.
        ///
        /// The old window drew the near-static package checklist first and the per-session
        /// bHaptics Player and VRChat OSC rows last, below the fold of a 560px window - which is
        /// backwards, because those two change between play sessions and are the usual cause of
        /// silent no-haptics. The avatar step is passed in rather than built here: inspecting an
        /// avatar needs the VRChat SDK and VRCFury, and this file has to compile without them.
        /// </summary>
        internal static IReadOnlyList<bSetupGroup> Build(
            bCompanionStatusResult companion,
            bEnvironment environment,
            bSetupStep? avatarStep,
            bSetupActions actions)
        {
            if (actions == null)
                actions = new bSetupActions();

            return new[]
            {
                new bSetupGroup(GroupPc, "On your PC", BuildPcSteps(companion, environment, actions)),
                new bSetupGroup(
                    GroupAvatar,
                    "Your avatar",
                    avatarStep.HasValue ? new[] { avatarStep.Value } : Array.Empty<bSetupStep>()),
                new bSetupGroup(GroupProject, "Unity project", BuildProjectSteps()),
            };
        }

        /// <summary>
        /// The companion step on its own, for the surfaces that report only that one thing - the
        /// inspector strip and the pre-upload console warning. Both used to carry their own copy
        /// of the status wording; now there is one.
        /// </summary>
        internal static bSetupStep DescribeCompanion(bCompanionStatusResult result)
            => BuildCompanionStep(result, new bSetupActions());

        /// <summary>The worst thing across every group - what the header pill reports.</summary>
        internal static bStepState WorstState(IReadOnlyList<bSetupGroup> groups)
        {
            bStepState worst = bStepState.Ok;
            if (groups == null)
                return worst;

            foreach (bSetupGroup group in groups)
            {
                if (group.WorstState > worst)
                    worst = group.WorstState;
            }

            return worst;
        }

        /// <summary>
        /// The single step the header banner leads with: the first blocking one, or failing that
        /// the first that merely needs attention. Groups are already in urgency order, so first
        /// wins.
        ///
        /// Only when there is neither does it fall back to the first step that could not be
        /// checked, so that the banner is not left declaring everything ready over a probe that
        /// came back empty. That last one is a report and not a task, and the banner is expected
        /// to look at the state it got back before wording it as something to go and do.
        /// </summary>
        internal static bSetupStep? FirstActionable(IReadOnlyList<bSetupGroup> groups)
        {
            if (groups == null)
                return null;

            bSetupStep? firstAttention = null;
            bSetupStep? firstUnchecked = null;
            foreach (bSetupGroup group in groups)
            {
                foreach (bSetupStep step in group.Steps)
                {
                    if (step.State == bStepState.Blocked)
                        return step;

                    if (step.State == bStepState.Attention && firstAttention == null)
                        firstAttention = step;

                    if (step.State == bStepState.Unknown && firstUnchecked == null)
                        firstUnchecked = step;
                }
            }

            return firstAttention ?? firstUnchecked;
        }

        /// <summary>A short phrase for the header pill, in place of the standing intro paragraph.</summary>
        internal static string DescribeOverall(IReadOnlyList<bSetupGroup> groups)
        {
            int blocked = 0;
            int attention = 0;
            if (groups != null)
            {
                foreach (bSetupGroup group in groups)
                {
                    foreach (bSetupStep step in group.Steps)
                    {
                        if (step.State == bStepState.Blocked)
                            blocked++;
                        else if (step.State == bStepState.Attention)
                            attention++;
                    }
                }
            }

            if (blocked > 0)
                return blocked == 1 ? "1 problem" : blocked + " problems";

            if (attention > 0)
                return attention == 1 ? "1 thing to do" : attention + " things to do";

            // Nothing is asking for anything, but a check that never ran is not a check that
            // passed, and "Ready to play" printed over an unread OSC setting is how a silent
            // no-haptics session begins. It is reported as a bare count rather than as a problem,
            // and the pill's colour comes from WorstState, so this stays grey rather than turning
            // amber - the user is being told what is not known, not handed a job.
            int notChecked = CountUnchecked(groups);
            if (notChecked > 0)
                return notChecked == 1 ? "1 not checked" : notChecked + " not checked";

            return "Ready to play";
        }

        /// <summary>
        /// How many steps could not be checked at all. Unknown is nothing to fix, but it is not a
        /// pass either, so every surface that would otherwise announce an all-clear reports this
        /// count instead of quietly rounding it down to "fine".
        /// </summary>
        internal static int CountUnchecked(IReadOnlyList<bSetupGroup> groups)
        {
            int count = 0;
            if (groups == null)
                return count;

            foreach (bSetupGroup group in groups)
            {
                foreach (bSetupStep step in group.Steps)
                {
                    if (step.State == bStepState.Unknown)
                        count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ on your PC

        private static IReadOnlyList<bSetupStep> BuildPcSteps(
            bCompanionStatusResult companion,
            bEnvironment environment,
            bSetupActions actions)
        {
            var steps = new List<bSetupStep> { BuildCompanionStep(companion, actions) };

            if (companion.HasConflictingProcess)
                steps.Add(BuildConflictStep(companion, actions));

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                steps.Add(new bSetupStep(
                    StepPlayer,
                    "bHaptics Player and VRChat OSC",
                    bStepState.Unknown,
                    "Checked on your PC",
                    "Not checkable from this editor.",
                    "Both live on the Windows PC you play on, so they cannot be checked from here."));

                return steps;
            }

            steps.Add(BuildPlayerStep(environment, actions));
            steps.Add(BuildOscStep(environment, actions));

            if (environment.HasHapticAvatar)
            {
                steps.Add(new bSetupStep(
                    StepChain,
                    "Haptic avatar seen",
                    bStepState.Ok,
                    environment.HapticAvatarName,
                    null,
                    "VRChat has loaded '" + environment.HapticAvatarName + "' with this package's haptic "
                    + "parameters, so the avatar side of the chain is working. This is the only proof "
                    + "available inside Unity that the whole thing hangs together."));
            }

            return steps;
        }

        private static bSetupStep BuildCompanionStep(bCompanionStatusResult result, bSetupActions actions)
        {
            const string title = "bHapticsOSC companion app";
            string required = result.RequiredVersion;
            string detected = result.DetectedVersion;

            // DetectedVersion falls back to string.Empty, and the ReadyRunning Value below already
            // allows for that, so the sentence that sits beside it has to survive the same blank
            // rather than rendering "Version  meets the ... requirement". No Ready state can carry
            // an empty version today - CompareVersions throws on one it cannot parse, so a result
            // with no version never gets that far - but the two halves of a single step disagreeing
            // about whether the value can be empty is how that quietly stops being true.
            string versionPhrase = string.IsNullOrWhiteSpace(detected)
                ? "The installed version"
                : "Version " + detected;

            switch (result.Status)
            {
                case bCompanionStatus.ReadyRunning:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Ok,
                        string.IsNullOrWhiteSpace(detected) ? "Running" : "Running · " + detected,
                        null,
                        $"{versionPhrase} meets the {required} requirement and is currently running.");

                case bCompanionStatus.ReadyStopped:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Attention,
                        null,
                        "Installed, but not running.",
                        $"{versionPhrase} meets the {required} requirement. Launch it before using "
                        + "haptics in VRChat - nothing reaches your gear while it is closed.",
                        new bStepAction("Start bHapticsOSC", actions.Launch, true));

                case bCompanionStatus.NotLocated:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Blocked,
                        null,
                        "Not found on this PC.",
                        "No running bHapticsOSC process or remembered executable was found. Install it, "
                        + "search the usual download folders, or point at an existing copy if you keep it "
                        + "somewhere unusual.",
                        new bStepAction("Install the companion app", actions.InstallOrLocate, true),
                        new bStepAction("Locate existing app", actions.LocateExisting));

                case bCompanionStatus.MissingPath:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Blocked,
                        null,
                        "The remembered app is no longer on disk.",
                        "The remembered executable path no longer exists - it was moved, renamed or "
                        + "deleted. Locate the app again, or download the matching version.",
                        new bStepAction("Install the companion app", actions.InstallOrLocate, true),
                        new bStepAction("Locate existing app", actions.LocateExisting));

                case bCompanionStatus.InvalidProduct:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Blocked,
                        null,
                        "That file is not bHapticsOSC.",
                        string.IsNullOrWhiteSpace(result.DetectedProductName)
                            ? "The selected file does not identify itself as "
                              + bCompanionRequirements.ProductName + "."
                            : $"The selected file identifies itself as '{result.DetectedProductName}', not "
                              + bCompanionRequirements.ProductName + ".",
                        new bStepAction("Locate bHapticsOSC.exe", actions.LocateExisting, true));

                case bCompanionStatus.ForeignBuild:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Blocked,
                        null,
                        "A different bHapticsOSC build is installed.",
                        BuildForeignExplanation(result),
                        new bStepAction("Install the supported build", actions.InstallOrLocate, true),
                        new bStepAction("Open the releases page", actions.OpenReleases));

                case bCompanionStatus.RunningUninspectable:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Attention,
                        null,
                        "Running, but its version could not be checked.",
                        BuildUninspectableExplanation(result),
                        new bStepAction("Locate bHapticsOSC.exe", actions.LocateExisting, true));

                case bCompanionStatus.UnknownVersion:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Attention,
                        null,
                        "Its version could not be read.",
                        $"The app version could not be read from the file. Version {required} or newer is "
                        + "required, so this may or may not work.",
                        new bStepAction("Install the supported build", actions.InstallOrLocate, true));

                case bCompanionStatus.Outdated:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Blocked,
                        null,
                        $"Version {detected} is too old.",
                        $"Detected version {detected}; version {required} or newer is required.",
                        new bStepAction("Install the supported build", actions.InstallOrLocate, true),
                        new bStepAction("Open the releases page", actions.OpenReleases));

                case bCompanionStatus.UnsupportedPlatform:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Unknown,
                        "Checked on your PC",
                        "Not checkable from this editor.",
                        "bHapticsOSC is a portable Windows app. Download, locate and run it on the Windows "
                        + "PC used for VRChat.",
                        new bStepAction("Open the releases page", actions.OpenReleases, true));

                default:
                    return new bSetupStep(
                        StepCompanion,
                        title,
                        bStepState.Unknown,
                        null,
                        "Status unavailable.",
                        "The companion app status could not be established.",
                        new bStepAction("Recheck", actions.Recheck, true));
            }
        }

        /// <summary>
        /// The upstream bHaptics release is the app most users already have, and it looks correct
        /// from the outside. Say plainly that it is a different build and has to be replaced, not
        /// updated - its version number is not comparable to this fork's.
        /// </summary>
        private static string BuildForeignExplanation(bCompanionStatusResult result)
        {
            string identity = string.IsNullOrWhiteSpace(result.DetectedProductName)
                ? "A different bHapticsOSC build"
                : "'" + result.DetectedProductName + "'";
            string version = string.IsNullOrWhiteSpace(result.DetectedVersion)
                ? string.Empty
                : " (version " + result.DetectedVersion + ")";
            string running = result.IsRunning
                ? " Stop it first: it holds the VRChat OSC port."
                : string.Empty;

            return identity + version + " is installed, not the build this package needs. It does not "
                   + "understand the compressed contact parameters the avatar setup generates, so haptics "
                   + "will not respond. Replace it with version " + result.RequiredVersion + " of the "
                   + "maintained build." + running;
        }

        private static string BuildUninspectableExplanation(bCompanionStatusResult result)
        {
            string subject = string.IsNullOrWhiteSpace(result.DetectedProcessName)
                ? "A bHapticsOSC process is running"
                : "'" + result.DetectedProcessName + "' is running";

            return subject + ", but Windows would not say which file it came from, so its version could "
                   + "not be checked. This usually means the app was started as administrator while Unity "
                   + "was not. Point at its executable, or restart both at the same permission level.";
        }

        private static bSetupStep BuildConflictStep(bCompanionStatusResult result, bSetupActions actions)
        {
            string name = string.IsNullOrWhiteSpace(result.ConflictingProcessName)
                ? "Another bHapticsOSC app"
                : "'" + result.ConflictingProcessName + "'";

            return new bSetupStep(
                StepConflict,
                "OSC port conflict",
                bStepState.Blocked,
                null,
                name + " is also running.",
                "Two companion apps cannot share the VRChat OSC port, so only one of them receives "
                + "anything. Haptics may stop working silently until the unsupported one is closed.",
                new bStepAction("Stop the unsupported app", actions.StopUnsupported, true));
        }

        private static bSetupStep BuildPlayerStep(bEnvironment environment, bSetupActions actions)
        {
            const string title = "bHaptics Player";
            const string pairing = "Your devices also need to be paired and switched on inside it, which "
                                   + "cannot be checked from here.";

            if (environment.PlayerRunning == bProbeState.Yes)
            {
                return new bSetupStep(
                    StepPlayer,
                    title,
                    bStepState.Ok,
                    string.IsNullOrEmpty(environment.PlayerVersion)
                        ? "Running"
                        : "Running · " + environment.PlayerVersion,
                    null,
                    "Running and serving on its SDK port. " + pairing);
            }

            if (environment.PlayerInstalled == bProbeState.Yes
                && environment.PlayerRunning == bProbeState.No)
            {
                return new bSetupStep(
                    StepPlayer,
                    title,
                    bStepState.Attention,
                    null,
                    "Installed, but not running.",
                    "Start it and pair your devices before playing - nothing reaches your gear without "
                    + "it. " + pairing);
            }

            // The running probe watches for a listener on the SDK port and falls back to enumerating
            // processes, and both of those can be refused rather than answered - most often when the
            // Player was started elevated and Unity was not. That leaves the install known and the
            // liveness unknown, which is not the same as stopped, and telling someone to start an app
            // they are looking at is how a status panel loses their trust.
            if (environment.PlayerInstalled == bProbeState.Yes)
            {
                return new bSetupStep(
                    StepPlayer,
                    title,
                    bStepState.Unknown,
                    "Installed",
                    "Whether it is running could not be checked.",
                    "It is installed on this PC, but this editor could not tell whether it is running. "
                    + "Start it before playing if it is not already up - nothing reaches your gear "
                    + "without it. " + pairing);
            }

            if (environment.PlayerInstalled == bProbeState.No)
            {
                return new bSetupStep(
                    StepPlayer,
                    title,
                    bStepState.Blocked,
                    null,
                    "Not found on this PC.",
                    "This is bHaptics' own app, and it is what actually drives your gear. The companion "
                    + "app talks to it; without it nothing reaches your devices.",
                    new bStepAction("Get bHaptics Player", actions.OpenPlayerDownloads, true));
            }

            // Nothing came back either way: reading the install path threw instead of reporting a
            // missing file, so the Player may well be sitting there working. Blocked is reserved for
            // a probe that actually said no, because a red row telling a user to install software
            // they already have is worse than admitting the check did not happen. The download stays
            // on offer for the case where it really is absent.
            return new bSetupStep(
                StepPlayer,
                title,
                bStepState.Unknown,
                "Could not be read",
                "Whether it is installed could not be checked.",
                "This editor could not read whether bHaptics Player is on this PC, so it may already be "
                + "here. It is bHaptics' own app, and it is what actually drives your gear - the "
                + "companion app talks to it, and without it nothing reaches your devices.",
                new bStepAction("Get bHaptics Player", actions.OpenPlayerDownloads, true));
        }

        private static bSetupStep BuildOscStep(bEnvironment environment, bSetupActions actions)
        {
            const string title = "VRChat OSC";

            switch (environment.OscEnabled)
            {
                case bProbeState.Yes:
                    return new bSetupStep(
                        StepOsc,
                        title,
                        bStepState.Ok,
                        "Turned on",
                        null,
                        AppendConfigEvidence(environment, "Turned on in VRChat on this PC."));

                case bProbeState.No:
                    return new bSetupStep(
                        StepOsc,
                        title,
                        bStepState.Blocked,
                        null,
                        "Turned off in VRChat.",
                        AppendConfigEvidence(
                            environment,
                            "In VRChat, open the Action Menu and turn on OSC > Enabled. Without it your "
                            + "avatar's touches never reach the companion app."),
                        new bStepAction("How to turn on OSC", actions.OpenOscGuide, true));

                default:
                    return new bSetupStep(
                        StepOsc,
                        title,
                        bStepState.Unknown,
                        "Could not be read",
                        "VRChat's setting could not be read.",
                        AppendConfigEvidence(
                            environment,
                            "In VRChat, open the Action Menu and make sure OSC > Enabled is on."),
                        new bStepAction("How to turn on OSC", actions.OpenOscGuide, true));
            }
        }

        /// <summary>
        /// Adds what VRChat's own files show, when they show something short of the full chain.
        /// The positive case is promoted to its own step instead, so it is not buried here.
        ///
        /// The date is formatted against the invariant culture deliberately. The day-month-year order
        /// is already fixed by the format string, so deferring to the host's culture would not give a
        /// French or Japanese user the layout they actually expect - it would only substitute that
        /// culture's month name into a sentence that stays English either way, which reads worse than
        /// leaving it English. Pinning it also keeps the wording assertable in a test, rather than
        /// leaving the result to whichever locale the runner happens to boot with.
        /// </summary>
        private static string AppendConfigEvidence(bEnvironment environment, string lead)
        {
            if (environment.HasHapticAvatar || !environment.HasSeenOscConfig)
                return lead;

            return lead + "\n\nVRChat last saved an OSC config on "
                        + environment.OscConfigWritten.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                        + ", but none of the recent ones carry this package's haptic parameters yet.";
        }

        // ------------------------------------------------------------------ unity project

        private static IReadOnlyList<bSetupStep> BuildProjectSteps()
        {
            var steps = new List<bSetupStep>();

            PackageInfo avatars = FindPackage(bCompanionRequirements.VrchatAvatarsPackageId);
            steps.Add(avatars != null
                ? new bSetupStep(
                    StepAvatarsSdk,
                    "VRChat Avatars SDK",
                    bStepState.Ok,
                    avatars.version,
                    null,
                    "Installed: " + avatars.version + ".")
                : new bSetupStep(
                    StepAvatarsSdk,
                    "VRChat Avatars SDK",
                    bStepState.Blocked,
                    null,
                    "Not installed.",
                    "Resolve the VRChat Avatars SDK in the Creator Companion. The avatar setup cannot run "
                    + "without it."));

            steps.Add(BuildVrcFuryStep(FindPackage(bCompanionRequirements.VrcFuryPackageId)));
            return steps;
        }

        private static bSetupStep BuildVrcFuryStep(PackageInfo package)
        {
            const string title = "VRCFury";

            if (package == null)
            {
                return new bSetupStep(
                    StepVrcFury,
                    title,
                    bStepState.Blocked,
                    null,
                    "Not installed.",
                    "Resolve VRCFury in the Creator Companion. The generated setup is applied through "
                    + "VRCFury so that your avatar is never modified destructively.");
            }

            if (!bCompanionStatusDetector.TryNormalizeVersion(package.version, out _, out string version))
            {
                return new bSetupStep(
                    StepVrcFury,
                    title,
                    bStepState.Attention,
                    null,
                    "Its version could not be evaluated.",
                    "The installed version could not be evaluated: " + package.version
                    + ". The supported range is >= " + bCompanionRequirements.MinimumVrcFuryVersion
                    + " and < " + bCompanionRequirements.MaximumVrcFuryVersion + ".");
            }

            bool supported =
                bCompanionStatusDetector.CompareVersions(version, bCompanionRequirements.MinimumVrcFuryVersion) >= 0
                && bCompanionStatusDetector.CompareVersions(version, bCompanionRequirements.MaximumVrcFuryVersion) < 0;

            if (supported)
            {
                return new bSetupStep(
                    StepVrcFury,
                    title,
                    bStepState.Ok,
                    version,
                    null,
                    "Installed: " + version + ".");
            }

            return new bSetupStep(
                StepVrcFury,
                title,
                bStepState.Blocked,
                null,
                "Version " + version + " is outside the supported range.",
                "Installed: " + version + "; the supported range is >= "
                + bCompanionRequirements.MinimumVrcFuryVersion + " and < "
                + bCompanionRequirements.MaximumVrcFuryVersion + ". Change it in the Creator Companion.");
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
    }
}
#endif
