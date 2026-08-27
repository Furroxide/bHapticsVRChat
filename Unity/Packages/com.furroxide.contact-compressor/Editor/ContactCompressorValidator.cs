using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>How well a fitted region survives a round trip.</summary>
    public struct ValidationResult
    {
        public bool Ran;
        public float SenderRadius;

        public int PointsChecked;

        /// <summary>Points whose decoded position resolves to a *different* authored point than the one touched.</summary>
        public int Misattributed;

        /// <summary>Points where an axis saturated against a box face.</summary>
        public int Saturated;

        public float WorstErrorMetres;
        public float MeanErrorMetres;
        public string WorstPointId;

        public bool IsClean => Ran && Misattributed == 0 && Saturated == 0;
    }

    /// <summary>
    /// Simulates a touch on every authored point and checks it comes back as that same point.
    ///
    /// This is the question an author actually cares about, and it is not the same as "is the
    /// position accurate": a millimetre of error is irrelevant if the points are 10 cm apart, and
    /// fatal if they are 5 mm apart. Reporting misattributions directly answers "will touching
    /// point 7 fire point 7".
    ///
    /// The simulation runs through <see cref="VrcProximityReference"/> - VRChat's own proximity
    /// maths, ported literally out of the SDK - so this is checking against what the game computes
    /// rather than against the encoder's own assumptions.
    /// </summary>
    public static class ContactCompressorValidator
    {
        /// <summary>Radius to simulate with by default. Roughly VRChat's stock hand collider.</summary>
        public const float DefaultSenderRadius = 0.05f;

        public static ValidationResult Validate(FittedRegion fit, float senderRadius = DefaultSenderRadius)
        {
            var result = new ValidationResult { SenderRadius = senderRadius };
            if (fit == null || !fit.IsValid || fit.Points.Count == 0) return result;

            // Several receivers can sit at the same spot under different parameters (a self/others
            // pair). They are one point for this purpose.
            var points = fit.Points
                .GroupBy(p => p.PointId)
                .Select(g => g.First())
                .ToList();

            var box = new VrcProximityReference.Vec(fit.BoxExtents.x, fit.BoxExtents.y, fit.BoxExtents.z);
            var origin = new VrcProximityReference.Vec(0, 0, 0);
            var boxExtents = new EncodedPoint(fit.BoxExtents.x, fit.BoxExtents.y, fit.BoxExtents.z);
            var regionExtents = new EncodedPoint(fit.RegionExtents.x, fit.RegionExtents.y, fit.RegionExtents.z);

            var plus = new float[3];
            var minus = new float[3];
            var senders = new VrcProximityReference.Sender[1];

            float errorSum = 0f;

            foreach (var point in points)
            {
                // The emitted encoder sits at the region centre with identity rotation relative to
                // the frame, so region space is frame space shifted by the centre.
                Vector3 local = point.Local - fit.CentreLocal;
                senders[0] = VrcProximityReference.Sender.Sphere(
                    new VrcProximityReference.Vec(local.x, local.y, local.z), senderRadius);

                VrcProximityReference.ReadRegion(origin, box, senders, plus, minus);

                var solution = ContactEncoderSolver.Solve(plus, minus, boxExtents, regionExtents, fit.Axes);
                if (!solution.InContact) continue;

                result.PointsChecked++;
                if (solution.SaturatedAxes != EncoderAxes.None) result.Saturated++;

                Vector3 decoded = RegionToLocal(solution.Position, fit);
                float error = Vector3.Distance(decoded, point.Local);

                errorSum += error;
                if (error > result.WorstErrorMetres)
                {
                    result.WorstErrorMetres = error;
                    result.WorstPointId = point.PointId;
                }

                if (NearestPoint(points, solution.Position, fit.Axes) != point.PointId)
                    result.Misattributed++;
            }

            if (result.PointsChecked > 0)
            {
                result.MeanErrorMetres = errorSum / result.PointsChecked;
                result.Ran = true;
            }

            return result;
        }

        static Vector3 RegionToLocal(EncodedPoint normalised, FittedRegion fit)
        {
            var min = fit.CentreLocal - fit.RegionExtents * 0.5f;
            return new Vector3(
                min.x + normalised.X * fit.RegionExtents.x,
                min.y + normalised.Y * fit.RegionExtents.y,
                min.z + normalised.Z * fit.RegionExtents.z);
        }

        static string NearestPoint(List<FittedPoint> points, EncodedPoint solved, EncoderAxes axes)
        {
            string best = null;
            float bestDistance = float.MaxValue;

            foreach (var point in points)
            {
                float d = 0f;
                if ((axes & EncoderAxes.X) != 0) { float t = point.Normalised.x - solved.X; d += t * t; }
                if ((axes & EncoderAxes.Y) != 0) { float t = point.Normalised.y - solved.Y; d += t * t; }
                if ((axes & EncoderAxes.Z) != 0) { float t = point.Normalised.z - solved.Z; d += t * t; }

                if (d < bestDistance) { bestDistance = d; best = point.PointId; }
            }

            return best;
        }
    }
}
