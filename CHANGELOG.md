# Changelog

## [Unreleased]

### Fixed
- Per-node touch on the desktop **Without Mesh** device column, which never reached the hardware.
  Every receiver in that column was named in a `bOSC_v1_<Device>_<Node>` form - `bOSC_v1_Head_3`,
  `bOSC_v1_VestFront_7`, and so on - while the companion app subscribes to
  `bOSC/v2/<Device>/<Node>/<self|others>` and to a legacy `bHapticsOSC_Vest_Back_1` form. It
  matched neither, and unmatched OSC addresses are dropped without a log line, so an avatar built
  from this column felt punch impacts - those receivers are named separately - and nothing at all
  from being touched. All 140 parameters now use the v2 scheme, splitting self from others to
  match the `allowSelf` and `allowOthers` already set on each receiver.
- The setup pipeline destroying contact-compressor groups a user had placed by hand. The cleanup
  it ran on the default path swept the whole device subtree rather than the groups it had created,
  so an ordinary `CREATE VRCFURY SETUP` press took them with it.
- The `bHapticsOSC Integration` component being reported as an illegal component by the VRChat
  SDK. Anyone who uploaded before pressing the setup button got a red error naming the component
  the documentation had just told them to add.

### Upgrade notes
- **Only avatars built from the desktop "Without Mesh" column need anything.** That column is
  reached by unticking **Show mesh** on a device; the default is on. The desktop "With Mesh"
  column and both Quest columns were already on the v2 scheme and are untouched, as are punch
  haptics, which worked either way.
