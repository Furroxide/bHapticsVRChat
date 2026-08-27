#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury && bHapticsOSC_HasContactCompressor
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;

namespace bHapticsOSC.VRChat.Tests
{
    /// <summary>
    /// Checks the compression plans against the prefabs that actually ship, in all four variants.
    ///
    /// The plans are regexes over receiver parameter names, and the four prefab sets do not name
    /// their receivers the same way: "With Mesh" uses bOSC/v2/VestFront/7/others, "Without Mesh"
    /// uses bOSC_v1_VestFront_7, and the two Quest sets use bOSC/v2m/. A plan that matches nothing
    /// is silent in the inspector and only surfaces as a refused avatar upload, so the counts have
    /// to be asserted against the prefabs rather than against a restatement of the pattern.
    /// </summary>
    public class bCompressorPrefabCoverageTests
    {
        static GameObject WithMesh(bDeviceType device) => bDevice.AllTemplates[device].PrefabMesh;
        static GameObject WithoutMesh(bDeviceType device) => bDevice.AllTemplates[device].Prefab;
        static GameObject MobileWithMesh(bDeviceType device) => bDevice.AllTemplates[device].PrefabMeshMobile;
        static GameObject MobileWithoutMesh(bDeviceType device) => bDevice.AllTemplates[device].PrefabMobile;

        static bCompressor.PlanInfo PlanFor(bDeviceType device)
        {
            foreach (bCompressor.PlanInfo plan in bCompressor.PlansForTests)
                if (plan.Device == device)
                    return plan;

            Assert.Fail($"No compression plan for {device}.");
            return default;
        }

        static int Matches(GameObject prefab, bDeviceType device)
        {
            Assert.IsNotNull(prefab, $"Prefab for {device} did not resolve.");
            return bCompressor.CountMatchingReceiversForTests(prefab, PlanFor(device).SourcePattern);
        }

        // Both desktop sets carry the same dense motor grid, so both have to be compressible. The
        // "Without Mesh" column is the regression: its bOSC_v1_ naming used to match nothing, which
        // attached a group that failed the build.
        [TestCase(bDeviceType.VEST, 80)]
        [TestCase(bDeviceType.HEAD, 8)]
        [TestCase(bDeviceType.ARM_LEFT, 12)]
        [TestCase(bDeviceType.ARM_RIGHT, 12)]
        public void WithMeshPrefabs_AreCompressible(bDeviceType device, int expected)
            => Assert.That(Matches(WithMesh(device), device), Is.EqualTo(expected));

        [TestCase(bDeviceType.VEST, 80)]
        [TestCase(bDeviceType.HEAD, 12)]
        [TestCase(bDeviceType.ARM_LEFT, 12)]
        [TestCase(bDeviceType.ARM_RIGHT, 12)]
        public void WithoutMeshPrefabs_AreCompressible(bDeviceType device, int expected)
            => Assert.That(Matches(WithoutMesh(device), device), Is.EqualTo(expected));

        // The Quest prefabs carry ten vest receivers and two per remaining device, so compressing
        // them to six box receivers would save nothing. They must match zero and be skipped, not
        // match partially and produce a group that cannot fit.
        [TestCase(bDeviceType.VEST)]
        [TestCase(bDeviceType.HEAD)]
        [TestCase(bDeviceType.ARM_LEFT)]
        [TestCase(bDeviceType.ARM_RIGHT)]
        public void MobilePrefabs_AreLeftAlone(bDeviceType device)
        {
            Assert.That(Matches(MobileWithMesh(device), device), Is.Zero);
            Assert.That(Matches(MobileWithoutMesh(device), device), Is.Zero);
        }

        [Test]
        public void Plans_NeverMatchPunchReceivers()
        {
            // Punch receivers are velocity-triggered rather than positional; folding them into a
            // region would encode a position for an impact that has none.
            string[] punchParameters =
            {
                "bOSC/v2/Punch/VestFront/3/Light",
                "bOSC/v2/Punch/VestBack/17/Hard",
                bPunch.EnabledParameter,
                bPunch.StrengthParameter
            };

            foreach (bCompressor.PlanInfo plan in bCompressor.PlansForTests)
            {
                var matcher = new Regex(plan.SourcePattern);
                foreach (string parameter in punchParameters)
                    Assert.That(matcher.IsMatch(parameter), Is.False, $"{plan.RegionId} matched {parameter}");
            }
        }

        /// <summary>
        /// The companion app splits each manifest point id at its last slash to find the device and
        /// motor number, so an id without one is skipped and that motor never fires. Both desktop
        /// namings therefore have to normalise to the same "Device/node" form.
        ///
        /// The two head prefabs genuinely expose different motor counts - the mesh variant models
        /// four, the mesh-free one six - so the expectation is per variant rather than per device.
        /// </summary>
        [TestCase(bDeviceType.VEST, 40, 40)]
        [TestCase(bDeviceType.HEAD, 4, 6)]
        [TestCase(bDeviceType.ARM_LEFT, 6, 6)]
        [TestCase(bDeviceType.ARM_RIGHT, 6, 6)]
        public void PointIds_NormaliseToDeviceAndNode_InBothDesktopNamings(
            bDeviceType device,
            int withMeshMotors,
            int withoutMeshMotors)
        {
            AssertPointIds(WithMesh(device), device, withMeshMotors);
            AssertPointIds(WithoutMesh(device), device, withoutMeshMotors);
        }

