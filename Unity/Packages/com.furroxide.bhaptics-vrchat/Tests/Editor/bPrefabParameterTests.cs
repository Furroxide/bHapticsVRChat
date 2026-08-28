#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
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

        /// <summary>
        /// Which of the app's device tokens a prefab for each bDeviceType is allowed to address.
        ///
        /// The AppNodeCounts check above only proves a token is one the app knows about, not that
        /// it is the one this prefab is for. An ArmLeft prefab whose receivers say HandL sails
        /// straight through it, because HandL is a perfectly real key, and the mistake then only
        /// shows up as the wrong Tactosy buzzing on someone's arm.
        ///
        /// Vest is a single prefab covering two of the app's devices, so VEST maps to both halves.
        /// VEST_FRONT and VEST_BACK have no prefab of their own today - bDevice looks for
        /// VestFront.prefab and VestBack.prefab, gets null, and those cases self-ignore - but they
        /// are listed so this stays a complete statement about the enum rather than a list of
        /// whichever members happened to have an asset when it was written.
        /// </summary>
        private static readonly Dictionary<bDeviceType, string[]> ExpectedTokens =
            new Dictionary<bDeviceType, string[]>
            {
                { bDeviceType.HEAD, new[] { "Head" } },
                { bDeviceType.VEST, new[] { "VestFront", "VestBack" } },
                { bDeviceType.VEST_FRONT, new[] { "VestFront" } },
                { bDeviceType.VEST_BACK, new[] { "VestBack" } },
                { bDeviceType.ARM_LEFT, new[] { "ForearmL" } },
                { bDeviceType.ARM_RIGHT, new[] { "ForearmR" } },
                { bDeviceType.HAND_LEFT, new[] { "HandL" } },
                { bDeviceType.HAND_RIGHT, new[] { "HandR" } },
                { bDeviceType.FOOT_LEFT, new[] { "FootL" } },
                { bDeviceType.FOOT_RIGHT, new[] { "FootR" } },
            };

        /// <summary>
        /// A documented exemption from the map above, deliberately not folded into it.
        ///
        /// The two mobile arm prefabs ship addressing bOSC/v2m/HandL/0 and bOSC/v2m/HandR/0 - the
        /// very addresses the mobile hand prefabs already claim - instead of ForearmL and
        /// ForearmR. Nothing about the mobile build asks for that: the companion app builds its
        /// v2m addresses from the same DeviceSchemes rows as its v2 ones, so bOSC/v2m/ForearmL/0
        /// is subscribed to and would work. It reads as the mobile arm prefabs having been
        /// authored from the hand ones and keeping the parameter, and the effect on hardware is
        /// that an arm contact drives the hand Tactosy while the arm Tactosy stays silent.
        ///
        /// Correcting it means re-authoring two shipped prefabs, which is a change of its own and
        /// not one this test gets to force by going red. Naming the two cases here keeps every
        /// other mobile prefab - head, vest, both hands - under the strict map, and leaves the
        /// arms as something a reader is told about rather than a hole they have to spot. Delete
        /// this table when the prefabs are fixed.
        /// </summary>
        private static readonly Dictionary<bDeviceType, string> MobileArmPrefabsAddressTheHand =
            new Dictionary<bDeviceType, string>
            {
                { bDeviceType.ARM_LEFT, "HandL" },
                { bDeviceType.ARM_RIGHT, "HandR" },
            };

        /// <summary>
        /// The scheme is captured rather than skipped over so each column can be held to its own
        /// address table: bOSC/v2 is the desktop one and bOSC/v2m the Quest one. A desktop prefab
        /// quietly carrying a v2m address, or the reverse, is the same class of mistake as a wrong
        /// device token - a prefab that looks entirely fine and whose parameters land in a table
        /// nothing on that platform is feeding.
        /// </summary>
        private static readonly Regex NodeParameter = new Regex(
            @"^bOSC/(?<scheme>v2m|v2)/(?<device>[A-Za-z]+)/(?<node>\d+)/(?<scope>self|others)$",
            RegexOptions.Compiled);

        /// <summary>Punch receivers are generated, not authored, but may appear on a saved prefab.</summary>
        private static readonly Regex PunchParameter = new Regex(
            @"^bOSC/v2/Punch/(VestFront|VestBack)/\d+/(Light|Hard)$",
            RegexOptions.Compiled);

        private static IEnumerable<TestCaseData> AllPrefabs()
        {
            foreach (KeyValuePair<bDeviceType, bDeviceTemplate> entry in bDevice.AllTemplates)
            {
                yield return Case(entry.Value.PrefabMesh, entry.Key, "With Mesh", false);
                yield return Case(entry.Value.Prefab, entry.Key, "Without Mesh", false);
                yield return Case(entry.Value.PrefabMeshMobile, entry.Key, "Mobile With Mesh", true);
                yield return Case(entry.Value.PrefabMobile, entry.Key, "Mobile Without Mesh", true);
            }
        }

        /// <summary>
        /// The device used to be spent entirely on the test's display name, which is why the
        /// assertions could only ever talk about a parameter string in isolation. It is a real
        /// argument now, along with whether this is one of the Quest columns, so a case knows both
        /// which device's prefab it is looking at and which of the two address tables that prefab
        /// is supposed to be drawing from.
        /// </summary>
        private static TestCaseData Case(GameObject prefab, bDeviceType device, string column, bool mobile)
            => new TestCaseData(prefab, device, mobile).SetName($"{column}/{device}");

        /// <summary>
        /// The tokens this prefab is allowed to address: the strict map, plus the mobile arm
        /// exemption when - and only when - the prefab comes from one of the two Quest columns.
        /// The desktop arm prefabs are correct and stay strictly checked.
        /// </summary>
        private static HashSet<string> ExpectedTokensFor(bDeviceType device, bool mobile)
        {
            var allowed = new HashSet<string>(ExpectedTokens[device]);

            if (mobile && MobileArmPrefabsAddressTheHand.TryGetValue(device, out string exemption))
                allowed.Add(exemption);

            return allowed;
        }

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
        public void EveryReceiverParameter_UsesASchemeTheCompanionAppListensFor(
            GameObject prefab,
            bDeviceType device,
            bool mobile)
        {
            // Several device/column combinations legitimately ship no prefab - there are no
            // mobile foot prefabs, for one - and that is not this test's business.
            if (prefab == null)
                Assert.Ignore("No prefab in this column.");

            // A bDeviceType with no entry is not a pass, it is a device nobody has written the
            // expected token down for. Say that here rather than letting a KeyNotFoundException
            // fall out of the lookup below and read as an unrelated crash.
            Assert.That(
                ExpectedTokens.ContainsKey(device),
                Is.True,
                $"{device} has no entry in ExpectedTokens; add the app device token its prefabs address.");

            HashSet<string> allowedTokens = ExpectedTokensFor(device, mobile);
            string expectedScheme = mobile ? "v2m" : "v2";

            foreach (string parameter in ParametersOf(prefab))
            {
                if (PunchParameter.IsMatch(parameter))
                    continue;

                Match match = NodeParameter.Match(parameter);
                Assert.That(
                    match.Success,
                    Is.True,
                    $"'{parameter}' on {prefab.name} is not an address the companion app subscribes to.");

                Assert.That(
                    match.Groups["scheme"].Value,
                    Is.EqualTo(expectedScheme),
                    $"'{parameter}' on {prefab.name} is in a {(mobile ? "mobile" : "desktop")} column, "
                    + $"so it has to use the bOSC/{expectedScheme}/ table.");

                string token = match.Groups["device"].Value;
                Assert.That(
                    AppNodeCounts.ContainsKey(token),
                    Is.True,
                    $"'{parameter}' names device '{token}', which is not in the app's DeviceSchemes table.");

                Assert.That(
                    allowedTokens.Contains(token),
                    Is.True,
                    $"'{parameter}' on {prefab.name} is a {device} prefab addressing the app's '{token}' device; "
                    + $"it should address {string.Join(" or ", allowedTokens)}.");

                int node = int.Parse(match.Groups["node"].Value);
                Assert.That(
                    node,
                    Is.InRange(0, AppNodeCounts[token] - 1),
                    $"'{parameter}' is outside the {AppNodeCounts[token]} nodes the app subscribes to for {token}.");
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
#endif
