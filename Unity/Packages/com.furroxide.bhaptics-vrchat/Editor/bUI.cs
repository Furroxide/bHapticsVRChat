#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The small amount of plumbing every bHapticsOSC UI Toolkit surface needs: the shared
    /// stylesheet, the skin class the stylesheet keys its accent colours off, and the built-in
    /// editor icons that carry step state.
    /// </summary>
    internal static class bUI
    {
        private const string StyleSheetPath = "UI/bTheme.uss";

        private static readonly Dictionary<bStepState, Texture2D> IconCache =
            new Dictionary<bStepState, Texture2D>();

        /// <summary>
        /// Attaches the stylesheet and the skin class. Called once per root element.
        ///
        /// The skin class exists because USS in the editor has no media query for light/dark and
        /// the ok/attention/blocked hues have no --unity-colors-* counterpart. Everything that
        /// does have a palette entry uses it directly in the stylesheet.
        /// </summary>
        internal static void ApplyTheme(VisualElement root)
        {
            if (root == null)
                return;

            root.AddToClassList("b-root");

            // Both come off before either goes on, because this is called more than once on the
            // same element: an EditorWindow keeps one rootVisualElement for its whole life, and
            // CreateGUI runs again after every domain reload, where root.Clear() empties the
            // children but leaves the element's own classes alone. Change the editor skin with the
            // window open and the second pass would otherwise add the new theme alongside the old
            // one. The two rules carry equal specificity, so the winner would just be whichever
            // USS declared last - .b-theme--light - and the window would come back in the light
            // palette on a dark skin.
            root.RemoveFromClassList("b-theme--dark");
            root.RemoveFromClassList("b-theme--light");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "b-theme--dark" : "b-theme--light");

            StyleSheet sheet = bPackageAssetResolver.LoadAsset<StyleSheet>(StyleSheetPath);
            if (sheet != null && !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);
        }

        /// <summary>The USS modifier suffix for a state, e.g. "ok" for <c>.b-step--ok</c>.</summary>
        internal static string StateClass(bStepState state)
        {
            switch (state)
            {
                case bStepState.Ok: return "ok";
                case bStepState.Attention: return "attention";
                case bStepState.Blocked: return "blocked";
                default: return "unknown";
            }
        }

        /// <summary>
        /// The built-in editor icon for a state, or null when it cannot be resolved - callers
        /// fall back to a plain coloured dot rather than drawing nothing.
        /// </summary>
        internal static Texture2D StateIcon(bStepState state)
        {
            if (IconCache.TryGetValue(state, out Texture2D cached))
                return cached;

            string name;
            switch (state)
            {
                case bStepState.Ok: name = "TestPassed"; break;
                case bStepState.Attention: name = "console.warnicon.sml"; break;
                case bStepState.Blocked: name = "console.erroricon.sml"; break;
                default: name = "console.infoicon.sml"; break;
            }

            Texture2D icon = null;
            try
            {
                icon = EditorGUIUtility.IconContent(name)?.image as Texture2D;
            }
            catch
            {
                // Icon names are not part of Unity's public contract; a miss is not worth a log.
            }

            IconCache[state] = icon;
            return icon;
        }

        /// <summary>
        /// Builds the leading state marker: the editor icon when there is one, otherwise a dot
        /// coloured from the same USS variable.
        /// </summary>
        internal static VisualElement CreateStateMarker(bStepState state, string blockClass)
        {
            var marker = new VisualElement();
            marker.AddToClassList(blockClass);
            marker.pickingMode = PickingMode.Ignore;

            Texture2D icon = StateIcon(state);
            if (icon != null)
            {
                marker.style.backgroundImage = icon;
                return marker;
            }

            marker.AddToClassList(blockClass + "--dot");
            marker.AddToClassList(blockClass + "--" + StateClass(state));
            return marker;
        }

        /// <summary>The IMGUI equivalent, for the few places still drawing with EditorGUILayout.</summary>
        internal static MessageType MessageTypeFor(bStepState state)
        {
            switch (state)
            {
                case bStepState.Blocked: return MessageType.Error;
                case bStepState.Attention: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }

        /// <summary>
        /// Session-scoped disclosure state. Foldouts the user opens stay open while the editor is
        /// running and reset on restart, which is the right lifetime for "I am debugging this now".
        /// </summary>
        internal static bool GetFlag(string key, bool fallback)
            => SessionState.GetBool(bCompanionRequirements.PackageId + "." + key, fallback);

        internal static void SetFlag(string key, bool value)
            => SessionState.SetBool(bCompanionRequirements.PackageId + "." + key, value);

        /// <summary>Replaces every USS class matching a prefix with a single new one.</summary>
        internal static void SetStateClass(VisualElement element, string block, bStepState state)
        {
            if (element == null)
                return;

            element.RemoveFromClassList(block + "--ok");
            element.RemoveFromClassList(block + "--unknown");
            element.RemoveFromClassList(block + "--attention");
            element.RemoveFromClassList(block + "--blocked");
            element.AddToClassList(block + "--" + StateClass(state));
        }
    }
}
#endif
