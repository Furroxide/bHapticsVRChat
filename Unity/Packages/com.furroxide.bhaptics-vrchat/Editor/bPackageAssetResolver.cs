#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    internal static class bPackageAssetResolver
    {
        internal const string AnchorAssetGuid = "ea7a05231eb084e4fb40976385b26f2b";
        private const string AnchorFileName = "ParameterExclusions.txt";
        private const string VpmContentRoot = "Packages/com.furroxide.bhaptics-vrchat/Runtime";
        private const string LegacyContentRoot = "Assets/bHapticsOSC/VRChat";

        private static string contentRoot;

        internal static string ContentRoot => contentRoot ??= ResolveContentRoot();

        internal static string GetAssetPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return ContentRoot;

            return $"{ContentRoot}/{relativePath.Replace('\\', '/').Trim('/')}";
        }

        internal static T LoadAsset<T>(string relativePath) where T : Object
            => AssetDatabase.LoadAssetAtPath<T>(GetAssetPath(relativePath));

        private static string ResolveContentRoot()
        {
            string anchorPath = AssetDatabase.GUIDToAssetPath(AnchorAssetGuid);
            if (!string.IsNullOrWhiteSpace(anchorPath))
                return Normalize(Path.GetDirectoryName(anchorPath));

            if (AssetDatabase.LoadAssetAtPath<TextAsset>($"{VpmContentRoot}/{AnchorFileName}") != null)
                return VpmContentRoot;

            if (AssetDatabase.LoadAssetAtPath<TextAsset>($"{LegacyContentRoot}/{AnchorFileName}") != null)
                return LegacyContentRoot;

            return VpmContentRoot;
        }

        private static string Normalize(string path)
            => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }
}
#endif
