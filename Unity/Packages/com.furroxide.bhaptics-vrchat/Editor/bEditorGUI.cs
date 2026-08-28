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

        // Set while a panel rebuild is waiting for the event that asked for it to finish
        // dispatching. See DeferDeviceSetChanged.
        private bool deviceSetChangePending;

        // Set while a rebuild driven by an undo or by a hierarchy change is running. Rebuilding is
        // not purely a read - see RebuildFromExternalChange - so it can provoke the very
        // notification that started it, and this keeps that from starting a second one on top.
        private bool rebuilding;

        public override VisualElement CreateInspectorGUI()
        {
            editorComp = (bHapticsOSCIntegration)target;

            root = new VisualElement();
            bUI.ApplyTheme(root);
            Rebuild();
            return root;
        }

        /// <summary>
        /// The IMGUI inspector this replaced re-read the scene on every repaint, so an undo, a
        /// redo, or a device object deleted from the Hierarchy window corrected the panel by
        /// itself on the next frame. A UI Toolkit tree is built once and then left alone, so the
        /// same events have to be listened for explicitly - without this the panel goes on
        /// offering "Remove device" for a device the user has just undone, until they think to
        /// reselect the avatar.
        /// </summary>
        private void OnEnable()
        {
            Undo.undoRedoPerformed += RebuildFromExternalChange;
            EditorApplication.hierarchyChanged += RebuildFromExternalChange;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildFromExternalChange;
            EditorApplication.hierarchyChanged -= RebuildFromExternalChange;
        }

        /// <summary>
        /// The two subscriptions above come through here rather than going straight to Rebuild.
        ///
        /// A rebuild is not purely a read: it validates the component, which repairs the
        /// generated-asset key when that is missing and records an undo step to do it. Recording
        /// an undo step does not raise undoRedoPerformed, so that path cannot feed back on itself,
        /// but exactly which edits raise hierarchyChanged is not something worth relying on. The
        /// flag keeps a rebuild that causes a notification from being re-entered by it.
        /// </summary>
        private void RebuildFromExternalChange()
        {
            if (rebuilding)
                return;

            rebuilding = true;
            try
            {
                Rebuild();
            }
            finally
            {
                rebuilding = false;
            }
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

            // Undo and the hierarchy notification also arrive for the edits that destroy the
            // component itself - undoing the drag that added it, or the Setup Assistant moving it
            // to the avatar root - and this editor is still alive, holding a destroyed target,
            // when they do. Everything below dereferences the component, so there is nothing left
            // to draw.
            if (editorComp == null)
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

        /// <summary>
        /// The same thing, but after the event that asked for it has finished dispatching.
        ///
        /// A field's change callback runs partway through that field's own event. Rebuilding the
        /// panel from inside one tears the field out of the hierarchy while the rest of the
        /// propagation path is still walking it, and the keyboard focus goes with it. The buttons
        /// in this panel get away with rebuilding themselves because a click is over by the time
        /// the handler runs; a Toggle reporting a value change is not. Waiting one panel tick lets
        /// the toggle finish reporting before the element it lives in is replaced.
        ///
        /// The flag collapses a burst of changes into a single rebuild, and scheduling on the root
        /// rather than through EditorApplication.delayCall means an inspector that is closed before
        /// the tick arrives simply never runs it.
        /// </summary>
        private void DeferDeviceSetChanged()
        {
            if (root == null || deviceSetChangePending)
                return;

            deviceSetChangePending = true;
            root.schedule.Execute(() =>
            {
                deviceSetChangePending = false;

                // The setup pipeline destroys the component on success, and the panel can outlive
                // it by a tick.
                if (editorComp == null)
                    return;

                OnDeviceSetChanged();
            });
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
                    new bStepAction("Setup Assistant", bCompanionSetupWindow.ShowWindow, true),
                    new bStepAction("Recheck", RecheckCompanion)));
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

            var recheck = new Button(RecheckCompanion) { text = "Recheck" };
            recheck.AddToClassList("b-companion-strip__button");
            strip.Add(recheck);

            companionStrip.Add(strip);
            return companionStrip;
        }

        /// <summary>
        /// Drops the cached probe result and redraws the panel.
        ///
        /// The detector only goes back to the filesystem and the process list once its short-lived
        /// cache has expired, and this inspector builds its tree once and is then left alone - so
        /// after the user installs the companion app or starts it, nothing here notices until some
        /// unrelated edit happens to rebuild the panel. Recheck is the user saying "look again now",
        /// which is why it has to invalidate first: rebuilding on its own would only redraw the
        /// answer the detector already had.
        /// </summary>
        private void RecheckCompanion()
        {
            bCompanionStatusDetector.InvalidateCache();
            Rebuild();
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
                    // Reset() destroys the prefab through Undo but clears the tag list, the
                    // touch-view colours and the mesh flags on the settings object outside it.
                    // Snapshotting the settings first keeps both halves in the one undo step, so
                    // Ctrl+Z cannot bring the device object back while the settings still say
                    // there is none. bGUI's DrawHeaderButton did this before the panel moved to
                    // UI Toolkit.
                    Undo.RegisterCompleteObjectUndo(
                        settings,
                        $"[{bHapticsOSCIntegration.SystemName}] Clicked Reset");
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

                // Not merely a dirty flag. The setter swaps this device for the other prefab
                // variant, and the two do not carry the same receivers - the head is 12 without the
                // mesh and 8 with it - so the compression estimate below describes the prefab that
                // has just been destroyed until the panel is rebuilt. OnDeviceSetChanged marks the
                // scene dirty itself, which is why that call is gone from here.
                DeferDeviceSetChanged();
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
                // DestroyCurrentPrefab() destroys the prefab through Undo and then clears
                // CurrentPrefab, the custom tags and the touch-view colours on the settings object
                // without recording any of it. Snapshotting the settings first keeps the scene and
                // the settings on the same side of a Ctrl+Z, the way bGUI's DrawButton did before
                // the panel moved to UI Toolkit.
                Undo.RegisterCompleteObjectUndo(
                    settings,
                    $"[{bHapticsOSCIntegration.SystemName}] Clicked Remove device");
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
            row.Add(toggle);

            var note = new Label();
            note.AddToClassList("b-inline-note");
            row.Add(note);

            // Everything that reads off the toggle's own value, gathered so the change callback can
            // bring the row up to date by rewriting a label and two tooltips. Rebuilding the row
            // instead would destroy this toggle from inside this toggle's own callback, and the
            // estimate a rebuild would recompute cannot have moved: nothing on this row changes
            // which prefabs are in the scene.
            void SyncRow(bool consolidating)
            {
                // Live whenever there is something to compress, and also whenever it is already on.
                // A setup that had compressible devices and then lost them - the vest removed, or
                // the desktop prefabs swapped for the Quest ones, which match no plan - would
                // otherwise leave the setting switched on behind a control the user can no longer
                // reach to switch it back off.
                toggle.SetEnabled(before > 0 || consolidating);

                if (before > 0)
                {
                    note.text = "· " + before + " → " + after;
                    toggle.tooltip = consolidating
                        ? "Vest, head and arm receivers become " + after + " instead of " + before + " at build "
                          + "time. The companion app decodes the touch position, so contact spreads smoothly "
                          + "across neighbouring motors. Export the manifest from a Contact Compressor Group "
                          + "into the app's Config folder."
                        : "Currently " + before + " contact receivers across the vest, head and arms. "
                          + "Consolidating would make it " + after + " at build time.";
                }
                else if (consolidating)
                {
                    // On, with nothing to apply it to. Silence here would read as "this worked", and
                    // the setting lives on the component rather than on the devices, so it outlives
                    // whatever justified switching it on - swapping the vest for its Quest prefab
                    // lands exactly here.
                    note.text = "· on, nothing to consolidate";
                    toggle.tooltip =
                        "None of the currently selected devices can be consolidated, so this changes nothing "
                        + "at build time. It stays on, and applies again if a desktop vest, head or forearm "
                        + "device is added.";
                }
                else
                {
                    // Deliberately does not guess why there is nothing to do: it is equally reached
                    // by a hands-only setup and by a Quest one, and naming the wrong cause is worse
                    // than naming none.
                    note.text = "· nothing to consolidate";
                    toggle.tooltip =
                        "This applies to the vest, head and forearm devices on the desktop prefabs. None of "
                        + "the currently selected devices use it, so they are left as they are.";
                }

                note.tooltip = toggle.tooltip;
            }

            SyncRow(editorComp.ConsolidateContacts);

            toggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(
                    editorComp,
                    $"[{bHapticsOSCIntegration.SystemName}] Toggled Consolidate contact receivers");
                editorComp.ConsolidateContacts = evt.newValue;
                SyncRow(evt.newValue);
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
