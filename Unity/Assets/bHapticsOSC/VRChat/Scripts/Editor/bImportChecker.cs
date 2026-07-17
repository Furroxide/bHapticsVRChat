#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    [InitializeOnLoad]
    public class bImportChecker : Editor
    {
        private static string LegacyHasDependencyDefine => "bHapticsOSC_Has" + "A" + "ac";
        private static string LegacyWarningDefine => "bHapticsOSC_" + "A" + "acWarning";

        static bImportChecker() =>
            Refresh();

        [InitializeOnLoadMethod]

        public static void Refresh()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string definitionsStr = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            List<string> definitionsTbl = string.IsNullOrEmpty(definitionsStr)
                ? new List<string>()
                : definitionsStr.Split(';').ToList();
            bool shouldApplyDefinitions = false;

            VRCSDKCheck(ref definitionsTbl, ref shouldApplyDefinitions);
            VrcFuryCheck(ref definitionsTbl, ref shouldApplyDefinitions);

            if (shouldApplyDefinitions)
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", definitionsTbl.ToArray()));
        }

        private static void VRCSDKCheck(ref List<string> definitionsTbl, ref bool shouldApplyDefinitions)
        {
            if (definitionsTbl.Contains("VRC_SDK_VRCSDK3"))
            {
                if (definitionsTbl.Contains("bHapticsOSC_VRCSDKWarning"))
                {
                    definitionsTbl.Remove("bHapticsOSC_VRCSDKWarning");
                    shouldApplyDefinitions = true;
                }
            }
            else
            {
                if (!definitionsTbl.Contains("bHapticsOSC_VRCSDKWarning"))
                {
                    definitionsTbl.Add("bHapticsOSC_VRCSDKWarning");
                    shouldApplyDefinitions = true;
                    VRCSDKWarning();
                }
            }
        }

#if !VRC_SDK_VRCSDK3
        [MenuItem("bHapticsOSC/VRChat SDK 3.0 is Required!")]
#endif
        private static void VRCSDKWarning()
        {
            Debug.LogError("bHapticsOSC requires VRChat SDK 3.0!");
            EditorUtility.DisplayDialog("bHapticsOSC", "bHapticsOSC requires VRChat SDK 3.0!\nPlease import it.", "OK");
            Application.OpenURL("https://docs.vrchat.com/docs/setting-up-the-sdk#step-2---importing-the-sdk");
        }

        private static void VrcFuryCheck(ref List<string> definitionsTbl, ref bool shouldApplyDefinitions)
        {
            if (definitionsTbl.Remove(LegacyWarningDefine))
                shouldApplyDefinitions = true;
            if (definitionsTbl.Remove(LegacyHasDependencyDefine))
                shouldApplyDefinitions = true;

            if (HasVrcFury())
            {
                if (!definitionsTbl.Contains("bHapticsOSC_HasVrcFury"))
                {
                    definitionsTbl.Add("bHapticsOSC_HasVrcFury");
                    shouldApplyDefinitions = true;
                }

                if (definitionsTbl.Contains("bHapticsOSC_VrcFuryWarning"))
                {
                    definitionsTbl.Remove("bHapticsOSC_VrcFuryWarning");
                    shouldApplyDefinitions = true;
                }
            }
            else
            {
                if (definitionsTbl.Contains("bHapticsOSC_HasVrcFury"))
                {
                    definitionsTbl.Remove("bHapticsOSC_HasVrcFury");
                    shouldApplyDefinitions = true;
                }

                if (!definitionsTbl.Contains("bHapticsOSC_VrcFuryWarning"))
                {
                    definitionsTbl.Add("bHapticsOSC_VrcFuryWarning");
                    shouldApplyDefinitions = true;
                    VrcFuryWarning();
                }
            }
        }

#if !bHapticsOSC_HasVrcFury
        [MenuItem("bHapticsOSC/VRCFury is Required!")]
#endif
        private static void VrcFuryWarning()
        {
            Debug.LogError("bHapticsOSC requires VRCFury!");
            EditorUtility.DisplayDialog("bHapticsOSC", "bHapticsOSC requires VRCFury!\nPlease install it through VCC.", "OK");
            Application.OpenURL("https://vrcfury.com/download/");
        }

        private static bool HasVrcFury()
        {
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("com.vrcfury.api.FuryComponents") != null)
                    return true;
            }

            try
            {
                return Assembly.Load("com.vrcfury.api").GetType("com.vrcfury.api.FuryComponents") != null;
            }
            catch { }
            return false;
        }
    }
}
#endif
