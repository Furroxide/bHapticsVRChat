#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// Everything the avatar side does, and nothing about how it is presented.
    ///
    /// Lifted out of the inspector so that file can be about drawing. Both routes into the setup -
    /// the inspector's CREATE button and the one-press action in bAvatarSetup - run this identical
    /// sequence; a second copy would drift, and the order is not obvious enough to rediscover.
    /// </summary>
    internal static class bSetupPipeline
    {
        /// <summary>
        /// The whole avatar side, in the order it has to happen. The caller owns the undo group
        /// and the failure handling; this either completes or throws.
        /// </summary>
        internal static void Run(bHapticsOSCIntegration editorComp)
        {
            try
            {
                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Preparing bHaptics objects...", 0.1f);
                editorComp.GetOrCreateVrcFuryRoot(true);
                foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
                    settings.MoveToStagingRoot(editorComp, true);

                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Applying contact tags...", 0.25f);
                bContacts.ApplyNewTags(editorComp);

                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Preparing punch receivers...", 0.35f);
                bPunch.ApplyReceivers(editorComp);

                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Applying contact compression...", 0.40f);
                ApplyContactCompression(editorComp);

                if (bConstraints.ShouldApply(editorComp, bDeviceType.HAND_LEFT, out bUserSettings leftHandSettings)
                    || bConstraints.ShouldApply(editorComp, bDeviceType.HAND_RIGHT, out bUserSettings rightHandSettings))
                {
                    EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Applying ParentConstraints...", 0.45f);
                    bConstraints.Apply(editorComp);
                }

                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Generating VRCFury assets...", 0.65f);
                bGeneratedAnimatorAssets generatedAssets = bAnimator.CreateGeneratedAssets(editorComp);

                EditorUtility.DisplayProgressBar(bHapticsOSCIntegration.SystemName, "Creating VRCFury components...", 0.85f);
                bVrcFury.Apply(editorComp, generatedAssets);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log("VRCFury setup complete. To remove its generated assets, delete the bHapticsOSC VRCFury object, save, and close the scene or prefab.");

            // Destroyed through Undo so the whole setup collapses into one entry: a single
            // Ctrl+Z brings the user back to the device picker with their choices intact.
            Undo.DestroyObjectImmediate(editorComp);

            bCompanionSetupWindow.ShowAvatarSetupComplete();
        }

        /// <summary>
        /// Builds the per-device settings the inspector normally creates on its first draw, so the
        /// one-click action can run without the inspector ever having been opened.
        /// </summary>
        internal static void EnsureUserSettings(bHapticsOSCIntegration editorComp)
        {
            if (editorComp.AllUserSettings != null)
                return;

            editorComp.AllUserSettings = new Dictionary<bDeviceTemplate, bUserSettings>();
            foreach (bDeviceTemplate template in bDevice.AllTemplates.Values)
            {
                if (!template.HasBone)
                    continue;

                bUserSettings newSettings = ScriptableObject.CreateInstance<bUserSettings>();
                newSettings.Bone = template.Bone;

                var getNewPrefab = new Func<bUserSettings, GameObject>(x => x.ShowMesh
                    ? (x.IsMobile ? template.PrefabMeshMobile : template.PrefabMesh)
                    : (x.IsMobile ? template.PrefabMobile : template.Prefab));

                newSettings.OnShowMeshChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
                newSettings.OnIsMobileChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
                editorComp.AllUserSettings[template] = newSettings;
            }
        }

        /// <summary>Moves the component to the avatar root, keeping it undoable.</summary>
        internal static void MoveToAvatarRoot(bHapticsOSCIntegration comp, GameObject root)
        {
            if (root == null)
                return;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move bHapticsOSC Integration to the avatar root");

            Undo.AddComponent<bHapticsOSCIntegration>(root);
            Undo.DestroyObjectImmediate(comp);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root;
        }

        private static void ApplyContactCompression(bHapticsOSCIntegration editorComp)
        {
#if bHapticsOSC_HasContactCompressor
            if (!editorComp.ConsolidateContacts)
            {
                // Only the groups this added. RemoveGroups swept the whole device subtree and
                // took a user's own groups with it, on the default path.
                bCompressor.RemoveGeneratedGroups(editorComp);
                return;
            }

            int applied = bCompressor.ApplyGroups(editorComp);
            if (applied <= 0)
            {
                Debug.LogWarning(
                    $"[{bHapticsOSCIntegration.SystemName}] Contact consolidation is enabled, but none of the "
                    + "selected devices have receivers it can compress. The avatar is unchanged.");
                return;
            }

            // Emitted here rather than left to the user: the layout is fitted to this avatar,
            // so a manifest from anywhere else describes the wrong geometry and drives the
            // wrong motors.
            if (!string.IsNullOrEmpty(bCompressor.ExportManifest(editorComp)))
                return;

            // Compressed receivers with no manifest is the worst of both worlds: the per-motor
            // receivers are gone at build time and the companion app has nothing to decode the
            // replacements with. Take back only the groups this added - a user's own groups
            // elsewhere on the avatar are not ours to delete - and then fail loudly rather than
            // letting the setup report success.
            bCompressor.RemoveGeneratedGroups(editorComp);
            throw new InvalidOperationException(
                $"Contact compression was applied to {applied} device(s) but no manifest could be produced, so it "
                + "has been taken back off. See the console for the region that would not fit.\n"
                + "Setup stopped partway: use Undo to return the avatar to its previous state before trying again.");
#endif
        }
    }
}
#endif
