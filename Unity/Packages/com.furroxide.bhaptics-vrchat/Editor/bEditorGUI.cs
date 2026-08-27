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
			editorComp.Validate();
			if (editorComp.avatar == null)
				return;

			if (editorComp.AllUserSettings == null)
            {
				editorComp.AllUserSettings = new Dictionary<bDeviceTemplate, bUserSettings>();
				for (int i = 0; i < bDevice.AllTemplates.Values.Count; i++)
				{
					bDeviceTemplate template = bDevice.AllTemplates.Values.ElementAt(i);
					if (!template.HasBone)
						continue;

					bUserSettings newSettings = CreateInstance<bUserSettings>();
					newSettings.Bone = template.Bone;

					var getNewPrefab = new Func<bUserSettings, GameObject>(x =>
					{
						if (x.ShowMesh)
						{
							return x.IsMobile ? template.PrefabMeshMobile : template.PrefabMesh;
						}
						else
						{
							return x.IsMobile ? template.PrefabMobile : template.Prefab;
						}
					});
					
					
					newSettings.OnShowMeshChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
					newSettings.OnIsMobileChange = thisSettings => thisSettings.SwapPrefabs(editorComp, getNewPrefab(thisSettings));
					editorComp.AllUserSettings[template] = newSettings;
				}
			}

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

			if (!editorComp.IsReadyToApply())
			{
				bGUI.DrawHelpBox(bGUI.HelpBoxType.NotReadyToApply);
				return;
			}

			DrawContactCompressionToggle(editorComp);

			if (bGUI.DrawButton("CREATE VRCFURY SETUP"))
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

					EditorUtility.ClearProgressBar();
					Debug.Log("VRCFury setup complete. To remove its generated assets, delete the bHapticsOSC VRCFury object, save, and close the scene or prefab.");
					bCompanionSetupWindow.ShowAvatarSetupComplete();
					DestroyImmediate(editorComp);
				}
				catch (System.Exception e)
				{
					EditorUtility.ClearProgressBar();
					Debug.LogException(e);
					EditorUtility.DisplayDialog(bHapticsOSCIntegration.SystemName, $"Unable to create VRCFury setup:\n{e.Message}", "OK");
				}
			}

			GUILayout.Space(6);

			if (GUI.changed)
				EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
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
#endif
		}

		private static void ApplyContactCompression(bHapticsOSCIntegration editorComp)
		{
#if bHapticsOSC_HasContactCompressor
			if (editorComp.ConsolidateContacts)
			{
				bCompressor.ApplyGroups(editorComp);

				// Emitted here rather than left to the user: the layout is fitted to this avatar,
				// so a manifest from anywhere else describes the wrong geometry and drives the
				// wrong motors.
				bCompressor.ExportManifest(editorComp);
			}
			else
			{
				bCompressor.RemoveGroups(editorComp);
			}
#endif
		}

	}
}
#endif
