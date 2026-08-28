#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// The bHapticsOSC Integration inspector.
    ///
    /// The device picker is what the user opens this for, so it is now the first thing in the
    /// panel. It used to sit under a bold heading and a full paragraph of companion-app status,
    /// which is background information dressed as the main event; that is a one-line strip now
    /// and only grows when something is actually wrong with it.
    /// </summary>
    [CustomEditor(typeof(bHapticsOSCIntegration))]
    public class bEditorGUI : Editor
    {
        private bHapticsOSCIntegration editorComp;

        private VisualElement root;
        private VisualElement devicePanel;
        private VisualElement compressionHost;
        private Button createButton;
        private bRigPickerElement picker;

        private string autoFitMessage;
        private bStepState autoFitState = bStepState.Ok;

        public override VisualElement CreateInspectorGUI()
        {
            editorComp = (bHapticsOSCIntegration)target;

            root = new VisualElement();
            bUI.ApplyTheme(root);
            Rebuild();
            return root;
        }

        /// <summary>
        /// Rebuilds the whole panel. Cheap enough to do on every change, and it keeps the tree a
        /// pure function of the component's state rather than something that has to be patched in
        /// the right order after each edit.
        /// </summary>
        private void Rebuild()
        {
            if (root == null)
                return;

            root.Clear();
            picker = null;

            root.Add(BuildCompanionStrip());

            if (!TryDrawSetupProblem())
                return;

            bSetupPipeline.EnsureUserSettings(editorComp);
            editorComp.FindExistingPrefabs(bDevice.AllTemplates);

            picker = new bRigPickerElement(editorComp, OnDeviceSelected);
            root.Add(picker);

            devicePanel = new VisualElement();
            root.Add(devicePanel);
            RebuildDevicePanel();

            compressionHost = new VisualElement();
            root.Add(compressionHost);
            RebuildCompression();

            root.Add(BuildCreateButton());
        }

        private void OnDeviceSelected()
        {
            autoFitMessage = null;
            picker?.Refresh(editorComp);
            RebuildDevicePanel();
            MarkSceneDirty();
        }

        /// <summary>Everything downstream of a device being added, removed or swapped.</summary>
        private void OnDeviceSetChanged()
        {
            picker?.Refresh(editorComp);
            RebuildDevicePanel();
            RebuildCompression();
            RefreshCreateButton();
            MarkSceneDirty();
        }

        // ------------------------------------------------------------------ companion strip

        /// <summary>
        /// One line when there is nothing to say, a full step card when there is.
        ///
        /// This used to be a bold label above a help-box paragraph, unconditionally, at the very
        /// top of the inspector - so the picker started below the fold on a narrow panel and the
        /// user read the same three sentences about the companion app every time they nudged a
        /// device.
        /// </summary>
        private VisualElement BuildCompanionStrip()
        {
            bCompanionStatusResult status = bCompanionStatusDetector.Detect();
            bSetupStep step = bSetupModel.DescribeCompanion(status);

            var companionStrip = new VisualElement();

            if (step.NeedsAttention)
            {
                var card = new bStepRowElement(new bSetupStep(
                    step.Id,
                    step.Title,
                    step.State,
                    step.Value,
                    step.Detail,
                    step.Explanation,
                    new bStepAction("Setup Assistant", bCompanionSetupWindow.ShowWindow, true)));
                companionStrip.Add(card);

                if (status.HasConflictingProcess)
                {
                    companionStrip.Add(new bStepRowElement(new bSetupStep(
                        bSetupModel.StepConflict,
                        "OSC port conflict",
                        bStepState.Blocked,
                        null,
                        "'" + status.ConflictingProcessName + "' is also running.",
                        "Two companion apps cannot share the VRChat OSC port, so only one of them "
                        + "receives anything. Open the Setup Assistant to close it.",
                        new bStepAction("Setup Assistant", bCompanionSetupWindow.ShowWindow, true))));
                }

                return companionStrip;
            }

            var strip = new VisualElement();
            strip.AddToClassList("b-companion-strip");
            strip.tooltip = step.Explanation;
            strip.Add(bUI.CreateStateMarker(step.State, "b-step__icon"));

            var label = new Label(step.Title + " · " + step.Value);
            label.AddToClassList("b-companion-strip__text");
            strip.Add(label);

            var button = new Button(bCompanionSetupWindow.ShowWindow) { text = "Setup Assistant" };
            button.AddToClassList("b-companion-strip__button");
            strip.Add(button);

            companionStrip.Add(strip);
            return companionStrip;
        }

        // ------------------------------------------------------------------ placement problems

        /// <summary>
        /// Explains why the component cannot be used where it is, and offers the one-click fix.
        /// Returns true when the component is usable and the rest of the inspector should draw.
        ///
        /// The component used to delete itself here instead, which looked like the package was
        /// broken rather than like the component was in the wrong place.
        /// </summary>
        private bool TryDrawSetupProblem()
        {
            bHapticsOSCIntegration.bSetupProblem problem = editorComp.TryValidate();
            if (problem == bHapticsOSCIntegration.bSetupProblem.Ok)
                return true;

            switch (problem)
            {
                case bHapticsOSCIntegration.bSetupProblem.NoAvatarDescriptor:
                {
                    GameObject avatarRoot = editorComp.FindAvatarRoot();
                    var action = avatarRoot != null
                        ? new bStepAction(
                            "Move it to '" + avatarRoot.name + "'",
                            () =>
                            {
                                bSetupPipeline.MoveToAvatarRoot(editorComp, avatarRoot);
                            },
                            true)
                        : default;

                    root.Add(new bStepRowElement(new bSetupStep(
                        "placement",
                        "Wrong object",
                        bStepState.Blocked,
                        null,
                        avatarRoot != null
                            ? "This belongs on '" + avatarRoot.name + "', not a child object."
                            : "This belongs on your avatar's root object.",
                        "It has to sit on the object carrying the VRC Avatar Descriptor - that is where "
                        + "the humanoid rig is, and the devices attach to its bones.",
                        avatarRoot != null ? new[] { action } : new bStepAction[0])));
                    break;
                }

                case bHapticsOSCIntegration.bSetupProblem.NoAnimator:
                    root.Add(new bStepRowElement(new bSetupStep(
                        "placement",
                        "No Animator",
                        bStepState.Blocked,
                        null,
                        "This avatar has no Animator.",
                        "There are no bones to attach haptic devices to. Add an Animator with a humanoid "
                        + "avatar rig to the same object, then come back.")));
                    break;

                case bHapticsOSCIntegration.bSetupProblem.DuplicateComponent:
                    root.Add(new bStepRowElement(new bSetupStep(
                        "placement",
                        "Already present",
                        bStepState.Blocked,
                        null,
                        "This avatar already has a bHapticsOSC Integration.",
                        "Only one can be used at a time - remove this one and use the original.",
                        new bStepAction(
                            "Remove this component",
                            () => Undo.DestroyObjectImmediate(editorComp),
                            true))));
                    break;
            }

            return false;
        }

        // ------------------------------------------------------------------ device panel

        private void RebuildDevicePanel()
        {
            if (devicePanel == null)
                return;

            devicePanel.Clear();

            bDeviceTemplate template = bDevice.AllTemplates[editorComp.CurrentDevice];
            bUserSettings settings = editorComp.AllUserSettings[template];

            var panel = new VisualElement();
            panel.AddToClassList("b-device-panel");
            devicePanel.Add(panel);

            var head = new VisualElement();
            head.AddToClassList("b-device-panel__head");
            var title = new Label(template.Name);
            title.AddToClassList("b-device-panel__title");
            head.Add(title);

            if (settings.CurrentPrefab != null)
            {
                var reset = new Button(() =>
                {
                    settings.Reset();
                    OnDeviceSetChanged();
                })
                { text = "Reset" };
                reset.AddToClassList("b-step__inline-action");
                head.Add(reset);
            }

            panel.Add(head);

            if (settings.CurrentPrefab == null)
            {
                panel.Add(BuildAddRow(template, settings));
                return;
            }

            panel.Add(BuildToggles(template, settings));

            // Promoted above the transform fields: it is the recommended path, and it is what
            // makes the fields below unnecessary for most people.
            if (bAutoFit.Supports(editorComp.CurrentDevice))
                panel.Add(BuildAutoFit(settings));

            panel.Add(BuildTransformFoldout(settings));
            panel.Add(BuildTagsList(settings));
            panel.Add(BuildDeviceFooter(settings));
        }

        private VisualElement BuildAddRow(bDeviceTemplate template, bUserSettings settings)
        {
            var row = new VisualElement();
            row.AddToClassList("b-row");

            row.Add(new Button(() =>
            {
                settings.Reset();
                settings.IsMobile = false;
                OnDeviceSetChanged();
            })
            { text = "Add device (PC)" });

            if (template.PrefabMeshMobile != null)
            {
                row.Add(new Button(() =>
                {
                    settings.Reset();
                    settings.IsMobile = true;
                    OnDeviceSetChanged();
                })
                { text = "Add device (Quest)" });
            }

            return row;
        }

        private VisualElement BuildToggles(bDeviceTemplate template, bUserSettings settings)
        {
            var host = new VisualElement();

            var showMesh = new Toggle("Show mesh") { value = settings.ShowMesh };
            showMesh.tooltip =
                "Whether the device model is visible on the avatar. Turning it off keeps the haptics "
                + "and drops the geometry.";
            showMesh.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, $"[{bHapticsOSCIntegration.SystemName}] Toggled Show Mesh");
                settings.ShowMesh = evt.newValue;
                MarkSceneDirty();
            });
            host.Add(showMesh);

            if (!template.HasParentConstraints)
                return host;

            var constraints = new Toggle("Apply ParentConstraints") { value = settings.ApplyParentConstraints };
            constraints.tooltip =
                "Constrains the device to the tracked bone so it follows the hand rather than the "
                + "animated mesh.";
            constraints.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(settings, $"[{bHapticsOSCIntegration.SystemName}] Toggled Apply ParentConstraints");
                settings.ApplyParentConstraints = evt.newValue;
                MarkSceneDirty();
            });
            host.Add(constraints);

            return host;
        }

        private VisualElement BuildAutoFit(bUserSettings settings)
        {
            var host = new VisualElement();

            var button = new Button(() =>
            {
                bool applied = bAutoFit.TryApply(
                    editorComp,
                    editorComp.CurrentDevice,
                    settings,
                    out autoFitMessage);
                autoFitState = applied ? bStepState.Ok : bStepState.Attention;
                if (applied)
                    MarkSceneDirty();

                RebuildDevicePanel();
            })
            { text = "Auto fit" };
            button.tooltip = "Scales and places this device to match the avatar's proportions.";
            button.style.minHeight = 24f;
            host.Add(button);

            if (!string.IsNullOrEmpty(autoFitMessage))
            {
                host.Add(new HelpBox(
                    autoFitMessage,
                    autoFitState == bStepState.Ok ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            }

            return host;
        }

        /// <summary>
        /// Collapsed by default: auto fit is the route most people take, and three Vector3 fields
        /// open by default read as work that has to be done rather than as an escape hatch.
        /// </summary>
        private VisualElement BuildTransformFoldout(bUserSettings settings)
        {
            const string key = "inspector.transform";
            var foldout = new Foldout { text = "Transform", value = bUI.GetFlag(key, false) };
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    bUI.SetFlag(key, evt.newValue);
            });

            foldout.Add(BuildVector3(
                "Position",
                settings.GetBoneLocalPosition(editorComp.avatarAnimator),
                value => settings.SetBoneLocalPosition(editorComp.avatarAnimator, value),
                settings));
            foldout.Add(BuildVector3(
                "Rotation",
                settings.GetBoneLocalEulerAngles(editorComp.avatarAnimator),
                value => settings.SetBoneLocalEulerAngles(editorComp.avatarAnimator, value),
                settings));
            foldout.Add(BuildVector3(
                "Scale",
                settings.GetBoneLocalScale(editorComp.avatarAnimator),
                value => settings.SetBoneLocalScale(editorComp.avatarAnimator, value),
                settings));

            return foldout;
        }

        private Vector3Field BuildVector3(
            string label,
            Vector3 current,
            System.Action<Vector3> apply,
            bUserSettings settings)
        {
            var field = new Vector3Field(label) { value = current };
            field.RegisterValueChangedCallback(evt =>
            {
                Object undoTarget = settings.CurrentPrefab != null
                    ? (Object)settings.CurrentPrefab.transform
                    : settings;
                Undo.RecordObject(undoTarget, $"[{bHapticsOSCIntegration.SystemName}] Changed {label}");
                apply(evt.newValue);
                MarkSceneDirty();
            });

            return field;
        }

        /// <summary>
        /// A plain bound ListView, in place of a hand-drawn ReorderableList that positioned its
        /// own header and footer with rect arithmetic and lived, editor-only, in the runtime
        /// assembly.
        /// </summary>
        private VisualElement BuildTagsList(bUserSettings settings)
        {
            var host = new VisualElement();
            var serialized = new SerializedObject(settings);
            SerializedProperty tags = serialized.FindProperty("CustomContactTags");

            var list = new ListView
            {
                headerTitle = "Custom contact tags",
                showFoldoutHeader = true,
                showAddRemoveFooter = true,
                showBorder = true,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                bindingPath = tags.propertyPath,
            };
            list.tooltip =
                "Extra VRChat contact tags this device also responds to, on top of the stock bHaptics "
                + "ones. Add tags here to react to another creator's contact senders.";

            list.Bind(serialized);
            host.Add(list);
            return host;
        }

        private VisualElement BuildDeviceFooter(bUserSettings settings)
        {
            var row = new VisualElement();
            row.AddToClassList("b-row");
            row.style.marginTop = 4f;

            row.Add(new Button(settings.SelectCurrentPrefab) { text = "Select in scene" });
            row.Add(new Button(() =>
            {
                settings.DestroyCurrentPrefab();
                OnDeviceSetChanged();
            })
            { text = "Remove device" });

            return row;
        }

        // ------------------------------------------------------------------ contact compression

        /// <summary>
        /// Offers contact compression, with the numbers that make the trade-off concrete rather
        /// than abstract.
        ///
        /// The numbers used to arrive inside a three-sentence help-box that was on screen in one
        /// of its three variants at all times. The count is the part that decides anything, so it
        /// is on the toggle's own line; the reasoning is on hover.
        /// </summary>
        private void RebuildCompression()
        {
            if (compressionHost == null)
                return;

            compressionHost.Clear();

#if bHapticsOSC_HasContactCompressor
            bCompressor.EstimateSavings(editorComp, out int before, out int after);

            var row = new VisualElement();
            row.AddToClassList("b-toggle-row");

            var toggle = new Toggle("Consolidate contact receivers") { value = editorComp.ConsolidateContacts };
            toggle.SetEnabled(before > 0);
            row.Add(toggle);

            var note = new Label();
            note.AddToClassList("b-inline-note");
            row.Add(note);

            if (before > 0)
            {
                note.text = "· " + before + " → " + after;
                toggle.tooltip =
                    "Vest, head and arm receivers become " + after + " instead of " + before + " at build "
                    + "time. The companion app decodes the touch position, so contact spreads smoothly "
                    + "across neighbouring motors. Export the manifest from a Contact Compressor Group "
                    + "into the app's Config folder.";
            }
            else
            {
                // Silence here used to read as "this worked". Say plainly that there is nothing to
                // compress, so the toggle being on is not mistaken for the feature being active.
                // Deliberately does not guess why: it is equally reached by a hands-only setup and
                // by a Quest one, and naming the wrong cause is worse than naming none.
                note.text = "· nothing to consolidate";
                toggle.tooltip =
                    "This applies to the vest, head and forearm devices on the desktop prefabs. None of "
                    + "the currently selected devices use it, so they are left as they are.";
            }

            note.tooltip = toggle.tooltip;

            toggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(
                    editorComp,
                    $"[{bHapticsOSCIntegration.SystemName}] Toggled Consolidate contact receivers");
                editorComp.ConsolidateContacts = evt.newValue;
                MarkSceneDirty();
            });

            compressionHost.Add(row);
