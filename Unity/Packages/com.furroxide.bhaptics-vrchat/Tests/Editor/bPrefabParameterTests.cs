using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using VRC.Dynamics;

namespace bHapticsOSC.VRChat.Tests
{
    /// <summary>
    /// Guards the receiver parameter names in the shipped prefabs against the only thing that
    /// actually consumes them: the companion app's address table.
    ///
    /// This exists because the entire desktop "Without Mesh" column used to name its receivers
    /// bOSC_v1_VestBack_0. The app listens for /avatar/parameters/bOSC/v2/VestBack/0/self and
    /// for a legacy bHapticsOSC_Vest_Back_1 form, and matches neither of those against
    /// bOSC_v1_*; unmatched OSC addresses are dropped without a log line. bAnimator registers
    /// whatever string is on the receiver verbatim as an expression parameter, so nothing
    /// downstream corrected it either. The column was reachable by unticking "Show mesh" and
    /// produced an avatar on which punch impacts worked - those are named separately - and
    /// per-node touch did nothing at all.
    ///
    /// A name is only wrong in a way a human notices at the very end of the chain, on a headset,
    /// so assert it here instead.
    /// </summary>
    public class bPrefabParameterTests
    {
        /// <summary>
        /// Node counts the companion app subscribes to, from its DeviceSchemes table in
        /// External/bHapticsOSC/bHapticsOSC/VRChatSupport.cs. A receiver numbered at or beyond
        /// these is never listened for.
        /// </summary>
        private static readonly Dictionary<string, int> AppNodeCounts = new Dictionary<string, int>
        {
            { "Head", 6 },
            { "VestFront", 20 },
            { "VestBack", 20 },
            { "ForearmL", 6 },
            { "ForearmR", 6 },
            { "HandL", 3 },
            { "HandR", 3 },
            { "FootL", 3 },
            { "FootR", 3 },
        };

        private static readonly Regex NodeParameter = new Regex(
            @"^bOSC/v2m?/(?<device>[A-Za-z]+)/(?<node>\d+)/(?<scope>self|others)$",
            RegexOptions.Compiled);

        /// <summary>Punch receivers are generated, not authored, but may appear on a saved prefab.</summary>
        private static readonly Regex PunchParameter = new Regex(
            @"^bOSC/v2/Punch/(VestFront|VestBack)/\d+/(Light|Hard)$",
            RegexOptions.Compiled);

        private static IEnumerable<TestCaseData> AllPrefabs()
        {
            foreach (KeyValuePair<bDeviceType, bDeviceTemplate> entry in bDevice.AllTemplates)
            {
                yield return Case(entry.Value.PrefabMesh, entry.Key, "With Mesh");
                yield return Case(entry.Value.Prefab, entry.Key, "Without Mesh");
                yield return Case(entry.Value.PrefabMeshMobile, entry.Key, "Mobile With Mesh");
                yield return Case(entry.Value.PrefabMobile, entry.Key, "Mobile Without Mesh");
            }
        }

        private static TestCaseData Case(GameObject prefab, bDeviceType device, string column)
            => new TestCaseData(prefab).SetName($"{column}/{device}");

        private static List<string> ParametersOf(GameObject prefab)
        {
            var found = new List<string>();
            if (prefab == null)
                return found;

            foreach (ContactReceiver receiver in prefab.GetComponentsInChildren<ContactReceiver>(true))
            {
                if (!string.IsNullOrWhiteSpace(receiver.parameter))
                    found.Add(receiver.parameter);
            }

            return found;
        }

        [TestCaseSource(nameof(AllPrefabs))]
        public void EveryReceiverParameter_UsesASchemeTheCompanionAppListensFor(GameObject prefab)
        {
            // Several device/column combinations legitimately ship no prefab - there are no
            // mobile foot prefabs, for one - and that is not this test's business.
            if (prefab == null)
                Assert.Ignore("No prefab in this column.");

            foreach (string parameter in ParametersOf(prefab))
            {
                if (PunchParameter.IsMatch(parameter))
                    continue;

                Match match = NodeParameter.Match(parameter);
                Assert.That(
                    match.Success,
                    Is.True,
                    $"'{parameter}' on {prefab.name} is not an address the companion app subscribes to.");

                string device = match.Groups["device"].Value;
                Assert.That(
                    AppNodeCounts.ContainsKey(device),
                    Is.True,
                    $"'{parameter}' names device '{device}', which is not in the app's DeviceSchemes table.");

                int node = int.Parse(match.Groups["node"].Value);
                Assert.That(
                    node,
                    Is.InRange(0, AppNodeCounts[device] - 1),
                    $"'{parameter}' is outside the {AppNodeCounts[device]} nodes the app subscribes to for {device}.");
            }
        }

        /// <summary>
        /// The desktop prefabs drive each motor from a pair of receivers - one that fires on your
        /// own touch and one on everyone else's. A node carrying only half the pair is a motor
        /// that responds to one and not the other, which reads as flaky hardware.
        ///
        /// The Quest prefabs are deliberately not held to this: they are /others-only, to stay
        /// inside the mobile contact budget.
        /// </summary>
        [Test]
        public void DesktopReceivers_PairSelfAndOthersOnEveryNode(
            [Values("With Mesh", "Without Mesh")] string column)
        {
            foreach (KeyValuePair<bDeviceType, bDeviceTemplate> entry in bDevice.AllTemplates)
            {
                GameObject prefab = column == "With Mesh" ? entry.Value.PrefabMesh : entry.Value.Prefab;
                if (prefab == null)
                    continue;

                var scopes = new Dictionary<string, HashSet<string>>();
                foreach (string parameter in ParametersOf(prefab))
                {
                    Match match = NodeParameter.Match(parameter);
                    if (!match.Success)
                        continue;

                    string node = match.Groups["device"].Value + "/" + match.Groups["node"].Value;
                    if (!scopes.TryGetValue(node, out HashSet<string> seen))
                        scopes[node] = seen = new HashSet<string>();

                    Assert.That(
                        seen.Add(match.Groups["scope"].Value),
                        Is.True,
                        $"{prefab.name}: {parameter} is declared twice.");
                }

                foreach (KeyValuePair<string, HashSet<string>> node in scopes)
                {
                    Assert.That(
                        node.Value,
                        Is.EquivalentTo(new[] { "self", "others" }),
                        $"{column}/{prefab.name}: node {node.Key} does not have both self and others.");
                }
            }
        }

        /// <summary>
        /// The specific string that caused this. Kept as its own assertion so a regression names
        /// itself rather than surfacing as a pattern mismatch.
        /// </summary>
        [Test]
        public void NoPrefabStillUsesTheV1ParameterScheme()
        {
            foreach (KeyValuePair<bDeviceType, bDeviceTemplate> entry in bDevice.AllTemplates)
            {
                foreach (GameObject prefab in new[]
                         {
                             entry.Value.PrefabMesh,
                             entry.Value.Prefab,
                             entry.Value.PrefabMeshMobile,
                             entry.Value.PrefabMobile,
                         })
                {
                    foreach (string parameter in ParametersOf(prefab))
                    {
                        Assert.That(
                            parameter.StartsWith("bOSC_v1_", System.StringComparison.Ordinal),
                            Is.False,
                            $"{prefab.name} still carries the v1 parameter '{parameter}'.");
                    }
                }
            }
        }
    }
}
