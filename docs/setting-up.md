# Setting Up bHaptics VRChatOSC

This guide covers the normal user setup for running bHaptics in VRChat and adding the bHaptics avatar integration to a VRChat avatar.

## Requirements

- [bHaptics Player](https://www.bhaptics.com/support/downloads)
- [bHaptics VRChatOSC](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe)
- Unity `2022.3.22f1` for avatar setup
- VRChat SDK 3.0 / Avatars SDK, installed through VRChat Creator Companion
- [VRCFury](https://vrcfury.com/), installed through VCC from `https://vcc.vrcfury.com/`
- `bHapticsOSC-VRChat.unitypackage` when you are adding the integration to your own avatar

## Use an Existing bHaptics Avatar

1. Install and open bHaptics Player.
2. Pair or connect your bHaptics devices in bHaptics Player.
3. Download and run `bHapticsOSC.exe`.
4. Leave both bHaptics Player and bHapticsOSC running while playing VRChat.
5. Enter the [bHaptics Avatar World](https://vrchat.com/home/world/wrld_7b1fed5e-50da-4263-b68a-81344fab1ac7), or use another avatar that already includes bHapticsOSC support.

Read [How to play VRChat with bHaptics](https://bhaptics.notion.site/How-to-play-VRChat-with-bHaptics-1226d5724b8b80229ab9e0001ab70b61) for the full end-user flow.

## Add bHaptics to Your Avatar

1. Create or open your avatar project in VRChat Creator Companion.
2. Make sure the project uses Unity `2022.3.22f1`.
3. Add the VRCFury VCC repository: `https://vcc.vrcfury.com/`.
4. Add or resolve the VRChat Avatars SDK and VRCFury packages in VCC.
5. Open the Unity project.
6. Import `bHapticsOSC-VRChat.unitypackage`.
7. Select your avatar root object. It must have a `VRCAvatarDescriptor` and an `Animator`.
8. Add the `bHapticsOSC Integration` component to the avatar root.
9. Select each bHaptics device you want to support and use `+ ADD DEVICE (PC)` or `+ ADD DEVICE (Quest)`.
10. Position, rotate, scale, or `AUTO FIT` the device objects as needed.
11. Use `CREATE VRCFURY SETUP` in the bHapticsOSC Integration inspector.
12. Upload the avatar through the VRChat SDK.

The generated setup is stored under the `bHapticsOSC VRCFury` object on your avatar. Deleting that object removes the VRCFury setup and the generated bHapticsOSC assets for that setup.

## Notes

- The Unity package does not bundle the VRChat SDK or VRCFury. Keep those dependencies managed by VCC.
- If Unity shows a missing VRChat SDK or VRCFury warning, resolve the project packages in VCC and reopen Unity.
- Keep `bHapticsOSC.exe` running while using bHapticsOSC avatars in VRChat.
