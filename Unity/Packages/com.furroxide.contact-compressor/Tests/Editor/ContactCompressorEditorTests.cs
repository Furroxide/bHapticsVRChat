using System.Collections.Generic;
using System.Linq;
using Furroxide.ContactCompressor.Editor;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using Object = UnityEngine.Object;

namespace Furroxide.ContactCompressor.Tests
{
    /// <summary>
    /// Exercises the editor half against real Unity objects: real transforms, real
    /// VRCContactReceivers, and the real preprocess hook. The .NET suite covers the maths; this
    /// covers everything that only exists once Unity is running.
    /// </summary>
    public class ContactCompressorEditorTests
    {
        readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
                if (o != null)
                    Object.DestroyImmediate(o, true);
            _spawned.Clear();
        }

        GameObject NewObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _spawned.Add(go);
            return go;
        }

        /// <summary>A 4 x 5 grid of receivers on a panel, shaped like one side of a vest.</summary>
        GameObject BuildPanel(string panel, Transform parent, float z, int columns = 4, int rows = 5)
        {
            var root = NewObject(panel, parent);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int node = row * columns + col;
                    var go = NewObject($"{panel}_{node}", root.transform);
                    go.transform.localPosition = new Vector3(
                        -0.15f + col * 0.10f,
                        0.20f - row * 0.10f,
                        z);

                    foreach (string source in new[] { "self", "others" })
                    {
                        var receiver = go.AddComponent<VRCContactReceiver>();
                        receiver.parameter = $"bOSC/v2/{panel}/{node}/{source}";
                        receiver.shapeType = ContactBase.ShapeType.Sphere;
                        receiver.radius = 0.045f;
                        receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
                        receiver.allowSelf = source == "self";
                        receiver.allowOthers = source == "others";
                        receiver.localOnly = false;
                        receiver.collisionTags = new List<string> { "Hand", "Finger" };
                    }
                }
            }
            return root;
        }

        ContactCompressorGroup BuildVestGroup(out GameObject root)
        {
            root = NewObject("Vest");
            BuildPanel("VestFront", root.transform, 0.12f);
            BuildPanel("VestBack", root.transform, -0.12f);

            var group = root.AddComponent<ContactCompressorGroup>();
            group.regionId = "Torso";
            group.axes = EncoderAxes.XYZ;
            group.paddingMetres = 0.10f;
            group.sourceParameterPattern = @"^bOSC/v2/(?:VestFront|VestBack)/\d+/(?:self|others)$";
            group.pointIdPattern = "^bOSC/v2/(.+)/(?:self|others)$";
            group.pointIdReplacement = "$1";
            return group;
        }

        // ---------------------------------------------------------------- fitting

        [Test]
        public void FitMeasuresTheRegionFromTheReceivers()
        {
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);

            Assert.IsTrue(fit.IsValid, string.Join("; ", fit.Errors));

            // Columns span -0.15..0.15, rows 0.20..-0.20, panels +/-0.12.
            Assert.AreEqual(0.30f, fit.RegionExtents.x, 1e-4f);
            Assert.AreEqual(0.40f, fit.RegionExtents.y, 1e-4f);
            Assert.AreEqual(0.24f, fit.RegionExtents.z, 1e-4f);

            Assert.AreEqual(Vector3.zero, fit.CentreLocal);

            // Padding is per side, in metres.
            Assert.AreEqual(0.50f, fit.BoxExtents.x, 1e-4f);
            Assert.AreEqual(0.44f, fit.BoxExtents.z, 1e-4f);
        }

        [Test]
        public void FitCollapsesSelfAndOthersIntoOnePoint()
        {
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);

            // 40 motors, each with a self and an others receiver.
            Assert.AreEqual(80, fit.Points.Count);
            Assert.AreEqual(40, fit.Points.Select(p => p.PointId).Distinct().Count());
            CollectionAssert.Contains(fit.Points.Select(p => p.PointId).ToList(), "VestFront/7");
        }

        [Test]
        public void FitUnionsTagsAndAllowFlagsAcrossSources()
        {
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);

            Assert.IsTrue(fit.AllowSelf, "self receivers were present");
            Assert.IsTrue(fit.AllowOthers, "others receivers were present");
            CollectionAssert.AreEquivalent(new[] { "Hand", "Finger" }, fit.CollisionTags);

            // Merging differently-filtered receivers widens what can trigger them, which is worth saying out loud.
            Assert.IsNotEmpty(fit.Warnings);
        }

        [Test]
        public void FitIgnoresReceiversTheFilterExcludes()
        {
            var group = BuildVestGroup(out GameObject root);

            // Stand in for the punch receivers, which must not be swept into the fit.
            var punch = NewObject("Punch", root.transform);
            punch.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            var extra = punch.AddComponent<VRCContactReceiver>();
            extra.parameter = "bOSC/v2/Punch/VestFront/0/Light";
            extra.receiverType = ContactReceiver.ReceiverType.OnEnter;
            extra.collisionTags = new List<string> { "Hand" };

            FittedRegion fit = ContactRegionFitter.Fit(group);

            Assert.AreEqual(80, fit.Points.Count);
            CollectionAssert.DoesNotContain(fit.Points.Select(p => p.Parameter).ToList(),
                                            "bOSC/v2/Punch/VestFront/0/Light");
        }

        [Test]
        public void FitRejectsAnUnusableGroup()
        {
            var root = NewObject("Empty");
            var group = root.AddComponent<ContactCompressorGroup>();
            group.regionId = "Nothing";

            FittedRegion fit = ContactRegionFitter.Fit(group);

            Assert.IsFalse(fit.IsValid);
            Assert.IsNotEmpty(fit.Errors);
        }

        [Test]
        public void FitRejectsARegionIdThatWouldBreakTheParameterName()
        {
            var group = BuildVestGroup(out _);
            group.regionId = "Torso/Front";

            FittedRegion fit = ContactRegionFitter.Fit(group);

            Assert.IsFalse(fit.IsValid);
        }

        // ---------------------------------------------------------------- emitting

        [Test]
        public void EmitCreatesAnOpposedPairPerAxis()
        {
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);

            GameObject host = ContactCompressorEmitter.Emit(fit);
            Assert.IsNotNull(host);

            var emitted = host.GetComponents<VRCContactReceiver>();
            Assert.AreEqual(6, emitted.Length);

            foreach (var receiver in emitted)
            {
                Assert.AreEqual(ContactBase.ShapeType.Box, receiver.shapeType);
                Assert.IsTrue(receiver.useFaceProximity, "face proximity is the whole mechanism");
                Assert.AreEqual(ContactReceiver.ReceiverType.Proximity, receiver.receiverType);
                CollectionAssert.AreEquivalent(new[] { "Hand", "Finger" }, receiver.collisionTags);
            }

            CollectionAssert.AreEquivalent(
                new[] { "bOSC/v3/Torso/Xp", "bOSC/v3/Torso/Xn",
                        "bOSC/v3/Torso/Yp", "bOSC/v3/Torso/Yn",
                        "bOSC/v3/Torso/Zp", "bOSC/v3/Torso/Zn" },
                emitted.Select(r => r.parameter).ToList());
        }

        [Test]
        public void EmitOnlyCoversTheSelectedAxes()
        {
            var group = BuildVestGroup(out _);
            group.axes = EncoderAxes.X;

            GameObject host = ContactCompressorEmitter.Emit(ContactRegionFitter.Fit(group));

            var emitted = host.GetComponents<VRCContactReceiver>();
            Assert.AreEqual(2, emitted.Length);
            CollectionAssert.AreEquivalent(new[] { "bOSC/v3/Torso/Xp", "bOSC/v3/Torso/Xn" },
                                           emitted.Select(r => r.parameter).ToList());
        }

        [Test]
        public void EmittedBoxesCoverTheRegionWhicheverWayTheyFace()
        {
            // The receivers are rotated, so their local box size has to be permuted to match. This
            // was derived rather than hand-written, and getting it wrong would silently distort
            // every decode, so check the box occupies the same world volume for every rotation.
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);
            GameObject host = ContactCompressorEmitter.Emit(fit);

            foreach (var receiver in host.GetComponents<VRCContactReceiver>())
            {
                Vector3 worldSize = receiver.rotation * receiver.size;
                worldSize = new Vector3(Mathf.Abs(worldSize.x), Mathf.Abs(worldSize.y), Mathf.Abs(worldSize.z));

                Assert.AreEqual(fit.BoxExtents.x, worldSize.x, 1e-4f, $"{receiver.parameter} width");
                Assert.AreEqual(fit.BoxExtents.y, worldSize.y, 1e-4f, $"{receiver.parameter} height");
                Assert.AreEqual(fit.BoxExtents.z, worldSize.z, 1e-4f, $"{receiver.parameter} depth");
            }
        }

        [Test]
        public void EmittedRotationsPointAtOpposingFaces()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 plus = ContactCompressorEmitter.RotationFor(axis, true) * Vector3.forward;
                Vector3 minus = ContactCompressorEmitter.RotationFor(axis, false) * Vector3.forward;

                Assert.AreEqual(1f, Mathf.Abs(plus[axis]), 1e-4f, "the pair must measure along its own axis");
                Assert.AreEqual(-1f, Vector3.Dot(plus, minus), 1e-4f, "the pair must face opposite ways");
            }
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void EveryPointRoundTripsBackToItself()
        {
            var group = BuildVestGroup(out _);
            FittedRegion fit = ContactRegionFitter.Fit(group);

            ValidationResult result = ContactCompressorValidator.Validate(fit, 0.05f);

            Assert.IsTrue(result.Ran);
            Assert.AreEqual(40, result.PointsChecked);
            Assert.AreEqual(0, result.Misattributed,
                $"worst was {result.WorstPointId} at {result.WorstErrorMetres * 1000f:F2} mm");
            Assert.AreEqual(0, result.Saturated);
        }

        [Test]
        public void UnderPaddedRegionsAreReportedRatherThanSilentlyWrong()
        {
            var group = BuildVestGroup(out _);
            group.paddingMetres = 0.02f;      // below a stock hand collider

            ValidationResult result = ContactCompressorValidator.Validate(ContactRegionFitter.Fit(group), 0.09f);

            Assert.Greater(result.Saturated, 0, "saturation must surface, not pass quietly");
        }

        // ---------------------------------------------------------------- the build hook

        [Test]
        public void HookReplacesReceiversAndRegistersFloatParameters()
        {
            var avatar = NewObject("Avatar");
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();

            var controller = new AnimatorController();
            _spawned.Add(controller);
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    animatorController = controller,
                    isDefault = false
                }
            };

            var vest = BuildVestGroup(out GameObject vestRoot);
            vestRoot.transform.SetParent(avatar.transform, false);

            int before = avatar.GetComponentsInChildren<ContactReceiver>(true).Length;
            Assert.AreEqual(80, before);

            var hook = new ContactCompressorHook();
            Assert.IsTrue(hook.OnPreprocessAvatar(avatar), "the hook should accept a valid avatar");

            int after = avatar.GetComponentsInChildren<ContactReceiver>(true).Length;
            Assert.AreEqual(6, after, "80 per-motor receivers should become one opposed pair per axis");

            var expected = new[] { "bOSC/v3/Torso/Xp", "bOSC/v3/Torso/Xn",
                                   "bOSC/v3/Torso/Yp", "bOSC/v3/Torso/Yn",
                                   "bOSC/v3/Torso/Zp", "bOSC/v3/Torso/Zn" };

            Assert.IsNotNull(descriptor.expressionParameters);
            foreach (string name in expected)
            {
                var parameter = descriptor.expressionParameters.parameters.FirstOrDefault(p => p.name == name);
                Assert.IsNotNull(parameter, $"{name} was not declared");
                Assert.AreEqual(VRCExpressionParameters.ValueType.Float, parameter.valueType);
                Assert.IsFalse(parameter.networkSynced, "these are local; they must not cost synced bits");
            }

            var fx = (AnimatorController)descriptor.baseAnimationLayers[0].animatorController;
            foreach (string name in expected)
            {
                var parameter = fx.parameters.FirstOrDefault(p => p.name == name);
                Assert.IsNotNull(parameter, $"{name} missing from the FX controller");
                Assert.AreEqual(AnimatorControllerParameterType.Float, parameter.type);
            }

            _spawned.Add(descriptor.expressionParameters);
            _spawned.Add(fx);
        }

        [Test]
        public void HookLeavesAvatarsWithoutGroupsAlone()
        {
            var avatar = NewObject("Untouched");
            avatar.AddComponent<VRCAvatarDescriptor>();
            BuildPanel("VestFront", avatar.transform, 0.12f);

            int before = avatar.GetComponentsInChildren<ContactReceiver>(true).Length;

            Assert.IsTrue(new ContactCompressorHook().OnPreprocessAvatar(avatar));
            Assert.AreEqual(before, avatar.GetComponentsInChildren<ContactReceiver>(true).Length);
        }

        [Test]
        public void HookRunsBeforeTheSdkStripsEditorOnlyComponents()
        {
            // ContactCompressorGroup is IEditorOnly. The VRCSDK removes those at -1024, so running
            // any later would find nothing to do and ship the avatar uncompressed.
            Assert.Less(ContactCompressorHook.CallbackOrder, -1024);

            // ...but after tools that generate receivers, VRCFury's build being at -10000.
            Assert.Greater(ContactCompressorHook.CallbackOrder, -10000);
        }
    }
}
