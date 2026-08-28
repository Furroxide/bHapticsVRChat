# Upgrading bHaptics VRChatOSC

Use this guide when updating the Windows app, the Unity avatar package, or an avatar that already has bHapticsOSC setup generated.

## Update the Windows App

1. In Unity, open **bHapticsOSC > Setup Assistant** and review the version shown in the **bHapticsOSC Setup** window.
2. Use **Download matching version** to start the direct executable download required by the installed Unity package, or **Latest release** to open the newest GitHub Release in your browser.
3. Close any running copy of `bHapticsOSC.exe`, or use **Stop the unsupported app** when the assistant offers it.
4. Replace the old executable in its existing folder so the adjacent `Config` folder and your settings remain in place. If you use a different folder, choose **Find automatically** or **Locate existing app** again.
5. Use **Recheck** to verify the executable's product identity and file version.
6. Start bHaptics Player, then use **Launch** or start `bHapticsOSC.exe` yourself and leave it open while using VRChat.
7. In VRChat, confirm **Action Menu → OSC → Enabled** is on. See VRChat's [OSC overview](https://docs.vrchat.com/docs/osc-overview).

The Setup Assistant never downloads, replaces, or launches software
automatically, and never closes a running app without asking first. The direct
latest-download link remains available at
[`bHapticsOSC.exe`](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe).

### Coming from the Official bHaptics App

The official bHaptics releases (`bHapticsOSC_v2.2.1.exe` and older) are a
different build, not an older version of this one. They cannot decode the
compressed contact parameters this package generates, so haptics stay silent
even though everything looks connected. The Setup Assistant reports these as
**Unsupported bHapticsOSC build** rather than as an update, and offers to close
the running copy so it stops holding the VRChat OSC port. Download the
supported build, stop the old app, and point the assistant at the new
executable. The old executable can then be deleted; keep its `Config` folder if
you want to carry your device settings over.

## Update the Unity Package

1. Back up or version-control your avatar project before updating packages.
2. Open the avatar project through VRChat Creator Companion.
3. Make sure the VRCFury repository (`https://vcc.vrcfury.com/`) and bHaptics repository (`https://vpm.furroxide.dev/index.json`) are added to VCC.
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

**Do not delete `bHapticsOSC VRCFury` first.** Your devices are parented under that object, so
deleting it destroys their positions, their custom contact tags, their generated punch receivers
and any contact-compressor groups. Re-running the setup already replaces the old VRCFury
components by itself. Deleting the object is the way to *remove* the setup, not to regenerate it.

Before you start, check two things in the Hierarchy:

- The `bHapticsOSC VRCFury` object and every device under it must be **active**. The scan that
  adopts your existing devices ignores disabled objects, so a disabled device is skipped: it is
  left behind unadopted while the rest are picked up, and the new setup is built without it. If
  none of your devices can be adopted - all of them disabled, or the whole VRCFury object
  disabled - the setup treats the avatar as fresh and seeds the default device set instead.
- The devices must still be prefab instances. If you unpacked one it cannot be adopted; delete it
  and add the device again from the inspector instead.

Then pick a route.

### Route A - one press, refits everything

1. Open the avatar scene or prefab in Unity and select the avatar in the Hierarchy.
2. Either open **bHapticsOSC > Setup Assistant** and press **Set up \<avatar\> again**, or
   right-click the avatar and choose **bHapticsOSC > Set up this avatar**. Accept the prompt.
3. Save the scene or prefab.
4. Upload the avatar again through the VRChat SDK.

Route A re-fits every device to the rig, so any position or scale you tuned by hand is recomputed.
It also leaves **Consolidate contact receivers** switched off, because that setting lives on the
`bHapticsOSC Integration` component and the setup destroys that component when it finishes. If you
were using contact compression, use Route B.

### Route B - inspector, keeps your positions

1. Open the avatar scene or prefab in Unity.
2. Add the `bHapticsOSC Integration` component to the avatar root **if it is not already there**,
   and let the inspector pick up your existing devices. A completed setup removes the component,
   so normally it is absent - but if a previous run was cancelled or failed partway it may still
   be on the avatar. Nothing stops you adding a second one, and two of them means two inspectors
   competing over the same devices, so use the one that is there rather than adding another.
3. Re-tick **Consolidate contact receivers** if you were using it.
4. Press `CREATE VRCFURY SETUP`.
5. Save the scene or prefab.
6. Upload the avatar again through the VRChat SDK.

Route B does not auto-fit, so nothing moves.

### Afterwards

- A fresh folder appears under `Assets/bHapticsOSC/VRChat/Generated`, named after your avatar, and
  the VRCFury Full Controller now points at it. Your custom contact tags are carried over
  automatically - they are read back off the receivers before being re-applied.
- If you use contact consolidation, the setup rewrites
  `Assets/bHapticsOSC/VRChat/Generated/contact-compressor.json`. Copy it into the companion app's
  `Config` folder again; it describes this avatar's motor layout specifically, and the old copy is
  now out of date.
- Your previous generated subfolder is left behind and is no longer referenced. Once the new setup
  uploads correctly you may delete that one subfolder. Never delete the `Generated` folder itself,
  which also holds `contact-compressor.json`.

If you only updated `bHapticsOSC.exe`, you usually do not need to regenerate the avatar setup.
Regenerate the setup when you import a newer Unity package, change the devices on the avatar, or
need new generated VRCFury assets.

## Artifact Sources

- Use GitHub Release downloads for normal user updates.
- Pull request artifacts are temporary review builds and expire according to the repository retention policy.
- PR artifact names are `bHapticsOSC.exe`, `com.furroxide.bhaptics-vrchat-<version>.zip`, `package.json`, and `bHapticsOSC-VRChat.unitypackage`.
- Release and pull request builds reject a Windows executable whose product name or file version does not match the release metadata.
