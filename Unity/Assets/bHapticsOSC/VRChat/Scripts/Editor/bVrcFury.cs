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
            MethodInfo method = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == methodName && ParametersMatch(candidate, args));

            if (method == null)
                throw new MissingMethodException(type.FullName, methodName);

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

        private static void RemoveComponents(IEnumerable<MonoBehaviour> components)
        {
            foreach (MonoBehaviour component in components.Where(component => component != null).ToArray())
                UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
#endif
