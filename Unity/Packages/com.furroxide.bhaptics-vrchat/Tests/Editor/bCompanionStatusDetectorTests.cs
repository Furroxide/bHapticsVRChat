using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

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
        }

        [TearDown]
        public void TearDown()
        {
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
