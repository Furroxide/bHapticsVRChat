using System;
using System.Collections.Generic;

namespace Furroxide.ContactCompressor
{
    /// <summary>
    /// What the build produced, in a form an OSC consumer can read.
    ///
    /// This is the automagic part. The author places one receiver per point exactly as they always
    /// have; the compressor records where each of those points sits inside the fitted region, so a
    /// consumer decoding a position back into "which point is being touched" needs no hard-coded
    /// layout table. The receivers the author already wrote *are* the calibration.
    /// </summary>
    [Serializable]
    public class ContactCompressorManifest
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        /// <summary>Parameter namespace these regions were emitted under.</summary>
        public string prefix = ContactParameterNames.DefaultPrefix;

        /// <summary>Free-form identifier for whoever generated this, for diagnostics.</summary>
        public string generator = "";

        public List<ContactRegionManifest> regions = new List<ContactRegionManifest>();

        public ContactRegionManifest Find(string regionId)
        {
            if (regions == null) return null;
            for (int i = 0; i < regions.Count; i++)
                if (string.Equals(regions[i].id, regionId, StringComparison.Ordinal))
                    return regions[i];
            return null;
        }
    }

    [Serializable]
    public class ContactRegionManifest
    {
        /// <summary>Region identifier as it appears in the parameter names.</summary>
        public string id = "";

        /// <summary>Encoded axes, as the letters present: "XYZ", "XZ", "Y", and so on.</summary>
        public string axes = "XYZ";

        /// <summary>Padded box size in metres, per axis. Needed to convert proximity into a sender radius.</summary>
        public float[] boxExtents = new float[3];

        /// <summary>Unpadded region size in metres, per axis. The volume the points actually occupy.</summary>
        public float[] regionExtents = new float[3];

        /// <summary>The original per-point receivers, with their region-normalised positions.</summary>
        public List<ContactPointManifest> points = new List<ContactPointManifest>();

        public EncoderAxes ParsedAxes
        {
            get
            {
                var result = EncoderAxes.None;
                if (string.IsNullOrEmpty(axes)) return result;
                if (axes.IndexOf('X') >= 0) result |= EncoderAxes.X;
                if (axes.IndexOf('Y') >= 0) result |= EncoderAxes.Y;
                if (axes.IndexOf('Z') >= 0) result |= EncoderAxes.Z;
                return result;
            }
        }

        public EncodedPoint BoxExtentsPoint =>
            new EncodedPoint(Get(boxExtents, 0), Get(boxExtents, 1), Get(boxExtents, 2));

        public EncodedPoint RegionExtentsPoint =>
            new EncodedPoint(Get(regionExtents, 0), Get(regionExtents, 1), Get(regionExtents, 2));

        /// <summary>
        /// Padding per side, in metres, derived from the box and region extents rather than stored.
        /// Storing it too would create a second source of truth that could disagree with the boxes
        /// actually emitted onto the avatar.
        /// </summary>
        public float PaddingMetres(int axis)
            => ContactEncoderMath.PaddingOf(Get(boxExtents, axis), Get(regionExtents, axis));

        /// <summary>Largest collider radius this region can resolve without saturating, in metres.</summary>
        public float MaxResolvableRadius
        {
            get
            {
                float smallest = float.MaxValue;
                for (int axis = 0; axis < 3; axis++)
                {
                    var flag = ContactEncoderSolver.AxisFlag(axis);
                    if ((ParsedAxes & flag) == 0) continue;
                    float p = PaddingMetres(axis);
                    if (p < smallest) smallest = p;
                }
                return smallest == float.MaxValue ? 0f : smallest;
            }
        }

        static float Get(float[] a, int i) => a != null && i < a.Length ? a[i] : 0f;
    }

    [Serializable]
    public class ContactPointManifest
    {
        /// <summary>The animator parameter the original receiver drove, e.g. "bOSC/v2/VestFront/7/others".</summary>
        public string id = "";

        /// <summary>Region-normalised position, 0..1 per axis.</summary>
        public float u, v, w;

        /// <summary>Radius of the original receiver, in metres. Indicates how far its influence reached.</summary>
        public float radius;

        public EncodedPoint Position => new EncodedPoint(u, v, w);

        /// <summary>Squared distance to a solved contact position, in region-normalised units.</summary>
        public float DistanceSquaredTo(EncodedPoint p, EncoderAxes axes)
        {
            float d = 0f;
            if ((axes & EncoderAxes.X) != 0) { float t = u - p.X; d += t * t; }
            if ((axes & EncoderAxes.Y) != 0) { float t = v - p.Y; d += t * t; }
            if ((axes & EncoderAxes.Z) != 0) { float t = w - p.Z; d += t * t; }
            return d;
        }
    }
}
