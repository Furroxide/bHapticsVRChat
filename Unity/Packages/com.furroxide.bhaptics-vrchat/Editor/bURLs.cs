#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace bHapticsOSC.VRChat
{
    internal static class bURLs
    {
        [MenuItem("bHapticsOSC/Setup Assistant", priority = 0)]
        private static void OpenSetupAssistant()
            => bCompanionSetupWindow.ShowWindow();

        [MenuItem("bHapticsOSC/Download matching version", priority = 20)]
        private static void DownloadMatchingVersion()
            => Application.OpenURL(bCompanionRequirements.GetMatchingDownloadUrl());

        [MenuItem("bHapticsOSC/Latest release", priority = 21)]
        private static void OpenLatestRelease()
            => Application.OpenURL(bCompanionRequirements.LatestReleaseUrl);

        [MenuItem("bHapticsOSC/Avatar How-to Guide", priority = 40)]
        private static void OpenGuide()
            => Application.OpenURL(bCompanionRequirements.AvatarGuideUrl);

        [MenuItem("bHapticsOSC/VRChat OSC Guide", priority = 41)]
        private static void OpenVrchatOscGuide()
            => Application.OpenURL(bCompanionRequirements.VrchatOscGuideUrl);

        [MenuItem("bHapticsOSC/GitHub Repository", priority = 60)]
        private static void OpenRepository()
            => Application.OpenURL(bCompanionRequirements.RepositoryUrl);

        [MenuItem("bHapticsOSC/bHaptics Player Downloads", priority = 61)]
        private static void OpenbHapticsPlayerDownloads()
            => Application.OpenURL(bCompanionRequirements.BHapticsPlayerUrl);
    }
}
#endif
