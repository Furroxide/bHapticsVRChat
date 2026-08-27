using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat.Tests
{
    public class bCompanionStatusDetectorTests
    {
        [TestCase("2.3.1", "2.3.1")]
        [TestCase(" 2.3.1 ", "2.3.1")]
        [TestCase("v2.3.1", "2.3.1")]
        [TestCase("2.3.1.0", "2.3.1")]
        [TestCase("2.3.1.42", "2.3.1")]
        public void TryNormalizeVersion_AcceptsStableSemanticVersions(
            string input,
            string expectedNormalizedVersion)
        {
            bool success = bCompanionStatusDetector.TryNormalizeVersion(
                input,
                out Version parsedVersion,
                out string normalizedVersion);

            Assert.That(success, Is.True);
            Assert.That(normalizedVersion, Is.EqualTo(expectedNormalizedVersion));
            Version expectedVersion = new Version(expectedNormalizedVersion);
            Assert.That(parsedVersion.Major, Is.EqualTo(expectedVersion.Major));
            Assert.That(parsedVersion.Minor, Is.EqualTo(expectedVersion.Minor));
            Assert.That(parsedVersion.Build, Is.EqualTo(expectedVersion.Build));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("2.3")]
        [TestCase("2.3.1-beta.1")]
        [TestCase("2.3.1+build.7")]
        [TestCase("version 2.3.1")]
        public void TryNormalizeVersion_RejectsNonReleaseVersions(string input)
        {
            bool success = bCompanionStatusDetector.TryNormalizeVersion(
                input,
                out _,
                out _);

            Assert.That(success, Is.False);
        }

        [TestCase("2.3.1", "2.3.1.0", 0)]
        [TestCase("v2.3.1", "2.3.1", 0)]
        [TestCase("2.4.0", "2.3.1", 1)]
        [TestCase("2.2.9", "2.3.1", -1)]
        public void CompareVersions_UsesNormalizedSemanticVersions(
            string left,
            string right,
            int expectedSign)
        {
            int comparison = bCompanionStatusDetector.CompareVersions(left, right);

            Assert.That(Math.Sign(comparison), Is.EqualTo(expectedSign));
        }

        [Test]
        public void Evaluate_NonWindows_IsUnsupportedPlatform()
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: false,
                pathExists: true,
                isRunning: true,
                productName: bCompanionRequirements.ProductName,
                fileVersion: "2.3.1");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.UnsupportedPlatform));
            Assert.That(result.IsReady, Is.False);
        }

        [Test]
        public void Evaluate_EmptyExecutablePath_IsNotLocated()
        {
            bCompanionStatusResult result = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: false,
                executablePath: string.Empty,
                isRunning: false,
                productName: null,
                fileVersion: null,
                requiredVersion: "2.3.1");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.NotLocated));
        }

        [Test]
        public void Evaluate_MissingExecutable_IsMissingPath()
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: true,
                pathExists: false,
                isRunning: false,
                productName: null,
                fileVersion: null);

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.MissingPath));
        }

        [Test]
        public void Evaluate_WrongProduct_IsInvalidProduct()
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: true,
                pathExists: true,
                isRunning: false,
                productName: "Different Product",
                fileVersion: "2.3.1");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.InvalidProduct));
        }

        [Test]
        public void Evaluate_UnparseableVersion_IsUnknownVersion()
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: true,
                pathExists: true,
                isRunning: false,
                productName: bCompanionRequirements.ProductName,
                fileVersion: "unknown");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.UnknownVersion));
        }

        [Test]
        public void Evaluate_OlderVersion_IsOutdated()
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: true,
                pathExists: true,
                isRunning: true,
                productName: bCompanionRequirements.ProductName,
                fileVersion: "2.3.0");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.Outdated));
            Assert.That(result.IsReady, Is.False);
        }

        [TestCase("2.3.1", false, (int)bCompanionStatus.ReadyStopped)]
        [TestCase("2.4.0", false, (int)bCompanionStatus.ReadyStopped)]
        [TestCase("2.4.0", true, (int)bCompanionStatus.ReadyRunning)]
        public void Evaluate_CompatibleVersion_ReflectsRunningState(
            string detectedVersion,
            bool isRunning,
            int expectedStatusValue)
        {
            bCompanionStatusResult result = Evaluate(
                isWindows: true,
                pathExists: true,
                isRunning: isRunning,
                productName: bCompanionRequirements.ProductName,
                fileVersion: detectedVersion);

            Assert.That(result.Status, Is.EqualTo((bCompanionStatus)expectedStatusValue));
            Assert.That(result.DetectedVersion, Is.EqualTo(detectedVersion));
            Assert.That(result.RequiredVersion, Is.EqualTo("2.3.1"));
            Assert.That(result.IsReady, Is.True);
        }

        [Test]
        public void Evaluate_UsesProductMetadataInsteadOfExecutableFilename()
        {
            bCompanionStatusResult result = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: true,
                executablePath: "C:/Tools/renamed-tool.exe",
                isRunning: false,
                productName: bCompanionRequirements.ProductName,
                fileVersion: "2.3.1",
                requiredVersion: "2.3.1");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.ReadyStopped));
            Assert.That(result.ExecutablePath, Does.EndWith("renamed-tool.exe"));
        }

        [Test]
        public void ClassifyLineage_ExactProductName_IsSupported()
        {
            bCompanionBuildLineage lineage = bCompanionStatusDetector.ClassifyLineage(
                bCompanionRequirements.ProductName);

            Assert.That(lineage, Is.EqualTo(bCompanionBuildLineage.Supported));
        }

        // The metadata below is what the official bHaptics releases actually carry:
        // v2.2.1 ships OriginalFilename bHapticsOSC.exe, v1.1.4 ships its own display name.
        [TestCase("bHaptics OSC for VRChat", "bHaptics OSC for VRChat", "bHaptics", "bHapticsOSC.exe")]
        [TestCase("bHaptics OSC for VRChat", "bHaptics OSC for VRChat", "bHaptics", "bHaptics OSC for VRChat.exe")]
        [TestCase(null, null, "bHaptics", null)]
        [TestCase(null, null, null, "bHapticsOSC.exe")]
        public void ClassifyLineage_OtherBHapticsBuilds_AreForeign(
            string productName,
            string fileDescription,
            string companyName,
            string originalFilename)
        {
            bCompanionBuildLineage lineage = bCompanionStatusDetector.ClassifyLineage(
                productName,
                fileDescription,
                companyName,
                originalFilename);

            Assert.That(lineage, Is.EqualTo(bCompanionBuildLineage.Foreign));
        }

        [Test]
        public void ClassifyLineage_StrippedMetadata_FallsBackToTheFilename()
        {
            bCompanionBuildLineage lineage = bCompanionStatusDetector.ClassifyLineage(
                null,
                null,
                null,
                null,
                "C:/Users/example/Downloads/bHapticsOSC_v2.2.1.exe");

            Assert.That(lineage, Is.EqualTo(bCompanionBuildLineage.Foreign));
        }

        [Test]
        public void ClassifyLineage_UnrelatedExecutable_IsUnrelated()
        {
            bCompanionBuildLineage lineage = bCompanionStatusDetector.ClassifyLineage(
                "Notepad",
                "Notepad",
                "Microsoft Corporation",
                "NOTEPAD.EXE",
                "C:/Windows/notepad.exe");

            Assert.That(lineage, Is.EqualTo(bCompanionBuildLineage.Unrelated));
        }

        [Test]
        public void Evaluate_UpstreamBuild_IsForeignRatherThanInvalidProduct()
        {
            bCompanionStatusResult result = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: true,
                executablePath: "C:/Users/example/Downloads/bHapticsOSC_v2.2.1.exe",
                isRunning: true,
                productName: "bHaptics OSC for VRChat",
                fileVersion: "2.2.1.0",
                requiredVersion: "2.3.1",
                fileDescription: "bHaptics OSC for VRChat",
                companyName: "bHaptics",
                originalFilename: "bHapticsOSC.exe");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.ForeignBuild));
            Assert.That(result.Lineage, Is.EqualTo(bCompanionBuildLineage.Foreign));
            Assert.That(result.DetectedVersion, Is.EqualTo("2.2.1"));
            Assert.That(result.DetectedProductName, Is.EqualTo("bHaptics OSC for VRChat"));
            Assert.That(result.IsRunning, Is.True);
            Assert.That(result.IsReady, Is.False);
            Assert.That(result.HasUnsupportedProcessRunning, Is.True);
        }

        [Test]
        public void Evaluate_ForeignBuildWithANewerNumber_IsStillForeign()
        {
            // Upstream numbering is independent of this fork's, so a higher number upstream
            // must not read as "up to date".
            bCompanionStatusResult result = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: true,
                executablePath: "C:/Tools/bHapticsOSC.exe",
                isRunning: false,
                productName: "bHaptics OSC for VRChat",
                fileVersion: "9.9.9",
                requiredVersion: "2.3.1",
                companyName: "bHaptics");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.ForeignBuild));
            Assert.That(result.IsReady, Is.False);
        }

        [Test]
        public void Evaluate_ForeignBuildWithAnUnreadableVersion_IsStillForeign()
        {
            bCompanionStatusResult result = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: true,
                executablePath: "C:/Tools/bHapticsOSC.exe",
                isRunning: false,
                productName: "bHaptics OSC for VRChat",
                fileVersion: "unknown",
                requiredVersion: "2.3.1",
                companyName: "bHaptics");

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.ForeignBuild));
            Assert.That(result.DetectedVersion, Is.Empty);
        }

        [TestCase("bHapticsOSC")]
        [TestCase("bHapticsOSC_v2.2.1")]
        [TestCase("bHapticsOSC v1.1.4")]
        [TestCase("BHAPTICSOSC")]
        public void IsCompanionProcessName_MatchesRenamedReleases(string processName)
            => Assert.That(bCompanionStatusDetector.IsCompanionProcessName(processName), Is.True);

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("BhapticsPlayer")]
        [TestCase("VRChat")]
        [TestCase("my-bHapticsOSC")]
        public void IsCompanionProcessName_RejectsUnrelatedProcesses(string processName)
            => Assert.That(bCompanionStatusDetector.IsCompanionProcessName(processName), Is.False);

        [Test]
        public void WithConflictingProcess_MarksASupportedBuildAsContested()
        {
            bCompanionStatusResult ready = Evaluate(
                isWindows: true,
                pathExists: true,
                isRunning: true,
                productName: bCompanionRequirements.ProductName,
                fileVersion: "2.3.1");

            Assert.That(ready.HasConflictingProcess, Is.False);
            Assert.That(ready.HasUnsupportedProcessRunning, Is.False);

            bCompanionStatusResult contested = ready.WithConflictingProcess("bHapticsOSC_v2.2.1");

            Assert.That(contested.Status, Is.EqualTo(bCompanionStatus.ReadyRunning));
            Assert.That(contested.ConflictingProcessName, Is.EqualTo("bHapticsOSC_v2.2.1"));
            Assert.That(contested.HasConflictingProcess, Is.True);
            Assert.That(contested.HasUnsupportedProcessRunning, Is.True);
            Assert.That(contested.DetectedVersion, Is.EqualTo(ready.DetectedVersion));
            Assert.That(contested.ExecutablePath, Is.EqualTo(ready.ExecutablePath));
        }

        private static bCompanionStatusResult Evaluate(
            bool isWindows,
            bool pathExists,
            bool isRunning,
            string productName,
            string fileVersion)
            => bCompanionStatusDetector.Evaluate(
                isWindows,
                pathExists,
                "C:/Program Files/bHapticsOSC/bHapticsOSC.exe",
                isRunning,
                productName,
                fileVersion,
                "2.3.1");
    }

    public class bCompanionStatusDetectorPreferenceTests
    {
        private bool hadOriginalPreference;
        private string originalPreference;

        [SetUp]
        public void SetUp()
        {
            hadOriginalPreference = EditorPrefs.HasKey(bCompanionStatusDetector.RememberedPathPreferenceKey);
            originalPreference = EditorPrefs.GetString(
                bCompanionStatusDetector.RememberedPathPreferenceKey,
                string.Empty);
            bCompanionStatusDetector.SetRememberedExecutablePath(null);

            // Detection reads the machine's live process table. Without a stub these tests pass
            // or fail depending on whether the developer happens to have the companion open.
            SetRunningProcesses();
        }

        [TearDown]
        public void TearDown()
        {
            bCompanionStatusDetector.ResetRunningProcessProvider();

            // Use the public mutation path first so the detector cache is invalidated.
            bCompanionStatusDetector.SetRememberedExecutablePath(null);
            if (hadOriginalPreference)
            {
                EditorPrefs.SetString(
                    bCompanionStatusDetector.RememberedPathPreferenceKey,
                    originalPreference);
            }
            else
            {
                EditorPrefs.DeleteKey(bCompanionStatusDetector.RememberedPathPreferenceKey);
            }
        }

        [Test]
        public void SetRememberedExecutablePath_PersistsAndClearsSelectedPath()
        {
            string selectedPath = GetTestExecutablePath("selected");

            bCompanionStatusDetector.SetRememberedExecutablePath(selectedPath);

            Assert.That(EditorPrefs.HasKey(bCompanionStatusDetector.RememberedPathPreferenceKey), Is.True);
            Assert.That(bCompanionStatusDetector.RememberedExecutablePath, Is.EqualTo(selectedPath));

            bCompanionStatusDetector.SetRememberedExecutablePath("  ");

            Assert.That(EditorPrefs.HasKey(bCompanionStatusDetector.RememberedPathPreferenceKey), Is.False);
            Assert.That(bCompanionStatusDetector.RememberedExecutablePath, Is.Empty);
        }

        [Test]
        public void Detect_UsesCachedResultUntilForceRefreshed()
        {
            string pathA = GetTestExecutablePath("cache-a");
            string pathB = GetTestExecutablePath("cache-b");
            bCompanionStatusDetector.SetRememberedExecutablePath(pathA);
            bCompanionStatusResult initial = bCompanionStatusDetector.Detect(true);

            EditorPrefs.SetString(bCompanionStatusDetector.RememberedPathPreferenceKey, pathB);

            bCompanionStatusResult cached = bCompanionStatusDetector.Detect(false);
            bCompanionStatusResult refreshed = bCompanionStatusDetector.Detect(true);

            Assert.That(initial.ExecutablePath, Is.EqualTo(pathA));
            Assert.That(cached.ExecutablePath, Is.EqualTo(pathA));
            Assert.That(refreshed.ExecutablePath, Is.EqualTo(pathB));
        }

        [Test]
        public void TryLaunch_RefusesAStaleReadyResult()
        {
            string deletedPath = GetTestExecutablePath("stale-launch");
            bCompanionStatusResult stale = bCompanionStatusDetector.Evaluate(
                isWindows: true,
                pathExists: true,
                executablePath: deletedPath,
                isRunning: false,
                productName: bCompanionRequirements.ProductName,
                fileVersion: bCompanionRequirements.FallbackRequiredVersion,
                requiredVersion: bCompanionRequirements.FallbackRequiredVersion);

            bool launched = bCompanionStatusDetector.TryLaunch(stale, out string error);

            Assert.That(launched, Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void OnboardingDismissal_IsScopedToTheRequiredVersion()
        {
            const string versionA = "999.999.998";
            const string versionB = "999.999.999";
            string keyA = bCompanionOnboarding.GetPreferenceKey(versionA);
            string keyB = bCompanionOnboarding.GetPreferenceKey(versionB);
            bool hadKeyA = EditorPrefs.HasKey(keyA);
            bool hadKeyB = EditorPrefs.HasKey(keyB);
            bool originalA = EditorPrefs.GetBool(keyA, false);
            bool originalB = EditorPrefs.GetBool(keyB, false);

            try
            {
                EditorPrefs.DeleteKey(keyA);
                EditorPrefs.DeleteKey(keyB);

                Assert.That(bCompanionOnboarding.IsDismissed(versionA), Is.False);
                Assert.That(bCompanionOnboarding.IsDismissed(versionB), Is.False);

                bCompanionOnboarding.Dismiss(versionA);

                Assert.That(bCompanionOnboarding.IsDismissed(versionA), Is.True);
                Assert.That(bCompanionOnboarding.IsDismissed(versionB), Is.False);
            }
            finally
            {
                RestoreBooleanPreference(keyA, hadKeyA, originalA);
                RestoreBooleanPreference(keyB, hadKeyB, originalB);
            }
        }

        [Test]
        public void Detect_UninspectableProcessWithNothingRemembered_ReportsItAsUninspectable()
        {
            RequireWindowsEditor();

            // Windows refuses MainModule for a process running at a higher integrity level.
            // That is a gap in our visibility, not a verdict on the app's version.
            SetRunningProcesses(new bCompanionProcessSnapshot("bHapticsOSC", null));

            bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.RunningUninspectable));
            Assert.That(result.IsRunning, Is.True);
            Assert.That(result.DetectedProcessName, Is.EqualTo("bHapticsOSC"));
            Assert.That(result.HasUnsupportedProcessRunning, Is.True);
        }

        [Test]
        public void Detect_UninspectableProcessMatchingTheRememberedApp_MarksItRunning()
        {
            RequireWindowsEditor();

            string remembered = GetTestExecutablePath("running-remembered");
            bCompanionStatusDetector.SetRememberedExecutablePath(remembered);
            SetRunningProcesses(new bCompanionProcessSnapshot(
                Path.GetFileNameWithoutExtension(remembered),
                null));

            bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

            // The fixture path is never created, so the interesting assertion is the running
            // flag: without the name fallback this reports a stopped app and offers Launch,
            // which would start a second copy fighting for the OSC port.
            Assert.That(result.ExecutablePath, Is.EqualTo(remembered));
            Assert.That(result.IsRunning, Is.True);
        }

        [Test]
        public void Detect_UninspectableProcessWithAnUnrelatedName_LeavesTheRememberedAppStopped()
        {
            RequireWindowsEditor();

            string remembered = GetTestExecutablePath("stopped-remembered");
            bCompanionStatusDetector.SetRememberedExecutablePath(remembered);
            SetRunningProcesses(new bCompanionProcessSnapshot("bHapticsOSC_v2.2.1", null));

            bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

            Assert.That(result.IsRunning, Is.False);
        }

        [Test]
        public void Detect_RunningProcessAtAVanishedPath_ReportsTheRunningProcess()
        {
            RequireWindowsEditor();

            SetRunningProcesses(new bCompanionProcessSnapshot(
                "bHapticsOSC",
                GetTestExecutablePath("deleted-while-running")));

            bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.MissingPath));
            Assert.That(result.IsRunning, Is.True);
        }

        [Test]
        public void Detect_NoProcesses_FallsBackToTheRememberedApp()
        {
            RequireWindowsEditor();

            string remembered = GetTestExecutablePath("nothing-running");
            bCompanionStatusDetector.SetRememberedExecutablePath(remembered);

            bCompanionStatusResult result = bCompanionStatusDetector.Detect(true);

            Assert.That(result.Status, Is.EqualTo(bCompanionStatus.MissingPath));
            Assert.That(result.IsRunning, Is.False);
            Assert.That(result.ExecutablePath, Is.EqualTo(remembered));
        }

        /// <summary>The process sweep only runs on Windows; elsewhere Detect short-circuits.</summary>
        private static void RequireWindowsEditor()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("The bHapticsOSC companion app is Windows-only.");
        }

        private static void SetRunningProcesses(params bCompanionProcessSnapshot[] snapshots)
        {
            bCompanionProcessSnapshot[] captured = snapshots ?? Array.Empty<bCompanionProcessSnapshot>();
            bCompanionStatusDetector.RunningProcessProvider = () => captured;
            bCompanionStatusDetector.InvalidateCache();
        }

        private static void RestoreBooleanPreference(string key, bool existed, bool value)
        {
            if (existed)
                EditorPrefs.SetBool(key, value);
            else
                EditorPrefs.DeleteKey(key);
        }

        private static string GetTestExecutablePath(string suffix)
            => Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                $"bhaptics-osc-tests-{suffix}-{Guid.NewGuid():N}.exe"));
    }
}
