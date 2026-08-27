using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Furroxide.ContactCompressor.VrcProximityReference;

namespace Furroxide.ContactCompressor.Tests
{
    /// <summary>
    /// Every positional assertion here runs the contact through <see cref="VrcProximityReference"/> - a
    /// literal port of VRChat's own proximity maths - rather than through the algebra the decoder
    /// was derived from. If the derivation were wrong, asserting against the derivation would pass
    /// anyway.
    /// </summary>
    public class ContactEncoderTests
    {
        // A torso: 0.40 wide, 0.55 tall, 0.26 front-to-back. The thin Z axis is what makes padding
        // interesting, so it is deliberately the real number rather than a comfortable one.
        static readonly Vec RegionSize = new Vec(0.40f, 0.55f, 0.26f);
        const float Padding = ContactEncoderMath.DefaultPaddingMetres;   // 0.10 m

        static Vec BoxSize => new Vec(
            ContactEncoderMath.BoxExtent(RegionSize.X, Padding),
            ContactEncoderMath.BoxExtent(RegionSize.Y, Padding),
            ContactEncoderMath.BoxExtent(RegionSize.Z, Padding));

        static EncodedPoint BoxExtents => new EncodedPoint(BoxSize.X, BoxSize.Y, BoxSize.Z);
        static EncodedPoint RegionExtents => new EncodedPoint(RegionSize.X, RegionSize.Y, RegionSize.Z);

        /// <summary>Region-normalised (0..1 per axis) to metres relative to the region centre.</summary>
        static Vec ToMetres(float u, float v, float w) => new Vec(
            (u - 0.5f) * RegionSize.X,
            (v - 0.5f) * RegionSize.Y,
            (w - 0.5f) * RegionSize.Z);

        static ContactSolution SolveFor(params Sender[] senders)
        {
            var (plus, minus) = ReadRegion(new Vec(0, 0, 0), BoxSize, senders);
            return ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.XYZ);
        }

        // ------------------------------------------------------------------ position

        [Theory]
        [InlineData(0.02f)]
        [InlineData(0.05f)]
        [InlineData(0.09f)]
        public void SphereSender_PositionIsExact_RegardlessOfSize(float radius)
        {
            foreach (var (u, v, w) in SampleGrid())
            {
                var solution = SolveFor(Sender.Sphere(ToMetres(u, v, w), radius));

                Assert.True(solution.InContact);
                Assert.Equal(u, solution.Position.X, 3);
                Assert.Equal(v, solution.Position.Y, 3);
                Assert.Equal(w, solution.Position.Z, 3);
            }
        }

        [Fact]
        public void PositionIsIndependentOfSenderSize()
        {
            // The whole point of the opposed pair. A child's fingertip and an adult's palm touching
            // the same spot must decode to the same spot.
            var at = ToMetres(0.7f, 0.35f, 0.9f);

            var small = SolveFor(Sender.Sphere(at, 0.015f));
            var large = SolveFor(Sender.Sphere(at, 0.09f));

            Assert.Equal(small.Position.X, large.Position.X, 3);
            Assert.Equal(small.Position.Y, large.Position.Y, 3);
            Assert.Equal(small.Position.Z, large.Position.Z, 3);
        }

        [Theory]
        [InlineData(0.04f, 0f)]
        [InlineData(0.04f, 45f)]
        [InlineData(0.10f, 0f)]
        [InlineData(0.10f, 45f)]
        [InlineData(0.10f, 90f)]
        public void CapsuleSender_PositionIsAccurate(float length, float tilt)
        {
            // VRChat's avatar colliders are capsules, not spheres, and the cancellation is only
            // provably exact for spheres. Measured residual at realistic collider sizes is sub-
            // millimetre; 5 mm is a generous ceiling and still far inside one vest grid cell.
            const float toleranceMetres = 0.005f;

            foreach (var (u, v, w) in SampleGrid())
            {
                var centre = ToMetres(u, v, w);
                var solution = SolveFor(Sender.Capsule(centre, length, 0.03f, tilt));
                if (!solution.InContact) continue;

                var decoded = ToMetres(solution.Position.X, solution.Position.Y, solution.Position.Z);
                Assert.True((decoded - centre).Length < toleranceMetres,
                    $"len={length} tilt={tilt} at ({u},{v},{w}): off by {(decoded - centre).Length:F5} m");
            }
        }

        [Fact]
        public void SingleSidedBox_IsWrongByRadius_WhichIsWhyPairsExist()
        {
            // Documents the failure mode the opposed pair removes. A single +face box reports
            // t + r/L, so without knowing r the decode is displaced by r along every axis.
            const float radius = 0.09f;
            var at = ToMetres(0.5f, 0.5f, 0.5f);
            var sender = Sender.Sphere(at, radius);

            var (plus, _) = ReadRegion(new Vec(0, 0, 0), BoxSize, sender);

            float displacement = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                float extent = BoxExtents[axis];
                float region = RegionExtents[axis];
                float naive = ContactEncoderMath.BoxToRegion(plus[axis], extent, region) * region;
                float truth = ContactEncoderMath.BoxToRegion(0.5f, extent, region) * region;
                float d = naive - truth;
                displacement += d * d;
            }
            displacement = (float)Math.Sqrt(displacement);