- **Updating the package is not enough.** Reimporting repairs the receivers on your avatar,
  because the rename changed only the prefabs' parameter names and left their GUIDs and structure
  alone. It does not touch the animator and expression-parameters assets under
  `Assets/bHapticsOSC/VRChat/Generated`, which were written once at setup time and still declare
  the old names. Nothing regenerates them on its own, so the avatar keeps uploading dead
  parameters until you re-run the setup and upload again. Follow
  [Regenerate an Existing Avatar Setup](docs/upgrading.md#regenerate-an-existing-avatar-setup).
- **Do not delete `bHapticsOSC VRCFury` to force a regeneration.** Your devices are parented under
  it, so deleting it destroys their positions, custom contact tags, punch receivers and compressor
  groups - and the next one-click setup then reseeds the default device set with **Show mesh** back
  on, quietly moving you to the other column. Re-running replaces the old VRCFury components by
  itself. Earlier versions of the upgrade guide said to delete it first; that instruction was
  wrong and has been corrected.
- **If you use "Consolidate contact receivers", re-run from the inspector rather than the one-press
  route.** That setting lives on the `bHapticsOSC Integration` component, which the setup destroys
  when it finishes, so a one-press re-run starts with it off and removes your compressor groups.
  Add the component, re-tick the option, then press `CREATE VRCFURY SETUP`. Afterwards, copy the
  rewritten `contact-compressor.json` into the companion app's `Config` folder again.
- Have the companion app running the next time you load the avatar in VRChat. It clears VRChat's
  cached per-avatar OSC config on avatar change, which is what makes VRChat publish the new
  parameter names.

## [2.4.0] - 2026-08-28

**This is a test build.** It is the first release of the Furroxide-maintained fork, published
so the whole chain - installing through VCC, setting an avatar up, installing the companion
app, and feeling haptics in VRChat - can be exercised end to end for the first time. Expect
rough edges, and please report anything that goes wrong at
https://github.com/furroxide/bHapticsVRChat/issues

### Added
- Contact Compressor package and standalone decoder. A dense grid of contact receivers is
  replaced at build time by six box receivers that encode where the touch happened, so the
  companion app is told *where you were touched* rather than *which motor fired*. The vest
  drops from 80 receivers to 6, well under VRChat's 32-contact performance budget, and the
  contact position becomes continuous instead of one motor at a time.
- A per-avatar contact manifest, written automatically next to the generated assets. It
  describes that avatar's own motor layout, because a manifest from a different avatar drives
  the wrong motors and fails silently.
- One-press avatar setup. With an avatar selected, a single action adds the component, picks a
  starting device set, scales every device to that avatar, and builds the VRCFury setup. A
  confirmation dialog names the devices, the platform, and anything it had to skip, and
  nothing happens until it is accepted. An avatar that already has devices keeps every choice
  and position rather than being reseeded.
- One-press companion app install, replacing the round trip through a browser, the Downloads
  folder and a file picker. The download is verified - transport, length, PE header and
  version resource - before the file is ever given the name the rest of the tool looks for.
- Auto-fit for all nine devices. Previously only the vest adapted; the other eight carried
  transforms authored for one reference avatar, so they sat at the right bone in the wrong
  size on anyone else.

### Changed
- The Setup Assistant now answers by observation what it used to ask the user to confirm by
  hand: whether bHaptics Player is installed and running, whether VRChat's OSC switch is on,
  and - when VRChat has loaded an avatar carrying this package's parameters - that the whole
  chain is working. That last one was previously only discoverable by wearing the headset and
  feeling nothing.
- The companion section offers one primary action chosen from the current state, rather than
  eight equal buttons, with the rest behind Other options.
- The window has a section for the avatar, which is the half of the journey it used to leave
  out entirely.

### Fixed
- The Setup Assistant reported "not located" while a companion app was installed and running.
  The process sweep matched the process name exactly, so a release named
  `bHapticsOSC_v2.2.1.exe` was never found, and build identity was an exact match against
  `ProductName`, so the official bHaptics build read as "not bHapticsOSC" rather than as the
  wrong build. Detection now classifies build lineage separately from version and explains
  that an upstream build has to be replaced, not updated.
- A second companion app holding the VRChat OSC port is now detected and can be closed from
  the window. While one is up, the other receives nothing.
- Punch receivers used a component VRChat strips from avatars, so the SDK refused the upload -
  and its Auto Fix removed the receivers while leaving behind a punch menu that drove nothing.
- Contact compression matched only one of the four shipped device prefab sets, so enabling it
  with the mesh-free or Quest prefabs attached a group that matched nothing and failed every
  avatar upload.
- The contact encoder could fabricate a touch spread from an uninitialised value, lighting
  every motor in a region at full intensity instead of a four-motor falloff.
- The bHapticsOSC Integration component deleted itself when added to the wrong object, which
  read as a broken package. It now explains the problem and offers to move itself to the
  avatar root.
- Creating the VRCFury setup is now a single undoable operation that rolls the avatar back if
  it fails, instead of leaving it half-built.
- The release pipeline rejected the very manifest it validates, and never published the
  Contact Compressor package that manifest depends on.

### Upgrade notes
- There is no previous release to upgrade from; this is the first one.
- Install through VCC from https://vpm.furroxide.dev/index.json, or import the
  `.unitypackage` if you are not using VCC. Do not use both formats in one project.
- The companion app is a separate Windows program. Let the Setup Assistant install it, or
  download `bHapticsOSC.exe` from this release.
- If you already run the official bHaptics `bHapticsOSC_v2.2.1.exe`, close it and replace it
  with this build. It is a different program, not an older version of this one, and it cannot
  decode the compressed contact parameters this package generates.

## [2.3.1] - 2026-07-18

The first public release of the Furroxide-maintained fork adds a non-destructive
VRCFury avatar workflow, configurable punch impact haptics, Quest-compatible
device assets, and reproducible Windows and Unity release artifacts.

### Added
- Non-destructive VRCFury avatar setup that generates the FX controller,
  expression parameters, menus, contact animations, and punch controls without
  replacing the avatar's existing FX controller.
- Punch impact haptics for the front and back vest, with light and hard impact
  bands, optional ripple across nearby motors, and in-game controls for enable,
  ripple, strength, and duration.
- Launcher configuration for punch enablement, ripple behavior, strength and
  duration multipliers, light and hard impact intensity and duration, and ripple
  delay.
- Quest/mobile device prefabs and materials, with separate PC and Quest device
  choices in the Unity inspector.
- Vest auto-fit based on humanoid bones and avatar bounds, while retaining
  manual position, rotation, and scale controls for final adjustments.
- Automatic cleanup of per-avatar generated assets after the corresponding
  `bHapticsOSC VRCFury` setup object is removed and its scene is saved and closed.
- Automated pull request builds for a single-file `bHapticsOSC.exe`, a
  VCC-compatible VPM ZIP, and a dependency-clean
  `bHapticsOSC-VRChat.unitypackage`, including temporary artifact links posted
  to the pull request.
- Automated GitHub releases from `main` with Windows, VPM, and legacy Unity
  artifacts, plus release metadata, version, changelog, line-ending, and
  tracked-binary validation.
- VRChat Creator Companion distribution through the
  `com.furroxide.bhaptics-vrchat` VPM package and a same-repository GitHub Pages
  listing, with a versioned VPM ZIP included in release and pull request builds.
- A Unity **bHapticsOSC > Setup Assistant** that verifies the portable Windows
  companion app's identity and version, remembers a located executable, launches
  it on request, and opens matching-version or latest-release downloads in the
  browser.
- A non-blocking pre-upload advisory, shown only for bHaptics-enabled avatars,
  that reports an incomplete VRCFury setup or a missing or outdated companion
  app without ever preventing the upload.
- Setup, upgrade, contribution, and Unity package export documentation.
- Furroxide maintainer credit in the VRChat radial menu and launcher control
  panel, with a GitHub profile link in the launcher.

### Changed
- Replaced the legacy Animator As Code V0 integration with VRCFury. VRCFury and
  the VRChat SDK are resolved as external VCC/VPM dependencies and are not
  bundled in the Unity package.
- Made VCC the recommended installation and update path for the Unity
  integration while retaining `bHapticsOSC-VRChat.unitypackage` as a fallback.
  Existing static package files migrate automatically without removing assets
  from the legacy `Generated` directory.
- Added the maintained launcher source as the `External/bHapticsOSC` submodule
  and removed the previously committed executable from source control.
- Pointed launcher metadata, source links, documentation, and latest-release
  downloads to the Furroxide-maintained forks.
- Packaged the launcher and its managed dependencies as one directly
  downloadable `bHapticsOSC.exe`.
- Added release guards that keep the Setup Assistant's fallback companion
  requirement aligned with package metadata and verify the built executable's
  `ProductName` and file version before publishing it.
- Added modern `bOSC/v2` and Quest `bOSC/v2m` device parameter handling while
  retaining the legacy OSC parameter paths.
- Updated the Unity project to `2022.3.22f1` with VRCFury `1.1341.0`, VRChat
  Avatars SDK `3.10.4`, and VPM Resolver `0.1.29` release metadata.

### Fixed
- Preserved generated avatar assets during scene unloads, unsaved closes,
  delete-and-undo operations, and while another setup still references them.
- Made legacy `ParameterExclusions.txt` migration work across published and
  fork-specific file GUIDs.
- Combined simultaneous legacy, v2, self, and others OSC sources per motor so
  one source turning off no longer clears haptics that remain active from
  another source.
- Reset punch menu state and active pulses safely when avatars change, reject
  invalid OSC values and motor indices, and clear active pulses immediately
  when punch haptics are disabled.
- Made release baseline detection fall back directly to the latest semantic tag
  when the requested ref has no `VERSION`, including safe handling of empty and
  all-zero push refs.

### Upgrade notes
- Existing users can replace only `bHapticsOSC.exe`; avatar regeneration is not
  required unless the Unity integration is also being updated.
- Avatar authors must install VRCFury through VCC before installing the Unity
  integration. To regenerate an existing avatar, remove its old generated
  setup, recreate the `bHapticsOSC VRCFury` setup, and upload the avatar again.
- Legacy Unity-package users can migrate by backing up and closing the project,
  then installing `com.furroxide.bhaptics-vrchat` through VCC. Do not keep both
  installation formats in the same project; generated assets are preserved.
