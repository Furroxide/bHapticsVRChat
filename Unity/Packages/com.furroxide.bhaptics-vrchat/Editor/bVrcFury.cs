#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace bHapticsOSC.VRChat
{
    public static class bVrcFury
    {
        private const string FuryComponentsTypeName = "com.vrcfury.api.FuryComponents";
        private const string VrcFuryComponentTypeName = "VF.Model.VRCFury";
        private const float DefaultSliderValue = 1f;

        public static bool IsAvailable => GetFuryComponentsType() != null;

        public static void Apply(bHapticsOSCIntegration editorComp, bGeneratedAnimatorAssets generatedAssets)
        {
            Type furyComponentsType = GetFuryComponentsType();
            if (furyComponentsType == null)
                throw new InvalidOperationException("VRCFury public API was not found. Install VRCFury through VCC and reopen the Unity project.");

            Transform root = editorComp.GetOrCreateVrcFuryRoot(true);
            editorComp.ConfigureVrcFurySetup(generatedAssets.FolderPath, true);
            var existingComponents = new Dictionary<GameObject, MonoBehaviour[]>();
            CaptureVrcFuryComponents(existingComponents, root.gameObject);

            try
            {
                object fullController = InvokeStatic(furyComponentsType, "CreateFullController", root.gameObject);
                Invoke(fullController, "AddController", generatedAssets.Controller, VRCAvatarDescriptor.AnimLayerType.FX);
                Invoke(fullController, "AddParams", generatedAssets.Parameters);
                if (generatedAssets.CreditsMenu != null)
                    Invoke(fullController, "AddMenu", generatedAssets.CreditsMenu, bAnimator.RootMenuPath);
                Invoke(fullController, "AddGlobalParam", "bOSC/*");
                Invoke(fullController, "AddGlobalParam", "bOSC_v1_*");
                AddSavedDefaultOnToggle(
                    furyComponentsType,
                    fullController,
                    root.gameObject,
                    generatedAssets.HasDeviceMeshToggle,
                    bAnimator.DeviceMeshToggleMenuPath,
                    bAnimator.DeviceMeshToggleParameter);
                AddSavedDefaultOnToggle(
                    furyComponentsType,
                    fullController,
                    root.gameObject,
                    generatedAssets.HasMotorMeshToggle,
                    bAnimator.MotorMeshToggleMenuPath,
                    bAnimator.MotorMeshToggleParameter);
                AddPunchControls(
                    furyComponentsType,
                    fullController,
                    root.gameObject,
                    generatedAssets.HasPunchControls);

                foreach (var pair in bDevice.AllTemplates)
                {
                    bDeviceTemplate template = pair.Value;
                    if (!template.HasBone)
                        continue;

                    bUserSettings settings = editorComp.AllUserSettings[template];
                    if (settings.CurrentPrefab == null)
                        continue;

                    settings.MoveToStagingRoot(editorComp, true);
                    CaptureVrcFuryComponents(existingComponents, settings.CurrentPrefab);

                    object armatureLink = InvokeStatic(furyComponentsType, "CreateArmatureLink", settings.CurrentPrefab);
                    Invoke(armatureLink, "LinkFrom", settings.CurrentPrefab);
                    Invoke(armatureLink, "LinkTo", template.Bone, string.Empty);
                    Invoke(armatureLink, "SetRecursive", false);
                    Invoke(armatureLink, "SetAlign", false);
                    EditorUtility.SetDirty(settings.CurrentPrefab);
                }

                // VRCFury's API adds its components with a plain AddComponent, so nothing it
                // created is on the undo stack. Register the exact set this run added - the same
                // diff the failure path below already uses - or Ctrl+Z leaves the avatar looking
                // reverted while stale VRCFury components stay behind on it.
                foreach (KeyValuePair<GameObject, MonoBehaviour[]> pair in existingComponents)
                {
                    foreach (MonoBehaviour created in GetNewVrcFuryComponents(pair.Key, pair.Value))
                        Undo.RegisterCreatedObjectUndo(created, "Create bHapticsOSC VRCFury setup");
                }

                RemoveComponents(existingComponents.Values.SelectMany(components => components));
            }
            catch
            {
                foreach (KeyValuePair<GameObject, MonoBehaviour[]> pair in existingComponents)
                    RemoveComponents(GetNewVrcFuryComponents(pair.Key, pair.Value));

                throw;
            }

            EditorUtility.SetDirty(root.gameObject);
        }

        private static void AddSavedDefaultOnToggle(Type furyComponentsType, object fullController, GameObject target, bool shouldAdd, string menuPath, string parameterName)
        {
            if (!shouldAdd)
                return;

            Invoke(fullController, "AddGlobalParam", parameterName);

            object toggle = InvokeStatic(furyComponentsType, "CreateToggle", target);
            Invoke(toggle, "SetMenuPath", menuPath);
            Invoke(toggle, "SetDefaultOn");
            Invoke(toggle, "SetSaved");
            Invoke(toggle, "SetGlobalParameter", parameterName);
        }

        private static void AddPunchControls(Type furyComponentsType, object fullController, GameObject target, bool shouldAdd)
        {
            if (!shouldAdd)
                return;

            AddSavedDefaultOnToggle(
                furyComponentsType,
                fullController,
                target,
                true,
                bPunch.EnabledMenuPath,
                bPunch.EnabledParameter);
            AddSavedDefaultOnToggle(
                furyComponentsType,
                fullController,
                target,
                true,
                bPunch.RippleMenuPath,
                bPunch.RippleParameter);
            AddSavedDefaultSlider(
                furyComponentsType,
                fullController,
                target,
                bPunch.StrengthMenuPath,
                bPunch.StrengthParameter);
            AddSavedDefaultSlider(
                furyComponentsType,
                fullController,
                target,
                bPunch.DurationMenuPath,
                bPunch.DurationParameter);
        }

        private static void AddSavedDefaultSlider(Type furyComponentsType, object fullController, GameObject target, string menuPath, string parameterName)
        {
            Invoke(fullController, "AddGlobalParam", parameterName);

            object slider = InvokeStatic(furyComponentsType, "CreateToggle", target);
            Invoke(slider, "SetMenuPath", menuPath);
            Invoke(slider, "SetSlider", true);
            SetDefaultSliderValue(slider, DefaultSliderValue);
            Invoke(slider, "SetSaved");
            Invoke(slider, "SetGlobalParameter", parameterName);
        }

        private static void SetDefaultSliderValue(object slider, float value)
        {
            MethodInfo publicSetter = slider.GetType().GetMethod(
                "SetDefaultSliderValue",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float) },
                null);
            if (publicSetter != null)
            {
                publicSetter.Invoke(slider, new object[] { value });
                return;
            }

            // VRCFury 1.1341 exposes SetDefaultOn, but that flag is ignored for
            // sliders. Fall back to its serialized model until the public API
            // exposes a slider-default setter.
            FieldInfo modelField = slider.GetType().GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
            object model = modelField?.GetValue(slider);
            FieldInfo valueField = model?.GetType().GetField(
                "defaultSliderValue",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (valueField == null || valueField.FieldType != typeof(float))
            {
                throw new MissingFieldException(
                    slider.GetType().FullName,
                    "defaultSliderValue");
            }

            valueField.SetValue(model, value);
        }

        private static Type GetFuryComponentsType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(FuryComponentsTypeName);
                if (type != null)
                    return type;
            }

            try
            {
                return Assembly.Load("com.vrcfury.api").GetType(FuryComponentsTypeName);
            }
            catch
            {
                return null;
            }
        }

        private static object InvokeStatic(Type type, string methodName, params object[] args)
            => InvokeReflected(type, null, methodName, args);

        private static object Invoke(object target, string methodName, params object[] args)
            => InvokeReflected(target.GetType(), target, methodName, args);

        private static object InvokeReflected(Type type, object target, string methodName, params object[] args)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(candidate =>
                    candidate.Name == methodName &&
                    candidate.IsStatic == (target == null) &&
                    ParametersMatch(candidate, args))
                .ToArray();

            if (matches.Length == 0)
                throw new MissingMethodException(type.FullName, methodName);

            if (matches.Length > 1)
            {
                string matchingOverloads = string.Join("; ", matches.Select(FormatMethodSignature));
                throw new AmbiguousMatchException(
                    $"Ambiguous VRCFury API method match for {FormatMethodCall(type, methodName, args)}. Matching overloads: {matchingOverloads}.");
            }

            MethodInfo method = matches[0];
            return method.Invoke(target, args);
        }

        private static bool ParametersMatch(MethodInfo method, object[] args)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                    continue;

                Type expected = parameters[i].ParameterType;
                Type actual = args[i].GetType();
                if (!expected.IsAssignableFrom(actual))
                    return false;
            }

            return true;
        }

        private static string FormatMethodCall(Type type, string methodName, object[] args)
            => $"{type.FullName}.{methodName}({FormatArgumentTypes(args)})";

        private static string FormatArgumentTypes(object[] args)
            => string.Join(", ", args.Select(arg => arg == null ? "null" : arg.GetType().FullName));

        private static string FormatMethodSignature(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            string parameterTypes = string.Join(", ", parameters.Select(parameter => parameter.ParameterType.FullName));
            return $"{method.DeclaringType.FullName}.{method.Name}({parameterTypes})";
        }

        private static void CaptureVrcFuryComponents(
            IDictionary<GameObject, MonoBehaviour[]> snapshots,
            GameObject obj)
        {
            if (!snapshots.ContainsKey(obj))
                snapshots[obj] = GetVrcFuryComponents(obj);
        }

        private static MonoBehaviour[] GetNewVrcFuryComponents(GameObject obj, MonoBehaviour[] existingComponents)
        {
            var existingSet = new HashSet<MonoBehaviour>(existingComponents);
            return GetVrcFuryComponents(obj)
                .Where(component => !existingSet.Contains(component))
                .ToArray();
        }

        private static MonoBehaviour[] GetVrcFuryComponents(GameObject obj)
        {
            return obj.GetComponents<MonoBehaviour>()
                .Where(component => component != null && component.GetType().FullName == VrcFuryComponentTypeName)
                .ToArray();
        }

        /// <summary>
        /// Destroys through Undo so that replacing a previous setup is reversible too - the
        /// components this removes were part of the user's avatar a moment ago.
        /// </summary>
        private static void RemoveComponents(IEnumerable<MonoBehaviour> components)
        {
            foreach (MonoBehaviour component in components.Where(component => component != null).ToArray())
                Undo.DestroyObjectImmediate(component);
        }
    }
}
#endif
