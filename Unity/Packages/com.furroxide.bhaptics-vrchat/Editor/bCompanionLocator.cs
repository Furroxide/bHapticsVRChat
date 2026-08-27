#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// Finds a bHapticsOSC executable on disk without making the user go hunting for it.
    /// The companion app is a portable single file, so it is wherever the browser put it -
    /// usually Downloads. The scan is deliberately bounded: it exists to save a file dialog,
    /// not to index the machine.
    /// </summary>
    internal static class bCompanionLocator
    {
        /// <summary>
        /// Depth for the broad, low-signal roots (Program Files, the user profile, AppData). These
        /// are enormous and the app is not usually buried in them.
        /// </summary>
        private const int ShallowSearchDepth = 2;

        /// <summary>
        /// Depth for the few roots where the app really does live several folders down: Downloads
        /// and Desktop, where a release zip unpacks into its own folder, and the project folder,
        /// where a build lands under something like External/bHapticsOSC/Output/Release. Depth 2
        /// missed that entirely and ranked an older copy instead, so the window reported an update
        /// the user did not need and offered to download a build they already had.
        /// </summary>
        private const int DeepSearchDepth = 4;

        /// <summary>
        /// Per root, not for the whole sweep. One hoarder's Downloads folder used to exhaust the
        /// shared budget before the later roots were looked at at all, which made the deliberate
        /// most-likely-first ordering of the roots meaningless.
        /// </summary>
        private const int MaxDirectoriesPerRoot = 1200;

        private const int SearchBudgetMilliseconds = 4000;

        /// <summary>
        /// Folders that never contain a downloaded application but do contain tens of thousands of
        /// directories. Skipping them is what makes the deeper search affordable.
        /// </summary>
        private static readonly string[] SkippedDirectoryNames =
        {
            "Library", "Temp", "obj", "bin~", "node_modules", ".git", "PackageCache", "Logs",
        };

        /// <summary>
        /// Repainting the progress bar costs more than reading a directory does, so it is only
        /// refreshed periodically. Cancellation is only observed on those same ticks.
        /// </summary>
        private const int ProgressUpdateInterval = 8;

        internal readonly struct bLocatorResult
        {
            internal bLocatorResult(string executablePath, bool cancelled)
            {
                ExecutablePath = executablePath ?? string.Empty;
                Cancelled = cancelled;
            }

            /// <summary>Best candidate found, or empty. The caller re-inspects it for its verdict.</summary>
            internal string ExecutablePath { get; }

            internal bool Cancelled { get; }
            internal bool Found => !string.IsNullOrEmpty(ExecutablePath);
        }

        /// <summary>
        /// Scans the handful of places a downloaded portable app realistically lives and
        /// returns the best candidate found. Prefers a supported, current build; falls back to
        /// an outdated or upstream one so the caller can explain what is actually installed
        /// rather than claiming nothing is.
        /// </summary>
        internal static bLocatorResult Locate()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return new bLocatorResult(null, false);

            string requiredVersion = bCompanionRequirements.RequiredVersion;
            var stopwatch = Stopwatch.StartNew();
            bool cancelled = false;

            string bestPath = null;
            bCompanionStatus bestStatus = bCompanionStatus.NotLocated;
            Version bestVersion = null;
            int scanned = 0;

            try
            {
                foreach (SearchRoot root in EnumerateSearchRoots())
                {
                    if (cancelled || stopwatch.ElapsedMilliseconds > SearchBudgetMilliseconds)
                        break;

                    foreach (string directory in EnumerateDirectories(root, stopwatch))
                    {
                        if (scanned++ % ProgressUpdateInterval == 0
                            && EditorUtility.DisplayCancelableProgressBar(
                                "bHapticsOSC",
                                $"Searching for the companion app in {Shorten(directory)}...",
                                Mathf.Clamp01(stopwatch.ElapsedMilliseconds / (float)SearchBudgetMilliseconds)))
                        {
                            cancelled = true;
                            break;
                        }

                        foreach (string file in EnumerateExecutables(directory))
                        {
                            bCompanionStatusResult inspected =
                                bCompanionStatusDetector.InspectExecutable(file, false, requiredVersion);

                            if (!IsBetter(inspected, bestStatus, bestVersion))
                                continue;

                            bestPath = file;
                            bestStatus = inspected.Status;
                            bestVersion = ParseOrNull(inspected.DetectedVersion);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return new bLocatorResult(bestPath, cancelled);
        }

        /// <summary>
        /// Ranks one candidate against the best so far. A supported current build always wins;
        /// among equals the higher version wins; an upstream build is only kept when nothing
        /// better has been seen.
        /// </summary>
        private static bool IsBetter(bCompanionStatusResult candidate, bCompanionStatus bestStatus, Version bestVersion)
        {
            int candidateRank = Rank(candidate.Status);
            if (candidateRank == 0)
                return false;

            int bestRank = Rank(bestStatus);
            if (candidateRank != bestRank)
                return candidateRank > bestRank;

            Version candidateVersion = ParseOrNull(candidate.DetectedVersion);
            if (candidateVersion == null)
                return false;
            if (bestVersion == null)
                return true;

            return candidateVersion > bestVersion;
        }

        private static int Rank(bCompanionStatus status)
        {
            switch (status)
            {
                case bCompanionStatus.ReadyStopped:
                case bCompanionStatus.ReadyRunning:
                    return 4;
                case bCompanionStatus.Outdated:
                    return 3;
                case bCompanionStatus.ForeignBuild:
                    return 2;
                case bCompanionStatus.UnknownVersion:
                    return 1;
                default:
                    return 0;
            }
        }

        private static Version ParseOrNull(string version)
            => bCompanionStatusDetector.TryNormalizeVersion(version, out Version parsed, out _) ? parsed : null;

        /// <summary>
        /// The places a portable download realistically ends up. User folders are checked on
        /// every fixed drive because Windows lets Downloads and Desktop be relocated off the
        /// system drive, and the .NET SpecialFolder enum has no entry for Downloads at all.
        /// </summary>
        /// <summary>A place to look, and how far down it is worth looking there.</summary>
        private readonly struct SearchRoot
        {
            internal SearchRoot(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }

            internal string Path { get; }
            internal int Depth { get; }
        }

        private static IEnumerable<SearchRoot> EnumerateSearchRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SearchRoot root in CandidateRoots())
            {
                if (string.IsNullOrWhiteSpace(root.Path))
                    continue;

                string full;
                try
                {
                    full = Path.GetFullPath(root.Path);
                }
                catch
                {
                    continue;
                }

                if (!seen.Add(full) || !DirectoryExists(full))
                    continue;

                yield return new SearchRoot(full, root.Depth);
            }
        }

        /// <summary>
        /// Ordered cheapest and most likely first, because the whole scan shares one time and
        /// directory budget: whatever comes last only gets whatever is left over.
        /// </summary>
        private static IEnumerable<SearchRoot> CandidateRoots()
        {
            // A running companion already said exactly where it lives. Reading that one directory
            // beats any amount of searching, so it goes first and needs no depth at all.
            foreach (bCompanionProcessSnapshot snapshot in SafeRunningProcesses())
            {
                if (snapshot.PathReadable)
                    yield return new SearchRoot(SafeGetDirectoryName(snapshot.ExecutablePath), 0);
            }

            string remembered = bCompanionStatusDetector.RememberedExecutablePath;
            if (!string.IsNullOrWhiteSpace(remembered))
                yield return new SearchRoot(SafeGetDirectoryName(remembered), 1);

            // Where the one-click install puts it, so a previous install is found without a search.
            yield return new SearchRoot(bCompanionInstaller.InstallDirectory, 0);

            // Deep: a release archive unpacks into a folder of its own here.
            foreach (string folder in DownloadAndDesktopFolders())
                yield return new SearchRoot(folder, DeepSearchDepth);

            // Deep: a build from source lands well below the project. This repository's own build
            // output is four levels down, which the old depth of two never reached - so the window
            // ranked an older copy instead and offered to download a build the user already had.
            string projectRoot = SafeGetDirectoryName(Application.dataPath);
            yield return new SearchRoot(projectRoot, DeepSearchDepth);
            yield return new SearchRoot(SafeGetDirectoryName(projectRoot), DeepSearchDepth);

            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.MyDocuments), ShallowSearchDepth);
            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.ProgramFiles), ShallowSearchDepth);
            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.ProgramFilesX86), ShallowSearchDepth);
            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.UserProfile), ShallowSearchDepth);
            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShallowSearchDepth);
            yield return new SearchRoot(SafeGetFolderPath(Environment.SpecialFolder.ApplicationData), ShallowSearchDepth);
        }

        /// <summary>The running companion processes, or none if the sweep fails.</summary>
        private static IReadOnlyList<bCompanionProcessSnapshot> SafeRunningProcesses()
        {
            try
            {
                return bCompanionStatusDetector.RunningProcessProvider() ?? EmptySnapshots;
            }
            catch
            {
                return EmptySnapshots;
            }
        }

        private static readonly bCompanionProcessSnapshot[] EmptySnapshots = new bCompanionProcessSnapshot[0];

        private static IEnumerable<string> DownloadAndDesktopFolders()
        {
            string userProfile = SafeGetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                yield return SafeCombine(userProfile, "Downloads");

            yield return SafeGetFolderPath(Environment.SpecialFolder.Desktop);

            // Windows lets Downloads and Desktop be relocated to another drive, and the .NET
            // SpecialFolder enum has no entry for Downloads at all, so the per-drive user
            // folders are probed directly. Missing ones cost a single Directory.Exists each.
            string userName = SafeUserName();
            if (string.IsNullOrWhiteSpace(userName))
                yield break;

            foreach (string driveRoot in FixedDriveRoots())
            {
                string userRoot = SafeCombine(SafeCombine(driveRoot, "Users"), userName);
                if (string.IsNullOrWhiteSpace(userRoot))
                    continue;

                yield return SafeCombine(userRoot, "Downloads");
                yield return SafeCombine(userRoot, "Desktop");
            }
        }

        private static IEnumerable<string> FixedDriveRoots()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                yield break;
            }

            foreach (DriveInfo drive in drives)
            {
                string root = null;
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                        root = drive.RootDirectory.FullName;
                }
                catch
                {
                    root = null;
                }

                if (!string.IsNullOrWhiteSpace(root))
                    yield return root;
            }
        }

        /// <summary>
        /// Breadth-first walk of one root, capped by that root's own depth, its own directory
        /// allowance, and the sweep's shared clock.
        /// </summary>
        private static IEnumerable<string> EnumerateDirectories(SearchRoot root, Stopwatch stopwatch)
        {
            var results = new List<string>();
            var frontier = new Queue<KeyValuePair<string, int>>();
            frontier.Enqueue(new KeyValuePair<string, int>(root.Path, 0));
            int visited = 0;

            while (frontier.Count > 0)
            {
                if (visited >= MaxDirectoriesPerRoot || stopwatch.ElapsedMilliseconds > SearchBudgetMilliseconds)
                    break;

                KeyValuePair<string, int> current = frontier.Dequeue();
                visited++;
                results.Add(current.Key);

                if (current.Value >= root.Depth)
                    continue;

                foreach (string child in SafeEnumerateDirectories(current.Key))
                {
                    if (IsSkipped(child))
                        continue;

                    frontier.Enqueue(new KeyValuePair<string, int>(child, current.Value + 1));
                }
            }

            return results;
        }

        /// <summary>
        /// True for folders that hold no downloaded application but enormous numbers of
        /// directories. Skipping them is what pays for searching the useful roots more deeply.
        /// </summary>
        private static bool IsSkipped(string directory)
        {
            string name = SafeGetLeafName(directory);
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (string skipped in SkippedDirectoryNames)
            {
                if (string.Equals(name, skipped, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string SafeGetLeafName(string path)
        {
            try
            {
                return Path.GetFileName(
                    path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string directory)
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch
            {
                // Permission-denied, reparse points and vanished folders are all expected here.
                return Array.Empty<string>();
            }

            return children;
        }

        private static IEnumerable<string> EnumerateExecutables(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, bCompanionRequirements.ExecutableSearchPattern);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool DirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeGetFolderPath(Environment.SpecialFolder folder)
        {
            try
            {
                return Environment.GetFolderPath(folder);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeGetDirectoryName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeCombine(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return string.Empty;

            try
            {
                return Path.Combine(left, right);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeUserName()
        {
            try
            {
                return Environment.UserName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= 60)
                return path;

            return "..." + path.Substring(path.Length - 57);
        }
    }
}
#endif
