using System;

namespace Furroxide.ContactCompressor
{
    /// <summary>
    /// The encode/decode maths for face-proximity contact position encoding.
    ///
    /// A box <c>VRCContactReceiver</c> with <c>Use Face Proximity</c> reports a value that is
    /// perfectly linear in the sender's position along the box's local Z axis. Verified against a
    /// literal port of <c>VRC.Dynamics.ContactManager.UpdateReceiversFunctions.CalcProximity</c>
    /// (VRChat SDK 3.10.4): for a sender whose centre sits at normalised position <c>t</c> along
    /// the box depth <c>L</c>, with radius <c>r</c>,
    ///
    /// <code>P = clamp01(t + r / L)</code>
    ///
    /// The <c>r / L</c> term is the problem: <c>r</c> is the *toucher's* collider radius, which
    /// varies per avatar and cannot be known. A single box is therefore wrong by <c>r</c> per axis
    /// (measured: <c>r * sqrt(3)</c> in 3D, i.e. 173 mm for a 0.10 m collider).
    ///
    /// Two boxes covering the same volume, one rotated 180 degrees, cancel it exactly:
    ///
    /// <code>
    ///   P+ = t + r/L
    ///   P- = (1 - t) + r/L
    ///   =>  t = (P+ - P- + 1) / 2      exact, independent of r
    ///   =>  r = L * (P+ + P- - 1) / 2  the toucher's collider size, for free
    /// </code>
    ///
    /// Both identities were measured exact to float epsilon for sphere senders, and to within
    /// 0.64 mm for capsule senders at realistic VRChat collider sizes.
    /// </summary>
    public static class ContactEncoderMath
    {
        /// <summary>Proximity below which a receiver is considered not in contact.</summary>
        public const float ContactEpsilon = 1e-4f;

        /// <summary>
        /// Default padding, in metres per side.
        ///
        /// The box must be larger than the region it describes, because <c>P</c> clamps at 1.0 once
        /// a sender reaches a face and the algebra stops working there. Substituting the saturation
        /// condition <c>t + r/L &lt;= 1</c> into <c>L = regionExtent + 2*padding</c> collapses to a
        /// strikingly simple rule:
        ///
        /// <code>padding >= r</code>
        ///
        /// That is, padding must simply exceed the largest collider you expect to be touched with,
        /// independent of how big the region is. Padding expressed as a *fraction* of the region
        /// fails exactly where it matters: a torso is ~0.26 m front-to-back, so 30% padding leaves
        /// only 0.078 m of headroom and a 0.10 m collider saturates against the chest - the single
        /// most common contact there is.
        ///
        /// 0.10 m comfortably covers VRChat's stock hand and foot colliders.
        /// </summary>
        public const float DefaultPaddingMetres = 0.10f;

        /// <summary>Box extent along one axis, in metres.</summary>
        public static float BoxExtent(float regionExtent, float paddingMetres)
            => regionExtent + 2f * paddingMetres;

        /// <summary>Padding per side implied by a fitted box, in metres.</summary>
        public static float PaddingOf(float boxExtent, float regionExtent)
            => (boxExtent - regionExtent) * 0.5f;

        /// <summary>Largest sender radius an axis can resolve without saturating, in metres.</summary>
        public static float MaxResolvableRadius(float boxExtent, float regionExtent)
            => PaddingOf(boxExtent, regionExtent);

        /// <summary>Region-normalised [0,1] -> box-normalised [0,1].</summary>
        public static float RegionToBox(float u, float boxExtent, float regionExtent)
            => boxExtent <= 0f ? 0.5f : (PaddingOf(boxExtent, regionExtent) + u * regionExtent) / boxExtent;

        /// <summary>
        /// Box-normalised [0,1] -> region-normalised [0,1]. Deliberately unclamped: a result outside
        /// [0,1] means the sender is inside the padding, near the region but not on it.
        /// </summary>
        public static float BoxToRegion(float t, float boxExtent, float regionExtent)
            => regionExtent <= 0f ? 0.5f : (t * boxExtent - PaddingOf(boxExtent, regionExtent)) / regionExtent;

        /// <summary>
        /// What a +face receiver reports for a sender at box-normalised position <paramref name="t"/>.
        /// Present so tests and the region fitter can predict hardware behaviour without Unity.
        /// </summary>
        public static float Proximity(float t, float senderRadius, float boxExtent)
            => Clamp01(t + senderRadius / boxExtent);

        /// <summary>Box-normalised position along an axis, from an opposed receiver pair. Exact; independent of sender size.</summary>
        public static float DecodeAxis(float pPlus, float pMinus)
            => (pPlus - pMinus + 1f) * 0.5f;

        /// <summary>
        /// Total extent of contact along this axis, in metres: <c>2r</c> for a single sender of
        /// radius <c>r</c>, and <c>2r + separation</c> when two distinct senders are present.
        ///
        /// VRChat combines overlapping senders with <c>math.max</c>, so <c>P+</c> locks onto
        /// whichever sender is nearest its own face and <c>P-</c> onto the other; the pair sum
        /// then measures the span between them. A single axis cannot tell "one big collider" from
        /// "two small ones" - that separation is what <see cref="ContactEncoderSolver"/> does by
        /// comparing footprints across axes.
        /// </summary>
        public static float DecodeFootprint(float pPlus, float pMinus, float boxExtent)
            => boxExtent * (pPlus + pMinus - 1f);

        /// <summary>
        /// The touching collider's radius along this axis, in metres, assuming a single sender.
        /// Free by-product of the pair. Prefer the smallest value across axes: a second sender can
        /// only ever inflate a footprint, never shrink it.
        /// </summary>
        public static float DecodeSenderRadius(float pPlus, float pMinus, float boxExtent)
            => DecodeFootprint(pPlus, pMinus, boxExtent) * 0.5f;

        /// <summary>True when an axis reads as saturated, i.e. the sender reached a face and the decode is unreliable.</summary>
        public static bool IsSaturated(float pPlus, float pMinus)
            => pPlus >= 1f || pMinus >= 1f;

        /// <summary>True when neither receiver of the pair sees anything.</summary>
        public static bool IsIdle(float pPlus, float pMinus)
            => pPlus <= ContactEpsilon && pMinus <= ContactEpsilon;

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
