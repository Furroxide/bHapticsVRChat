using System.Collections.Generic;
using System.IO;
using System.Linq;
using Furroxide.ContactCompressor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Furroxide.ContactCompressor.Tests
{
    /// <summary>
    /// Checks that the manifest shipped to the desktop app still describes the prefabs the avatar
    /// is actually built from.
    ///
    /// These are produced by two different code paths on purpose: the app's copy is generated
    /// offline by Tools/build_manifest.py, which parses the prefab YAML directly, while the avatar
    /// is built by ContactRegionFitter inside Unity. If they ever disagree - because a motor moved,
    /// a prefab was re-authored, or the fitter changed - nothing would fail loudly. Touches would
    /// simply land on the wrong motors, which is close to impossible to diagnose from the outside.
    ///
    /// Skips rather than fails when the bHaptics package or the manifest is absent, so the Contact
    /// Compressor package remains usable on its own.
    /// </summary>
    public class BHapticsLayoutParityTests
    {
        const string PrefabRoot = "Packages/com.furroxide.bhaptics-vrchat/Runtime/Prefabs/With Mesh";
        const string ManifestRelativePath = "../Decoder/manifests/bhaptics-default.json";

        /// <summary>
        /// Mirrors the plans in bCompressor. Deliberately restated rather than referenced, so this
        /// package's tests do not depend on the bHaptics package's assembly - which means these
        /// strings have to be kept in step with bCompressor.Plans by hand.
        ///
        /// The patterns cover both desktop namings because bCompressor does. Only the "With Mesh"
        /// prefabs are compared against the shipped manifest, though: that manifest describes those
        /// prefabs, and the mesh-free head is a different device (six motors, not four). The
        /// bHaptics package's own bCompressorPrefabCoverageTests covers the other variants.
        /// </summary>
        static readonly object[] RegionCases =
        {
            new object[] { "Torso", "Vest.prefab", EncoderAxes.XYZ, @"^(?:bOSC/v2/(?:VestFront|VestBack)/\d+/(?:self|others)|bOSC_v1_(?:VestFront|VestBack)_\d+)$" },
            new object[] { "Head", "Head.prefab", EncoderAxes.X, @"^(?:bOSC/v2/Head/\d+/(?:self|others)|bOSC_v1_Head_\d+)$" },
            new object[] { "ForearmL", "ArmLeft.prefab", EncoderAxes.XYZ, @"^(?:bOSC/v2/ForearmL/\d+/(?:self|others)|bOSC_v1_ForearmL_\d+)$" },
            new object[] { "ForearmR", "ArmRight.prefab", EncoderAxes.XYZ, @"^(?:bOSC/v2/ForearmR/\d+/(?:self|others)|bOSC_v1_ForearmR_\d+)$" }
        };

        static ContactCompressorManifest LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestRelativePath));
            if (!File.Exists(path))
                Assert.Ignore($"Reference manifest not found at {path}");

            // JsonUtility here, ManifestJson in the app: two readers over one file, so a schema
            // slip shows up on this side too.
            var manifest = ContactCompressorManifestBuilder.FromJson(File.ReadAllText(path));
            Assert.IsNotNull(manifest, "manifest failed to parse");
            return manifest;
        }

        [Test]
        [TestCaseSource(nameof(RegionCases))]
        public void FitterAgreesWithTheShippedManifest(string regionId, string prefabName, EncoderAxes axes, string pattern)
        {
            ContactCompressorManifest manifest = LoadManifest();
            ContactRegionManifest expected = manifest.Find(regionId);
            Assert.IsNotNull(expected, $"manifest has no region '{regionId}'");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{prefabName}");
            if (prefab == null)
                Assert.Ignore($"{prefabName} not found; the bHaptics package is not installed");

            var instance = (GameObject)Object.Instantiate(prefab);
            try
            {
                var group = instance.AddComponent<ContactCompressorGroup>();
                group.regionId = regionId;
                group.parameterPrefix = ContactParameterNames.DefaultPrefix;
                group.axes = axes;
                group.paddingMetres = 0.10f;
                group.sourceRoot = instance.transform;
                group.frameOverride = instance.transform;
                group.sourceParameterPattern = pattern;
                group.pointIdPattern = @"^(?:bOSC/v2/(?<dev>[^/]+)/(?<node>\d+)/(?:self|others)|bOSC_v1_(?<dev>[A-Za-z]+)_(?<node>\d+))$";
                group.pointIdReplacement = "${dev}/${node}";

                FittedRegion fit = ContactRegionFitter.Fit(group);
                Assert.IsTrue(fit.IsValid, string.Join("; ", fit.Errors));

                Assert.AreEqual(expected.regionExtents[0], fit.RegionExtents.x, 1e-4f, "region width");
                Assert.AreEqual(expected.regionExtents[1], fit.RegionExtents.y, 1e-4f, "region height");
                Assert.AreEqual(expected.regionExtents[2], fit.RegionExtents.z, 1e-4f, "region depth");

                Assert.AreEqual(expected.boxExtents[0], fit.BoxExtents.x, 1e-4f, "box width");
                Assert.AreEqual(expected.boxExtents[1], fit.BoxExtents.y, 1e-4f, "box height");
                Assert.AreEqual(expected.boxExtents[2], fit.BoxExtents.z, 1e-4f, "box depth");

                var fitted = new Dictionary<string, Vector3>();
                foreach (FittedPoint point in fit.Points)
                    if (!fitted.ContainsKey(point.PointId))
                        fitted[point.PointId] = point.Normalised;

                Assert.AreEqual(expected.points.Count, fitted.Count, "motor count");

                foreach (ContactPointManifest point in expected.points)
                {
                    Assert.IsTrue(fitted.TryGetValue(point.id, out Vector3 actual),
                        $"{point.id} is in the manifest but the fitter did not produce it");

                    Assert.AreEqual(point.u, actual.x, 1e-3f, $"{point.id} u");
                    Assert.AreEqual(point.v, actual.y, 1e-3f, $"{point.id} v");
                    Assert.AreEqual(point.w, actual.z, 1e-3f, $"{point.id} w");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [TestCaseSource(nameof(RegionCases))]
        public void EveryMotorInTheRealLayoutResolvesToItself(string regionId, string prefabName, EncoderAxes axes, string pattern)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{prefabName}");
            if (prefab == null)
                Assert.Ignore($"{prefabName} not found; the bHaptics package is not installed");

            var instance = (GameObject)Object.Instantiate(prefab);
            try
            {
                var group = instance.AddComponent<ContactCompressorGroup>();
                group.regionId = regionId;
                group.axes = axes;
                group.paddingMetres = 0.10f;
                group.sourceRoot = instance.transform;
                group.frameOverride = instance.transform;
                group.sourceParameterPattern = pattern;
                group.pointIdPattern = @"^(?:bOSC/v2/(?<dev>[^/]+)/(?<node>\d+)/(?:self|others)|bOSC_v1_(?<dev>[A-Za-z]+)_(?<node>\d+))$";
                group.pointIdReplacement = "${dev}/${node}";

                FittedRegion fit = ContactRegionFitter.Fit(group);
                Assert.IsTrue(fit.IsValid, string.Join("; ", fit.Errors));

                // A stock VRChat hand collider, which is what will actually be touching the avatar.
                ValidationResult result = ContactCompressorValidator.Validate(fit, 0.05f);

                Assert.IsTrue(result.Ran, "validation did not run");
                Assert.AreEqual(0, result.Misattributed,
                    $"{regionId}: {result.Misattributed} motors decode closer to a different motor "
                    + $"(worst {result.WorstPointId} at {result.WorstErrorMetres * 1000f:F2} mm)");
                Assert.AreEqual(0, result.Saturated, $"{regionId}: {result.Saturated} motors saturated");

                Debug.Log($"[parity] {regionId}: {result.PointsChecked} motors, "
                          + $"mean {result.MeanErrorMetres * 1000f:F3} mm, "
                          + $"worst {result.WorstErrorMetres * 1000f:F3} mm");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
