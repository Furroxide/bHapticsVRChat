using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Furroxide.ContactCompressor.VrcProximityReference;

namespace Furroxide.ContactCompressor.Tests
{
    public class ContactParameterNameTests
    {
        [Theory]
        [InlineData("Torso", 0, true, "bOSC/v3/Torso/Xp")]
        [InlineData("Torso", 2, false, "bOSC/v3/Torso/Zn")]
        [InlineData("ForearmL", 1, true, "bOSC/v3/ForearmL/Yp")]
        public void FormatsAsExpected(string region, int axis, bool positive, string expected)
        {
            Assert.Equal(expected, ContactParameterNames.Parameter(null, region, axis, positive));
            Assert.Equal("/avatar/parameters/" + expected,
                         ContactParameterNames.OscAddress(null, region, axis, positive));
        }

        [Fact]
        public void RoundTrips()
        {
            foreach (var region in new[] { "Torso", "HandL", "Foot_R" })
                for (int axis = 0; axis < 3; axis++)
                    foreach (bool positive in new[] { true, false })
                    {
                        string name = ContactParameterNames.Parameter(null, region, axis, positive);
                        Assert.True(ContactParameterNames.TryParse(name, null, out var r, out int a, out bool p));
                        Assert.Equal(region, r);
                        Assert.Equal(axis, a);
                        Assert.Equal(positive, p);
                    }
        }

        [Fact]
        public void ParsesFullOscAddresses()
        {
            Assert.True(ContactParameterNames.TryParse(
                "/avatar/parameters/bOSC/v3/Torso/Yn", null, out var region, out int axis, out bool positive));
            Assert.Equal("Torso", region);
            Assert.Equal(1, axis);
            Assert.False(positive);
        }

        [Theory]
        [InlineData("/avatar/parameters/bOSC/v2/VestFront/3/self")]   // the legacy per-motor scheme
        [InlineData("/avatar/parameters/VRCEmote")]
        [InlineData("bOSC/v3/Torso/Qp")]                              // not an axis
        [InlineData("bOSC/v3/Torso/Xq")]                              // not a sign
        [InlineData("")]
        [InlineData(null)]
        public void RejectsForeignNames(string input)
        {
            Assert.False(ContactParameterNames.TryParse(input, null, out _, out _, out _));
        }
    }

    public class ContactCompressorDecoderTests
    {
        static readonly Vec RegionSize = new Vec(0.40f, 0.55f, 0.26f);
        const float Padding = ContactEncoderMath.DefaultPaddingMetres;

        static Vec BoxSize => new Vec(
            ContactEncoderMath.BoxExtent(RegionSize.X, Padding),
            ContactEncoderMath.BoxExtent(RegionSize.Y, Padding),
            ContactEncoderMath.BoxExtent(RegionSize.Z, Padding));

        static Vec ToMetres(float u, float v, float w) => new Vec(
            (u - 0.5f) * RegionSize.X, (v - 0.5f) * RegionSize.Y, (w - 0.5f) * RegionSize.Z);

        /// <summary>A bHaptics-shaped torso: 4 columns x 5 rows on the front, the same on the back.</summary>
        static ContactCompressorManifest VestManifest()
        {
            var region = new ContactRegionManifest
            {
                id = "Torso",
                axes = "XYZ",
                boxExtents = new[] { BoxSize.X, BoxSize.Y, BoxSize.Z },
                regionExtents = new[] { RegionSize.X, RegionSize.Y, RegionSize.Z }
            };

            foreach (var (panel, w) in new[] { ("VestFront", 0.95f), ("VestBack", 0.05f) })
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 4; col++)
                        region.points.Add(new ContactPointManifest
                        {
                            id = $"bOSC/v2/{panel}/{row * 4 + col}/others",
                            u = (col + 0.5f) / 4f,
                            v = 1f - (row + 0.5f) / 5f,
                            w = w,
                            radius = 0.045f
                        });

            return new ContactCompressorManifest { regions = { region } };
        }

        static ContactCompressorDecoder Feed(ContactCompressorManifest manifest, params Sender[] senders)
        {
            var decoder = new ContactCompressorDecoder(manifest);
            var (plus, minus) = ReadRegion(new Vec(0, 0, 0), BoxSize, senders);
            for (int axis = 0; axis < 3; axis++)
            {
                decoder.Accept(ContactParameterNames.OscAddress(null, "Torso", axis, true), plus[axis]);
                decoder.Accept(ContactParameterNames.OscAddress(null, "Torso", axis, false), minus[axis]);
            }
            return decoder;
        }

