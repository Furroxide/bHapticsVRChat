#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    /// <summary>What a probe found. Absence of evidence is not evidence of absence.</summary>
    internal enum bProbeState
    {
        /// <summary>Nothing could be established - say so, rather than guessing either way.</summary>
        Unknown,

        No,
        Yes,
    }

    /// <summary>
    /// Everything the editor can observe about the world outside Unity that the haptics depend on.
    ///
    /// The setup window used to print "this must be confirmed manually" for bHaptics Player and for
    /// VRChat's OSC switch, which asked the user to go and verify by hand two things the machine
    /// already knows - and told a user whose Player was closed precisely nothing. Both are
    /// observable, cheaply and read-only.
    /// </summary>
    internal readonly struct bEnvironment
    {
        internal bEnvironment(
            bProbeState playerInstalled,
            bProbeState playerRunning,
            string playerVersion,
            bProbeState oscEnabled,
            DateTime oscConfigWritten,
            string hapticAvatarName)
        {
            PlayerInstalled = playerInstalled;
            PlayerRunning = playerRunning;
            PlayerVersion = playerVersion ?? string.Empty;
            OscEnabled = oscEnabled;
            OscConfigWritten = oscConfigWritten;
            HapticAvatarName = hapticAvatarName ?? string.Empty;
        }

        internal bProbeState PlayerInstalled { get; }
        internal bProbeState PlayerRunning { get; }
        internal string PlayerVersion { get; }

        /// <summary>VRChat's own OSC setting, read from where VRChat stores it.</summary>
        internal bProbeState OscEnabled { get; }

        /// <summary>When VRChat last wrote an OSC config, or default when it never has.</summary>
        internal DateTime OscConfigWritten { get; }

        /// <summary>
        /// An avatar VRChat has loaded that carried this package's haptic parameters. This is the
        /// only proof available inside Unity that the whole chain actually worked.
        /// </summary>
        internal string HapticAvatarName { get; }

        internal bool HasSeenOscConfig => OscConfigWritten != default;
        internal bool HasHapticAvatar => !string.IsNullOrEmpty(HapticAvatarName);
    }

    /// <summary>
    /// Read-only probes for bHaptics Player and VRChat. Everything here observes; nothing here
    /// changes anything on disk, in the registry, or in another process.
    /// </summary>
    internal static class bEnvironmentProbes
    {
        /// <summary>
        /// The bHaptics Player's own SDK endpoint - the port bHapticsLib connects to, which is how
        /// the companion app talks to it. A listener here means the Player is up and serving.
        /// </summary>
        private const int PlayerServicePort = 15881;

        private const string PlayerProcessName = "BhapticsPlayer";
        private const string PlayerExecutableName = "BhapticsPlayer.exe";
        private const string PlayerInstallFolderName = "bHapticsPlayer";

        /// <summary>Unity writes PlayerPrefs to the registry as "&lt;name&gt;_h&lt;hash&gt;", so match by prefix.</summary>
        private const string VrchatRegistryKey = @"Software\VRChat\VRChat";
        private const string OscPreferencePrefix = "UI.Settings.Osc";

        /// <summary>Cheap, but not free. Probed at most this often, and never from OnGUI.</summary>
        private const double CacheLifetimeSeconds = 5.0d;

        /// <summary>
        /// Reading avatar configs means opening files that run to tens of kilobytes each, so it is
        /// done far less often than the liveness probes and never on the repaint path.
        /// </summary>
        private const double AvatarScanLifetimeSeconds = 60.0d;

        private const int MaxAvatarConfigsRead = 12;

        private static bEnvironment cached;
        private static double cachedAt;
        private static bool hasCached;

        private static string cachedAvatarName = string.Empty;
        private static DateTime cachedConfigWritten;
        private static double avatarScannedAt;
        private static bool hasScannedAvatars;

        /// <summary>Seam for tests, so probing does not depend on the developer's own machine.</summary>
        internal static Func<bEnvironment> OverrideProvider { get; set; }

        internal static void ResetOverride()
        {
            OverrideProvider = null;
            Invalidate();
        }

        internal static void Invalidate()
        {
            hasCached = false;
            hasScannedAvatars = false;
        }

        internal static bEnvironment Probe(bool forceRefresh = false)
        {
            if (OverrideProvider != null)
                return OverrideProvider();

            double now = EditorApplication.timeSinceStartup;

            // Time running backwards means the editor restarted; treat the cache as gone.
            if (!forceRefresh && hasCached && now >= cachedAt && now - cachedAt < CacheLifetimeSeconds)
                return cached;

            cached = ProbeUncached(now, forceRefresh);
            cachedAt = now;
            hasCached = true;
            return cached;
        }

        private static bEnvironment ProbeUncached(double now, bool forceRefresh)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return new bEnvironment(
                    bProbeState.Unknown, bProbeState.Unknown, null,
                    bProbeState.Unknown, default, null);
            }

            bProbeState running = ProbePlayerRunning();
            ProbePlayerInstall(out bProbeState installed, out string version);

            // A running Player is proof it is installed, whatever the file probe made of the path.
            if (running == bProbeState.Yes)
                installed = bProbeState.Yes;

            RefreshAvatarScan(now, forceRefresh);

            return new bEnvironment(
                installed,
                running,
                version,
                ProbeOscEnabled(),
                cachedConfigWritten,
                cachedAvatarName);
        }

        // ------------------------------------------------------------------ bHaptics Player

        /// <summary>
        /// Running means the Player's service port has a listener. The process name alone would
        /// also do, but a listening port is what the companion app actually needs.
        /// </summary>
        private static bProbeState ProbePlayerRunning()
        {
            try
            {
                foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                {
                    if (endpoint.Port == PlayerServicePort)
                        return bProbeState.Yes;
                }
            }
            catch
            {
                // Enumerating listeners can be denied. Fall through to the process check.
            }

            try
            {
                foreach (Process process in Process.GetProcessesByName(PlayerProcessName))
                {
                    process.Dispose();
                    return bProbeState.Yes;
                }

                return bProbeState.No;
            }
            catch
            {
                return bProbeState.Unknown;
            }
        }

        private static void ProbePlayerInstall(out bProbeState installed, out string version)
        {
            installed = bProbeState.Unknown;
            version = string.Empty;

            string path = PlayerExecutablePath();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // The install is per-user; a relocated or machine-wide install simply is not
                    // here, which is not the same as not installed.
                    installed = bProbeState.No;
                    return;
                }

                installed = bProbeState.Yes;
                version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
            }
            catch
            {
                installed = bProbeState.Unknown;
            }
        }

        private static string PlayerExecutablePath()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                    return string.Empty;

                return Path.Combine(Path.Combine(local, PlayerInstallFolderName), PlayerExecutableName);
            }
            catch
            {
                return string.Empty;
            }
        }

        // ------------------------------------------------------------------ VRChat

        /// <summary>
        /// VRChat stores its OSC switch as a Unity PlayerPref, which on Windows lands in the
        /// registry. This is the setting itself rather than a symptom of it, so it answers the
        /// question the window has been asking users to answer by hand.
        /// </summary>
        private static bProbeState ProbeOscEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VrchatRegistryKey))
                {
                    if (key == null)
                        return bProbeState.Unknown;

                    foreach (string name in key.GetValueNames())
                    {
                        if (!name.StartsWith(OscPreferencePrefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        object value = key.GetValue(name);
                        if (value is int flag)
                            return flag != 0 ? bProbeState.Yes : bProbeState.No;
                    }

                    // VRChat has run but never touched the setting.
                    return bProbeState.Unknown;
                }
            }
            catch
            {
                return bProbeState.Unknown;
            }
        }

        /// <summary>
        /// Looks for evidence that VRChat has loaded an avatar carrying this package's haptic
        /// parameters. That is the one thing inside Unity that proves the whole chain worked -
        /// the alternative is putting a headset on and feeling whether anything happens.
        /// </summary>
        private static void RefreshAvatarScan(double now, bool forceRefresh)
        {
            bool fresh = hasScannedAvatars && now >= avatarScannedAt && now - avatarScannedAt < AvatarScanLifetimeSeconds;
            if (fresh && !forceRefresh)
                return;

            avatarScannedAt = now;
            hasScannedAvatars = true;
            cachedAvatarName = string.Empty;
            cachedConfigWritten = default;

            string oscRoot = VrchatOscFolder();
            if (string.IsNullOrEmpty(oscRoot))
                return;

            string[] files;
            try
            {
                if (!Directory.Exists(oscRoot))
                    return;

                files = Directory.GetFiles(oscRoot, "*.json", SearchOption.AllDirectories);
            }
            catch
            {
                return;
            }

            Array.Sort(files, CompareByWriteTimeDescending);
            if (files.Length > 0)
                cachedConfigWritten = SafeWriteTime(files[0]);

            int read = 0;
            foreach (string file in files)
            {
                if (read++ >= MaxAvatarConfigsRead)
                    break;

                try
                {
                    string text = File.ReadAllText(file);
                    if (text.IndexOf("bHapticsOSC/", StringComparison.Ordinal) < 0)
                        continue;

                    Match name = Regex.Match(text, "\"name\"\\s*:\\s*\"(?<name>[^\"]*)\"");
                    cachedAvatarName = name.Success ? name.Groups["name"].Value : "an uploaded avatar";
                    return;
                }
                catch
                {
                    // A config being rewritten underneath us is not worth reporting.
                }
            }
        }

        /// <summary>
        /// VRChat's per-avatar OSC configs. Read only - these are the user's, and the companion app
        /// already owns the one feature that deletes them.
        /// </summary>
        private static string VrchatOscFolder()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                    return string.Empty;

                string low = Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow");
                return Path.Combine(Path.Combine(Path.Combine(low, "VRChat"), "VRChat"), "OSC");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int CompareByWriteTimeDescending(string left, string right)
            => SafeWriteTime(right).CompareTo(SafeWriteTime(left));

        private static DateTime SafeWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return default;
            }
        }
    }
}
#endif
