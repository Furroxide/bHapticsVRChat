#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace bHapticsOSC.VRChat
{
    public static class bCompanionRequirements
    {
        public const string FallbackRequiredVersion = "2.3.1";
        public const string PackageId = "com.furroxide.bhaptics-vrchat";
        public const string VrchatAvatarsPackageId = "com.vrchat.avatars";
        public const string VrcFuryPackageId = "com.vrcfury.vrcfury";
        public const string MinimumVrcFuryVersion = "1.1341.0";
        public const string MaximumVrcFuryVersion = "2.0.0";

        /// <summary>
        /// The Win32 ProductName this fork stamps into its executable. The release workflow
        /// refuses to publish a build whose ProductName differs, so an exact match is the
        /// fork's identity.
        /// </summary>
        public const string ProductName = "bHapticsOSC";

        /// <summary>
        /// Every bHapticsOSC build carries this marker somewhere in its version resource -
        /// this fork's ("bHapticsOSC" / "Lava Gang") and the ones bHaptics publish upstream
        /// ("bHaptics OSC for VRChat" / "bHaptics"). It separates "a bHapticsOSC build we do
        /// not support" from "some unrelated executable the user picked by mistake".
        /// </summary>
        internal const string ProductFamilyMarker = "bhaptics";

        /// <summary>
        /// Upstream publishes its releases as bHapticsOSC_v2.2.1.exe and older builds as
        /// "bHapticsOSC v1.1.4.exe", so the running process is named after whichever file the
        /// user downloaded. Match the process name as a prefix; the executable's version
        /// resource - not its filename - decides whether the build is actually supported.
        /// </summary>
        public const string ProcessNamePrefix = "bHapticsOSC";

        public const string ExecutableName = "bHapticsOSC.exe";
        public const string ExecutableSearchPattern = "bHapticsOSC*.exe";
        public const string LatestReleaseUrl = "https://github.com/furroxide/bHapticsVRChat/releases/latest";
        public const string LatestDownloadUrl = "https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe";
        public const string ReleasesUrl = "https://github.com/furroxide/bHapticsVRChat/releases";
        public const string AvatarGuideUrl = "https://bhaptics.notion.site/How-to-play-VRChat-with-bHaptics-1226d5724b8b80229ab9e0001ab70b61";
        public const string BHapticsPlayerUrl = "https://www.bhaptics.com/support/downloads";
        public const string VrchatOscGuideUrl = "https://docs.vrchat.com/docs/osc-overview";
        public const string RepositoryUrl = "https://github.com/furroxide/bHapticsVRChat";

        public static string RequiredVersion
        {
            get
            {
                try
                {
                    PackageInfo package = PackageInfo.FindForAssembly(typeof(bCompanionRequirements).Assembly);
                    if (package != null
                        && package.name == PackageId
                        && bCompanionStatusDetector.TryNormalizeVersion(package.version, out _, out string packageVersion))
                    {
                        return packageVersion;
                    }
                }
                catch
                {
                    // A legacy .unitypackage is not associated with a PackageInfo entry.
                }

                return FallbackRequiredVersion;
            }
        }

        public static string GetMatchingDownloadUrl(string version = null)
        {
            string candidate = string.IsNullOrWhiteSpace(version) ? RequiredVersion : version;
            if (!bCompanionStatusDetector.TryNormalizeVersion(candidate, out _, out string normalized))
                normalized = FallbackRequiredVersion;

            return $"{ReleasesUrl}/download/v{normalized}/{ExecutableName}";
        }
    }

    internal enum bCompanionStatus
    {
        UnsupportedPlatform,
        NotLocated,
        MissingPath,
        InvalidProduct,
        UnknownVersion,
        Outdated,
        ReadyStopped,
        ReadyRunning,

        /// <summary>
        /// A real bHapticsOSC build that is not this fork - in practice the official bHaptics
        /// release. It speaks the same OSC protocol but does not understand the compressed
        /// contact parameters this package generates, so it has to be replaced rather than
        /// merely updated.
        /// </summary>
        ForeignBuild,

        /// <summary>
        /// A bHapticsOSC process is running, but Windows refused to tell us which file it came
        /// from - it runs at a higher integrity level than the editor, or it exited mid-scan.
        /// Saying "version unknown" here would be a verdict on the app; this is a verdict on
        /// our own visibility, and the two need different advice.
        /// </summary>
        RunningUninspectable,
    }

    /// <summary>Which bHapticsOSC build family an executable belongs to.</summary>
    internal enum bCompanionBuildLineage
    {
        /// <summary>Not a bHapticsOSC build at all.</summary>
        Unrelated,

        /// <summary>This maintained fork.</summary>
        Supported,

        /// <summary>A bHapticsOSC build published by somebody else, i.e. upstream bHaptics.</summary>
        Foreign,
    }

    internal readonly struct bCompanionStatusResult
    {
        internal bCompanionStatusResult(
            bCompanionStatus status,
            string requiredVersion,
            string executablePath = null,
            string detectedVersion = null,
            string detectedProductName = null,
            bool isRunning = false,
            bCompanionBuildLineage lineage = bCompanionBuildLineage.Unrelated,
            string conflictingProcessName = null,
            string detectedProcessName = null)
        {
            Status = status;
            RequiredVersion = requiredVersion;
            ExecutablePath = executablePath ?? string.Empty;
            DetectedVersion = detectedVersion ?? string.Empty;
            DetectedProductName = detectedProductName ?? string.Empty;
            IsRunning = isRunning;
            Lineage = lineage;
            ConflictingProcessName = conflictingProcessName ?? string.Empty;
            DetectedProcessName = detectedProcessName ?? string.Empty;
        }

        internal bCompanionStatus Status { get; }
        internal string RequiredVersion { get; }
        internal string ExecutablePath { get; }
        internal string DetectedVersion { get; }
        internal string DetectedProductName { get; }
        internal bool IsRunning { get; }
        internal bCompanionBuildLineage Lineage { get; }

        /// <summary>
        /// Set when a second, unsupported bHapticsOSC build is running alongside the one being
        /// reported. Both bind the same VRChat OSC port, so only one of them receives anything.
        /// </summary>
        internal string ConflictingProcessName { get; }

        /// <summary>The running process this result describes, when it is known by name only.</summary>
        internal string DetectedProcessName { get; }

        internal bool HasConflictingProcess => !string.IsNullOrEmpty(ConflictingProcessName);
        internal bool IsReady => Status == bCompanionStatus.ReadyStopped || Status == bCompanionStatus.ReadyRunning;

        /// <summary>True when something is running that has to be stopped before the right build can work.</summary>
        internal bool HasUnsupportedProcessRunning
            => HasConflictingProcess || (IsRunning && !IsReady);

        internal bCompanionStatusResult WithConflictingProcess(string processName)
            => new bCompanionStatusResult(
                Status,
                RequiredVersion,
                ExecutablePath,
                DetectedVersion,
                DetectedProductName,
                IsRunning,
                Lineage,
                processName,
                DetectedProcessName);
    }

    /// <summary>
    /// What a single process sweep learned about one running companion. The path is empty when
    /// Windows refused to hand it over, which is a normal outcome rather than an error.
    /// </summary>
    internal readonly struct bCompanionProcessSnapshot
    {
        internal bCompanionProcessSnapshot(string processName, string executablePath)
        {
            ProcessName = processName ?? string.Empty;
            ExecutablePath = executablePath ?? string.Empty;
        }

        internal string ProcessName { get; }
        internal string ExecutablePath { get; }
        internal bool PathReadable => !string.IsNullOrEmpty(ExecutablePath);
    }

    internal static class bCompanionStatusDetector
    {
        internal const string RememberedPathPreferenceKey = bCompanionRequirements.PackageId + ".companion-path";
        private const double CacheLifetimeSeconds = 2.0d;
        private const int StopGracePeriodMilliseconds = 4000;

        private static bCompanionStatusResult cachedResult;
        private static double cachedAt;
        private static bool hasCachedResult;

        internal static string RememberedExecutablePath => EditorPrefs.GetString(RememberedPathPreferenceKey, string.Empty);

        internal static bCompanionStatusResult Detect(bool forceRefresh = false)
        {
            double now = EditorApplication.timeSinceStartup;

            // timeSinceStartup restarts at zero when the editor relaunches, which would
            // otherwise make a stale cache look fresh. Treat time moving backwards as a reset
            // and re-detect.
            if (!forceRefresh && hasCachedResult && now >= cachedAt && now - cachedAt < CacheLifetimeSeconds)
                return cachedResult;

            cachedResult = DetectUncached();
            cachedAt = now;
            hasCachedResult = true;
            return cachedResult;
        }

        internal static void SetRememberedExecutablePath(string path)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrEmpty(normalized))
                EditorPrefs.DeleteKey(RememberedPathPreferenceKey);
            else
                EditorPrefs.SetString(RememberedPathPreferenceKey, normalized);

            InvalidateCache();
        }

        internal static bCompanionStatusResult Evaluate(
            bool isWindows,
            string executablePath,
            bool isRunning,
            string productName,
            string fileVersion,
            string requiredVersion)
        {
            bool pathExists = !string.IsNullOrWhiteSpace(executablePath) && FileExists(executablePath);
            return Evaluate(isWindows, pathExists, executablePath, isRunning, productName, fileVersion, requiredVersion);
        }

        internal static bCompanionStatusResult Evaluate(
            bool isWindows,
            bool pathExists,
            string executablePath,
            bool isRunning,
            string productName,
            string fileVersion,
            string requiredVersion,
            string fileDescription = null,
            string companyName = null,
            string originalFilename = null)
        {
            string normalizedRequired = NormalizeRequiredVersion(requiredVersion);
            if (!isWindows)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.UnsupportedPlatform,
                    normalizedRequired,
                    executablePath,
                    isRunning: isRunning);
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.NotLocated,
                    normalizedRequired,
                    isRunning: isRunning);
            }

            if (!pathExists)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.MissingPath,
                    normalizedRequired,
                    executablePath,
                    isRunning: isRunning);
            }

            bCompanionBuildLineage lineage = ClassifyLineage(
                productName,
                fileDescription,
                companyName,
                originalFilename,
                executablePath);

            if (lineage == bCompanionBuildLineage.Unrelated)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.InvalidProduct,
                    normalizedRequired,
                    executablePath,
                    detectedProductName: productName,
                    isRunning: isRunning,
                    lineage: lineage);
            }

            bool hasVersion = TryNormalizeVersion(fileVersion, out _, out string normalizedDetected);

            // A foreign build is reported as foreign whatever its version says: upstream's
            // numbering is independent of this fork's, so "newer" there means nothing here.
            if (lineage == bCompanionBuildLineage.Foreign)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.ForeignBuild,
                    normalizedRequired,
                    executablePath,
                    hasVersion ? normalizedDetected : null,
                    productName,
                    isRunning,
                    lineage);
            }

            if (!hasVersion)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.UnknownVersion,
                    normalizedRequired,
                    executablePath,
                    detectedProductName: productName,
                    isRunning: isRunning,
                    lineage: lineage);
            }

            if (CompareVersions(normalizedDetected, normalizedRequired) < 0)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.Outdated,
                    normalizedRequired,
                    executablePath,
                    normalizedDetected,
                    productName,
                    isRunning,
                    lineage);
            }

            return new bCompanionStatusResult(
                isRunning ? bCompanionStatus.ReadyRunning : bCompanionStatus.ReadyStopped,
                normalizedRequired,
                executablePath,
                normalizedDetected,
                productName,
                isRunning,
                lineage);
        }

        /// <summary>
        /// Decides which bHapticsOSC build family an executable belongs to from its version
        /// resource. Filenames are ignored except as a last resort, because both this fork and
        /// upstream get renamed by whoever downloads them.
        /// </summary>
        internal static bCompanionBuildLineage ClassifyLineage(
            string productName,
            string fileDescription = null,
            string companyName = null,
            string originalFilename = null,
            string executablePath = null)
        {
            if (string.Equals(productName, bCompanionRequirements.ProductName, StringComparison.Ordinal))
                return bCompanionBuildLineage.Supported;

            if (ContainsFamilyMarker(productName)
                || ContainsFamilyMarker(fileDescription)
                || ContainsFamilyMarker(companyName)
                || ContainsFamilyMarker(originalFilename))
            {
                return bCompanionBuildLineage.Foreign;
            }

            // Resource-stripped rebuilds carry no usable metadata at all. Fall back to the
            // filename so they surface as "an unsupported bHapticsOSC build" rather than "you
            // picked the wrong file", which would send the user hunting for a file they have.
            if (string.IsNullOrWhiteSpace(productName)
                && string.IsNullOrWhiteSpace(fileDescription)
                && string.IsNullOrWhiteSpace(companyName)
                && string.IsNullOrWhiteSpace(originalFilename)
                && ContainsFamilyMarker(SafeGetFileName(executablePath)))
            {
                return bCompanionBuildLineage.Foreign;
            }

            return bCompanionBuildLineage.Unrelated;
        }

        internal static bool IsCompanionProcessName(string processName)
            => !string.IsNullOrWhiteSpace(processName)
               && processName.TrimStart().StartsWith(
                   bCompanionRequirements.ProcessNamePrefix,
                   StringComparison.OrdinalIgnoreCase);

        internal static bool TryNormalizeVersion(string value, out Version version, out string normalized)
        {
            version = null;
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string candidate = value.Trim();
            if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                candidate = candidate.Substring(1);

            string[] parts = candidate.Split('.');
            if (parts.Length != 3 && parts.Length != 4)
                return false;

            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                    return false;
            }

            version = new Version(numbers[0], numbers[1], numbers[2]);
            normalized = version.ToString(3);
            return true;
        }

        internal static int CompareVersions(string left, string right)
        {
            if (!TryNormalizeVersion(left, out Version leftVersion, out _))
                throw new FormatException($"Invalid semantic version: '{left}'.");
            if (!TryNormalizeVersion(right, out Version rightVersion, out _))
                throw new FormatException($"Invalid semantic version: '{right}'.");

            return leftVersion.CompareTo(rightVersion);
        }

        internal static bool TryLaunch(bCompanionStatusResult result, out string error)
        {
            error = string.Empty;
            if (result.Status != bCompanionStatus.ReadyStopped || string.IsNullOrWhiteSpace(result.ExecutablePath))
            {
                error = "Locate a supported stopped bHapticsOSC app before launching it.";
                return false;
            }

            // Recheck at the moment of launch so a deleted/replaced executable,
            // or a compatible copy that started since the last UI refresh, is
            // never launched from stale status.
            bCompanionStatusResult current = Detect(true);
            if (current.Status != bCompanionStatus.ReadyStopped
                || !string.Equals(
                    current.ExecutablePath,
                    result.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The companion status changed. Recheck it before launching.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = current.ExecutablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(current.ExecutablePath) ?? string.Empty,
                });
                InvalidateCache();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Stops every running bHapticsOSC process that is not a supported, current build.
        /// Clearing an upstream copy out of the way matters because it holds the VRChat OSC
        /// port: while it is up, the right build silently receives nothing.
        /// </summary>
        internal static bool TryStopUnsupported(out int stoppedCount, out string error)
        {
            stoppedCount = 0;
            error = string.Empty;

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                error = "The companion app only runs on Windows.";
                return false;
            }

            string requiredVersion = bCompanionRequirements.RequiredVersion;
            var failures = new List<string>();
            int candidates = 0;

            foreach (Process process in EnumerateCompanionProcesses())
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    string processPath = TryGetProcessPath(process);
                    if (!string.IsNullOrWhiteSpace(processPath)
                        && InspectExecutable(processPath, true, requiredVersion).IsReady)
                    {
                        // Leave a supported build alone; it is the one that should be running.
                        continue;
                    }

                    candidates++;
                    if (!process.CloseMainWindow() || !process.WaitForExit(StopGracePeriodMilliseconds))
                    {
                        process.Kill();
                        process.WaitForExit(StopGracePeriodMilliseconds);
                    }

                    stoppedCount++;
                }
                catch (Exception exception)
                {
                    failures.Add($"{SafeGetProcessName(process)}: {exception.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            InvalidateCache();

            if (failures.Count > 0)
            {
                error = string.Join("\n", failures.ToArray());
                return false;
            }

            if (candidates == 0)
            {
                error = "No unsupported bHapticsOSC process is running.";
                return false;
            }

            return true;
        }

        private static bCompanionStatusResult DetectUncached()
        {
            string requiredVersion = bCompanionRequirements.RequiredVersion;
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return Evaluate(
                    false,
                    false,
                    RememberedExecutablePath,
                    false,
                    null,
                    null,
                    requiredVersion);
            }

            IReadOnlyList<bCompanionProcessSnapshot> running = RunningProcessProvider();

            bCompanionStatusResult? compatibleRunning = null;
            bCompanionStatusResult? bestRunningDiagnostic = null;
            string unsupportedProcessName = null;
            string uninspectableProcessName = null;

            foreach (bCompanionProcessSnapshot snapshot in running)
            {
                if (!snapshot.PathReadable)
                {
                    // Not a verdict on the app, only on our visibility of it. It may well be the
                    // remembered executable, so it is matched by name further down instead of
                    // being counted as a rival process.
                    if (uninspectableProcessName == null)
                        uninspectableProcessName = snapshot.ProcessName;
                    continue;
                }

                bCompanionStatusResult result = InspectExecutable(snapshot.ExecutablePath, true, requiredVersion);
                if (result.Status == bCompanionStatus.ReadyRunning)
                {
                    if (!compatibleRunning.HasValue)
                        compatibleRunning = result;
                    continue;
                }

                if (unsupportedProcessName == null)
                    unsupportedProcessName = snapshot.ProcessName;

                if (!bestRunningDiagnostic.HasValue
                    || GetDiagnosticPriority(result.Status) > GetDiagnosticPriority(bestRunningDiagnostic.Value.Status))
                {
                    bestRunningDiagnostic = result;
                }
            }

            if (compatibleRunning.HasValue)
            {
                // Remember where the working build actually lives, so closing it later reports
                // "ready - stopped" instead of losing track of the app entirely.
                RememberDiscoveredPath(compatibleRunning.Value.ExecutablePath);
                return WithConflict(compatibleRunning.Value, unsupportedProcessName);
            }

            string rememberedPath = RememberedExecutablePath;
            bCompanionStatusResult remembered = InspectExecutable(
                rememberedPath,
                IsRememberedRunning(rememberedPath, running),
                requiredVersion);

            if (remembered.IsReady)
                return WithConflict(remembered, unsupportedProcessName);

            // Nothing usable is installed. A problem with something that is actually running
            // beats a problem with something that merely sits on disk.
            if (bestRunningDiagnostic.HasValue)
                return bestRunningDiagnostic.Value;

            // Only when there is nothing else to report. A remembered path is a concrete, fixable
            // fact - "the app you chose is no longer there" - and the uninspectable result carries
            // no path, so preferring it would throw that away and leave the window with nothing to
            // show. The remembered result already knows whether it is the process running, because
            // IsRememberedRunning matched it by name above.
            if (uninspectableProcessName != null && string.IsNullOrWhiteSpace(rememberedPath))
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.RunningUninspectable,
                    NormalizeRequiredVersion(requiredVersion),
                    isRunning: true,
                    detectedProcessName: uninspectableProcessName);
            }

            return remembered;
        }

        private static bCompanionStatusResult WithConflict(bCompanionStatusResult result, string conflictingProcessName)
            => conflictingProcessName == null ? result : result.WithConflictingProcess(conflictingProcessName);

        /// <summary>
        /// Decides whether the remembered executable is one of the running processes. Readable
        /// paths are compared exactly; the process name is only trusted as a stand-in for a
        /// process whose path could not be read, because two different builds are both routinely
        /// called bHapticsOSC.exe.
        /// </summary>
        private static bool IsRememberedRunning(string rememberedPath, IReadOnlyList<bCompanionProcessSnapshot> running)
        {
            if (string.IsNullOrWhiteSpace(rememberedPath) || running.Count == 0)
                return false;

            string rememberedName = SafeGetFileNameWithoutExtension(rememberedPath);

            foreach (bCompanionProcessSnapshot snapshot in running)
            {
                if (snapshot.PathReadable)
                {
                    if (string.Equals(snapshot.ExecutablePath, rememberedPath, StringComparison.OrdinalIgnoreCase))
                        return true;

                    continue;
                }

                if (!string.IsNullOrEmpty(rememberedName)
                    && string.Equals(snapshot.ProcessName, rememberedName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Seam for the process sweep. Tests replace it so detection can be exercised without
        /// depending on whatever happens to be running on the machine.
        /// </summary>
        internal static Func<IReadOnlyList<bCompanionProcessSnapshot>> RunningProcessProvider { get; set; }
            = SnapshotRunningProcesses;

        internal static void ResetRunningProcessProvider()
        {
            RunningProcessProvider = SnapshotRunningProcesses;
            InvalidateCache();
        }

        private static IReadOnlyList<bCompanionProcessSnapshot> SnapshotRunningProcesses()
        {
            var snapshots = new List<bCompanionProcessSnapshot>();
            foreach (Process process in EnumerateCompanionProcesses())
            {
                try
                {
                    snapshots.Add(new bCompanionProcessSnapshot(
                        SafeGetProcessName(process),
                        TryGetProcessPath(process)));
                }
                catch
                {
                    // A process that vanishes mid-sweep is simply not there.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return snapshots;
        }

        private static IEnumerable<Process> EnumerateCompanionProcesses()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                yield break;
            }

            foreach (Process process in processes)
            {
                bool matches;
                try
                {
                    matches = IsCompanionProcessName(process.ProcessName);
                }
                catch
                {
                    matches = false;
                }

                if (matches)
                {
                    yield return process;
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        private static string TryGetProcessPath(Process process)
        {
            try
            {
                // MainModule throws for processes of a different bitness or a higher integrity
                // level; an unreadable path is not a reason to ignore the process.
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeGetProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return bCompanionRequirements.ProcessNamePrefix;
            }
        }

        internal static bCompanionStatusResult InspectExecutable(string path, bool isRunning, string requiredVersion)
        {
            if (string.IsNullOrWhiteSpace(path) || !FileExists(path))
                return Evaluate(true, false, path, isRunning, null, null, requiredVersion);

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string fileVersion = string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
                return Evaluate(
                    true,
                    true,
                    path,
                    isRunning,
                    info.ProductName,
                    fileVersion,
                    requiredVersion,
                    info.FileDescription,
                    info.CompanyName,
                    info.OriginalFilename);
            }
            catch
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.UnknownVersion,
                    NormalizeRequiredVersion(requiredVersion),
                    path,
                    isRunning: isRunning);
            }
        }

        private static void RememberDiscoveredPath(string path)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrEmpty(normalized))
                return;

            if (string.Equals(RememberedExecutablePath, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            EditorPrefs.SetString(RememberedPathPreferenceKey, normalized);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                // Path.GetFullPath throws on invalid characters and on over-long paths; an
                // unusable selection must not take the window down with it.
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool FileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeGetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFileName(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeGetFileNameWithoutExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ContainsFamilyMarker(string value)
            => !string.IsNullOrWhiteSpace(value)
               && value.IndexOf(bCompanionRequirements.ProductFamilyMarker, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string NormalizeRequiredVersion(string requiredVersion)
            => TryNormalizeVersion(requiredVersion, out _, out string normalized)
                ? normalized
                : bCompanionRequirements.FallbackRequiredVersion;

        private static int GetDiagnosticPriority(bCompanionStatus status)
        {
            switch (status)
            {
                case bCompanionStatus.ForeignBuild:
                    return 4;
                case bCompanionStatus.Outdated:
                    return 3;
                case bCompanionStatus.UnknownVersion:
                    return 2;
                case bCompanionStatus.InvalidProduct:
                    return 1;
                default:
                    return 0;
            }
        }

        internal static void InvalidateCache()
            => hasCachedResult = false;
    }
}
#endif
