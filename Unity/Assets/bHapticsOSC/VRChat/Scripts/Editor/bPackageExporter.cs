#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    public static class bPackageExporter
    {
        private const string PackageRoot = "Assets/bHapticsOSC";
        private const string GeneratedRoot = "Assets/bHapticsOSC/VRChat/Generated";
        private const string ExporterScript = "Assets/bHapticsOSC/VRChat/Scripts/Editor/bPackageExporter.cs";
        private const string OutputArg = "-bHapticsExportPath";

        [MenuItem("bHapticsOSC/Export Unity Package")]
        public static void ExportMenu()
            => Export(DefaultOutputPath());

        public static void ExportFromCommandLine()
        {
            try
            {
                Export(GetCommandLineValue(OutputArg) ?? DefaultOutputPath());
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Export(string outputPath)
        {
            string absoluteOutputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            string[] assetPaths = CollectAssetPaths();
            if (assetPaths.Length == 0)
                throw new InvalidOperationException($"No exportable assets found under {PackageRoot}.");

            AssetDatabase.ExportPackage(assetPaths, absoluteOutputPath, ExportPackageOptions.Default);
            Debug.Log($"Exported {assetPaths.Length} bHapticsOSC assets to {absoluteOutputPath}");
        }

        private static string[] CollectAssetPaths()
        {
            var paths = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { PackageRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || IsExcluded(path))
                    continue;

                paths.Add(path);
            }

            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static bool IsExcluded(string path)
            => path.Equals(ExporterScript, StringComparison.Ordinal)
               || path.StartsWith($"{GeneratedRoot}/", StringComparison.Ordinal)
               || path.Equals(GeneratedRoot, StringComparison.Ordinal);

        private static string DefaultOutputPath()
            => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "dist", "bHapticsOSC-VRChat.unitypackage");

        private static string GetCommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
#endif
