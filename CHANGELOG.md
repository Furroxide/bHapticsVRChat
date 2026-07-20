# Changelog

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
