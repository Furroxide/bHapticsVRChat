#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// Downloads, verifies and installs the companion app, replacing a round trip through the
    /// browser, the Downloads folder, SmartScreen and a file picker.
    ///
    /// Two things shape the design. The download never blocks the editor - it is pumped from
    /// EditorApplication.update and drawn as progress in the window, not behind a modal bar that
    /// would freeze Unity for the whole transfer. And nothing is trusted until it has been checked:
    /// a 404 body saved under this name would otherwise be handed to the detector, which reads its
    /// empty version resource, falls back to the filename, and cheerfully reports it as an
    /// unsupported bHapticsOSC build. So the file is verified before it is ever named
    /// bHapticsOSC.exe, and before the path is remembered.
    /// </summary>
    internal static class bCompanionInstaller
    {
        internal enum bInstallPhase
        {
            Idle,
            Checking,
            Downloading,
            Verifying,
            Done,
            Failed,
        }

        /// <summary>Writable without elevation, unlike Program Files, and not somewhere users clear.</summary>
        private const string InstallFolderName = "bHapticsOSC";

        private const int TimeoutSeconds = 20;
        private const long MinimumPlausibleBytes = 1L * 1024L * 1024L;
        private const long MaximumPlausibleBytes = 64L * 1024L * 1024L;

        private static UnityWebRequest request;
        private static string targetPath;
        private static string partPath;
        private static string expectedVersion;
        private static long expectedLength;
        private static bool reloadLocked;

        internal static bInstallPhase Phase { get; private set; } = bInstallPhase.Idle;
        internal static float Progress { get; private set; }
        internal static string Message { get; private set; } = string.Empty;
        internal static string InstalledPath { get; private set; } = string.Empty;

        internal static bool IsBusy
            => Phase == bInstallPhase.Checking
               || Phase == bInstallPhase.Downloading
               || Phase == bInstallPhase.Verifying;

        internal static bool IsSupportedPlatform => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>Where an installed copy lives, so the locator can find it without searching.</summary>
        internal static string InstallDirectory
        {
            get
            {
                try
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return string.IsNullOrEmpty(local)
                        ? string.Empty
                        : Path.Combine(Path.Combine(local, "Programs"), InstallFolderName);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        internal static void Begin(string version)
        {
            if (IsBusy)
                return;

            if (!IsSupportedPlatform)
            {
                Fail("The companion app only runs on Windows, so it cannot be installed from this editor.");
                return;
            }

            expectedVersion = version;
            InstalledPath = string.Empty;
            Progress = 0f;
            Phase = bInstallPhase.Checking;
            Message = "Checking for the download...";

            string url = bCompanionRequirements.GetMatchingDownloadUrl(version);
            request = UnityWebRequest.Head(url);
            request.timeout = TimeoutSeconds;
            request.SendWebRequest();

            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        internal static void Cancel()
        {
            if (!IsBusy)
                return;

            Abort();
            Phase = bInstallPhase.Idle;
            Message = "Download cancelled.";
        }

        internal static void Dismiss()
        {
            if (IsBusy)
                return;

            Phase = bInstallPhase.Idle;
            Message = string.Empty;
        }

        // ------------------------------------------------------------------ the pump

        private static void Pump()
        {
            if (request == null)
            {
                Release();
                return;
            }

            if (!request.isDone)
            {
                if (Phase == bInstallPhase.Downloading)
                {
                    Progress = expectedLength > 0
                        ? Mathf.Clamp01(request.downloadedBytes / (float)expectedLength)
                        : 0f;
                    Message = expectedLength > 0
                        ? $"Downloading... {request.downloadedBytes / 1024L / 1024L} of {expectedLength / 1024L / 1024L} MB"
                        : "Downloading...";
                }

                return;
            }

            if (Phase == bInstallPhase.Checking)
                FinishPreflight();
            else if (Phase == bInstallPhase.Downloading)
                FinishDownload();
        }

        private static void FinishPreflight()
        {
            if (!Succeeded(request, out string transportError))
            {
                // The overwhelmingly likely case while this fork has no published releases. Say so
                // plainly instead of leaving the user at a browser 404.
                Fail(request.responseCode == 404
                    ? $"There is no published download for version {expectedVersion} yet. Until the first "
                      + "release is out, build the app yourself or use Locate existing app to point at a "
                      + "copy you already have."
                    : $"Could not reach the download. {transportError}");
                return;
            }

            expectedLength = ParseContentLength(request);
            if (expectedLength > 0 && (expectedLength < MinimumPlausibleBytes || expectedLength > MaximumPlausibleBytes))
            {
                Fail($"The download is {expectedLength / 1024L / 1024L} MB, which is not the size the "
                     + "companion app should be. Nothing was downloaded.");
                return;
            }

            string directory = InstallDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                Fail("Could not work out where to install the app.");
                return;
            }

            try
            {
                Directory.CreateDirectory(directory);
                targetPath = Path.Combine(directory, bCompanionRequirements.ExecutableName);
                partPath = targetPath + ".part";
                if (File.Exists(partPath))
                    File.Delete(partPath);
            }
            catch (Exception exception)
            {
                Fail($"Could not prepare the install folder. {exception.Message}");
                return;
            }

            Release();

            var handler = new DownloadHandlerFile(partPath) { removeFileOnAbort = true };
            request = new UnityWebRequest(
                bCompanionRequirements.GetMatchingDownloadUrl(expectedVersion),
                UnityWebRequest.kHttpVerbGET,
                handler,
                null);

            // No request timeout: a slow connection is not an error. The editor stays responsive
            // because this is pumped rather than waited on.
            request.timeout = 0;

            LockReload();
            Phase = bInstallPhase.Downloading;
            Progress = 0f;
            Message = "Downloading...";
            request.SendWebRequest();
        }

        private static void FinishDownload()
        {
            bool ok = Succeeded(request, out string transportError);
            Release();

            if (!ok)
            {
                SafeDelete(partPath);
                Fail($"The download did not finish. {transportError}");
                return;
            }

            Phase = bInstallPhase.Verifying;
            Message = "Checking the download...";

            if (!Verify(partPath, out string reason))
            {
                SafeDelete(partPath);
                Fail(reason);
                return;
            }

            try
            {
                // Only now does the file get the name the rest of the tool looks for, so a
                // cancelled or half-written download can never be mistaken for an install.
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(partPath, targetPath);
            }
            catch (Exception exception)
            {
                SafeDelete(partPath);
                Fail($"Could not put the app in place. {exception.Message}\n\n"
                     + "If a copy is already running, close it and try again.");
                return;
            }

            bCompanionStatusDetector.SetRememberedExecutablePath(targetPath);
            InstalledPath = targetPath;
            Progress = 1f;
            Phase = bInstallPhase.Done;
            Message = $"Installed to {targetPath}";
        }

        // ------------------------------------------------------------------ verification

        /// <summary>
        /// Cheapest checks first, and every one of them before the file is trusted. Reading the
        /// version resource last matters: an HTML error page saved under this name has no version
        /// resource at all, and the detector would read that absence as a build it recognises.
        /// </summary>
        private static bool Verify(string path, out string reason)
        {
            reason = string.Empty;

            long length;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    reason = "The download did not produce a file.";
                    return false;
                }

                length = info.Length;
            }
            catch (Exception exception)
            {
                reason = $"The download could not be read. {exception.Message}";
                return false;
            }

            if (length < MinimumPlausibleBytes || length > MaximumPlausibleBytes)
            {
                reason = $"The download is {length} bytes, which is not a working copy of the app. "
                         + "It was discarded.";
                return false;
            }

            if (expectedLength > 0 && length != expectedLength)
            {
                reason = "The download did not arrive complete. It was discarded.";
                return false;
            }

            if (!IsWindowsExecutable(path))
            {
                reason = "What arrived is not a Windows program, so it was discarded.";
                return false;
            }

            bCompanionStatusResult inspected = bCompanionStatusDetector.InspectExecutable(
                path,
                false,
                bCompanionRequirements.RequiredVersion);

            if (!inspected.IsReady)
            {
                reason = inspected.Lineage == bCompanionBuildLineage.Supported
                    ? $"The download identifies itself as version {inspected.DetectedVersion}, not "
                      + $"{expectedVersion}. It was discarded rather than installed."
                    : "The download is not the companion app this package needs, so it was discarded.";
                return false;
            }

            return true;
        }

        /// <summary>MZ at the start, and a PE signature where the header says it is.</summary>
        private static bool IsWindowsExecutable(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                        return false;

                    stream.Position = 0x3C;
                    int headerOffset = reader.ReadInt32();
                    if (headerOffset <= 0 || headerOffset + 4 > stream.Length)
                        return false;

                    stream.Position = headerOffset;
                    return reader.ReadUInt32() == 0x00004550;
                }
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ plumbing

        private static bool Succeeded(UnityWebRequest webRequest, out string error)
        {
            error = string.Empty;
            if (webRequest == null)
            {
                error = "The request was cancelled.";
                return false;
            }

            switch (webRequest.result)
            {
                case UnityWebRequest.Result.Success:
                    if (webRequest.responseCode >= 200 && webRequest.responseCode < 300)
                        return true;

                    error = $"The server answered {webRequest.responseCode}.";
                    return false;

                case UnityWebRequest.Result.ConnectionError:
                    error = "There was no connection to github.com.";
                    return false;

                case UnityWebRequest.Result.DataProcessingError:
                    error = "The download could not be written to disk.";
                    return false;

                default:
                    error = string.IsNullOrEmpty(webRequest.error)
                        ? $"The server answered {webRequest.responseCode}."
                        : webRequest.error;
                    return false;
            }
        }

        private static long ParseContentLength(UnityWebRequest webRequest)
        {
            try
            {
                string header = webRequest.GetResponseHeader("Content-Length");
                return long.TryParse(header, out long length) ? length : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        private static void Fail(string message)
        {
            Abort();
            Phase = bInstallPhase.Failed;
            Progress = 0f;
            Message = message;
        }

        private static void Abort()
        {
            try
            {
                request?.Abort();
            }
            catch
            {
                // Aborting an already-finished request is not worth reporting.
            }

            Release();
            SafeDelete(partPath);
        }

        private static void Release()
        {
            EditorApplication.update -= Pump;

            try
            {
                request?.Dispose();
            }
            catch
            {
                // Disposing twice is harmless.
            }

            request = null;
            UnlockReload();
        }

        /// <summary>
        /// A domain reload mid-transfer would strand the request and the part file. Holding
        /// reloads off for the duration is cheaper than trying to resume.
        /// </summary>
        private static void LockReload()
        {
            if (reloadLocked)
                return;

            EditorApplication.LockReloadAssemblies();
            reloadLocked = true;
        }

        private static void UnlockReload()
        {
            if (!reloadLocked)
                return;

            EditorApplication.UnlockReloadAssemblies();
            reloadLocked = false;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A locked leftover is not worth failing the report over.
            }
        }
    }
}
#endif
