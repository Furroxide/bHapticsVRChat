#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace bHapticsOSC.VRChat
{
    public static class bPunch
    {
        public const string EnabledParameter = "bOSC/v2/Punch/Enabled";
        public const string RippleParameter = "bOSC/v2/Punch/Ripple";
        public const string StrengthParameter = "bOSC/v2/Punch/Strength";
        public const string DurationParameter = "bOSC/v2/Punch/Duration";

        public const string EnabledMenuPath = "bHapticsOSC/Punch/Enabled";
        public const string RippleMenuPath = "bHapticsOSC/Punch/Ripple";
        public const string StrengthMenuPath = "bHapticsOSC/Punch/Strength";
        public const string DurationMenuPath = "bHapticsOSC/Punch/Duration";

        private const string ReceiverRootName = "bHapticsOSC Punch Receivers";
        private const string LegacyReceiverRootName = "Punch Receivers";
        private const float LightMinVelocity = 0.75f;
        private const float HardMinVelocity = 1.75f;

        private static readonly string[] VerifiedImpactTags =
        {
            "Hand",
            "HandL",
            "HandR",
            "Foot",
            "FootL",
            "FootR"
        };

        private static readonly PunchBand[] Bands =
        {
            new PunchBand("Light", LightMinVelocity),
            new PunchBand("Hard", HardMinVelocity)
        };

        private static readonly Regex V2VestParameter = new Regex(
            @"^bOSC/v2/(?<panel>VestFront|VestBack)/(?<node>\d+)/(self|others)$",
            RegexOptions.Compiled);

        private static readonly Regex V1VestParameter = new Regex(
            @"^bOSC_v1_(?<panel>VestFront|VestBack)_(?<node>\d+)$",
            RegexOptions.Compiled);

        private static readonly Regex LegacyVestParameter = new Regex(
            @"^bHapticsOSC_Vest_(?<panel>Front|Back)_(?<node>\d+)$",
            RegexOptions.Compiled);

        public static bool ApplyReceivers(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return false;

            if (!editorComp.AllUserSettings.TryGetValue(bDevice.AllTemplates[bDeviceType.VEST], out bUserSettings settings))
                return false;

            if (settings.CurrentPrefab == null)
                return false;

            RemoveGeneratedReceivers(settings.CurrentPrefab.transform);

            ContactReceiver[] sourceReceivers = settings.CurrentPrefab
                .GetComponentsInChildren<ContactReceiver>(true)
                .Where(IsPunchSourceReceiver)
                .OrderBy(receiver => receiver.parameter, System.StringComparer.Ordinal)
                .ToArray();

            if (sourceReceivers.Length <= 0)
                return false;

            GameObject root = new GameObject(ReceiverRootName);
            Undo.RegisterCreatedObjectUndo(root, $"[{bHapticsOSCIntegration.SystemName}] Created Punch Receivers");
            Undo.SetTransformParent(root.transform, settings.CurrentPrefab.transform, $"[{bHapticsOSCIntegration.SystemName}] Created Punch Receivers");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            foreach (ContactReceiver sourceReceiver in sourceReceivers)
                CreatePunchReceivers(root.transform, sourceReceiver);

            EditorUtility.SetDirty(settings.CurrentPrefab);
            return true;
        }

        public static bool HasReceivers(bHapticsOSCIntegration editorComp)
        {
            if (editorComp == null || editorComp.AllUserSettings == null)
                return false;

            if (!editorComp.AllUserSettings.TryGetValue(bDevice.AllTemplates[bDeviceType.VEST], out bUserSettings settings))
                return false;

            return settings.CurrentPrefab != null
                   && settings.CurrentPrefab.GetComponentsInChildren<ContactReceiver>(true).Any(IsPunchReceiver);
        }

        private static void CreatePunchReceivers(Transform root, ContactReceiver sourceReceiver)
        {
            if (!TryParseVestNode(sourceReceiver.parameter, out string panel, out int node))
                return;

            foreach (PunchBand band in Bands)
            {
                GameObject obj = new GameObject($"{panel}_{node}_{band.Name}");
                Undo.RegisterCreatedObjectUndo(obj, $"[{bHapticsOSCIntegration.SystemName}] Created Punch Receiver");
                Undo.SetTransformParent(obj.transform, root, $"[{bHapticsOSCIntegration.SystemName}] Created Punch Receiver");
                CopyWorldTransform(sourceReceiver.rootTransform == null ? sourceReceiver.transform : sourceReceiver.rootTransform, obj.transform);

                ContactReceiver receiver = Undo.AddComponent<ContactReceiver>(obj);
                receiver.rootTransform = obj.transform;
                receiver.shapeType = sourceReceiver.shapeType;
                receiver.radius = sourceReceiver.radius;
                receiver.height = sourceReceiver.height;
                receiver.size = sourceReceiver.size;
                receiver.position = sourceReceiver.position;
                receiver.rotation = sourceReceiver.rotation;
                receiver.localOnly = sourceReceiver.localOnly;
                receiver.contentTypes = sourceReceiver.contentTypes;
                receiver.collisionTags = new List<string>(VerifiedImpactTags);
                receiver.allowSelf = false;
                receiver.allowOthers = true;
                receiver.useFaceProximity = false;
                receiver.receiverType = ContactReceiver.ReceiverType.OnEnter;
                receiver.parameter = $"bOSC/v2/Punch/{panel}/{node}/{band.Name}";
                receiver.minVelocity = band.MinVelocity;

                EditorUtility.SetDirty(receiver);
                EditorUtility.SetDirty(obj);
            }
        }

        private static bool IsPunchSourceReceiver(ContactReceiver receiver)
            => receiver != null
               && receiver.allowOthers
               && !string.IsNullOrWhiteSpace(receiver.parameter)
               && !IsPunchReceiver(receiver)
               && TryParseVestNode(receiver.parameter, out _, out _);

        private static bool IsPunchReceiver(ContactReceiver receiver)
            => receiver != null
               && !string.IsNullOrWhiteSpace(receiver.parameter)
               && receiver.parameter.StartsWith("bOSC/v2/Punch/", System.StringComparison.Ordinal);

        private static bool TryParseVestNode(string parameter, out string panel, out int node)
        {
            panel = null;
            node = -1;

            if (string.IsNullOrWhiteSpace(parameter))
                return false;

            Match match = V2VestParameter.Match(parameter);
            if (match.Success)
                return TryReadNode(match, false, out panel, out node);

            match = V1VestParameter.Match(parameter);
            if (match.Success)
                return TryReadNode(match, false, out panel, out node);

            match = LegacyVestParameter.Match(parameter);
            if (match.Success)
                return TryReadNode(match, true, out panel, out node);

            return false;
        }

        private static bool TryReadNode(Match match, bool oneBased, out string panel, out int node)
        {
            panel = match.Groups["panel"].Value;
            if (panel == "Front")
                panel = "VestFront";
            else if (panel == "Back")
                panel = "VestBack";

            if (!int.TryParse(match.Groups["node"].Value, out node))
                return false;

            if (oneBased)
                node -= 1;

            return node >= 0 && node < 20;
        }

        private static void RemoveGeneratedReceivers(Transform vestRoot)
        {
            RemoveGeneratedReceiverRoot(vestRoot.Find(ReceiverRootName), false);
            RemoveGeneratedReceiverRoot(vestRoot.Find(LegacyReceiverRootName), true);
        }

        private static void RemoveGeneratedReceiverRoot(Transform root, bool requireGeneratedReceiver)
        {
            if (root == null)
                return;

            if (requireGeneratedReceiver
                && !root.GetComponentsInChildren<ContactReceiver>(true).Any(IsPunchReceiver))
            {
                return;
            }

            Undo.DestroyObjectImmediate(root.gameObject);
        }

        private static void CopyWorldTransform(Transform source, Transform target)
        {
            target.position = source.position;
            target.rotation = source.rotation;
            SetWorldScale(target, source.lossyScale);
        }

        private static void SetWorldScale(Transform target, Vector3 worldScale)
        {
            Transform parent = target.parent;
            target.localScale = parent == null
                ? worldScale
                : new Vector3(
                    parent.lossyScale.x == 0f ? 0f : worldScale.x / parent.lossyScale.x,
                    parent.lossyScale.y == 0f ? 0f : worldScale.y / parent.lossyScale.y,
                    parent.lossyScale.z == 0f ? 0f : worldScale.z / parent.lossyScale.z);
        }

        private readonly struct PunchBand
        {
            public readonly string Name;
            public readonly float MinVelocity;

            public PunchBand(string name, float minVelocity)
            {
                Name = name;
                MinVelocity = minVelocity;
            }
        }
    }
}
#endif
