using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using static Furroxide.ContactCompressor.VrcProximityReference;

namespace Furroxide.ContactCompressor.Tests
{
    /// <summary>
    /// Runs the real bHaptics device layout - extracted from the shipped prefabs - through VRChat's
    /// own proximity maths and checks that touching each motor decodes back to that motor.
    ///
    /// The synthetic tests prove the maths. This proves the maths survives contact with the actual
    /// geometry, which is where an encoding scheme usually falls over: real layouts are uneven,
    /// panels are tilted, and axes turn out to be degenerate.
    /// </summary>
    public class BHapticsLayoutTests
    {
        readonly ITestOutputHelper _output;

        public BHapticsLayoutTests(ITestOutputHelper output) => _output = output;

        static ContactCompressorManifest LoadDefaultManifest()
        {
            // Walk up to the repository root; the test binary sits several levels below it.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Decoder", "manifests", "bhaptics-default.json")))
                dir = dir.Parent;

            Assert.True(dir != null, "Could not locate Decoder/manifests/bhaptics-default.json");

            string json = File.ReadAllText(Path.Combine(dir.FullName, "Decoder", "manifests", "bhaptics-default.json"));

            // Deliberately the same reader the desktop app uses, so this suite covers it too.
            var manifest = ManifestJson.Parse(json);

            Assert.NotNull(manifest);
            return manifest;
        }

        public static IEnumerable<object[]> Regions()
        {
            foreach (var region in LoadDefaultManifest().regions)
                yield return new object[] { region.id };
        }

        [Fact]
        public void ManifestMatchesTheShippedDeviceLayout()
        {
            var manifest = LoadDefaultManifest();

            Assert.Equal(ContactCompressorManifest.CurrentVersion, manifest.version);
            Assert.Equal("bOSC/v3", manifest.prefix);

            var torso = manifest.Find("Torso");
            Assert.NotNull(torso);

            // TactSuit X40: 20 front + 20 back.
            Assert.Equal(40, torso.points.Count);
            Assert.Equal(20, torso.points.Count(p => p.id.StartsWith("VestFront/", StringComparison.Ordinal)));
            Assert.Equal(20, torso.points.Count(p => p.id.StartsWith("VestBack/", StringComparison.Ordinal)));

            Assert.Equal(4, manifest.Find("Head").points.Count);
            Assert.Equal(6, manifest.Find("ForearmL").points.Count);
            Assert.Equal(6, manifest.Find("ForearmR").points.Count);

            int receivers = manifest.regions.Sum(r => ContactEncoderSolver.ReceiverCount(r.ParsedAxes));
            _output.WriteLine($"{manifest.regions.Count} regions, "
                              + $"{manifest.regions.Sum(r => r.points.Count)} points, "
                              + $"{receivers} emitted receivers");

            // 112 source receivers across these four devices in the stock prefabs.
            Assert.True(receivers <= 24, $"expected a large reduction, got {receivers} receivers");
        }

        [Fact]
        public void FrontAndBackPanelsAreSeparableAlongZ()
        {
            var torso = LoadDefaultManifest().Find("Torso");

            float frontMin = torso.points.Where(p => p.id.StartsWith("VestFront/", StringComparison.Ordinal)).Min(p => p.w);
            float backMax = torso.points.Where(p => p.id.StartsWith("VestBack/", StringComparison.Ordinal)).Max(p => p.w);

            _output.WriteLine($"front w >= {frontMin:F3}, back w <= {backMax:F3}");

            // A comfortable gap either side of the w = 0.5 split the decoder relies on.
            Assert.True(frontMin > 0.6f, $"front panel reaches down to w={frontMin:F3}");
            Assert.True(backMax < 0.4f, $"back panel reaches up to w={backMax:F3}");
        }

        [Theory]
        [MemberData(nameof(Regions))]
        public void EveryMotorDecodesBackToItself(string regionId)
        {
            // Roughly VRChat's stock hand collider - the thing that will actually be touching you.
            const float senderRadius = 0.05f;

            var manifest = LoadDefaultManifest();
            var region = manifest.Find(regionId);
            var axes = region.ParsedAxes;

            var box = new Vec(region.boxExtents[0], region.boxExtents[1], region.boxExtents[2]);
            var origin = new Vec(0, 0, 0);
            var extents = region.RegionExtentsPoint;

            var plus = new float[3];
            var minus = new float[3];
            var senders = new Sender[1];

            int misattributed = 0;
            float worst = 0f;
            string worstId = null;

            foreach (var point in region.points)
            {
                // Normalised position -> metres relative to the region centre.
                var at = new Vec(
                    (point.u - 0.5f) * extents.X,
                    (point.v - 0.5f) * extents.Y,
                    (point.w - 0.5f) * extents.Z);

                senders[0] = Sender.Sphere(at, senderRadius);
                ReadRegion(origin, box, senders, plus, minus);

                var solution = ContactEncoderSolver.Solve(plus, minus, region.BoxExtentsPoint, extents, axes);
                Assert.True(solution.InContact, $"{point.id} registered no contact at all");

                float error = MathF.Sqrt(point.DistanceSquaredTo(solution.Position, axes));
                if (error > worst) { worst = error; worstId = point.id; }

                var nearest = region.points
                    .OrderBy(p => p.DistanceSquaredTo(solution.Position, axes))
                    .First();

                if (nearest.id != point.id)
                {
                    misattributed++;
                    _output.WriteLine($"  {point.id} decoded closest to {nearest.id}");
                }
            }

            _output.WriteLine($"{regionId}: {region.points.Count} points, "
                              + $"worst normalised error {worst:F4} at {worstId}, "
                              + $"{misattributed} misattributed");

            Assert.Equal(0, misattributed);
        }

        [Fact]
        public void ATouchOnTheChestDrivesTheChestMotors()
        {
            var manifest = LoadDefaultManifest();
            var torso = manifest.Find("Torso");
            var decoder = new ContactCompressorDecoder(manifest);

            // Upper-centre of the front panel.
            var target = torso.points.Single(p => p.id == "VestFront/1");
            var extents = torso.RegionExtentsPoint;
            var at = new Vec(
                (target.u - 0.5f) * extents.X,
                (target.v - 0.5f) * extents.Y,
                (target.w - 0.5f) * extents.Z);

            var box = new Vec(torso.boxExtents[0], torso.boxExtents[1], torso.boxExtents[2]);
            var (plus, minus) = ReadRegion(new Vec(0, 0, 0), box, Sender.Sphere(at, 0.05f));

            for (int axis = 0; axis < 3; axis++)
            {
                decoder.Accept(ContactParameterNames.OscAddress(null, "Torso", axis, true), plus[axis]);
                decoder.Accept(ContactParameterNames.OscAddress(null, "Torso", axis, false), minus[axis]);
            }

            var sample = decoder.Sample("Torso");
            _output.WriteLine(string.Join(", ", sample.Select(p => $"{p.Id} {p.Weight:P0}")));

            Assert.Equal("VestFront/1", sample[0].Id);

            // A palm-sized contact should stay on the front panel rather than bleeding through.
            Assert.All(sample, p => Assert.StartsWith("VestFront/", p.Id, StringComparison.Ordinal));
        }
    }
}
