using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>
    /// Replaces each <see cref="ContactCompressorGroup"/>'s per-point receivers with the six box
    /// receivers that encode the same information positionally, at build time.
    ///
    /// The author never sees this happen. Their scene keeps one receiver per point; only the
    /// uploaded avatar is different.
    /// </summary>
    public class ContactCompressorHook : IVRCSDKPreprocessAvatarCallback
    {
        /// <summary>
        /// Where this has to sit is tightly constrained from both sides:
        ///
        /// - It must run <b>after</b> anything that generates receivers, so it sees the finished
        ///   avatar. VRCFury's main build is at -10000.
        /// - It must run <b>before</b> -1024, where the VRCSDK's <c>RemoveAvatarEditorOnly</c>
        ///   strips <c>IEditorOnly</c> components. <see cref="ContactCompressorGroup"/> is one of
        ///   those, so running any later would find nothing to do and silently ship the avatar with
        ///   its full receiver count.
        ///
        /// (VRCFury defers component stripping to the end of the build, which would give more room,
        /// but this package does not depend on VRCFury and cannot assume that.)
        /// </summary>
        public const int CallbackOrder = -1100;

        public int callbackOrder => CallbackOrder;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return true;

            var groups = avatarGameObject.GetComponentsInChildren<ContactCompressorGroup>(true);
            if (groups == null || groups.Length == 0) return true;

            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogWarning("[Contact Compressor] No VRCAvatarDescriptor; leaving contacts alone.");
                return true;
            }

            var fits = new List<FittedRegion>();
            var usedRegionIds = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var group in groups)
            {
                if (group == null) continue;

                var fit = ContactRegionFitter.Fit(group);

                if (!string.IsNullOrWhiteSpace(group.regionId) && !usedRegionIds.Add(group.regionId))
                    fit.Errors.Add($"Region id '{group.regionId}' is used by more than one group on this avatar.");

                foreach (var warning in fit.Warnings)
                    Debug.LogWarning($"[Contact Compressor] {group.regionId}: {warning}", group);

                if (!fit.IsValid)
                {
                    // Failing the build is the right call: silently uploading with the original
                    // receiver count would look like success while blowing the performance rank and
                    // emitting parameters no consumer is listening for.
                    foreach (var error in fit.Errors)
                        Debug.LogError($"[Contact Compressor] {group.regionId}: {error}", group);
                    return false;
                }

                fits.Add(fit);
            }

            if (fits.Count == 0) return true;

            var parameters = new List<string>();
            int removed = 0, emitted = 0;

            foreach (var fit in fits)
            {
                var host = ContactCompressorEmitter.Emit(fit);
                if (host == null)
                {
                    Debug.LogError($"[Contact Compressor] Failed to emit receivers for '{fit.Group.regionId}'.", fit.Group);
                    return false;
                }

                emitted += fit.EmittedReceiverCount;
                parameters.AddRange(ContactCompressorEmitter.ParametersFor(fit));

                if (!fit.Group.keepSourceReceivers)
                    removed += RemoveSourceReceivers(fit);
            }

            if (!RegisterParameters(descriptor, parameters))
                return false;

            Debug.Log(
                $"[Contact Compressor] {fits.Count} region(s): replaced {removed} contact receivers with {emitted}. " +
                $"Net change {emitted - removed:+#;-#;0}.");

            return true;
        }

        static int RemoveSourceReceivers(FittedRegion fit)
        {
            int removed = 0;
            foreach (var point in fit.Points)
            {
                if (point.Receiver == null) continue;
                Object.DestroyImmediate(point.Receiver);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Declares the emitted floats on the avatar so VRChat drives them and sends them over OSC.
        ///
        /// Both halves are needed: the animator parameter is what the receiver actually writes to,
        /// and the expression parameter entry is what makes VRChat include it in the avatar's OSC
        /// config. They are registered unsynced, so they cost none of the 256 synced bits.
        /// </summary>
        static bool RegisterParameters(VRCAvatarDescriptor descriptor, List<string> parameters)
        {
            var wanted = parameters.Distinct().ToList();
            if (wanted.Count == 0) return true;

            return RegisterExpressionParameters(descriptor, wanted)
                && RegisterAnimatorParameters(descriptor, wanted);
        }

        static bool RegisterExpressionParameters(VRCAvatarDescriptor descriptor, List<string> wanted)
        {
            var source = descriptor.expressionParameters;

            // Clone before touching it: the descriptor points at a shared project asset, and the
            // build must not write into the user's project.
            var clone = source != null
                ? Object.Instantiate(source)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();

            clone.name = (source != null ? source.name : "ExpressionParameters") + " (Contact Compressor)";

            var existing = clone.parameters != null
                ? clone.parameters.ToList()
                : new List<VRCExpressionParameters.Parameter>();

            foreach (var name in wanted)
            {
                var already = existing.FirstOrDefault(p => p != null && p.name == name);
                if (already != null)
                {
                    if (already.valueType != VRCExpressionParameters.ValueType.Float)
                    {
                        Debug.LogError(
                            $"[Contact Compressor] Expression parameter '{name}' already exists as " +
                            $"{already.valueType}; it must be a Float.");
                        return false;
                    }
                    already.networkSynced = false;
                    continue;
                }

                existing.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false
                });
            }

            clone.parameters = existing.ToArray();
            descriptor.expressionParameters = clone;
            return true;
        }

        static bool RegisterAnimatorParameters(VRCAvatarDescriptor descriptor, List<string> wanted)
        {
            var layers = descriptor.baseAnimationLayers;
            if (layers == null)
            {
                Debug.LogError("[Contact Compressor] Avatar has no animation layers to register parameters on.");
                return false;
            }

            int fxIndex = -1;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    fxIndex = i;

            if (fxIndex < 0)
            {
                Debug.LogError("[Contact Compressor] Avatar has no FX playable layer to register parameters on.");
                return false;
            }

            var controller = layers[fxIndex].animatorController as AnimatorController;
            if (controller == null)
            {
                Debug.LogError(
                    "[Contact Compressor] The FX layer has no animator controller. Assign one (or let a tool such as " +
                    "VRCFury generate one) before building.");
                return false;
            }

            // Same reasoning as the expression parameters: clone before mutating a project asset.
            var clone = Object.Instantiate(controller);
            clone.name = controller.name + " (Contact Compressor)";

            var present = new HashSet<string>(clone.parameters.Select(p => p.name), System.StringComparer.Ordinal);

            foreach (var name in wanted)
            {
                if (present.Contains(name))
                {
                    var existing = clone.parameters.First(p => p.name == name);
                    if (existing.type != AnimatorControllerParameterType.Float)
                    {
                        Debug.LogError(
                            $"[Contact Compressor] Animator parameter '{name}' already exists as {existing.type}; " +
                            "it must be a Float.");
                        return false;
                    }
                    continue;
                }

                clone.AddParameter(new AnimatorControllerParameter
                {
                    name = name,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
                present.Add(name);
            }

            var layer = layers[fxIndex];
            layer.animatorController = clone;
            layer.isDefault = false;
            layers[fxIndex] = layer;
            descriptor.baseAnimationLayers = layers;

            return true;
        }
    }
}
