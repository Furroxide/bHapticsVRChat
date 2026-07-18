# Upgrading bHaptics VRChatOSC

Use this guide when updating the Windows app, the Unity avatar package, or an avatar that already has bHapticsOSC setup generated.

## Update the Windows App

1. Close any running copy of `bHapticsOSC.exe`.
2. Download the latest `bHapticsOSC.exe` from the [latest GitHub Release](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe).
3. Replace your old `bHapticsOSC.exe` with the new file.
4. Start bHaptics Player.
5. Run the new `bHapticsOSC.exe` and leave it open while using VRChat.

## Update the Unity Package

1. Back up or version-control your avatar project before importing a new package.
2. Open the avatar project through VRChat Creator Companion.
3. Resolve the project packages in VCC, including the VRChat Avatars SDK and VRCFury.
4. Confirm the project opens in Unity `2022.3.22f1`.
5. Import the newer `bHapticsOSC-VRChat.unitypackage`.
6. Let Unity finish compiling before changing the avatar setup.

The bHapticsOSC Unity package expects VRCFury to come from the VCC repository `https://vcc.vrcfury.com/`. Do not rely on old copied VRCFury files inside the Unity project.

## Regenerate an Existing Avatar Setup

1. Open the avatar scene or prefab in Unity.
2. Find the `bHapticsOSC VRCFury` object under the avatar.
3. Delete `bHapticsOSC VRCFury` to remove the old generated setup and generated assets.
4. Add the `bHapticsOSC Integration` component to the avatar root again if it is not already present.
5. Re-add or confirm the bHaptics device objects you want on the avatar.
6. Use `CREATE VRCFURY SETUP` in the bHapticsOSC Integration inspector.
7. Upload the avatar again through the VRChat SDK.

If you only updated `bHapticsOSC.exe`, you usually do not need to regenerate the avatar setup. Regenerate the setup when you import a newer Unity package, change the devices on the avatar, or need new generated VRCFury assets.

## Artifact Sources

- Use GitHub Release downloads for normal user updates.
- Pull request artifacts are temporary review builds and expire according to the repository retention policy.
- PR artifact names are `bHapticsOSC.exe` and `bHapticsOSC-VRChat.unitypackage`.
