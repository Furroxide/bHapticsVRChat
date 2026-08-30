#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System.Collections.Generic;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The rig picker's art and geometry.
    ///
    /// This used to also be the package's IMGUI drawing layer - buttons, toggles, sections,
    /// separators, a hardcoded dark separator colour that was invisible on the light skin. All of
    /// that went when the inspector moved to UI Toolkit, where the stylesheet does it and follows
    /// the user's theme. What is left is the part that is genuinely data: which sprite belongs to
    /// which device, and where on the figure it goes.
    /// </summary>
    public static class bGUI
    {
        /// <summary>
        /// The rig art's own pixel size. Every placement below is in this space, recovered from a
        /// screenshot of the original IMGUI picker by template-matching each sprite against it, so
        /// the figure is laid out exactly as it always was rather than re-eyeballed.
        /// </summary>
        public static readonly Vector2 RigSize = new Vector2(200f, 443f);

        public static Sprite Rig;

        public static Dictionary<bDeviceType, bGUITemplateElements> Elements =
            new Dictionary<bDeviceType, bGUITemplateElements>();

        static bGUI()
        {
            Rig = LoadSprite("rig.png");

            Elements[bDeviceType.HEAD] = Load("tactal", new Rect(66f, 8f, 67f, 27f));
            Elements[bDeviceType.VEST] = Load("tactsuit", new Rect(50f, 59f, 100f, 155f));

            Elements[bDeviceType.ARM_LEFT] = Load("tactosyA_left", new Rect(140f, 151f, 40f, 50f));
            Elements[bDeviceType.ARM_RIGHT] = Load("tactosyA_right", new Rect(20f, 151f, 40f, 50f));

            Elements[bDeviceType.HAND_LEFT] = Load("tactosyH_left", new Rect(158f, 196f, 37f, 50f));
            Elements[bDeviceType.HAND_RIGHT] = Load("tactosyH_right", new Rect(5f, 196f, 37f, 50f));

            // Gloves

            Elements[bDeviceType.FOOT_LEFT] = Load("tactosyF_left", new Rect(132f, 394f, 39f, 47f));
            Elements[bDeviceType.FOOT_RIGHT] = Load("tactosyF_right", new Rect(27f, 394f, 39f, 47f));
        }

        private static bGUITemplateElements Load(string baseName, Rect placement) => new bGUITemplateElements
        {
            NotSelected = LoadSprite(baseName + ".png"),
            Selected = LoadSprite(baseName + "_selected.png"),
            Prefab = LoadSprite(baseName + "_prefab.png"),
            Placement = placement,
        };

        private static Sprite LoadSprite(string fileName)
            => bPackageAssetResolver.LoadAsset<Sprite>($"Textures/UI/{fileName}");
    }
}
#endif
