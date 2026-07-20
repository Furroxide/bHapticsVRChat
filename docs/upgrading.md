# Upgrading bHaptics VRChatOSC

Use this guide when updating the Windows app, the Unity avatar package, or an avatar that already has bHapticsOSC setup generated.

## Update the Windows App

1. In Unity, open **bHapticsOSC > Setup Assistant** and review the version shown in the **bHapticsOSC Setup** window.
2. Use **Download matching version** to start the direct executable download required by the installed Unity package, or **Latest release** to open the newest GitHub Release in your browser.
3. Close any running copy of `bHapticsOSC.exe`.
4. Replace the old executable in its existing folder so the adjacent `Config` folder and your settings remain in place. If you use a different folder, choose **Locate existing app** again.
5. Use **Recheck** to verify the executable's product identity and file version.
6. Start bHaptics Player, then use **Launch** or start `bHapticsOSC.exe` yourself and leave it open while using VRChat.
7. In VRChat, confirm **Action Menu → OSC → Enabled** is on. See VRChat's [OSC overview](https://docs.vrchat.com/docs/osc-overview).

The Setup Assistant never downloads, replaces, or launches software
automatically. The direct latest-download link remains available at
[`bHapticsOSC.exe`](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe).

## Update the Unity Package

1. Back up or version-control your avatar project before updating packages.
2. Open the avatar project through VRChat Creator Companion.
3. Make sure the VRCFury repository (`https://vcc.vrcfury.com/`) and bHaptics repository (`https://furroxide.github.io/bHapticsVRChat/index.json`) are added to VCC.
4. From **Manage Project**, resolve the VRChat Avatars SDK and VRCFury, then update **bHaptics VRChatOSC**.
5. Confirm the project opens in Unity `2022.3.22f1` and let Unity finish compiling before changing the avatar setup.
6. Open **bHapticsOSC > Setup Assistant** and confirm the located companion app satisfies the updated package requirement.

The bHapticsOSC package expects VRCFury to come from its VCC repository. Do not rely on old copied VRCFury files inside the Unity project.

### Move a Legacy Installation to VCC

1. Back up or version-control the avatar project and close Unity.
2. Add the VRCFury and bHaptics repositories to VCC.
3. Install **bHaptics VRChatOSC** from **Manage Project**.
4. Reopen Unity and let the package migration and compilation finish.

The VPM migration removes only the shipped legacy files and folders. Assets beneath `Assets/bHapticsOSC/VRChat/Generated` are preserved. Do not import `bHapticsOSC-VRChat.unitypackage` after installing the VPM package.

### Legacy Unity Package Fallback

Projects that cannot use the VPM package may continue importing the newer `bHapticsOSC-VRChat.unitypackage` from each GitHub Release. Resolve the VRChat SDK and VRCFury first, and never combine the legacy package with `com.furroxide.bhaptics-vrchat` in one project.

## Regenerate an Existing Avatar Setup

1. Open the avatar scene or prefab in Unity.
2. Find the `bHapticsOSC VRCFury` object under the avatar.
3. Delete `bHapticsOSC VRCFury`, save the scene or prefab, and close it to remove the old generated setup and generated assets.
4. Add the `bHapticsOSC Integration` component to the avatar root again if it is not already present.
5. Re-add or confirm the bHaptics device objects you want on the avatar.
6. Use `CREATE VRCFURY SETUP` in the bHapticsOSC Integration inspector.
7. Upload the avatar again through the VRChat SDK.

If you only updated `bHapticsOSC.exe`, you usually do not need to regenerate the avatar setup. Regenerate the setup when you import a newer Unity package, change the devices on the avatar, or need new generated VRCFury assets.

## Artifact Sources

- Use GitHub Release downloads for normal user updates.
- Pull request artifacts are temporary review builds and expire according to the repository retention policy.
- PR artifact names are `bHapticsOSC.exe`, `com.furroxide.bhaptics-vrchat-<version>.zip`, `package.json`, and `bHapticsOSC-VRChat.unitypackage`.
- Release and pull request builds reject a Windows executable whose product name or file version does not match the release metadata.
