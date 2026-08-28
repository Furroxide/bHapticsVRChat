#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury && bHapticsOSC_HasContactCompressor
using System.Collections.Generic;
using Furroxide.ContactCompressor;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// Opts the bHaptics device prefabs into contact compression.
    ///
    /// A TactSuit X40 needs 40 motors, and the straightforward way to detect touch on 40 motors is
    /// 40 receivers - 80 once self and others are split. VRChat allows 32 contacts before an avatar
    /// is rated Very Poor, and has a known bug where clustered receivers start reporting wrong
    /// values, which bHaptics users hit routinely.
    ///
    /// Adding a group here replaces those receivers, at build time, with six box receivers that
    /// encode where the contact happened. The prefabs are untouched: the per-motor receivers stay
    /// exactly as authored, and are what the compressor reads to learn the motor layout.
    /// </summary>
    public static class bCompressor
    {
        /// <summary>
        /// Padding, in metres, around each region's motors.
        ///
        /// Face proximity saturates once a sender reaches a box face, and padding must exceed the
        /// radius of the largest collider expected to touch you. 0.10 covers VRChat's stock hand
        /// and foot colliders with room to spare.
        /// </summary>
        private const float PaddingMetres = 0.10f;

        /// <summary>
        /// Collapses the self and others receivers at one motor into a single manifest point.
        /// Without this a consumer spreading a contact over its four nearest points would spend two
        /// of them on the same motor.
        ///
        /// Both shipped namings normalise to the same "Device/node" id: the "With Mesh" prefabs use
        /// bOSC/v2/VestFront/7/others, the "Without Mesh" ones bOSC_v1_VestFront_7. That form is
        /// not cosmetic - the companion app splits each point id at its last slash to find the
        /// device and motor number, so an id without one is skipped and that motor never fires.
        /// (.NET allows a group name to be reused across alternation branches; whichever branch
        /// matched supplies the capture.)
        /// </summary>
        internal const string PointIdPattern =
            @"^(?:bOSC/v2/(?<dev>[^/]+)/(?<node>\d+)/(?:self|others)|bOSC_v1_(?<dev>[A-Za-z]+)_(?<node>\d+))$";
        internal const string PointIdReplacement = "${dev}/${node}";

        private struct RegionPlan
        {
            internal bDeviceType Device;
            internal string RegionId;
            internal EncoderAxes Axes;
            internal string SourcePattern;
        }

        /// <summary>
        /// Which devices are worth compressing, and along which axes.
        ///
        /// Only devices where the saving is real: hands and feet carry three motors behind six
        /// receivers, so encoding them would cost as much as it saves and is left alone. The head's
        /// four motors sit in a straight line, so one axis - two receivers - describes it fully.
        ///
        /// Source patterns deliberately match only the per-motor node parameters, which keeps the
        /// punch receivers (generated separately, and velocity-triggered rather than positional)
        /// out of the fit.
        ///
        /// Each pattern covers both desktop namings - "With Mesh" prefabs use
        /// bOSC/v2/Device/n/self|others, "Without Mesh" ones bOSC_v1_Device_n - because both carry
        /// the same dense motor grid and both are reachable from the Show Mesh toggle.
        ///
        /// The Quest/mobile prefabs (bOSC/v2m/...) are deliberately absent, but only the head and
        /// arms are a closed case: they carry two receivers each against the six an XYZ region
        /// emits, so compressing them would cost more contacts than it saves. The mobile vest is
        /// not - it carries ten, which six would genuinely reduce. It is left out because the
        /// reference layout, the parity tests and the shipped manifest are all built from the
        /// desktop prefabs, so extending compression to Quest is a change to what Quest avatars
        /// upload rather than a bug fix. The mobile arms are also parameterised as HandL/HandR
        /// rather than ForearmL/R, so they would need their own plan regardless.
        ///
        /// A device whose pattern matches nothing is skipped outright rather than given a group
        /// that cannot fit.
        /// </summary>
        private static readonly RegionPlan[] Plans =
        {
            new RegionPlan
            {
                Device = bDeviceType.VEST,
                RegionId = "Torso",
                Axes = EncoderAxes.XYZ,
                // Front and back in one region: the Z coordinate tells them apart.
                SourcePattern = @"^(?:bOSC/v2/(?:VestFront|VestBack)/\d+/(?:self|others)|bOSC_v1_(?:VestFront|VestBack)_\d+)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.HEAD,
                RegionId = "Head",
                Axes = EncoderAxes.X,
                SourcePattern = @"^(?:bOSC/v2/Head/\d+/(?:self|others)|bOSC_v1_Head_\d+)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.ARM_LEFT,
                RegionId = "ForearmL",
                Axes = EncoderAxes.XYZ,
                SourcePattern = @"^(?:bOSC/v2/ForearmL/\d+/(?:self|others)|bOSC_v1_ForearmL_\d+)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.ARM_RIGHT,
                RegionId = "ForearmR",
                Axes = EncoderAxes.XYZ,
                SourcePattern = @"^(?:bOSC/v2/ForearmR/\d+/(?:self|others)|bOSC_v1_ForearmR_\d+)$"
            }
        };

        /// <summary>Devices this can compress, for the inspector to describe.</summary>
        public static IEnumerable<bDeviceType> SupportedDevices
        {
            get
            {
                foreach (RegionPlan plan in Plans)
                    yield return plan.Device;
            }
        }

        /// <summary>One plan's identity, so tests can check the patterns against the real prefabs.</summary>
        internal readonly struct PlanInfo
        {
            internal PlanInfo(bDeviceType device, string regionId, string sourcePattern)
            {
                Device = device;
                RegionId = regionId;
                SourcePattern = sourcePattern;
            }

            internal bDeviceType Device { get; }
            internal string RegionId { get; }
            internal string SourcePattern { get; }
        }

        /// <summary>
        /// The plans, for tests. A source pattern that stops matching the shipped prefabs is
        /// invisible at authoring time and only surfaces as a refused avatar upload, so it needs
        /// checking against the prefabs themselves rather than against a restatement of itself.
        /// </summary>
        internal static IEnumerable<PlanInfo> PlansForTests
        {
            get
            {
                foreach (RegionPlan plan in Plans)
                    yield return new PlanInfo(plan.Device, plan.RegionId, plan.SourcePattern);
            }
        }

        /// <summary>How many of a prefab's receivers a plan takes over. Exposed for tests.</summary>
        internal static int CountMatchingReceiversForTests(GameObject host, string sourcePattern)
            => CountMatchingReceivers(host, sourcePattern);

        /// <summary>
        /// Adds a compression group to every selected device that has a plan. Returns how many were
        /// added. Existing groups are replaced so re-running the setup is idempotent.
        /// </summary>
        public static int ApplyGroups(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return 0;

            int applied = 0;

            foreach (RegionPlan plan in Plans)
            {
                if (!bDevice.AllTemplates.TryGetValue(plan.Device, out bDeviceTemplate template))
                    continue;

                if (!editorComp.AllUserSettings.TryGetValue(template, out bUserSettings settings))
                    continue;

                if (settings.CurrentPrefab == null)
                    continue;

                GameObject host = settings.CurrentPrefab;

                ContactCompressorGroup group = host.GetComponent<ContactCompressorGroup>();

                // A group whose pattern matches nothing fails the fit, and the build hook rejects
                // the whole avatar on the first invalid group - so one mobile device would block an
                // upload that every other region was ready for. Skip the device instead, and clear
                // any group left over from a prefab variant that used to match.
                if (CountMatchingReceivers(host, plan.SourcePattern) == 0)
                {
                    if (group != null)
                        Undo.DestroyObjectImmediate(group);

                    continue;
                }

                if (group == null)
                    group = Undo.AddComponent<ContactCompressorGroup>(host);
                else
                    Undo.RecordObject(group, $"[{bHapticsOSCIntegration.SystemName}] Configure Contact Compression");

                group.regionId = plan.RegionId;
                group.parameterPrefix = ContactParameterNames.DefaultPrefix;
                group.axes = plan.Axes;
                group.paddingMetres = PaddingMetres;
                group.sourceRoot = host.transform;
                group.frameOverride = host.transform;
                group.sourceParameterPattern = plan.SourcePattern;
                group.pointIdPattern = PointIdPattern;
                group.pointIdReplacement = PointIdReplacement;

                // Preserve whatever the prefabs use, so enabling compression does not quietly change
                // who can see the avatar react.
                group.localOnly = LocalOnlyMode.PreserveSource;
                group.keepSourceReceivers = false;

                EditorUtility.SetDirty(group);
                EditorUtility.SetDirty(host);
                applied++;
            }

            return applied;
        }

        /// <summary>
        /// Writes the manifest describing this avatar's actual motor layout, next to the other
        /// generated assets. Returns the path, or null if there was nothing to export.
        ///
        /// This runs automatically rather than being left to the user, because a manifest that
        /// describes slightly different geometry fails silently - it drives the wrong motors
        /// instead of erroring, which is close to impossible to diagnose from the outside. A
        /// reference layout produced by a separate offline path was found to be off by more than
        /// one motor row, so the safe default is to emit the layout from the same code that builds
        /// the avatar, every time.
        /// </summary>
        public static string ExportManifest(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return null;

            var fits = new List<Furroxide.ContactCompressor.Editor.FittedRegion>();
            foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
            {
                if (settings.CurrentPrefab == null)
                    continue;

                foreach (ContactCompressorGroup group in
                         settings.CurrentPrefab.GetComponentsInChildren<ContactCompressorGroup>(true))
                {
                    var fit = Furroxide.ContactCompressor.Editor.ContactRegionFitter.Fit(group);
                    if (fit.IsValid)
                    {
                        fits.Add(fit);
                        continue;
                    }

                    // Not a partial success. The build hook refuses the whole avatar on the first
                    // group that will not fit, so a manifest written around the failure would only
                    // let the setup claim success for an upload that is already doomed.
                    Debug.LogError($"[{bHapticsOSCIntegration.SystemName}] Region '{group.regionId}' could not be "
                                   + "fitted, so no manifest was written: " + string.Join("; ", fit.Errors), group);
                    return null;
                }
            }

            if (fits.Count == 0)
                return null;

            string folder = bHapticsOSCIntegration.GeneratedAssetsRoot;
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            string path = System.IO.Path.Combine(folder, ManifestFileName);
            var manifest = Furroxide.ContactCompressor.Editor.ContactCompressorManifestBuilder.Build(
                fits, bHapticsOSCIntegration.SystemName);

            System.IO.File.WriteAllText(path,
                Furroxide.ContactCompressor.Editor.ContactCompressorManifestBuilder.ToJson(manifest));
            AssetDatabase.Refresh();

            int points = 0;
            foreach (var region in manifest.regions) points += region.points.Count;
            Debug.Log($"[{bHapticsOSCIntegration.SystemName}] Wrote contact compression manifest for "
                      + $"{manifest.regions.Count} region(s) and {points} motor(s) to {path}\n"
                      + "Copy this into the companion app's Config folder. It describes this avatar "
                      + "specifically - a manifest from another avatar will drive the wrong motors.");

            return path;
        }

        /// <summary>File name the companion app looks for.</summary>
        public const string ManifestFileName = "contact-compressor.json";

        /// <summary>
        /// Removes only the groups <see cref="ApplyGroups"/> puts on the planned device prefab
        /// roots, leaving any a user authored themselves elsewhere in the hierarchy alone. Used to
        /// back out of a failed setup, where destroying the user's own work would be a worse
        /// outcome than the failure.
        /// </summary>
        public static int RemoveGeneratedGroups(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return 0;

            int removed = 0;

            foreach (RegionPlan plan in Plans)
            {
                if (!bDevice.AllTemplates.TryGetValue(plan.Device, out bDeviceTemplate template))
                    continue;

                if (!editorComp.AllUserSettings.TryGetValue(template, out bUserSettings settings))
                    continue;

                if (settings.CurrentPrefab == null)
                    continue;

                ContactCompressorGroup group = settings.CurrentPrefab.GetComponent<ContactCompressorGroup>();
                if (group == null)
                    continue;

                Undo.DestroyObjectImmediate(group);
                removed++;
            }

            return removed;
        }

        // RemoveGroups used to live here. It claimed to strip "any groups this previously
        // added" but actually swept GetComponentsInChildren over the whole device subtree and
        // destroyed every ContactCompressorGroup it found, whoever had put it there - and it
        // ran on the default path, because ConsolidateContacts is false unless the user turns
        // it on. RemoveGeneratedGroups above is the narrowed version: it looks only at the
        // planned device prefab roots and deletes the single group each of those can hold, so
        // a group authored deeper in a device subtree, or anywhere else on the avatar, now
        // survives. It does not tell authorship apart, and cannot - ContactCompressorGroup is
        // [DisallowMultipleComponent] and carries no field recording who wrote it, so a group
        // a user put on a prefab root themselves is still removed. That is consistent with
        // ApplyGroups, which takes such a group over and overwrites its settings rather than
        // adding a second one. The narrower scope is the whole of the improvement, and this is
        // now the only entry point, so the subtree-wide behaviour cannot be reached by picking
        // the friendlier-looking name.

        /// <summary>
        /// Receiver counts before and after, for the inspector. Counts only the devices that have a
        /// plan and are actually selected.
        /// </summary>
        public static void EstimateSavings(bHapticsOSCIntegration editorComp, out int before, out int after)
        {
            before = 0;
            after = 0;

            if (editorComp == null || editorComp.AllUserSettings == null)
                return;

            foreach (RegionPlan plan in Plans)
            {
                if (!bDevice.AllTemplates.TryGetValue(plan.Device, out bDeviceTemplate template))
                    continue;

                if (!editorComp.AllUserSettings.TryGetValue(template, out bUserSettings settings))
                    continue;

                if (settings.CurrentPrefab == null)
                    continue;

                int matched = CountMatchingReceivers(settings.CurrentPrefab, plan.SourcePattern);
                if (matched == 0)
                    continue;

                before += matched;
                after += ContactEncoderSolver.ReceiverCount(plan.Axes);
            }
        }

        /// <summary>
        /// How many of a prefab's receivers a plan would actually take over. Zero means the device
        /// uses a naming this plan does not cover - the mobile prefabs, in practice - and must be
        /// left alone rather than given a group that cannot fit.
        /// </summary>
        private static int CountMatchingReceivers(GameObject host, string sourcePattern)
        {
            if (host == null || string.IsNullOrWhiteSpace(sourcePattern))
                return 0;

            var matcher = new System.Text.RegularExpressions.Regex(sourcePattern);
            int matched = 0;

            foreach (VRC.Dynamics.ContactReceiver receiver in
                     host.GetComponentsInChildren<VRC.Dynamics.ContactReceiver>(true))
            {
                if (receiver != null
                    && !string.IsNullOrWhiteSpace(receiver.parameter)
                    && matcher.IsMatch(receiver.parameter))
                {
                    matched++;
                }
            }

            return matched;
        }
    }
}
#endif
