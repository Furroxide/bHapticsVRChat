#if UNITY_EDITOR
using System;
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
        public const string ProductName = "bHapticsOSC";
        public const string ProcessName = "bHapticsOSC";
        public const string ExecutableName = "bHapticsOSC.exe";
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
    }

    internal readonly struct bCompanionStatusResult
    {
        internal bCompanionStatusResult(
            bCompanionStatus status,
            string requiredVersion,
            string executablePath = null,
            string detectedVersion = null,
            string detectedProductName = null,
            bool isRunning = false)
        {
            Status = status;
            RequiredVersion = requiredVersion;
            ExecutablePath = executablePath ?? string.Empty;
            DetectedVersion = detectedVersion ?? string.Empty;
            DetectedProductName = detectedProductName ?? string.Empty;
            IsRunning = isRunning;
        }

        internal bCompanionStatus Status { get; }
        internal string RequiredVersion { get; }
        internal string ExecutablePath { get; }
        internal string DetectedVersion { get; }
        internal string DetectedProductName { get; }
        internal bool IsRunning { get; }
        internal bool IsReady => Status == bCompanionStatus.ReadyStopped || Status == bCompanionStatus.ReadyRunning;
    }

    internal static class bCompanionStatusDetector
    {
        internal const string RememberedPathPreferenceKey = bCompanionRequirements.PackageId + ".companion-path";
        private const double CacheLifetimeSeconds = 2.0d;

        private static bCompanionStatusResult cachedResult;
        private static double cachedAt;
        private static bool hasCachedResult;

        internal static string RememberedExecutablePath => EditorPrefs.GetString(RememberedPathPreferenceKey, string.Empty);

        internal static bCompanionStatusResult Detect(bool forceRefresh = false)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!forceRefresh && hasCachedResult && now - cachedAt < CacheLifetimeSeconds)
                return cachedResult;

            cachedResult = DetectUncached();
            cachedAt = now;
            hasCachedResult = true;
            return cachedResult;
        }

        internal static void SetRememberedExecutablePath(string path)
        {
            string normalized = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());
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
            bool pathExists = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);
            return Evaluate(isWindows, pathExists, executablePath, isRunning, productName, fileVersion, requiredVersion);
        }

        internal static bCompanionStatusResult Evaluate(
            bool isWindows,
            bool pathExists,
            string executablePath,
            bool isRunning,
            string productName,
            string fileVersion,
            string requiredVersion)
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

            if (!string.Equals(productName, bCompanionRequirements.ProductName, StringComparison.Ordinal))
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.InvalidProduct,
                    normalizedRequired,
                    executablePath,
                    detectedProductName: productName,
                    isRunning: isRunning);
            }

            if (!TryNormalizeVersion(fileVersion, out _, out string normalizedDetected))
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.UnknownVersion,
                    normalizedRequired,
                    executablePath,
                    detectedProductName: productName,
                    isRunning: isRunning);
            }

            if (CompareVersions(normalizedDetected, normalizedRequired) < 0)
            {
                return new bCompanionStatusResult(
                    bCompanionStatus.Outdated,
                    normalizedRequired,
                    executablePath,
                    normalizedDetected,
                    productName,
                    isRunning);
            }

            return new bCompanionStatusResult(
                isRunning ? bCompanionStatus.ReadyRunning : bCompanionStatus.ReadyStopped,
                normalizedRequired,
                executablePath,
                normalizedDetected,
                productName,
                isRunning);
        }

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

            bCompanionStatusResult? bestRunningDiagnostic = null;
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(bCompanionRequirements.ProcessName);
            }
            catch
            {
                processes = Array.Empty<Process>();
            }

            bCompanionStatusResult? compatibleRunning = null;
            foreach (Process process in processes)
            {
                try
                {
                    string processPath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(processPath))
                        continue;

                    bCompanionStatusResult result = InspectExecutable(processPath, true, requiredVersion);
                    if (result.Status == bCompanionStatus.ReadyRunning)
                    {
                        compatibleRunning ??= result;
                    }
                    else if (!bestRunningDiagnostic.HasValue
                             || GetDiagnosticPriority(result.Status) > GetDiagnosticPriority(bestRunningDiagnostic.Value.Status))
                    {
                        bestRunningDiagnostic = result;
                    }
                }
                catch
                {
                    bCompanionStatusResult unknown = new bCompanionStatusResult(
                        bCompanionStatus.UnknownVersion,
                        NormalizeRequiredVersion(requiredVersion),
                        isRunning: true);
                    if (!bestRunningDiagnostic.HasValue
                        || GetDiagnosticPriority(unknown.Status) > GetDiagnosticPriority(bestRunningDiagnostic.Value.Status))
                    {
                        bestRunningDiagnostic = unknown;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (compatibleRunning.HasValue)
                return compatibleRunning.Value;

            bCompanionStatusResult remembered = InspectExecutable(RememberedExecutablePath, false, requiredVersion);
            if (remembered.IsReady)
                return remembered;

            if (remembered.Status == bCompanionStatus.NotLocated
                || remembered.Status == bCompanionStatus.MissingPath)
            {
                return bestRunningDiagnostic ?? remembered;
            }

            return remembered;
        }

        private static bCompanionStatusResult InspectExecutable(string path, bool isRunning, string requiredVersion)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Evaluate(true, false, path, isRunning, null, null, requiredVersion);

            bool exists = File.Exists(path);
            if (!exists)
                return Evaluate(true, false, path, isRunning, null, null, requiredVersion);

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string fileVersion = string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
                return Evaluate(true, true, path, isRunning, info.ProductName, fileVersion, requiredVersion);
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

        private static string NormalizeRequiredVersion(string requiredVersion)
            => TryNormalizeVersion(requiredVersion, out _, out string normalized)
                ? normalized
                : bCompanionRequirements.FallbackRequiredVersion;

        private static int GetDiagnosticPriority(bCompanionStatus status)
        {
            switch (status)
            {
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

        private static void InvalidateCache()
            => hasCachedResult = false;
    }
}
#endif
