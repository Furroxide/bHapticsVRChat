#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The body figure, and the eight device buttons sitting on it.
    ///
    /// The IMGUI version stacked the sprites in layout order and pulled them back into place with
    /// negative spacing - Space(-(Rig.rect.height - 3)), Space(142), and a comment reading
    /// "Rendering After Arms because Layering Derp". Every offset was tuned against the sprites'
    /// pixel sizes, so the figure could only ever be drawn at exactly one scale. Here each device
    /// is a percentage rect inside the figure, which is the thing the art already meant, and the
    /// whole picker scales with the inspector.
    /// </summary>
    internal sealed class bRigPickerElement : VisualElement
    {
        /// <summary>Never upscaled past the art's own resolution - it would only look soft.</summary>
        private const float MaxWidth = 200f;

        private readonly VisualElement figure;

        internal bRigPickerElement(bHapticsOSCIntegration editorComp, Action onSelectionChanged)
        {
            figure = new VisualElement();
            figure.AddToClassList("b-rig");
            if (bGUI.Rig != null)
                figure.style.backgroundImage = new StyleBackground(bGUI.Rig);

            foreach (var pair in bGUI.Elements)
                figure.Add(BuildDevice(editorComp, pair.Key, pair.Value, onSelectionChanged));

            Add(figure);

            // The figure has no intrinsic size once it is a background image, so it is measured
            // against whatever width the inspector gives us and keeps the art's proportions.
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float available = evt.newRect.width;
            if (available <= 0f || float.IsNaN(available))
                return;

            float width = Mathf.Min(MaxWidth, available);
            figure.style.width = width;
            figure.style.height = width * (bGUI.RigSize.y / bGUI.RigSize.x);
        }

        private static VisualElement BuildDevice(
            bHapticsOSCIntegration editorComp,
            bDeviceType device,
            bGUITemplateElements art,
            Action onSelectionChanged)
        {
            var button = new Button { name = "rig-" + device };
            button.AddToClassList("b-rig__device");

            Rect placement = art.Placement;
            button.style.left = Length.Percent(placement.x / bGUI.RigSize.x * 100f);
            button.style.top = Length.Percent(placement.y / bGUI.RigSize.y * 100f);
            button.style.width = Length.Percent(placement.width / bGUI.RigSize.x * 100f);
            button.style.height = Length.Percent(placement.height / bGUI.RigSize.y * 100f);

            var badge = new VisualElement();
            badge.AddToClassList("b-rig__badge");
            badge.pickingMode = PickingMode.Ignore;
            if (art.Prefab != null)
                badge.style.backgroundImage = new StyleBackground(art.Prefab);
            button.Add(badge);

            button.clicked += () =>
            {
                if (editorComp.CurrentDevice == device)
                    return;

                Undo.RecordObject(editorComp, $"[{bHapticsOSCIntegration.SystemName}] Selected Device");
                editorComp.CurrentDevice = device;
                onSelectionChanged?.Invoke();
            };

            Refresh(button, badge, editorComp, device, art);
            return button;
        }

        /// <summary>Repaints selection and the "already added" badge without rebuilding the tree.</summary>
        internal void Refresh(bHapticsOSCIntegration editorComp)
        {
            foreach (var pair in bGUI.Elements)
            {
                var button = figure.Q<Button>("rig-" + pair.Key);
                if (button == null)
                    continue;

                Refresh(button, button.Q(className: "b-rig__badge"), editorComp, pair.Key, pair.Value);
            }
        }

        private static void Refresh(
            Button button,
            VisualElement badge,
            bHapticsOSCIntegration editorComp,
            bDeviceType device,
            bGUITemplateElements art)
        {
            bool isSelected = editorComp.CurrentDevice == device;
            Sprite sprite = isSelected ? art.Selected : art.NotSelected;
            if (sprite != null)
                button.style.backgroundImage = new StyleBackground(sprite);

            bDeviceTemplate template = bDevice.AllTemplates[device];
            bool added = editorComp.AllUserSettings != null
                         && editorComp.AllUserSettings.TryGetValue(template, out bUserSettings settings)
                         && settings.CurrentPrefab != null;

            if (badge != null)
                badge.style.display = added && art.Prefab != null ? DisplayStyle.Flex : DisplayStyle.None;

            // The sprite lighting up was the only cue that anything had happened here. Saying it
            // in words costs nothing and makes the picker readable without trial and error.
            button.tooltip = added
                ? template.Name + " - added"
                : template.Name + " - not added yet";
        }
    }
}
#endif