        [Fact]
        public void PicksTheAuthoredPointThatWasActuallyTouched()
        {
            var manifest = VestManifest();

            // Aim at each front-panel point in turn and check it comes back on top.
            foreach (var expected in manifest.regions[0].points.Where(p => p.id.Contains("VestFront")))
            {
                var decoder = Feed(manifest, Sender.Sphere(ToMetres(expected.u, expected.v, expected.w), 0.03f));
                var sample = decoder.Sample("Torso");

                Assert.NotEmpty(sample);
                Assert.Equal(expected.id, sample[0].Id);
            }
        }

        [Fact]
        public void DistinguishesFrontFromBack()
        {
            var manifest = VestManifest();

            var front = Feed(manifest, Sender.Sphere(ToMetres(0.5f, 0.5f, 0.95f), 0.03f)).Sample("Torso");
            var back = Feed(manifest, Sender.Sphere(ToMetres(0.5f, 0.5f, 0.05f), 0.03f)).Sample("Torso");

            Assert.StartsWith("bOSC/v2/VestFront/", front[0].Id);
            Assert.StartsWith("bOSC/v2/VestBack/", back[0].Id);
        }

        [Fact]
        public void WeightsSumToOne()
        {
            var decoder = Feed(VestManifest(), Sender.Sphere(ToMetres(0.4f, 0.6f, 0.95f), 0.04f));
            var sample = decoder.Sample("Torso");

            Assert.Equal(1f, sample.Sum(p => p.Weight), 4);
        }

        [Fact]
        public void LargerCollidersSpreadAcrossMorePoints()
        {
            var manifest = VestManifest();
            var at = ToMetres(0.5f, 0.5f, 0.95f);

            var tight = Feed(manifest, Sender.Sphere(at, 0.015f)).Sample("Torso");
            var broad = Feed(manifest, Sender.Sphere(at, 0.09f)).Sample("Torso");

            // A fingertip should concentrate on its nearest point; a palm should share out.
            Assert.True(tight[0].Weight > broad[0].Weight,
                $"fingertip {tight[0].Weight:F3} should beat palm {broad[0].Weight:F3} on the nearest point");
        }

        [Fact]
        public void TwoHandsFallBackToARegionWideResponse()
        {
            var manifest = VestManifest();
            var decoder = Feed(manifest,
                Sender.Sphere(ToMetres(0.1f, 0.5f, 0.95f), 0.03f),
                Sender.Sphere(ToMetres(0.9f, 0.5f, 0.95f), 0.03f));

            var sample = decoder.Sample("Torso");

            // Rather than firing a phantom point midway between two hands, every point is returned
            // at equal weight.
            Assert.Equal(manifest.regions[0].points.Count, sample.Count);
            Assert.All(sample, p => Assert.Equal(1f / manifest.regions[0].points.Count, p.Weight, 4));
        }

        [Fact]
        public void ReportsNothingWhenNotTouched()
        {
            var decoder = new ContactCompressorDecoder(VestManifest());
            Assert.Empty(decoder.Sample("Torso"));

            Assert.True(decoder.TrySolve("Torso", out var solution));
            Assert.False(solution.InContact);
        }

        [Fact]
        public void IgnoresParametersItDoesNotOwn()
        {
            var decoder = new ContactCompressorDecoder(VestManifest());

            Assert.False(decoder.Accept("/avatar/parameters/VRCEmote", 1f));
            Assert.False(decoder.Accept("/avatar/parameters/bOSC/v2/VestFront/3/self", 1f));
            Assert.False(decoder.Accept("/avatar/parameters/bOSC/v3/Head/Xp", 1f));   // region not in manifest
            Assert.True(decoder.Accept("/avatar/parameters/bOSC/v3/Torso/Xp", 0.5f));
        }

        [Fact]
        public void ResetClearsState()
        {
            var manifest = VestManifest();
            var decoder = Feed(manifest, Sender.Sphere(ToMetres(0.5f, 0.5f, 0.95f), 0.03f));
            Assert.NotEmpty(decoder.Sample("Torso"));

            decoder.Reset();
            Assert.Empty(decoder.Sample("Torso"));
        }

        [Fact]
        public void UnknownRegionIsHandledGracefully()
        {
            var decoder = new ContactCompressorDecoder(VestManifest());

            Assert.False(decoder.TrySolve("NoSuchRegion", out _));
            Assert.Empty(decoder.Sample("NoSuchRegion"));
        }

        [Fact]
        public void RejectsManifestsFromTheFuture()
        {
            var manifest = VestManifest();
            manifest.version = ContactCompressorManifest.CurrentVersion + 1;

            Assert.Throws<NotSupportedException>(() => new ContactCompressorDecoder(manifest));
        }
    }
}
