using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Furroxide.ContactCompressor.Editor
{
    /// <summary>Builds the opposed box receivers for a fitted region.</summary>
    public static class ContactCompressorEmitter
    {
        public const string EncoderSuffix = " Encoder";

        /// <summary>
        /// Rotation whose local +Z points along region axis <paramref name="axis"/> in the given
        /// direction. Each receiver of an opposed pair measures from the face its +Z points at, so
        /// the pair is the same box rotated 180 degrees.
        /// </summary>
        public static Quaternion RotationFor(int axis, bool positive)
        {
            Vector3 forward =
                axis == 0 ? (positive ? Vector3.right : Vector3.left) :
                axis == 1 ? (positive ? Vector3.up : Vector3.down) :
                            (positive ? Vector3.forward : Vector3.back);

            // Any up vector works so long as it is not parallel to forward.
            Vector3 up = axis == 1 ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// Box size in the receiver's own local axes. The receiver is rotated, so its local X/Y/Z
        /// map onto different region axes and the extents have to follow. Derived from the rotation
        /// rather than hard-coded per case, so it stays correct if the rotations ever change.
        /// </summary>
        public static Vector3 LocalSizeFor(Vector3 regionBoxExtents, Quaternion rotation)
        {
            var size = Vector3.zero;
            for (int localAxis = 0; localAxis < 3; localAxis++)
            {
                Vector3 unit = localAxis == 0 ? Vector3.right : localAxis == 1 ? Vector3.up : Vector3.forward;
                Vector3 inRegion = rotation * unit;

                int best = 0;
                float bestMagnitude = -1f;
                for (int regionAxis = 0; regionAxis < 3; regionAxis++)
                {
                    float magnitude = Mathf.Abs(inRegion[regionAxis]);
                    if (magnitude > bestMagnitude) { bestMagnitude = magnitude; best = regionAxis; }
                }

                size[localAxis] = regionBoxExtents[best];
            }
            return size;
        }

        /// <summary>
        /// Creates the encoder object and its receivers under the group's frame. Returns the new
        /// GameObject, or null when the fit is not usable.
        /// </summary>
        public static GameObject Emit(FittedRegion fit)
        {
            if (fit == null || !fit.IsValid || fit.Group == null) return null;

            var group = fit.Group;
            Transform frame = group.ResolvedFrame;

            var host = new GameObject(group.regionId + EncoderSuffix);
            host.transform.SetParent(frame, false);
            host.transform.localPosition = fit.CentreLocal;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            for (int axis = 0; axis < 3; axis++)
            {
                var flag = ContactEncoderSolver.AxisFlag(axis);
                if ((fit.Axes & flag) == 0) continue;

                foreach (bool positive in new[] { true, false })
                {
                    var rotation = RotationFor(axis, positive);
                    var receiver = host.AddComponent<VRCContactReceiver>();

                    receiver.rootTransform = host.transform;
                    receiver.shapeType = ContactBase.ShapeType.Box;
                    receiver.size = LocalSizeFor(fit.BoxExtents, rotation);
                    receiver.position = Vector3.zero;
                    receiver.rotation = rotation;

                    receiver.useFaceProximity = true;
                    receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
                    receiver.parameter = ContactParameterNames.Parameter(
                        group.parameterPrefix, group.regionId, axis, positive);

                    receiver.allowSelf = fit.AllowSelf;
                    receiver.allowOthers = fit.AllowOthers;
                    receiver.localOnly = fit.LocalOnly;
                    receiver.collisionTags = new List<string>(fit.CollisionTags);
                }
            }

            return host;
        }

        /// <summary>Every float parameter a fitted region will drive.</summary>
        public static IEnumerable<string> ParametersFor(FittedRegion fit)
        {
            if (fit == null || fit.Group == null) yield break;

            for (int axis = 0; axis < 3; axis++)
            {
                var flag = ContactEncoderSolver.AxisFlag(axis);
                if ((fit.Axes & flag) == 0) continue;

                yield return ContactParameterNames.Parameter(fit.Group.parameterPrefix, fit.Group.regionId, axis, true);
                yield return ContactParameterNames.Parameter(fit.Group.parameterPrefix, fit.Group.regionId, axis, false);
            }
        }
    }
}
