#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace bHapticsOSC.VRChat
{
    public static class bImportChecker
    {
        private const string HasVrcFuryDefine = "bHapticsOSC_HasVrcFury";
        private const string HasContactCompressorDefine = "bHapticsOSC_HasContactCompressor";

        private static readonly string[] DeprecatedDefines =
        {
            "bHapticsOSC_AacWarning",
            "bHapticsOSC_HasAac",
            "bHapticsOSC_VRCSDKWarning",
            "bHapticsOSC_VrcFuryWarning",
        };

        [InitializeOnLoadMethod]
        private static void RefreshAfterAssemblyLoad()
            => Refresh();

        public static void Refresh()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (buildTargetGroup == BuildTargetGroup.Unknown)
                return;

            string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup) ?? string.Empty;
            var definitions = current
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(definition => definition.Trim())
                .Where(definition => definition.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (string deprecatedDefine in DeprecatedDefines)
                definitions.RemoveAll(definition => definition == deprecatedDefine);

            definitions.RemoveAll(definition => definition == HasVrcFuryDefine);
            definitions.RemoveAll(definition => definition == HasContactCompressorDefine);
            // VPM and UPM installations receive this symbol from asmdef
            // versionDefines. Only the asmdef-free legacy export needs a
            // project-wide scripting define fallback.
            if (!IsCanonicalPackageAssembly())
            {
                if (HasType("com.vrcfury.api", "com.vrcfury.api.FuryComponents"))
                    definitions.Add(HasVrcFuryDefine);
                if (HasType("Furroxide.ContactCompressor", "Furroxide.ContactCompressor.ContactCompressorGroup"))
                    definitions.Add(HasContactCompressorDefine);
            }

            string updated = string.Join(";", definitions);
            if (!string.Equals(current, updated, StringComparison.Ordinal))
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, updated);
        }

        private static bool IsCanonicalPackageAssembly()
        {
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(bImportChecker).Assembly);
                return package != null && package.name == bCompanionRequirements.PackageId;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasType(string assemblyName, string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType(typeName) != null)
                    return true;
            }

            try
            {
                return Assembly.Load(assemblyName).GetType(typeName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
