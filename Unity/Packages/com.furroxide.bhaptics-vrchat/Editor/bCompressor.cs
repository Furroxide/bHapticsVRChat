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
        /// </summary>
        private const string PointIdPattern = "^bOSC/v2/(.+)/(?:self|others)$";
        private const string PointIdReplacement = "$1";

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
        /// </summary>
        private static readonly RegionPlan[] Plans =
        {
            new RegionPlan
            {
                Device = bDeviceType.VEST,
                RegionId = "Torso",
                Axes = EncoderAxes.XYZ,
                // Front and back in one region: the Z coordinate tells them apart.
                SourcePattern = @"^bOSC/v2/(?:VestFront|VestBack)/\d+/(?:self|others)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.HEAD,
                RegionId = "Head",
                Axes = EncoderAxes.X,
                SourcePattern = @"^bOSC/v2/Head/\d+/(?:self|others)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.ARM_LEFT,
                RegionId = "ForearmL",
                Axes = EncoderAxes.XYZ,
                SourcePattern = @"^bOSC/v2/ForearmL/\d+/(?:self|others)$"
            },
            new RegionPlan
            {
                Device = bDeviceType.ARM_RIGHT,
                RegionId = "ForearmR",
                Axes = EncoderAxes.XYZ,
                SourcePattern = @"^bOSC/v2/ForearmR/\d+/(?:self|others)$"
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

        /// <summary>Strips any groups this previously added, for turning the option back off.</summary>
        public static int RemoveGroups(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return 0;

            int removed = 0;

            foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
            {
                if (settings.CurrentPrefab == null)
                    continue;

                foreach (ContactCompressorGroup group in settings.CurrentPrefab.GetComponentsInChildren<ContactCompressorGroup>(true))
                {
                    Undo.DestroyObjectImmediate(group);
                    removed++;
                }
            }

            return removed;
        }

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

                var matcher = new System.Text.RegularExpressions.Regex(plan.SourcePattern);
                foreach (VRC.Dynamics.ContactReceiver receiver in settings.CurrentPrefab.GetComponentsInChildren<VRC.Dynamics.ContactReceiver>(true))
                {
                    if (receiver != null
                        && !string.IsNullOrWhiteSpace(receiver.parameter)
                        && matcher.IsMatch(receiver.parameter))
                    {
                        before++;
                    }
                }

                after += ContactEncoderSolver.ReceiverCount(plan.Axes);
            }
        }
    }
}
#endif