#endif
        }

        // ------------------------------------------------------------------ the primary action

        /// <summary>
        /// Drawn disabled rather than hidden, with the blocker as its label: a primary action that
        /// vanishes leaves the user with nothing to aim at and no idea what is missing.
        /// </summary>
        private VisualElement BuildCreateButton()
        {
            createButton = new Button(RunSetup);
            createButton.AddToClassList("b-primary-button");
            RefreshCreateButton();
            return createButton;
        }

        private void RefreshCreateButton()
        {
            if (createButton == null)
                return;

            bool ready = editorComp.IsReadyToApply();
            createButton.SetEnabled(ready);
            createButton.text = ready ? "Create VRCFury setup" : "Add at least one device first";

            // The hint used to be a wrapped mini-label under the button, restating what the
            // disabled label already says. Once is enough on screen; the rest is on hover.
            createButton.tooltip = ready
                ? "Adds the contact receivers, generates the animator assets, and hands the result to "
                  + "VRCFury. It is a single undo step."
                : "Pick a body part on the figure above, then use Add device.";
        }

        private void RunSetup()
        {
            // One undo group for the whole pipeline. It creates objects, moves the user's
            // device prefabs, adds components and writes assets; without this, backing out
            // means dozens of separate Ctrl+Z presses through a half-built setup.
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create bHapticsOSC VRCFury setup");

            try
            {
                bSetupPipeline.Run(editorComp);
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
        }

        private static void MarkSceneDirty()
            => EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
#endif
