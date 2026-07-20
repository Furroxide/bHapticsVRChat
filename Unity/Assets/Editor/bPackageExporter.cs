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
        private const string ExporterScript = "Assets/Editor/bPackageExporter.cs";
        private const string OutputArg = "-bHapticsExportPath";

        private static readonly IReadOnlyDictionary<string, string> LegacyFolderGuids =
            new Dictionary<string, string>
            {
                ["Assets/bHapticsOSC"] = "9b3e57fee7640ff468876b1eff944969",
                ["Assets/bHapticsOSC/VRChat"] = "aa20f348b2d0ed2438d3fc45ceb17fe6",
                ["Assets/bHapticsOSC/VRChat/Materials"] = "13f92bc2b3af777418356c43e176eb0d",
                ["Assets/bHapticsOSC/VRChat/Models"] = "0ea71aee00703a54098c3828d5467e1d",
                ["Assets/bHapticsOSC/VRChat/Prefabs"] = "d4be18ff8ac3b7440b79abe75706e198",
                ["Assets/bHapticsOSC/VRChat/Scripts"] = "e5ed1b6b981cfd24daba2d9156e2093c",
                ["Assets/bHapticsOSC/VRChat/Shaders"] = "04ab7b92a321da2428e8bf372e46fe6b",
                ["Assets/bHapticsOSC/VRChat/Textures"] = "34984ce1bee61fe4b85179972649bfaa"
            };

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
            if (!AssetDatabase.IsValidFolder(PackageRoot))
                throw new InvalidOperationException($"Legacy package root was not imported: {PackageRoot}.");

            // Folder assets carry the GUIDs used by VPM's legacyFolders migration.
            // Keep the root explicit because FindAssets does not guarantee that the
            // search root itself is returned.
            paths.Add(PackageRoot);
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { PackageRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || IsExcluded(path))
                    continue;

                paths.Add(path);
            }

            foreach (KeyValuePair<string, string> expected in LegacyFolderGuids)
            {
                string actualGuid = AssetDatabase.AssetPathToGUID(expected.Key);
                if (!actualGuid.Equals(expected.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Legacy folder GUID mismatch for {expected.Key}: expected {expected.Value}, got {actualGuid}.");
                }

                if (!paths.Contains(expected.Key))
                    throw new InvalidOperationException($"Legacy folder was omitted from the export: {expected.Key}.");
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
