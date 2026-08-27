#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace bHapticsOSC.VRChat
{
    [CustomEditor(typeof(bHapticsOSCIntegration))]
    public class bEditorGUI : Editor
	{
		private bHapticsOSCIntegration editorComp;
		private string autoFitMessage;
		private MessageType autoFitMessageType = MessageType.None;

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			EditorGUILayout.Space();

			editorComp = (bHapticsOSCIntegration)target;
			bCompanionStatusGUI.DrawInspectorCard();

			if (!DrawSetupProblem(editorComp))
				return;

			EnsureUserSettings(editorComp);

			if (editorComp.AllCustomContactTagsContainers == null)
			{
				editorComp.AllCustomContactTagsContainers = new Dictionary<bUserSettings, bReorderableListContainer<string>>();
				foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
					editorComp.AllCustomContactTagsContainers[settings] = new bReorderableListContainer<string>("Custom Contact Tags", "New_Tag", bGUI.LabelStyle, new SerializedObject(settings).FindProperty("CustomContactTags"));
			}

			editorComp.FindExistingPrefabs(bDevice.AllTemplates);

			bDeviceTemplate CurrentTemplate = bDevice.AllTemplates[editorComp.CurrentDevice];
			bUserSettings userSettings = editorComp.AllUserSettings[CurrentTemplate];
			bReorderableListContainer<string> CustomContactTagsContainer = editorComp.AllCustomContactTagsContainers[userSettings];
			
			bGUI.DrawSection(string.Empty, () =>
			{
				// Rig
				EditorGUILayout.Space(-8);
				bGUI.DrawRig();

				// Head
				EditorGUILayout.Space(-(bGUI.Rig.rect.height - 3));
				bGUI.DrawTemplateButton(editorComp, bDeviceType.HEAD);

				// Arms
				EditorGUILayout.Space(bGUI.Elements[bDeviceType.VEST].NotSelected.rect.height - 44);
				bGUI.DrawTemplateButton(editorComp, bDeviceType.ARM_RIGHT);
				EditorGUILayout.Space(-(bGUI.Elements[bDeviceType.ARM_RIGHT].NotSelected.rect.height + 6));
				bGUI.DrawTemplateButton(editorComp, bDeviceType.ARM_LEFT);

				// Vest
				// Rendering After Arms because Layering Derp
				EditorGUILayout.Space(-(bGUI.Elements[bDeviceType.VEST].NotSelected.rect.height - 8));
				bGUI.DrawTemplateButton(editorComp, bDeviceType.VEST);

				// Hands
				EditorGUILayout.Space(-24);
				bGUI.DrawTemplateButton(editorComp, bDeviceType.HAND_RIGHT);
				EditorGUILayout.Space(-(bGUI.Elements[bDeviceType.HAND_RIGHT].NotSelected.rect.height + 6));
				bGUI.DrawTemplateButton(editorComp, bDeviceType.HAND_LEFT);

				// Gloves

				// Feet
				EditorGUILayout.Space(142);
				bGUI.DrawTemplateButton(editorComp, bDeviceType.FOOT_RIGHT);
				EditorGUILayout.Space(-(bGUI.Elements[bDeviceType.FOOT_RIGHT].NotSelected.rect.height + 6));
				bGUI.DrawTemplateButton(editorComp, bDeviceType.FOOT_LEFT);

				// END
				EditorGUILayout.Space(12);

				// Selected Device
				bGUI.DrawSection(CurrentTemplate.Name, () =>
				{
					if (userSettings.CurrentPrefab == null)
					{
						GUILayout.BeginHorizontal();
						if (bGUI.DrawButton("+ ADD DEVICE (PC)"))
						{
							userSettings.Reset();
							userSettings.IsMobile = false;
						}

						if (CurrentTemplate.PrefabMeshMobile)
						{
							if (bGUI.DrawButton("+ ADD DEVICE (Quest)"))
							{
								userSettings.Reset();
								userSettings.IsMobile = true;
							}
						}
						GUILayout.EndHorizontal();
						return;
					}

					userSettings.ShowMesh = bGUI.DrawToggle("Show Mesh", userSettings.ShowMesh, userSettings);
					GUILayout.Space(6);

					if (CurrentTemplate.HasParentConstraints)
					{
						userSettings.ApplyParentConstraints = bGUI.DrawToggle("Apply ParentConstraints", userSettings.ApplyParentConstraints, userSettings);
						GUILayout.Space(6);
					}

					// Transform Editor
					Vector3 localPosition = userSettings.GetBoneLocalPosition(editorComp.avatarAnimator);
					Vector3 newLocalPosition = bGUI.DrawVector3Field("Position", localPosition, userSettings.CurrentPrefab.transform);
					if (newLocalPosition != localPosition)
						userSettings.SetBoneLocalPosition(editorComp.avatarAnimator, newLocalPosition);

					Vector3 localEulerAngles = userSettings.GetBoneLocalEulerAngles(editorComp.avatarAnimator);
					Vector3 newLocalEulerAngles = bGUI.DrawVector3Field("Rotation", localEulerAngles, userSettings.CurrentPrefab.transform);
					if (newLocalEulerAngles != localEulerAngles)
						userSettings.SetBoneLocalEulerAngles(editorComp.avatarAnimator, newLocalEulerAngles);

					Vector3 localScale = userSettings.GetBoneLocalScale(editorComp.avatarAnimator);
					Vector3 newLocalScale = bGUI.DrawVector3Field("Scale", localScale, userSettings.CurrentPrefab.transform);
					if (newLocalScale != localScale)
						userSettings.SetBoneLocalScale(editorComp.avatarAnimator, newLocalScale);

					if (bAutoFit.Supports(editorComp.CurrentDevice))
					{
						if (bGUI.DrawButton("AUTO FIT"))
						{
							bool autoFitApplied = bAutoFit.TryApply(editorComp, editorComp.CurrentDevice, userSettings, out autoFitMessage);
							autoFitMessageType = autoFitApplied ? MessageType.Info : MessageType.Warning;
							if (autoFitApplied)
								EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
						}

						if (!string.IsNullOrEmpty(autoFitMessage))
						{
							EditorGUILayout.HelpBox(autoFitMessage, autoFitMessageType);
							GUILayout.Space(6);
						}
					}

					GUILayout.Space(10);

					// Custom Contact Tags
					CustomContactTagsContainer.Draw();

					// END
					bGUI.DrawSeparator();
					GUILayout.BeginHorizontal();
					if (bGUI.DrawButton("SELECT IN SCENE"))
						userSettings.SelectCurrentPrefab();
					if (bGUI.DrawButton("REMOVE DEVICE", userSettings, false))
						userSettings.DestroyCurrentPrefab();
					GUILayout.EndHorizontal();
				},
				() =>
				{
					if (userSettings.CurrentPrefab == null)
						return;

					GUILayout.Space(-20);

					GUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();
					GUILayout.FlexibleSpace();
					GUILayout.FlexibleSpace();
					GUILayout.FlexibleSpace();

					if (bGUI.DrawHeaderButton("RESET", userSettings, false))
						userSettings.Reset();

					GUILayout.EndHorizontal();
					GUILayout.Space(-GUILayoutUtility.GetLastRect().height);
				});

				GUILayout.Space(-4);
			});
			bGUI.DrawSeparator();

			/*
			bGUI.DrawSection("Extras", () =>
			{
				bGUI.DrawToggle("Udon AudioLink Extension Support", ref editorComp.AudioLink);
			},
			() =>
			{
				GUILayout.Space(-20);

				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				GUILayout.FlexibleSpace();
				GUILayout.FlexibleSpace();
				GUILayout.FlexibleSpace();

				bGUI.DrawHeaderButton("RESET", editorComp.ResetExtras);

				GUILayout.EndHorizontal();
				GUILayout.Space(-GUILayoutUtility.GetLastRect().height);
			});

			bGUI.DrawSeparator();
			*/

			DrawContactCompressionToggle(editorComp);

			// Drawn disabled rather than hidden, with the blocker as its label: a primary action
			// that vanishes leaves the user with nothing to aim at and no idea what is missing.
			bool readyToApply = editorComp.IsReadyToApply();
			bool create;
			using (new EditorGUI.DisabledScope(!readyToApply))
			{
				create = bGUI.DrawButton(readyToApply
					? "CREATE VRCFURY SETUP"
					: "ADD AT LEAST ONE DEVICE FIRST");
			}

			if (!readyToApply)
			{
				EditorGUILayout.LabelField(
					"Pick a body part on the figure above, then use + ADD DEVICE.",
					EditorStyles.wordWrappedMiniLabel);
			}

			if (create)
			{
				// One undo group for the whole pipeline. It creates objects, moves the user's
				// device prefabs, adds components and writes assets; without this, backing out
				// means dozens of separate Ctrl+Z presses through a half-built setup.
				int undoGroup = Undo.GetCurrentGroup();
				Undo.SetCurrentGroupName("Create bHapticsOSC VRCFury setup");

				try
				{
					RunSetupPipeline(editorComp);
					Undo.CollapseUndoOperations(undoGroup);
				}
				catch (System.Exception e)
				{
					EditorUtility.ClearProgressBar();
					Debug.LogException(e);

					// Roll the avatar back rather than leaving it half-built. Generated assets on
					// disk are cleaned up by the existing bVrcFurySetup cleanup path.
					Undo.RevertAllDownToGroup(undoGroup);

					EditorUtility.DisplayDialog(
						bHapticsOSCIntegration.SystemName,
						$"Unable to create the VRCFury setup, so the avatar was put back as it was.\n\n{e.Message}\n\n"
						+ "The Console has the full details.",
						"OK");
				}

				GUIUtility.ExitGUI();
			}

			GUILayout.Space(6);

			if (GUI.changed)
				EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
		}


		/// <summary>
		/// Everything the avatar side does, in the order it has to happen.
		///
		/// Shared with the one-click "Set up this avatar" action so both routes run identical code
		/// - a second copy of this sequence would drift, and the order is not obvious enough to
		/// rediscover. The caller owns the undo group and the failure handling; this either
		/// completes or throws.
		/// </summary>
		internal static void RunSetupPipeline(bHapticsOSCIntegration editorComp)
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

				bUserSettings newSettings = CreateInstance<bUserSettings>();
				newSettings.Bone = template.Bone;

				var getNewPrefab = new Func<bUserSettings, GameObject>(x => x.ShowMesh
					? (x.IsMobile ? template.PrefabMeshMobile : template.PrefabMesh)
					: (x.IsMobile ? template.PrefabMobile : template.Prefab));

				newSettings.OnShowMeshChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
				newSettings.OnIsMobileChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
				editorComp.AllUserSettings[template] = newSettings;
			}
		}

		/// <summary>
		/// Explains why the component cannot be used where it is, and offers the one-click fix.
		/// Returns true when the component is usable and the rest of the inspector should draw.
		///
		/// The component used to delete itself here instead, which looked like the package was
		/// broken rather than like the component was in the wrong place.
		/// </summary>
		private static bool DrawSetupProblem(bHapticsOSCIntegration comp)
		{
			bHapticsOSCIntegration.bSetupProblem problem = comp.TryValidate();
			if (problem == bHapticsOSCIntegration.bSetupProblem.Ok)
				return true;

			switch (problem)
			{
				case bHapticsOSCIntegration.bSetupProblem.NoAvatarDescriptor:
				{
					GameObject root = comp.FindAvatarRoot();
					EditorGUILayout.HelpBox(
						root != null
							? $"This belongs on your avatar's root object, '{root.name}' - the one with the VRC "
							  + "Avatar Descriptor. It is currently on a child object."
							: "This belongs on your avatar's root object - the one with the VRC Avatar Descriptor. "
							  + "Select that object and add the component there.",
						MessageType.Warning);

					using (new EditorGUI.DisabledScope(root == null))
					{
						if (GUILayout.Button(root != null ? $"Move it to '{root.name}'" : "Move it to the avatar root"))
							MoveToAvatarRoot(comp, root);
					}

					break;
				}

				case bHapticsOSCIntegration.bSetupProblem.NoAnimator:
					EditorGUILayout.HelpBox(
						"This avatar has no Animator, so there are no bones to attach haptic devices to. Add an "
						+ "Animator with a humanoid avatar rig to the same object, then come back.",
						MessageType.Warning);
					break;

				case bHapticsOSCIntegration.bSetupProblem.DuplicateComponent:
					EditorGUILayout.HelpBox(
						"This avatar already has a bHapticsOSC Integration component. Only one can be used at a "
						+ "time - remove this one and use the original.",
						MessageType.Warning);

					if (GUILayout.Button("Remove this component"))
					{
						Undo.DestroyObjectImmediate(comp);
						GUIUtility.ExitGUI();
					}

					break;
			}

			return false;
		}

		/// <summary>Moves the component to the avatar root, keeping it undoable.</summary>
		private static void MoveToAvatarRoot(bHapticsOSCIntegration comp, GameObject root)
		{
			if (root == null)
				return;

			int group = Undo.GetCurrentGroup();
			Undo.SetCurrentGroupName("Move bHapticsOSC Integration to the avatar root");

			Undo.AddComponent<bHapticsOSCIntegration>(root);
			Undo.DestroyObjectImmediate(comp);

			Undo.CollapseUndoOperations(group);
			Selection.activeGameObject = root;
			GUIUtility.ExitGUI();
		}

		/// <summary>
		/// Offers contact compression, with the numbers that make the trade-off concrete rather than
		/// abstract.
		/// </summary>
		private static void DrawContactCompressionToggle(bHapticsOSCIntegration editorComp)
		{
#if bHapticsOSC_HasContactCompressor
			GUILayout.Space(6);

			bool wanted = bGUI.DrawToggle("Consolidate contact receivers", editorComp.ConsolidateContacts, editorComp);
			if (wanted != editorComp.ConsolidateContacts)
				editorComp.ConsolidateContacts = wanted;

			bCompressor.EstimateSavings(editorComp, out int before, out int after);
			if (before > 0)
			{
				EditorGUILayout.HelpBox(
					editorComp.ConsolidateContacts
						? $"Vest, head and arm receivers become {after} instead of {before} at build time. "
						  + "The companion app decodes the touch position, so contact spreads smoothly across "
						  + "neighbouring motors. Export the manifest from a Contact Compressor Group into the "
						  + "app's Config folder."
						: $"Currently {before} contact receivers across the vest, head and arms. "
						  + $"Consolidating would make it {after}.",
					editorComp.ConsolidateContacts ? MessageType.Info : MessageType.None);
			}
			else
			{
				// Silence here used to read as "this worked". Say plainly that there is nothing to
				// compress, so the toggle being on is not mistaken for the feature being active.
				// Deliberately does not guess why: it is equally reached by a hands-only setup and
				// by a Quest one, and naming the wrong cause is worse than naming none.
				EditorGUILayout.HelpBox(
					"Nothing to consolidate. This applies to the vest, head and forearm devices on "
					+ "the desktop prefabs; none of the currently selected devices use it, so they "
					+ "are left as they are.",
					MessageType.None);
			}
#endif
		}

		private static void ApplyContactCompression(bHapticsOSCIntegration editorComp)
		{
#if bHapticsOSC_HasContactCompressor
			if (!editorComp.ConsolidateContacts)
			{
				bCompressor.RemoveGroups(editorComp);
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
			throw new System.InvalidOperationException(
				$"Contact compression was applied to {applied} device(s) but no manifest could be produced, so it "
				+ "has been taken back off. See the console for the region that would not fit.\n"
				+ "Setup stopped partway: use Undo to return the avatar to its previous state before trying again.");
#endif
		}

	}
}
#endif
