using System;

namespace Furroxide.ContactCompressor
{
    /// <summary>Which region axes a group encodes. Each enabled axis costs one opposed receiver pair (2 receivers).</summary>
    [Flags]
    public enum EncoderAxes
    {
        None = 0,
        X = 1 << 0,
        Y = 1 << 1,
        Z = 1 << 2,
        XY = X | Y,
        XZ = X | Z,
        YZ = Y | Z,
        XYZ = X | Y | Z
    }

    /// <summary>A plain 3-float point. Deliberately not <c>UnityEngine.Vector3</c> so this assembly stays engine-free.</summary>
    public struct EncodedPoint
    {
        public float X, Y, Z;
        public EncodedPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public float this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }

    /// <summary>What one region's opposed pairs resolved to for the current frame.</summary>
    public struct ContactSolution
    {
        /// <summary>False when no receiver in the region sees a sender.</summary>
        public bool InContact;

        /// <summary>Contact position, region-normalised. 0 = the low face of the region, 1 = the high face. Axes not encoded read 0.5.</summary>
        public EncodedPoint Position;

        /// <summary>
        /// Radius of the touching collider. Larger means a broader press; use it to widen the
        /// haptic footprint.
        ///
        /// Units are the manifest's units, which are avatar-local rather than true metres whenever
        /// the encoder box is authored at one scale and worn at another - as it is for anything
        /// auto-fitted to the wearer. Compare it against the region extents, never against a
        /// hard-coded distance.
        /// </summary>
        public float SenderRadius;

        /// <summary>Separation between two distinct senders, in the same units as <see cref="SenderRadius"/>.</summary>
        public float Spread;

        /// <summary>
        /// <see cref="Spread"/> as a fraction of the region's largest encoded extent. Scale
        /// invariant, so this is what multi-touch decisions are made on.
        /// </summary>
        public float SpreadNormalised;

        /// <summary>Axes whose decode saturated against a box face and was clamped.</summary>
        public EncoderAxes SaturatedAxes;

        /// <summary>
        /// 0..1. Drops when an axis saturates, when the point lands outside the unpadded region,
        /// or when the spread term indicates more than one sender. Below ~0.5 a consumer should
        /// fall back to a region-wide response instead of trusting the point.
        /// </summary>
        public float Confidence;

        /// <summary>True when the region is being touched in more than one place at once.</summary>
        public bool IsMultiTouch => SpreadNormalised > MultiTouchThreshold;

        /// <summary>
        /// Senders spread over more than this fraction of the region are treated as distinct
        /// touches. A fraction rather than a distance so it holds however the region is scaled.
        /// </summary>
        public const float MultiTouchThreshold = 0.2f;
    }

    /// <summary>
    /// Turns the raw proximity floats of a region's opposed receiver pairs into a contact point.
    /// Engine-free and allocation-free, so the same source compiles into an OSC consumer.
    /// </summary>
    public static class ContactEncoderSolver
    {
        /// <param name="pPlus">Proximity of the +face receiver of each axis pair, indexed X,Y,Z.</param>
        /// <param name="pMinus">Proximity of the -face receiver of each axis pair, indexed X,Y,Z.</param>
        /// <param name="boxExtents">Padded box extent per axis, in metres.</param>
        /// <param name="regionExtents">Unpadded region extent per axis, in metres. Padding is derived from the difference.</param>
        /// <param name="axes">Which axes this region actually encodes.</param>
        public static ContactSolution Solve(
            float[] pPlus,
            float[] pMinus,
            EncodedPoint boxExtents,
            EncodedPoint regionExtents,
            EncoderAxes axes)
        {
            if (pPlus == null) throw new ArgumentNullException(nameof(pPlus));
            if (pMinus == null) throw new ArgumentNullException(nameof(pMinus));
            if (pPlus.Length < 3 || pMinus.Length < 3)
                throw new ArgumentException("Expected three entries per array, indexed X, Y, Z.");

            var result = new ContactSolution
            {
                Position = new EncodedPoint(0.5f, 0.5f, 0.5f),
                Confidence = 1f
            };

            int active = 0;
            float footprintMin = float.MaxValue;
            float footprintMax = 0f;
            var position = new float[3] { 0.5f, 0.5f, 0.5f };

            for (int axis = 0; axis < 3; axis++)
            {
                var flag = AxisFlag(axis);
                if ((axes & flag) == 0) continue;

                float pp = pPlus[axis];
                float pn = pMinus[axis];
                if (ContactEncoderMath.IsIdle(pp, pn)) continue;

                active++;

                if (ContactEncoderMath.IsSaturated(pp, pn))
                {
                    result.SaturatedAxes |= flag;
                    result.Confidence *= 0.5f;
                }

                float extent = boxExtents[axis];
                float tBox = ContactEncoderMath.DecodeAxis(pp, pn);
                float u = ContactEncoderMath.BoxToRegion(tBox, extent, regionExtents[axis]);

                // A point outside the unpadded region means the sender is near the body but not on
                // it - the padded box is deliberately larger than the thing it describes.
                if (u < 0f || u > 1f)
                    result.Confidence *= 0.6f;

                position[axis] = ContactEncoderMath.Clamp01(u);

                float footprint = ContactEncoderMath.DecodeFootprint(pp, pn, extent);
                if (footprint < footprintMin) footprintMin = footprint;
                if (footprint > footprintMax) footprintMax = footprint;
            }

            if (active == 0)
                return default;

            result.InContact = true;
            result.Position = new EncodedPoint(position[0], position[1], position[2]);

            // A footprint is 2r for a single sender and 2r + separation for two. A second sender
            // can only inflate a footprint, so the *smallest* across axes is the least contaminated
            // radius estimate, and the excess of the largest over it is the separation. Averaging
            // instead would fold part of the separation back into the radius and under-report the
            // spread by a third.
            result.SenderRadius = Math.Max(0f, footprintMin * 0.5f);
            result.Spread = Math.Max(0f, footprintMax - footprintMin);

            float largestExtent = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                if ((axes & AxisFlag(axis)) == 0) continue;
                if (regionExtents[axis] > largestExtent) largestExtent = regionExtents[axis];
            }
            result.SpreadNormalised = largestExtent > 0f ? result.Spread / largestExtent : 0f;

            if (result.IsMultiTouch)
                result.Confidence *= 0.4f;

            return result;
        }

        internal static EncoderAxes AxisFlag(int axis)
            => axis == 0 ? EncoderAxes.X : axis == 1 ? EncoderAxes.Y : EncoderAxes.Z;

        /// <summary>Number of receivers a group with these axes will emit.</summary>
        public static int ReceiverCount(EncoderAxes axes)
        {
            int n = 0;
            if ((axes & EncoderAxes.X) != 0) n++;
            if ((axes & EncoderAxes.Y) != 0) n++;
            if ((axes & EncoderAxes.Z) != 0) n++;
            return n * 2;
        }
    }
}
