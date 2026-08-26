using System;
using System.Collections.Generic;

namespace Furroxide.ContactCompressor
{
    /// <summary>One authored point and how strongly the current contact falls on it.</summary>
    public struct WeightedPoint
    {
        /// <summary>The parameter name of the original per-point receiver, e.g. "bOSC/v2/VestFront/7/others".</summary>
        public string Id;

        /// <summary>0..1. Weights across a returned set sum to 1.</summary>
        public float Weight;

        /// <summary>Distance from the contact to this point, in region-normalised units.</summary>
        public float Distance;
    }

    /// <summary>
    /// Turns a stream of OSC parameter updates into contact positions, and then into weights over
    /// whatever points the avatar author originally placed.
    ///
    /// Feed it every <c>/avatar/parameters/...</c> value you receive via <see cref="Accept"/>; it
    /// ignores anything that is not one of its parameters. Then ask a region for its current
    /// contact. Nothing here knows what a motor is - the manifest supplies the point layout, so the
    /// same decoder drives a haptic vest, a light-up shader, or a DIY suit without changes.
    /// </summary>
    public sealed class ContactCompressorDecoder
    {
        readonly object _gate = new object();
        readonly ContactCompressorManifest _manifest;
        readonly Dictionary<string, RegionState> _regions = new Dictionary<string, RegionState>(StringComparer.Ordinal);

        sealed class RegionState
        {
            public ContactRegionManifest Manifest;
            public readonly float[] Plus = new float[3];
            public readonly float[] Minus = new float[3];
        }

        /// <param name="manifest">Layout emitted by the Unity build. Must not be null.</param>
        public ContactCompressorDecoder(ContactCompressorManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (manifest.version > ContactCompressorManifest.CurrentVersion)
                throw new NotSupportedException(
                    $"Manifest version {manifest.version} is newer than this decoder supports ({ContactCompressorManifest.CurrentVersion}).");

            if (manifest.regions != null)
                foreach (var region in manifest.regions)
                    if (region != null && !string.IsNullOrEmpty(region.id))
                        _regions[region.id] = new RegionState { Manifest = region };
        }

        /// <summary>Region ids this decoder knows about.</summary>
        public IEnumerable<string> RegionIds => _regions.Keys;

        /// <summary>
        /// Offer one parameter update. Accepts either a bare parameter name or a full OSC address.
        /// Returns true if the value belonged to this decoder and was stored.
        /// </summary>
        public bool Accept(string parameterOrAddress, float value)
        {
            if (!ContactParameterNames.TryParse(parameterOrAddress, _manifest.prefix,
                                                out string regionId, out int axis, out bool positive))
                return false;

            lock (_gate)
            {
                if (!_regions.TryGetValue(regionId, out var state)) return false;
                (positive ? state.Plus : state.Minus)[axis] = value;
                return true;
            }
        }

        /// <summary>Current contact for one region. Returns false for an unknown region.</summary>
        public bool TrySolve(string regionId, out ContactSolution solution)
        {
            solution = default;
            lock (_gate)
            {
                if (!_regions.TryGetValue(regionId, out var state)) return false;
                solution = ContactEncoderSolver.Solve(
                    state.Plus, state.Minus,
                    state.Manifest.BoxExtentsPoint,
                    state.Manifest.RegionExtentsPoint,
                    state.Manifest.ParsedAxes);
                return true;
            }
        }

        /// <summary>Clears every stored value, e.g. on avatar change.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                foreach (var state in _regions.Values)
                    for (int i = 0; i < 3; i++) { state.Plus[i] = 0f; state.Minus[i] = 0f; }
            }
        }

        /// <summary>
        /// Distributes the current contact across the region's authored points, nearest first.
        ///
        /// Weighting is inverse-distance over the <paramref name="maxPoints"/> nearest points,
        /// with the falloff width taken from the decoded collider size - so a fingertip lands
        /// tightly on one point and a full palm spreads across several, which a per-point on/off
        /// receiver could never express. Works for any point layout, not just grids.
        ///
        /// Returns an empty list when the region is not being touched.
        /// </summary>
        /// <param name="regionId">Region to sample.</param>
        /// <param name="maxPoints">How many points to spread across. 4 mimics bilinear interpolation on a grid.</param>
        /// <param name="minimumConfidence">Below this the contact is treated as unreliable and every point is returned at equal weight, i.e. a region-wide response rather than a phantom pinpoint.</param>
        public IReadOnlyList<WeightedPoint> Sample(string regionId, int maxPoints = 4, float minimumConfidence = 0.5f)
        {
            if (maxPoints < 1) throw new ArgumentOutOfRangeException(nameof(maxPoints));

            ContactRegionManifest region;
            ContactSolution solution;
            lock (_gate)
            {
                if (!_regions.TryGetValue(regionId, out var state)) return Array.Empty<WeightedPoint>();
                region = state.Manifest;
                solution = ContactEncoderSolver.Solve(
                    state.Plus, state.Minus, region.BoxExtentsPoint, region.RegionExtentsPoint, region.ParsedAxes);
            }

            if (!solution.InContact || region.points == null || region.points.Count == 0)
                return Array.Empty<WeightedPoint>();

            // Not confident where the touch is - most often two people touching one region at once,
            // where the per-axis maxima describe a point that nobody is actually at. Spreading
            // evenly is honest; pinpointing a phantom is not.
            if (solution.Confidence < minimumConfidence)
            {
                var flat = new WeightedPoint[region.points.Count];
                float share = 1f / region.points.Count;
                for (int i = 0; i < region.points.Count; i++)
                    flat[i] = new WeightedPoint { Id = region.points[i].id, Weight = share, Distance = float.NaN };
                return flat;
            }

            var axes = region.ParsedAxes;
            var scratch = new List<(ContactPointManifest point, float distSq)>(region.points.Count);
            foreach (var point in region.points)
                scratch.Add((point, point.DistanceSquaredTo(solution.Position, axes)));

            scratch.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            int take = Math.Min(maxPoints, scratch.Count);

            // Falloff width in region-normalised units. The decoded radius is in metres, so scale
            // it by the region size; clamp so a tiny collider still reaches its nearest point.
            float sigma = Math.Max(0.08f, NormalisedRadius(solution.SenderRadius, region));

            var results = new WeightedPoint[take];
            float total = 0f;
            for (int i = 0; i < take; i++)
            {
                float distance = (float)Math.Sqrt(scratch[i].distSq);
                float weight = (float)Math.Exp(-(distance * distance) / (2f * sigma * sigma));
                results[i] = new WeightedPoint { Id = scratch[i].point.id, Weight = weight, Distance = distance };
                total += weight;
            }

            if (total > 0f)
                for (int i = 0; i < take; i++) results[i].Weight /= total;
            else
                results[0].Weight = 1f;

            return results;
        }

        static float NormalisedRadius(float metres, ContactRegionManifest region)
        {
            float largest = 0f;
            if (region.regionExtents != null)
                foreach (float e in region.regionExtents)
                    if (e > largest) largest = e;
            return largest > 0f ? metres / largest : 0f;
        }
    }
}
