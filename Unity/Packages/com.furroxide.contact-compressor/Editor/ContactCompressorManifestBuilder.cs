using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>Turns fitted regions into the manifest an OSC consumer reads.</summary>
    public static class ContactCompressorManifestBuilder
    {
        public const string DefaultFileName = "contact-compressor.json";

        public static ContactCompressorManifest Build(IEnumerable<FittedRegion> fits, string generator = null)
        {
            var manifest = new ContactCompressorManifest
            {
                generator = string.IsNullOrEmpty(generator) ? "com.furroxide.contact-compressor" : generator
            };

            bool prefixTaken = false;

            foreach (var fit in fits)
            {
                if (fit == null || !fit.IsValid || fit.Group == null) continue;

                // The manifest carries one prefix, so mixed prefixes across groups cannot be
                // represented. Take the first and let the caller's validation surface the clash.
                if (!prefixTaken)
                {
                    manifest.prefix = string.IsNullOrWhiteSpace(fit.Group.parameterPrefix)
                        ? ContactParameterNames.DefaultPrefix
                        : fit.Group.parameterPrefix.Trim('/');
                    prefixTaken = true;
                }

                var region = new ContactRegionManifest
                {
                    id = fit.Group.regionId,
                    axes = fit.Group.AxesString,
                    boxExtents = new[] { fit.BoxExtents.x, fit.BoxExtents.y, fit.BoxExtents.z },
                    regionExtents = new[] { fit.RegionExtents.x, fit.RegionExtents.y, fit.RegionExtents.z }
                };

                // Several source receivers can describe one physical point - typically a self/others
                // pair at the same place. Collapsing them matters beyond tidiness: a consumer
                // interpolating over the four nearest points would otherwise spend two of them on
                // the same spot and skew the result toward it.
                foreach (var byPoint in fit.Points.GroupBy(p => p.PointId))
                {
                    var members = byPoint.ToList();
                    var sum = Vector3.zero;
                    float radius = 0f;

                    foreach (var member in members)
                    {
                        sum += member.Normalised;
                        radius += member.Radius;
                    }

                    region.points.Add(new ContactPointManifest
                    {
                        id = byPoint.Key,
                        u = sum.x / members.Count,
                        v = sum.y / members.Count,
                        w = sum.z / members.Count,
                        radius = radius / members.Count
                    });
                }

                manifest.regions.Add(region);
            }

            return manifest;
        }

        public static string ToJson(ContactCompressorManifest manifest, bool pretty = true)
            => JsonUtility.ToJson(manifest, pretty);

        public static ContactCompressorManifest FromJson(string json)
            => JsonUtility.FromJson<ContactCompressorManifest>(json);
    }
}
