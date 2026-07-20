# bHaptics VRChatOSC

This package adds the bHaptics OSC integration and avatar setup tools to VRChat Avatar projects.

## Requirements

- Unity 2022.3
- VRChat Avatars SDK 3.10.x
- VRCFury 1.1341.0 or newer in the 1.x series

Install and update this package through the VRChat Creator Companion. The separate `bHapticsOSC.exe` desktop application is still required while using the avatar integration in VRChat.

After installing or updating, Unity opens the non-modal **bHapticsOSC Setup** assistant once for that package version. You can reopen it from **bHapticsOSC > Setup Assistant** to download the matching portable Windows app, locate an existing copy, launch a verified copy, or recheck its status. Downloads and launches only happen after an explicit click.

Before playing, also run bHaptics Player and enable OSC in VRChat from **Action Menu > OSC > Enabled**. On macOS and Linux, the assistant remains available for guidance; run the companion app on the Windows machine used for VRChat.

Generated animator and VRCFury assets are written to `Assets/bHapticsOSC/VRChat/Generated`, where they remain editable and separate from the installed package.

For setup instructions and the legacy `.unitypackage` fallback, see the [project documentation](https://github.com/furroxide/bHapticsVRChat#readme).

## License

This package is licensed under GPL-3.0-only. See [LICENSE.md](LICENSE.md).
