#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && bHapticsOSC_HasVrcFury
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace bHapticsOSC.VRChat
{
    public class bGeneratedAnimatorAssets
    {
        public AnimatorController Controller;
        public VRCExpressionParameters Parameters;
        public string FolderPath;
        public bool HasDeviceMeshToggle;
        public bool HasMotorMeshToggle;
        public bool HasPunchControls;
    }

    public static class bAnimator
    {
        public const string DeviceMeshToggleParameter = "bHapticsOSC/DeviceMeshes";
        public const string DeviceMeshToggleMenuPath = "bHapticsOSC/Device Meshes";
        public const string MotorMeshToggleParameter = "bHapticsOSC/MotorMeshes";
        public const string MotorMeshToggleMenuPath = "bHapticsOSC/Motor Meshes";

        public static bGeneratedAnimatorAssets CreateGeneratedAssets(bHapticsOSCIntegration editorComp)
        {
            string folderPath = PrepareGeneratedFolder(editorComp);
            string controllerPath = $"{folderPath}/{bHapticsOSCIntegration.SystemName}.controller";

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var expressionParameters = new Dictionary<string, VRCExpressionParameters.ValueType>();

            AddContactParameters(editorComp, controller, expressionParameters);

            foreach (KeyValuePair<bDeviceType, bDeviceTemplate> pair in bDevice.AllTemplates)
            {
                if (pair.Value.NodeCount <= 0)
                    continue;

                bUserSettings userSettings = null;
                if (pair.Key is bDeviceType.VEST_FRONT or bDeviceType.VEST_BACK)
                {
                    userSettings = editorComp.AllUserSettings[bDevice.AllTemplates[bDeviceType.VEST]];
                }
                else
                {
                    userSettings = editorComp.AllUserSettings[pair.Value];
                }

                if (userSettings.CurrentPrefab == null)
                    continue;

                for (int node = 0; node < pair.Value.NodeCount; node++)
                {
                    string nodeName = $"{bHapticsOSCIntegration.SystemName}/{pair.Value.Name.Replace(" ", "")}/{node}";
                    CreateAnimatorLayerStates(node, nodeName, controller, editorComp, pair, folderPath, expressionParameters);
                }
            }

            bool hasDeviceMeshToggle = CreateMeshToggleLayer(
                controller,
                editorComp,
                folderPath,
                DeviceMeshToggleParameter,
                "bHapticsOSC/Device Meshes",
                "DeviceMeshes",
                includeMotors: false);
            bool hasMotorMeshToggle = CreateMeshToggleLayer(
                controller,
                editorComp,
                folderPath,
                MotorMeshToggleParameter,
                "bHapticsOSC/Motor Meshes",
                "MotorMeshes",
                includeMotors: true);
            VRCExpressionParameters parameters = CreateExpressionParameters(folderPath, expressionParameters);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new bGeneratedAnimatorAssets
            {
                Controller = controller,
                Parameters = parameters,
                FolderPath = folderPath,
                HasDeviceMeshToggle = hasDeviceMeshToggle,
                HasMotorMeshToggle = hasMotorMeshToggle,
                HasPunchControls = bPunch.HasReceivers(editorComp)
            };
        }

        private static bool CreateMeshToggleLayer(
            AnimatorController controller,
            bHapticsOSCIntegration editorComp,
            string folderPath,
            string parameterName,
            string layerName,
            string clipPrefix,
            bool includeMotors)
        {
            Renderer[] meshRenderers = FindDeviceMeshRenderers(editorComp, includeMotors);
            if (meshRenderers.Length <= 0)
                return false;

            AddBoolParameter(controller, parameterName, true);

            controller.AddLayer(layerName);

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;

            AnimationClip visibleClip = CreateRendererEnabledClip($"{folderPath}/{clipPrefix}_Visible.anim", meshRenderers, editorComp.GetOrCreateVrcFuryRoot(), true);
            AnimationClip hiddenClip = CreateRendererEnabledClip($"{folderPath}/{clipPrefix}_Hidden.anim", meshRenderers, editorComp.GetOrCreateVrcFuryRoot(), false);

            AnimatorState visibleState = layer.stateMachine.AddState("Visible", new Vector3(0, 0, 0));
            visibleState.writeDefaultValues = true;
            visibleState.motion = visibleClip;
            layer.stateMachine.defaultState = visibleState;

            AnimatorState hiddenState = layer.stateMachine.AddState("Hidden", new Vector3(250, 0, 0));
            hiddenState.writeDefaultValues = true;
            hiddenState.motion = hiddenClip;

            AnimatorStateTransition hideTransition = visibleState.AddTransition(hiddenState);
            hideTransition.duration = 0f;
            hideTransition.hasExitTime = false;
            hideTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            AnimatorStateTransition showTransition = hiddenState.AddTransition(visibleState);
            showTransition.duration = 0f;
            showTransition.hasExitTime = false;
            showTransition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            return true;
        }

        private static Renderer[] FindDeviceMeshRenderers(bHapticsOSCIntegration editorComp, bool includeMotors)
        {
            Transform stagingRoot = editorComp.GetOrCreateVrcFuryRoot();
            var renderers = new List<Renderer>();
            foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
            {
                if (settings.CurrentPrefab == null)
                    continue;

                foreach (Renderer renderer in settings.CurrentPrefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;

                    bool isMotorMesh = IsMotorRenderer(renderer) || IsTouchViewRenderer(renderer);
                    if (isMotorMesh != includeMotors)
                        continue;

                    renderers.Add(renderer);
                }
            }

            return renderers
                .Distinct()
                .OrderBy(renderer => AnimationUtility.CalculateTransformPath(renderer.transform, stagingRoot), System.StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsMotorRenderer(Renderer renderer)
        {
            Transform current = renderer.transform;
            while (current != null)
            {
                if (current.name.IndexOf("Motor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static bool IsTouchViewRenderer(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length <= 0)
                return false;

            return materials.Any(material => material != null && material.HasProperty("_Device"));
        }

        private static AnimationClip CreateRendererEnabledClip(string path, Renderer[] renderers, Transform root, bool enabled)
        {
            var clip = new AnimationClip
            {
                frameRate = 60f
            };

            float value = enabled ? 1f : 0f;
            foreach (Renderer renderer in renderers)
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root);
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(rendererPath, renderer.GetType(), "m_Enabled");
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
            }

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static void CreateAnimatorLayerStates(
            int node,
            string nodeName,
            AnimatorController controller,
            bHapticsOSCIntegration editorComp,
            KeyValuePair<bDeviceType, bDeviceTemplate> keyValuePair,
            string folderPath,
            IDictionary<string, VRCExpressionParameters.ValueType> expressionParameters)
        {
            string parameter = ConvertParameterAsBhaptics(nodeName);
            string selfParam = $"{parameter}/self";
            string othersParam = $"{parameter}/others";

            float shaderDeviceIndex = bDevice.GetShaderIndex(keyValuePair.Key, node);
            Renderer[] renderers = bShader.FindRenderersFromIndex(shaderDeviceIndex, editorComp.GetOrCreateVrcFuryRoot().gameObject);

            if (renderers == null || renderers.Length <= 0)
                return;

            AddBoolParameter(controller, selfParam);
            AddBoolParameter(controller, othersParam);
            AddExpressionParameter(expressionParameters, selfParam, VRCExpressionParameters.ValueType.Bool);
            AddExpressionParameter(expressionParameters, othersParam, VRCExpressionParameters.ValueType.Bool);

            string layerName = $"{keyValuePair.Value.Name.Replace(" ", "/")}/{node}";
            controller.AddLayer(layerName);

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;

            int shaderNode = keyValuePair.Key == bDeviceType.VEST_BACK ? node / 4 * 8 - node + 4 : node + 1;
            string clipPrefix = SanitizeFileName(layerName);

            AnimationClip falseClip = CreateMaterialClip($"{folderPath}/{clipPrefix}_False.anim", renderers, editorComp.GetOrCreateVrcFuryRoot(), shaderNode, 0f);
            AnimationClip trueClip = CreateMaterialClip($"{folderPath}/{clipPrefix}_True.anim", renderers, editorComp.GetOrCreateVrcFuryRoot(), shaderNode, 1f);

            AnimatorState falseState = layer.stateMachine.AddState("False", new Vector3(0, 0, 0));
            falseState.writeDefaultValues = true;
            falseState.motion = falseClip;

            AnimatorState trueState = layer.stateMachine.AddState("True", new Vector3(250, 0, 0));
            trueState.writeDefaultValues = true;
            trueState.motion = trueClip;

            AnimatorTransition falseTransition = layer.stateMachine.AddEntryTransition(falseState);
            falseTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, selfParam);
            falseTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, othersParam);

            AnimatorTransition trueSelfTransition = layer.stateMachine.AddEntryTransition(trueState);
            trueSelfTransition.AddCondition(AnimatorConditionMode.If, 0f, selfParam);

            AnimatorTransition trueOthersTransition = layer.stateMachine.AddEntryTransition(trueState);
            trueOthersTransition.AddCondition(AnimatorConditionMode.If, 0f, othersParam);

            AddExitTransition(falseState);
            AddExitTransition(trueState);
        }

        private static AnimationClip CreateMaterialClip(string path, Renderer[] renderers, Transform root, int shaderNode, float value)
        {
            var clip = new AnimationClip
            {
                frameRate = 60f
            };

            foreach (Renderer renderer in renderers)
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root);
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(rendererPath, typeof(Renderer), $"material._Node{shaderNode}");
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
            }

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static void AddExitTransition(AnimatorState state)
        {
            AnimatorStateTransition exitTransition = state.AddExitTransition();
            exitTransition.duration = 0f;
            exitTransition.exitTime = 1f;
            exitTransition.hasExitTime = true;
        }

        private static void AddContactParameters(
            bHapticsOSCIntegration editorComp,
            AnimatorController controller,
            IDictionary<string, VRCExpressionParameters.ValueType> expressionParameters)
        {
            foreach (bUserSettings settings in editorComp.AllUserSettings.Values)
            {
                if (settings.CurrentPrefab == null)
                    continue;

                foreach (ContactReceiver contactReceiver in settings.CurrentPrefab.GetComponentsInChildren<ContactReceiver>(true))
                {
                    if (string.IsNullOrWhiteSpace(contactReceiver.parameter))
                        continue;

                    AddBoolParameter(controller, contactReceiver.parameter);
                    AddExpressionParameter(expressionParameters, contactReceiver.parameter, VRCExpressionParameters.ValueType.Bool);
                }
            }
        }

        private static void AddBoolParameter(AnimatorController controller, string name, bool defaultValue = false)
        {
            if (controller.parameters.Any(parameter => parameter.name == name))
                return;

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = defaultValue
            });
        }

        private static void AddExpressionParameter(
            IDictionary<string, VRCExpressionParameters.ValueType> expressionParameters,
            string name,
            VRCExpressionParameters.ValueType valueType)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            expressionParameters[name] = valueType;
        }

        private static VRCExpressionParameters CreateExpressionParameters(
            string folderPath,
            IReadOnlyDictionary<string, VRCExpressionParameters.ValueType> parameterTypes)
        {
            var expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressionParameters.parameters = parameterTypes
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => new VRCExpressionParameters.Parameter
                {
                    name = pair.Key,
                    valueType = pair.Value,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false
                })
                .ToArray();

            AssetDatabase.CreateAsset(expressionParameters, $"{folderPath}/{bHapticsOSCIntegration.SystemName}.parameters.asset");
            return expressionParameters;
        }

        private static string PrepareGeneratedFolder(bHapticsOSCIntegration editorComp)
        {
            EnsureFolder("Assets/bHapticsOSC/VRChat", "Generated");

            string folderName = SanitizeFileName($"{editorComp.gameObject.name}_{editorComp.assetKey}");
            string folderPath = $"{bHapticsOSCIntegration.GeneratedAssetsRoot}/{folderName}";
            if (AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.DeleteAsset(folderPath);

            AssetDatabase.CreateFolder(bHapticsOSCIntegration.GeneratedAssetsRoot, Path.GetFileName(folderPath));
            return folderPath;
        }

        private static void EnsureFolder(string parent, string folderName)
        {
            string path = $"{parent}/{folderName}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, folderName);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "GeneratedAssets";

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidChar, '_');

            return value.Replace('/', '_').Replace('\\', '_');
        }

        private static string ConvertParameterAsBhaptics(string parameter)
        {
            parameter = parameter.Replace(bHapticsOSCIntegration.SystemName, "bOSC/v2");
            if (parameter.Contains("ArmLeft"))
            {
                parameter = parameter.Replace("ArmLeft", "ForearmL");
            }
            else if (parameter.Contains("ArmRight"))
            {
                parameter = parameter.Replace("ArmRight", "ForearmR");
            }
            else if (parameter.Contains("FootLeft"))
            {
                parameter = parameter.Replace("FootLeft", "FootL");
            }
            else if (parameter.Contains("FootRight"))
            {
                parameter = parameter.Replace("FootRight", "FootR");
            }
            else if (parameter.Contains("HandLeft"))
            {
                parameter = parameter.Replace("HandLeft", "HandL");
            }
            else if (parameter.Contains("HandRight"))
            {
                parameter = parameter.Replace("HandRight", "HandR");
            }

            return parameter;
        }
    }
}
#endif
