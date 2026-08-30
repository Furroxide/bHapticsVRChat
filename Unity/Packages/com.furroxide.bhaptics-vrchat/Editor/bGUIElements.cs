#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The art for one device on the rig picker, and where it sits.
    ///
    /// <see cref="Placement"/> replaced a per-device GUIStyle whose contentOffset was half of the
    /// positioning; the other half was a run of negative GUILayout.Space calls in the inspector
    /// that had to be read in drawing order to work out where anything ended up. One rect in the
    /// art's own coordinate space says the same thing and can be checked against the picture.
    /// </summary>
    public class bGUITemplateElements
    {
        public Sprite NotSelected;
        public Sprite Selected;
        public Sprite Prefab;

        /// <summary>Position and size within the rig art, in the art's own pixels.</summary>
        public Rect Placement;
    }
}
#endif