            // Error is r per axis, so r*sqrt(3) in 3D.
            Assert.Equal(radius * Math.Sqrt(3d), displacement, 3);

            var paired = SolveFor(sender);
            var pairedError = (ToMetres(paired.Position.X, paired.Position.Y, paired.Position.Z) - at).Length;
            Assert.True(pairedError < 0.001f, $"paired error {pairedError:F5} m");
        }

        // ------------------------------------------------------------------ collider size

        [Theory]
        [InlineData(0.02f)]
        [InlineData(0.05f)]
        [InlineData(0.09f)]
        public void SenderRadiusIsRecovered(float radius)
        {
            var solution = SolveFor(Sender.Sphere(ToMetres(0.5f, 0.5f, 0.5f), radius));
            Assert.Equal(radius, solution.SenderRadius, 3);
        }

        [Fact]
        public void PaddingBelowColliderRadius_Saturates()
        {
            // The rule padding >= r, demonstrated. Torso depth is 0.26 m, so a fractional padding
            // of 30% would give only 0.078 m and a stock hand collider would peg against the chest.
            var thinBox = new Vec(
                ContactEncoderMath.BoxExtent(RegionSize.X, 0.04f),
                ContactEncoderMath.BoxExtent(RegionSize.Y, 0.04f),
                ContactEncoderMath.BoxExtent(RegionSize.Z, 0.04f));

            var atFront = ToMetres(0.5f, 0.5f, 1.0f);
            var (plus, minus) = ReadRegion(new Vec(0, 0, 0), thinBox, Sender.Sphere(atFront, 0.09f));

            Assert.True(ContactEncoderMath.IsSaturated(plus[2], minus[2]));

            var solution = ContactEncoderSolver.Solve(
                plus, minus,
                new EncodedPoint(thinBox.X, thinBox.Y, thinBox.Z),
                RegionExtents, EncoderAxes.XYZ);

            Assert.True((solution.SaturatedAxes & EncoderAxes.Z) != 0);
            Assert.True(solution.Confidence < 1f, "a saturated axis must lower confidence");
        }

        // ------------------------------------------------------------------ multi-touch

        [Fact]
        public void TwoSenders_AreDetected_AndSpreadMatchesSeparation()
        {
            const float radius = 0.03f;
            var left = Sender.Sphere(ToMetres(0.1f, 0.5f, 0.5f), radius);
            var right = Sender.Sphere(ToMetres(0.9f, 0.5f, 0.5f), radius);
            float separation = (ToMetres(0.9f, 0.5f, 0.5f) - ToMetres(0.1f, 0.5f, 0.5f)).Length;

            var solution = SolveFor(left, right);

            Assert.True(solution.IsMultiTouch);
            Assert.Equal(separation, solution.Spread, 2);
            Assert.True(solution.Confidence < 0.5f,
                "two hands in one region must drop below the threshold that triggers a region-wide fallback");
        }

        [Fact]
        public void SingleSender_ReportsNoSpread()
        {
            var solution = SolveFor(Sender.Sphere(ToMetres(0.5f, 0.5f, 0.5f), 0.05f));

            Assert.False(solution.IsMultiTouch);
            Assert.True(solution.Spread < 0.01f, $"spread was {solution.Spread:F5}");
            Assert.Equal(1f, solution.Confidence, 3);
        }

        [Fact]
        public void NoContact_ReportsIdle()
        {
            var solution = ContactEncoderSolver.Solve(
                new float[3], new float[3], BoxExtents, RegionExtents, EncoderAxes.XYZ);

            Assert.False(solution.InContact);
        }

        // ------------------------------------------------------------------ torn frames

        /// <summary>
        /// Reads a genuine contact, then puts one face of one axis back to the value it held before
        /// the contact started - the state the decoder is in when a solve lands between the halves
        /// of an OSC bundle. The other two axes still carry current values, which is what makes this
        /// the realistic tear rather than the degenerate single-axis one.
        /// </summary>
        static (float[] plus, float[] minus) TornRead(float radius, int tornAxis, bool tearPositiveFace)
        {
            var (plus, minus) = ReadRegion(
                new Vec(0, 0, 0), BoxSize, new[] { Sender.Sphere(ToMetres(0.5f, 0.5f, 0.5f), radius) });

            if (tearPositiveFace) plus[tornAxis] = 0f;
            else minus[tornAxis] = 0f;

            return (plus, minus);
        }

        /// <summary>
        /// The torn axis must not drag the healthy axes' real footprint into a fabricated spread.
        /// Left unguarded this trips the multi-touch threshold, collapses confidence below Sample's
        /// default, and lights every motor in the region at full intensity on every contact edge.
        /// </summary>
        [Theory]
        [InlineData(0, true)]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(2, false)]
        public void TornAxis_DoesNotFabricateSpread(int tornAxis, bool tearPositiveFace)
        {
            var (plus, minus) = TornRead(0.06f, tornAxis, tearPositiveFace);

            var solution = ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.XYZ);

            Assert.True(solution.InContact);
            Assert.False(solution.IsMultiTouch,
                $"axis {tornAxis} tear reported spread {solution.Spread:F4} " +
                $"(normalised {solution.SpreadNormalised:F4})");
            Assert.True(solution.SpreadNormalised < ContactSolution.MultiTouchThreshold,
                $"spread normalised to {solution.SpreadNormalised:F4}");
        }

        /// <summary>
        /// Dropping the torn axis must not throw away what the healthy axes measured: the sender is
        /// still sized from them, so the response is a real falloff rather than a flat one.
        /// </summary>
        [Theory]
        [InlineData(0.03f)]
        [InlineData(0.06f)]
        [InlineData(0.09f)]
        public void TornAxis_StillMeasuresTheSenderFromTheHealthyAxes(float radius)
        {
            var (plus, minus) = TornRead(radius, 0, tearPositiveFace: false);

            var solution = ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.XYZ);

            Assert.True(solution.InContact);
            Assert.Equal(radius, solution.SenderRadius, 2);
        }

        /// <summary>
        /// When every active axis is torn there is a contact but nothing trustworthy about it. It
        /// must report as such rather than deriving a radius and spread from unset sentinels - the
        /// original defect, where the maximum's 0 seed became a spread nobody measured.
        /// </summary>
        [Fact]
        public void EveryAxisTorn_ReportsContactWithoutInventingGeometry()
        {
            var (plus, minus) = ReadRegion(
                new Vec(0, 0, 0), BoxSize, new[] { Sender.Sphere(ToMetres(0.5f, 0.5f, 0.5f), 0.05f) });
            for (int axis = 0; axis < 3; axis++) minus[axis] = 0f;

            var solution = ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.XYZ);

            Assert.True(solution.InContact);
            Assert.Equal(0f, solution.Spread, 5);
            Assert.Equal(0f, solution.SenderRadius, 5);
            Assert.False(solution.IsMultiTouch);
            Assert.True(solution.Confidence < 1f, "an entirely torn frame must not be reported as certain");
        }

        /// <summary>
        /// The single-axis head region has no second axis to fall back on, so a tear there has to
        /// degrade quietly rather than produce a spread out of one measurement.
        /// </summary>
        [Fact]
        public void TornAxis_OnASingleAxisRegion_ReportsNoSpread()
        {
            var (plus, minus) = TornRead(0.05f, 0, tearPositiveFace: false);

            var solution = ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.X);

            Assert.Equal(0f, solution.Spread, 5);
            Assert.False(solution.IsMultiTouch);
        }

        /// <summary>
        /// A parameter that arrives as NaN must not become a sender the size of the universe. NaN
        /// fails every ordered comparison, so it has to be rejected explicitly rather than left to
        /// the min/max updates.
        /// </summary>
        [Fact]
        public void NaNParameters_DoNotProduceAnInfiniteSender()
        {
            var plus = new[] { float.NaN, float.NaN, float.NaN };
            var minus = new[] { float.NaN, float.NaN, float.NaN };

            var solution = ContactEncoderSolver.Solve(plus, minus, BoxExtents, RegionExtents, EncoderAxes.XYZ);

            Assert.Equal(0f, solution.SenderRadius, 5);
            Assert.Equal(0f, solution.Spread, 5);
        }

        /// <summary>
        /// The guard must not be satisfiable by flattening every measurement: an untorn contact
        /// still reports its real radius and no spread.
        /// </summary>
        [Fact]
        public void UntornContacts_AreStillMeasuredNormally()
        {
            var solution = SolveFor(Sender.Sphere(ToMetres(0.5f, 0.5f, 0.5f), 0.05f));

            Assert.True(solution.InContact);
            Assert.Equal(0.05f, solution.SenderRadius, 2);
            Assert.Equal(0f, solution.Spread, 2);
            Assert.Equal(1f, solution.Confidence, 3);
        }

        // ------------------------------------------------------------------ helpers

        static IEnumerable<(float u, float v, float w)> SampleGrid()
        {
            for (int i = 1; i <= 3; i++)
                for (int j = 1; j <= 3; j++)
                    for (int k = 1; k <= 3; k++)
                        yield return (i / 4f, j / 4f, k / 4f);
        }
    }
}
