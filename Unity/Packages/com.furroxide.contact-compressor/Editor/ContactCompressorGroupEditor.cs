using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>
    /// Inspector for a group. Its job is to answer, before the author uploads anything, the two
    /// questions that actually matter: how many receivers will this save, and will the decode be
    /// accurate enough to be worth it.
    /// </summary>
    [CustomEditor(typeof(ContactCompressorGroup))]
    [CanEditMultipleObjects]
    public class ContactCompressorGroupEditor : UnityEditor.Editor
    {
        FittedRegion _fit;
        ValidationResult _validation;
        double _nextRefresh;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox("Select a single group to see its preview.", MessageType.None);
                return;
            }

            var group = (ContactCompressorGroup)target;

            // Refitting walks the whole receiver hierarchy, so throttle it rather than doing it
            // every repaint.
            if (_fit == null || EditorApplication.timeSinceStartup > _nextRefresh)
            {
                _fit = ContactRegionFitter.Fit(group);
                _validation = default;
                _nextRefresh = EditorApplication.timeSinceStartup + 0.5;
            }

            EditorGUILayout.Space();
            DrawPreview(group, _fit);
        }

        void DrawPreview(ContactCompressorGroup group, FittedRegion fit)
        {
            foreach (var error in fit.Errors)
                EditorGUILayout.HelpBox(error, MessageType.Error);

            foreach (var warning in fit.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            if (!fit.IsValid) return;

            int before = fit.SourceReceiverCount;
            int after = fit.EmittedReceiverCount;

            EditorGUILayout.LabelField("At build", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Contact receivers",
                    before + "  ->  " + after + (before > after ? $"   ({before - after} fewer)" : ""));
                EditorGUILayout.LabelField("Region size",
                    $"{fit.RegionExtents.x:F3} x {fit.RegionExtents.y:F3} x {fit.RegionExtents.z:F3}");
                EditorGUILayout.LabelField("Encoder box",
                    $"{fit.BoxExtents.x:F3} x {fit.BoxExtents.y:F3} x {fit.BoxExtents.z:F3}");
                EditorGUILayout.LabelField("Local only", fit.LocalOnly ? "yes (excluded from performance rank)" : "no");
                EditorGUILayout.LabelField("Collision tags", string.Join(", ", fit.CollisionTags));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Accuracy", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Resolves colliders up to", $"{fit.MaxResolvableRadius:F3} m radius");

                // Only useful if it beats the spacing of the points it has to tell apart.
                float spacing = NearestNeighbourSpacing(fit);
                if (spacing > 0f)
                    EditorGUILayout.LabelField("Closest two points", $"{spacing:F3} m apart");
            }

            if (fit.MaxResolvableRadius < 0.05f)
            {
                EditorGUILayout.HelpBox(
                    $"Padding of {group.paddingMetres:F3} m is below the radius of VRChat's stock hand colliders. " +
                    "Contacts near the edges of this region will saturate and decode inaccurately. " +
                    "Raise padding to at least 0.05, ideally 0.10.",
                    MessageType.Warning);
            }

            DrawValidation(fit);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export manifest..."))
                    ExportManifest(group, fit);

                if (GUILayout.Button("Select source receivers"))
                    Selection.objects = fit.Points
                        .Where(p => p.Receiver != null)
                        .Select(p => (Object)p.Receiver.gameObject)
                        .Distinct()
                        .ToArray();
            }
        }

        /// <summary>
        /// Simulates a touch on every authored point and reports whether it comes back as that
        /// point. Run on demand rather than continuously: it is a full sweep, and the answer only
        /// changes when the receivers or the padding do.
        /// </summary>
        void DrawValidation(FittedRegion fit)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Round-trip check", EditorStyles.boldLabel);
                if (GUILayout.Button("Run", GUILayout.Width(60)))
                    _validation = ContactCompressorValidator.Validate(fit);
            }

            if (!_validation.Ran)
            {
                EditorGUILayout.HelpBox(
                    "Simulates a touch on each of your points using VRChat's own proximity maths and checks it " +
                    "decodes back to the same point. Worth running before you upload.",
                    MessageType.None);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Simulated collider", $"{_validation.SenderRadius:F3} m radius");
                EditorGUILayout.LabelField("Points checked", _validation.PointsChecked.ToString());
                EditorGUILayout.LabelField("Position error",
                    $"mean {_validation.MeanErrorMetres * 1000f:F2} mm, worst {_validation.WorstErrorMetres * 1000f:F2} mm");
            }

            if (_validation.IsClean)
            {
                EditorGUILayout.HelpBox(
                    $"All {_validation.PointsChecked} points resolve back to themselves.", MessageType.Info);
                return;
            }

            if (_validation.Misattributed > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{_validation.Misattributed} of {_validation.PointsChecked} points decode closer to a " +
                    $"different point than the one touched (worst: {_validation.WorstPointId}). Those points are " +
                    "too close together to tell apart at this collider size. Either accept that neighbours will " +
                    "blend, or split them into separate regions.",
                    MessageType.Warning);
            }

            if (_validation.Saturated > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{_validation.Saturated} points saturate against a box face. Raise padding above the collider " +
                    "radius you expect.",
                    MessageType.Warning);
            }
        }

        static float NearestNeighbourSpacing(FittedRegion fit)
        {
            var points = fit.Points.Select(p => p.Local).ToList();
            if (points.Count < 2) return 0f;

            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    float d = Vector3.Distance(points[i], points[j]);
                    if (d > 1e-5f && d < best) best = d;   // ignore self/others pairs at the same spot
                }

            return best == float.MaxValue ? 0f : best;
        }

        static void ExportManifest(ContactCompressorGroup group, FittedRegion fit)
        {
            // Every group on the avatar goes into one manifest; a consumer wants the whole avatar,
            // not one region of it.
            var root = group.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var fits = root != null
                ? root.GetComponentsInChildren<ContactCompressorGroup>(true)
                      .Select(ContactRegionFitter.Fit)
                      .Where(f => f.IsValid)
                      .ToList()
                : new System.Collections.Generic.List<FittedRegion> { fit };

            string path = EditorUtility.SaveFilePanel(
                "Export contact compressor manifest",
                "",
                ContactCompressorManifestBuilder.DefaultFileName,
                "json");

            if (string.IsNullOrEmpty(path)) return;

            var manifest = ContactCompressorManifestBuilder.Build(fits);
            File.WriteAllText(path, ContactCompressorManifestBuilder.ToJson(manifest));

            Debug.Log($"[Contact Compressor] Wrote {manifest.regions.Count} region(s) to {path}");
        }

        void OnSceneGUI()
        {
            var group = (ContactCompressorGroup)target;
            var fit = _fit;
            if (fit == null || !fit.IsValid) return;

            Transform frame = group.ResolvedFrame;
            if (frame == null) return;

            using (new Handles.DrawingScope(frame.localToWorldMatrix))
            {
                Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
                Handles.DrawWireCube(fit.CentreLocal, fit.RegionExtents);

                // The padded box is what actually gets emitted, so show the difference.
                Handles.color = new Color(0.3f, 0.8f, 1f, 0.25f);
                Handles.DrawWireCube(fit.CentreLocal, fit.BoxExtents);

                Handles.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                foreach (var point in fit.Points)
                    Handles.SphereHandleCap(0, point.Local, Quaternion.identity, 0.008f, EventType.Repaint);
            }
        }
    }
}
