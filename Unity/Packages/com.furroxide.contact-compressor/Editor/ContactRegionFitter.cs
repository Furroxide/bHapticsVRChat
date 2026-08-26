using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VRC.Dynamics;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>One source receiver and where it sits inside the fitted region.</summary>
    public struct FittedPoint
    {
        public ContactReceiver Receiver;
        public string Parameter;

        /// <summary>
        /// Identity this point has in the manifest. Several receivers can share one - a self/others
        /// pair at the same spot is one physical point, not two.
        /// </summary>
        public string PointId;

        /// <summary>Position in the group's frame, in local units.</summary>
        public Vector3 Local;

        /// <summary>Position normalised to the region box, 0..1 per axis.</summary>
        public Vector3 Normalised;

        public float Radius;
    }

    /// <summary>Result of fitting an encoder box to a group's receivers.</summary>
    public class FittedRegion
    {
        public ContactCompressorGroup Group;
        public bool IsValid;

        /// <summary>Region centre in the frame's local space.</summary>
        public Vector3 CentreLocal;

        /// <summary>Extent of the points themselves, per axis, in the frame's local units.</summary>
        public Vector3 RegionExtents;

        /// <summary>Region extents plus padding on both sides. This is the box actually emitted.</summary>
        public Vector3 BoxExtents;

        public EncoderAxes Axes;
        public List<FittedPoint> Points = new List<FittedPoint>();

        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();

        /// <summary>Union of allowSelf across the sources.</summary>
        public bool AllowSelf;

        /// <summary>Union of allowOthers across the sources.</summary>
        public bool AllowOthers;

        /// <summary>Union of the sources' collision tags, or the group's override.</summary>
        public List<string> CollisionTags = new List<string>();

        public bool LocalOnly;

        public int SourceReceiverCount => Points.Count;
        public int EmittedReceiverCount => ContactEncoderSolver.ReceiverCount(Axes);

        /// <summary>
        /// Largest collider radius this fit can resolve before an axis saturates. Equal to the
        /// padding, by the identity padding &gt;= r.
        /// </summary>
        public float MaxResolvableRadius => Group != null ? Group.paddingMetres : 0f;
    }

    /// <summary>
    /// Works out the encoder box for a group by looking at the receivers the author already placed.
    ///
    /// This is what makes the whole thing automagic: nothing has to be described twice. The points
    /// define the region, the region defines the box, and the same points - expressed as normalised
    /// coordinates inside that box - become the manifest a decoder uses to turn a position back
    /// into "which point". Move a receiver and everything downstream follows.
    /// </summary>
    public static class ContactRegionFitter
    {
        /// <summary>
        /// Below this an axis carries no usable information - every point sits at the same place
        /// along it, so the decode would be a division by nearly zero.
        /// </summary>
        public const float MinimumRegionExtent = 0.02f;

        /// <summary>VRChat rejects contact shapes larger than this on any axis.</summary>
        public const float MaximumBoxExtent = ContactBase.MAX_SIZE;

        /// <summary>Receivers this group owns: under its source root, matching its filter, and not claimed by a nested group.</summary>
        public static List<ContactReceiver> CollectSources(ContactCompressorGroup group)
        {
            var result = new List<ContactReceiver>();
            if (group == null) return result;

            Transform root = group.ResolvedSourceRoot;
            if (root == null) return result;

            Regex filter = null;
            if (!string.IsNullOrWhiteSpace(group.sourceParameterPattern))
            {
                try { filter = new Regex(group.sourceParameterPattern); }
                catch (System.ArgumentException) { return result; }   // reported by Fit()
            }

            foreach (var receiver in root.GetComponentsInChildren<ContactReceiver>(true))
            {
                if (receiver == null) continue;
                if (string.IsNullOrWhiteSpace(receiver.parameter)) continue;

                // Anything we previously emitted, or a re-run over our own output.
                if (ContactParameterNames.TryParse(receiver.parameter, group.parameterPrefix, out _, out _, out _))
                    continue;

                // A receiver inside a nested group belongs to that group, not this one.
                var owner = receiver.GetComponentInParent<ContactCompressorGroup>();
                if (owner != null && owner != group) continue;

                if (filter != null && !filter.IsMatch(receiver.parameter)) continue;

                result.Add(receiver);
            }

            return result;
        }

        public static FittedRegion Fit(ContactCompressorGroup group)
        {
            var fit = new FittedRegion { Group = group };

            if (group == null)
            {
                fit.Errors.Add("No group.");
                return fit;
            }

            if (string.IsNullOrWhiteSpace(group.regionId))
                fit.Errors.Add("Region id is empty.");
            else if (group.regionId.Contains("/"))
                fit.Errors.Add($"Region id '{group.regionId}' must not contain '/'.");

            if (group.axes == EncoderAxes.None)
                fit.Errors.Add("No axes selected, so there is nothing to encode.");

            if (!string.IsNullOrWhiteSpace(group.sourceParameterPattern))
            {
                try { new Regex(group.sourceParameterPattern); }
                catch (System.ArgumentException e) { fit.Errors.Add($"Source pattern is not a valid regex: {e.Message}"); }
            }

            if (!string.IsNullOrWhiteSpace(group.pointIdPattern))
            {
                try { new Regex(group.pointIdPattern); }
                catch (System.ArgumentException e) { fit.Errors.Add($"Point id pattern is not a valid regex: {e.Message}"); }
            }

            var sources = CollectSources(group);
            if (sources.Count == 0)
            {
                fit.Errors.Add("Found no contact receivers to compress under the source root.");
                return fit;
            }

            Transform frame = group.ResolvedFrame;
            var localPositions = new List<Vector3>(sources.Count);
            foreach (var receiver in sources)
                localPositions.Add(frame.InverseTransformPoint(WorldCentreOf(receiver)));

            Vector3 min = localPositions[0], max = localPositions[0];
            foreach (var p in localPositions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            fit.CentreLocal = (min + max) * 0.5f;
            var extents = max - min;
            fit.Axes = group.axes;

            for (int axis = 0; axis < 3; axis++)
            {
                var flag = ContactEncoderSolver.AxisFlag(axis);
                if ((fit.Axes & flag) == 0) continue;

                if (extents[axis] < MinimumRegionExtent)
                {
                    fit.Warnings.Add(
                        $"Points vary by only {extents[axis] * 1000f:F1} mm along {ContactParameterNames.AxisLetter(axis)}. " +
                        "That axis carries almost no information - consider turning it off to save two receivers.");
                    extents[axis] = MinimumRegionExtent;
                }
            }

            fit.RegionExtents = extents;
            fit.BoxExtents = new Vector3(
                ContactEncoderMath.BoxExtent(extents.x, group.paddingMetres),
                ContactEncoderMath.BoxExtent(extents.y, group.paddingMetres),
                ContactEncoderMath.BoxExtent(extents.z, group.paddingMetres));

            Vector3 lossy = frame.lossyScale;
            for (int axis = 0; axis < 3; axis++)
            {
                float worldExtent = fit.BoxExtents[axis] * Mathf.Abs(lossy[axis]);
                if (worldExtent > MaximumBoxExtent)
                    fit.Errors.Add(
                        $"Box would be {worldExtent:F2} m along {ContactParameterNames.AxisLetter(axis)}; " +
                        $"VRChat caps contact shapes at {MaximumBoxExtent} m.");
            }

            for (int i = 0; i < sources.Count; i++)
            {
                var local = localPositions[i];
                var normalised = new Vector3(
                    Normalise(local.x, fit.CentreLocal.x, extents.x),
                    Normalise(local.y, fit.CentreLocal.y, extents.y),
                    Normalise(local.z, fit.CentreLocal.z, extents.z));

                fit.Points.Add(new FittedPoint
                {
                    Receiver = sources[i],
                    Parameter = sources[i].parameter,
                    PointId = ToPointId(group, sources[i].parameter),
                    Local = local,
                    Normalised = normalised,
                    Radius = sources[i].radius
                });
            }

            // Receivers that resolve to one logical point should also be in one place. If they are
            // not, the manifest position becomes an average of somewhere nobody is being touched.
            foreach (var group2 in fit.Points.GroupBy(p => p.PointId).Where(g => g.Count() > 1))
            {
                float spread = 0f;
                var members = group2.ToList();
                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                        spread = Mathf.Max(spread, Vector3.Distance(members[i].Local, members[j].Local));

                if (spread > 0.01f)
                    fit.Warnings.Add(
                        $"{members.Count} receivers map to point '{group2.Key}' but sit up to {spread * 1000f:F0} mm " +
                        "apart. Their manifest position will be the average, which may be somewhere none of them is.");
            }

            fit.AllowSelf = sources.Any(r => r.allowSelf);
            fit.AllowOthers = sources.Any(r => r.allowOthers);

            fit.CollisionTags = group.collisionTagsOverride != null && group.collisionTagsOverride.Count > 0
                ? group.collisionTagsOverride.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
                : sources.SelectMany(r => r.collisionTags ?? new List<string>())
                         .Where(t => !string.IsNullOrWhiteSpace(t))
                         .Distinct()
                         .ToList();

            if (fit.CollisionTags.Count == 0)
                fit.Errors.Add("No collision tags. A contact with no tags can never be triggered.");
            else if (fit.CollisionTags.Count > ContactBase.MAX_COLLISION_TAGS)
            {
                fit.Warnings.Add(
                    $"The sources use {fit.CollisionTags.Count} distinct tags but VRChat only honours the first " +
                    $"{ContactBase.MAX_COLLISION_TAGS}. Set an explicit tag list on the group to choose which ones survive.");
                fit.CollisionTags = fit.CollisionTags.Take(ContactBase.MAX_COLLISION_TAGS).ToList();
            }

            // Merging receivers that used different self/others splits into one channel widens what
            // can trigger it, which can make an avatar trigger on its own body.
            if (fit.AllowSelf && fit.AllowOthers && sources.Any(r => r.allowSelf != r.allowOthers))
            {
                fit.Warnings.Add(
                    "The sources were split into self-only and others-only receivers with different tag sets; " +
                    "merging them into one channel means the union of both tag sets now triggers for both. " +
                    "If the avatar starts triggering on itself, split this into two groups.");
            }

            switch (group.localOnly)
            {
                case LocalOnlyMode.Always: fit.LocalOnly = true; break;
                case LocalOnlyMode.Never: fit.LocalOnly = false; break;
                default: fit.LocalOnly = sources.All(r => r.localOnly); break;
            }

            fit.IsValid = fit.Errors.Count == 0;
            return fit;
        }

        /// <summary>
        /// World-space centre of a contact's shape. Mirrors <c>ContactBase.UpdateShape</c>, which
        /// builds the shape from <c>GetRootTransform()</c> and the local <c>position</c> offset -
        /// not from the component's own transform, which is often not the same thing.
        /// </summary>
        public static Vector3 WorldCentreOf(ContactBase contact)
        {
            Transform root = contact.GetRootTransform();
            if (root == null) root = contact.transform;
            return root.localToWorldMatrix.MultiplyPoint3x4(contact.position);
        }

        /// <summary>Applies the group's point-id regex, falling back to the parameter name.</summary>
        static string ToPointId(ContactCompressorGroup group, string parameter)
        {
            if (string.IsNullOrWhiteSpace(group.pointIdPattern)) return parameter;

            try
            {
                return Regex.Replace(parameter, group.pointIdPattern, group.pointIdReplacement ?? string.Empty);
            }
            catch (System.ArgumentException)
            {
                return parameter;   // reported as an error by Fit()
            }
        }

        static float Normalise(float value, float centre, float extent)
            => extent <= 0f ? 0.5f : Mathf.Clamp01((value - (centre - extent * 0.5f)) / extent);
    }
}
