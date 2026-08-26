using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace Furroxide.ContactCompressor
{
    /// <summary>How the emitted receivers should set <c>Local Only</c>.</summary>
    public enum LocalOnlyMode
    {
        /// <summary>Match whatever the receivers being replaced used. Preserves existing behaviour exactly.</summary>
        PreserveSource = 0,

        /// <summary>
        /// Force on. Contacts marked Local Only are skipped entirely by the SDK's performance
        /// scanner, so the avatar's Contacts metric goes to zero. The cost is that remote clients
        /// no longer evaluate the receivers, so anything driven off them is invisible to others.
        /// </summary>
        Always = 1,

        /// <summary>Force off. Remote clients still evaluate the receivers; they count toward the performance rank.</summary>
        Never = 2
    }

    /// <summary>
    /// Marks a set of contact receivers to be collapsed, at build time, into a handful of box
    /// receivers that encode the contact position instead.
    ///
    /// Put this on a GameObject whose children carry the receivers you already author - one per
    /// point, exactly as before. Nothing about the authoring workflow changes: the receivers stay
    /// individually inspectable, they still work in play mode, and any tooling that reads them
    /// still sees them. Only the uploaded avatar is different.
    ///
    /// The component is <see cref="IEditorOnly"/> and is stripped from the build.
    /// </summary>
    [AddComponentMenu("Furroxide/Contact Compressor Group")]
    [DisallowMultipleComponent]
    [HelpURL("https://github.com/furroxide/bHapticsVRChat/blob/main/Unity/Packages/com.furroxide.contact-compressor/README.md")]
    public class ContactCompressorGroup : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Identifies this region in the emitted parameter names and in the manifest. Must be unique on the avatar, and must not contain '/'.")]
        public string regionId = "Region";

        [Tooltip("Parameter namespace for the emitted floats. The full name is <prefix>/<regionId>/<Axis><Sign>.")]
        public string parameterPrefix = ContactParameterNames.DefaultPrefix;

        [Tooltip("Which axes to encode. Each axis costs two receivers. Drop an axis the points do not actually vary along.")]
        public EncoderAxes axes = EncoderAxes.XYZ;

        [Tooltip("How far to inflate the fitted box beyond the points, per side, in metres.\n\n" +
                 "Proximity saturates once a sender reaches a box face, and the maths behind this only works below " +
                 "saturation. The rule is exactly 'padding must be at least the radius of the largest collider you " +
                 "expect to be touched with' - it does not depend on how big the region is, which is why this is in " +
                 "metres rather than a percentage. 0.10 covers VRChat's stock hand and foot colliders.")]
        [Range(0.01f, 0.5f)]
        public float paddingMetres = ContactEncoderMath.DefaultPaddingMetres;

        [Tooltip("Root to search for receivers. Defaults to this GameObject.")]
        public Transform sourceRoot;

        [Tooltip("If set, only receivers whose parameter name matches this regular expression are collapsed. Leave empty to take every receiver under the root.")]
        public string sourceParameterPattern = "";

        [Tooltip("Optional regex used to turn a receiver's parameter name into a logical point id for the manifest.\n\n" +
                 "Use this when several receivers describe the same physical point - a self/others pair at one spot is " +
                 "the usual case. Without it each receiver becomes its own manifest point, so a consumer interpolating " +
                 "over the four nearest points would spend two of them on the same place.\n\n" +
                 "Example: '^bOSC/v2/(.+)/(self|others)$' with replacement '$1' collapses .../7/self and .../7/others " +
                 "into one point named VestFront/7.")]
        public string pointIdPattern = "";

        [Tooltip("Replacement for Point Id Pattern. Supports $1, $2 group references.")]
        public string pointIdReplacement = "";

        [Tooltip("Local Only setting for the emitted receivers.")]
        public LocalOnlyMode localOnly = LocalOnlyMode.PreserveSource;

        [Tooltip("Collision tags for the emitted receivers. Leave empty to use the union of the tags on the receivers being replaced.")]
        public List<string> collisionTagsOverride = new List<string>();

        [Tooltip("Leave the original receivers in place instead of deleting them. Useful for A/B testing both paths on one avatar; costs the full receiver count.")]
        public bool keepSourceReceivers;

        [Tooltip("Orientation the region box is fitted in. Defaults to this GameObject's rotation, which is normally what you want since it follows the bone.")]
        public Transform frameOverride;

        public Transform ResolvedSourceRoot => sourceRoot != null ? sourceRoot : transform;
        public Transform ResolvedFrame => frameOverride != null ? frameOverride : transform;

        /// <summary>How many receivers this group will emit.</summary>
        public int EmittedReceiverCount => ContactEncoderSolver.ReceiverCount(axes);

        public string AxesString
        {
            get
            {
                string s = "";
                if ((axes & EncoderAxes.X) != 0) s += "X";
                if ((axes & EncoderAxes.Y) != 0) s += "Y";
                if ((axes & EncoderAxes.Z) != 0) s += "Z";
                return s;
            }
        }
    }
}