        static void AssertPointIds(GameObject prefab, bDeviceType device, int expectedMotors)
        {
            Assert.IsNotNull(prefab, $"Prefab for {device} did not resolve.");

            var ids = new HashSet<string>();
            var matcher = new Regex(PlanFor(device).SourcePattern);

            foreach (ContactReceiver receiver in prefab.GetComponentsInChildren<ContactReceiver>(true))
            {
                if (receiver == null || string.IsNullOrWhiteSpace(receiver.parameter)) continue;
                if (!matcher.IsMatch(receiver.parameter)) continue;

                string id = Regex.Replace(receiver.parameter, bCompressor.PointIdPattern, bCompressor.PointIdReplacement);

                Assert.That(id, Does.Match(@"^[A-Za-z]+/\d+$"),
                    $"'{receiver.parameter}' produced the point id '{id}', which the companion app cannot split.");
                ids.Add(id);
            }

            // The self and others receivers at one motor collapse onto one id, so the distinct ids
            // are the motor count rather than the receiver count.
            Assert.That(ids.Count, Is.EqualTo(expectedMotors),
                $"{prefab.name} produced {ids.Count} distinct motors, expected {expectedMotors}");
        }

        /// <summary>
        /// Every receiver the package ships or generates has to be the SDK3 subclass. The base
        /// VRC.Dynamics.ContactReceiver is not on the avatar whitelist, and the SDK expands that
        /// whitelist to derived types only - so a base-class instance is stripped by the client and
        /// the build panel refuses the upload.
        /// </summary>
        [Test]
        public void ShippedPrefabReceivers_AreAllTheAvatarLegalSubclass()
        {
            var checkedPrefabs = 0;

            foreach (bDeviceTemplate template in bDevice.AllTemplates.Values)
            {
                foreach (GameObject prefab in new[]
                         {
                             template.Prefab, template.PrefabMesh,
                             template.PrefabMobile, template.PrefabMeshMobile
                         })
                {
                    if (prefab == null) continue;
                    checkedPrefabs++;

                    foreach (ContactReceiver receiver in prefab.GetComponentsInChildren<ContactReceiver>(true))
                    {
                        Assert.That(receiver, Is.InstanceOf<VRCContactReceiver>(),
                            $"{prefab.name}/{receiver.name} uses {receiver.GetType().Name}, which VRChat strips.");
                    }
                }
            }

            Assert.That(checkedPrefabs, Is.GreaterThan(0), "No device prefabs resolved, so nothing was checked.");
        }

        /// <summary>
        /// The generated punch receivers are the ones that were wrong: they were created with the
        /// base class, which the SDK build panel refuses. If the user then took its Auto Fix, the
        /// punch parameters vanished while the punch menu VRCFury had already generated stayed
        /// behind, driving nothing.
        /// </summary>
        [Test]
        public void GeneratedPunchReceivers_AreTheAvatarLegalSubclass()
        {
            bDeviceTemplate vest = bDevice.AllTemplates[bDeviceType.VEST];
            Assert.IsNotNull(vest.PrefabMesh, "The vest prefab did not resolve.");

            // bPunch creates its receiver root with `new GameObject`, which lands in whatever scene
            // is active, and registers a long Undo group. Leaving that in the scene the runner
            // happens to have open breaks the setup-cleanup fixture in this same assembly, which
            // asserts on active-scene state. A single-mode empty scene either side is exactly the
            // state that fixture's own teardown establishes, so it is what the rest of the suite
            // already expects; an additive scene cannot be used because the runner's scene is
            // routinely untitled and dirty, which NewScene refuses to add to.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject avatar = null;
            bUserSettings settings = null;
            try
            {
                avatar = new GameObject("bHapticsOSC punch receiver fixture");
                var integration = avatar.AddComponent<bHapticsOSCIntegration>();

                GameObject vestInstance = Object.Instantiate(vest.PrefabMesh, avatar.transform);
                settings = ScriptableObject.CreateInstance<bUserSettings>();
                settings.CurrentPrefab = vestInstance;

                integration.AllUserSettings = new Dictionary<bDeviceTemplate, bUserSettings> { { vest, settings } };

                Assert.That(bPunch.ApplyReceivers(integration), Is.True, "No punch receivers were generated.");

                ContactReceiver[] generated = vestInstance
                    .GetComponentsInChildren<ContactReceiver>(true)
                    .Where(r => r != null
                                && !string.IsNullOrEmpty(r.parameter)
                                && r.parameter.StartsWith("bOSC/v2/Punch/", System.StringComparison.Ordinal))
                    .ToArray();

                Assert.That(generated.Length, Is.GreaterThan(0), "No punch receivers were found after generation.");
                foreach (ContactReceiver receiver in generated)
                {
                    Assert.That(receiver, Is.InstanceOf<VRCContactReceiver>(),
                        $"{receiver.name} uses {receiver.GetType().Name}, which VRChat strips from the avatar.");
                }
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                if (settings != null) Object.DestroyImmediate(settings);

                // Drop the ~160 undo records bPunch registered before the objects they refer to
                // go away, then leave a clean untitled scene behind.
                Undo.ClearAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
#endif
