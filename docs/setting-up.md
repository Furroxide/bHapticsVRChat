# Setting Up bHaptics VRChatOSC

This guide covers the normal user setup for running bHaptics in VRChat and adding the bHaptics avatar integration to a VRChat avatar.

## Requirements

- [bHaptics Player](https://www.bhaptics.com/support/downloads)
- [bHaptics VRChatOSC](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe)
- Unity `2022.3.22f1` for avatar setup
- VRChat SDK 3.0 / Avatars SDK, installed through VRChat Creator Companion
- [VRCFury](https://vrcfury.com/), installed through VCC from `https://vcc.vrcfury.com/`
- bHaptics VRChatOSC, installed through VCC from `https://furroxide.github.io/bHapticsVRChat/index.json`

## Use an Existing bHaptics Avatar

1. Install and open bHaptics Player.
2. Pair or connect your bHaptics devices in bHaptics Player.
3. Download `bHapticsOSC.exe`, keep it in a writable folder where you want its adjacent `Config` folder to remain, and run it.
4. In VRChat, open **Action Menu → OSC** and turn **Enabled** on. See VRChat's [OSC overview](https://docs.vrchat.com/docs/osc-overview).
5. Leave both bHaptics Player and bHapticsOSC running while playing VRChat.
6. Enter the [bHaptics Avatar World](https://vrchat.com/home/world/wrld_7b1fed5e-50da-4263-b68a-81344fab1ac7), or use another avatar that already includes bHapticsOSC support.

Read [How to play VRChat with bHaptics](https://bhaptics.notion.site/How-to-play-VRChat-with-bHaptics-1226d5724b8b80229ab9e0001ab70b61) for the full end-user flow.

## Set Up the Windows Companion App from Unity

The VCC package does not install `bHapticsOSC.exe`; the app remains a separate,
portable Windows executable. In Unity, open **bHapticsOSC > Setup Assistant** to
open the **bHapticsOSC Setup** window.

- **Download matching version** starts the direct executable download for the
  companion version required by the installed Unity package.
- **Latest release** opens the latest GitHub Release in your browser.
- **Find automatically** searches your Downloads and Desktop folders, the usual
  install locations, and the folder next to the Unity project for an existing
  `bHapticsOSC` executable. It runs once by itself the first time the window
  reports that nothing was found, and can be cancelled at any point.
- **Locate existing app** lets you choose a previously downloaded
  `bHapticsOSC.exe` and verifies its product identity and file version.
- **Launch** starts the verified app only when you explicitly request it.
- **Stop the unsupported app** closes a running companion that is not a
  supported build, after asking for confirmation. This is only offered when such
  an app is actually running.
- **Recheck** refreshes the displayed app status after you download, replace, or
  start the executable.

The assistant never downloads, replaces, or launches software automatically, and
never closes a running app without asking first. Move the executable to its
intended permanent folder before locating it so its generated `Config` folder
remains alongside it.

### Replacing the Official bHaptics App

The companion app published by bHaptics as `bHapticsOSC_v2.2.1.exe` and similar
is a different build from the one this package needs: it speaks the same OSC
protocol, but it does not understand the compressed contact parameters the
avatar setup generates, so haptics will not respond. Its version number is not
comparable to this fork's, so it cannot be "updated" into a supported build.

When such an app is installed or running, the Setup Assistant reports
**Unsupported bHapticsOSC build** and names the product it found. Use **Download
the supported build**, then **Stop the unsupported app** so it releases the
VRChat OSC port, and finally **Find automatically** or **Locate existing app**
to point the assistant at the new executable. Two companion apps cannot share
the OSC port, so the assistant also warns when a second one is running
alongside a supported build.

On macOS and Linux, the Setup Assistant still opens for status guidance and
browser links. It explains that `bHapticsOSC.exe` must run on the Windows
machine where VRChat runs, and suppresses local executable and avatar-upload
warnings on those editor platforms.

## Add bHaptics to Your Avatar

1. Create or open your avatar project in VRChat Creator Companion.
2. Make sure the project uses Unity `2022.3.22f1`.
3. Add the VRCFury VCC repository: `https://vcc.vrcfury.com/`.
4. Add the [bHaptics VRChatOSC repository](https://furroxide.github.io/bHapticsVRChat/) to VCC. Its manual repository URL is `https://furroxide.github.io/bHapticsVRChat/index.json`.
5. From **Manage Project**, add or resolve the VRChat Avatars SDK and VRCFury, then install **bHaptics VRChatOSC** (`com.furroxide.bhaptics-vrchat`).
6. Open the Unity project.
7. Open **bHapticsOSC > Setup Assistant** and confirm the companion app is compatible. Use the explicit browser or locate actions if it is missing.
8. Select your avatar root object. It must have a `VRCAvatarDescriptor` and an `Animator`.
9. Add the `bHapticsOSC Integration` component to the avatar root.
10. Select each bHaptics device you want to support and use `+ ADD DEVICE (PC)` or `+ ADD DEVICE (Quest)`.
11. Position, rotate, scale, or `AUTO FIT` the device objects as needed.
12. Use `CREATE VRCFURY SETUP` in the bHapticsOSC Integration inspector.
13. Upload the avatar through the VRChat SDK.

The generated setup is stored under the `bHapticsOSC VRCFury` object on your avatar. After you delete that object and save and close its scene, Unity removes the generated bHapticsOSC assets for that setup.

On Windows, the package shows a non-blocking advisory before upload only for
bHaptics-enabled avatars when their VRCFury setup is incomplete, the located
companion app is missing, outdated or an unsupported build, or a second
companion app is competing for the OSC port. The advisory never prevents an
upload.

## Legacy Unity Package Fallback

If VCC/VPM installation is not available, download and import `bHapticsOSC-VRChat.unitypackage` from the latest GitHub Release after resolving the VRChat SDK and VRCFury in VCC.

Use only one installation format in a project. Do not import the legacy `.unitypackage` when `com.furroxide.bhaptics-vrchat` is already installed through VCC.

## Notes

- The VPM package and legacy Unity package do not bundle the VRChat SDK or VRCFury. Keep those dependencies managed by VCC.
- If Unity shows a missing VRChat SDK or VRCFury warning, resolve the project packages in VCC and reopen Unity.
- The Windows companion app must run on the same Windows machine as VRChat. The Setup Assistant remains available on macOS and Linux for guidance and download links.
- Keep `bHapticsOSC.exe` running while using bHapticsOSC avatars in VRChat.
