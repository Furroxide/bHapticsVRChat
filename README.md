# bHaptics VRChatOSC
This project is designed to use bHaptics devices in VRChat.

Please read **[How to play VRChat with bHaptics](https://bhaptics.notion.site/How-to-play-VRChat-with-bHaptics-1226d5724b8b80229ab9e0001ab70b61)** before attempting to use.

### Files
- [bHaptics Player](https://www.bhaptics.com/support/downloads)
- [bHaptics VRChatOSC](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe)
- [bHaptics VRChatOSC VCC Repository](https://furroxide.github.io/bHapticsVRChat/)

### Guides
- [Setting Up](docs/setting-up.md)
- [Upgrading](docs/upgrading.md)

### Quick Guide
- **Run** both [bHaptics Player](https://www.bhaptics.com/support/downloads) and [bHaptics VRChatOSC](https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe).
- **Enable OSC** in VRChat from **Action Menu → OSC → Enabled**. See VRChat's [OSC overview](https://docs.vrchat.com/docs/osc-overview).
- **Enter** [bHaptics Avatar World](https://vrchat.com/home/world/wrld_7b1fed5e-50da-4263-b68a-81344fab1ac7), or **Update** your avatar by referring to [How to Upload an Avatar with bHaptics Devices (PC)](https://bhaptics.notion.site/How-to-Upload-an-Avatar-with-bHaptics-Devices-PC-c0479c68b8984b9d9048423b8c44f503) / [How to Upload an Avatar with bHaptics Devices (Quest)](https://bhaptics.notion.site/How-to-Upload-an-Avatar-with-bHaptics-Devices-Quest-1356d5724b8b8090bae4e89cae7eb696).
  - This project uses [VRCFury](https://vrcfury.com/) for non-destructive avatar integration. Add the VRCFury VCC repository (`https://vcc.vrcfury.com/`) first.
  - Add the [bHaptics VRChatOSC VCC repository](https://furroxide.github.io/bHapticsVRChat/) and install `bHaptics VRChatOSC` from **Manage Project** in VCC.
  - In Unity, open **bHapticsOSC > Setup Assistant**. Use **Locate existing app** for a downloaded `bHapticsOSC.exe`, or use **Download matching version** to start that download directly. **Latest release** opens in your browser. The assistant never downloads or starts an app automatically.
  - In Unity, use **Create VRCFury Setup** from the bHapticsOSC Integration inspector. The setup is contained under the `bHapticsOSC VRCFury` object; after deleting that object and saving and closing its scene, the generated assets are removed.

### License
bHaptics VRChatOSC is licensed under the GPL-3.0 License. 
- This project is based on bHapticsOSC.
  - bHapticsOSC is licensed under the GPL-3.0 License.
- Third-party tools:
  - [VRCFury](https://vrcfury.com/) is an external VCC dependency and is not redistributed with this repository.

### Export Unity Packages
- From PowerShell: run `.\Tools\Build-VpmPackage.ps1` to create the VCC/VPM ZIP.
  - Default output: `dist\com.furroxide.bhaptics-vrchat-<version>.zip`.
- From PowerShell: run `.\Tools\Export-UnityPackage.ps1`.
  - Default output: `dist\bHapticsOSC-VRChat.unitypackage`.
  - The script stages the canonical VPM content in the legacy `Assets` layout before asking Unity to export it.
  - Unity must not already have the project open when using the CLI export.
  - The `.unitypackage` is a fallback for projects that do not use the VPM package. Do not install both formats in one project.
  - VRChat SDK and VRCFury remain external VCC/VPM dependencies and are not bundled.

### Build Artifacts
- Merges and direct pushes to `main` publish a GitHub Release automatically when `VERSION`, `CHANGELOG.md`, package metadata, the Setup Assistant's fallback requirement, and the Windows executable's product/file version agree on a new version.
- Before the first VCC release, set the repository's **Settings → Pages → Source** to **GitHub Actions**. The successful release then publishes the VPM listing automatically.
- The user-facing download link remains `https://github.com/furroxide/bHapticsVRChat/releases/latest/download/bHapticsOSC.exe`.
- Pull requests targeting `main` build temporary artifacts for review automatically:
  - `bHapticsOSC.exe` as a single packaged Windows executable.
  - `com.furroxide.bhaptics-vrchat-<version>.zip` for VCC/VPM distribution.
  - `package.json` as the standalone VPM manifest.
  - `bHapticsOSC-VRChat.unitypackage` from the Unity project.
- Pull requests targeting `main` must bump `VERSION` and add a matching top `CHANGELOG.md` entry before merge.
- The PR artifact workflow updates one sticky pull request comment with artifact links after a successful automatic or manual build.
- Unity package CI uses GameCI with Unity `2022.3.22f1`. Configure `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` for the `unity-pr-artifacts` environment as required by your Unity license type.

### Contributing
- See [CONTRIBUTING.md](CONTRIBUTING.md) for the cross-platform line-ending policy and repository hygiene guidelines.

### Links
- [How to play VRChat with bHaptics](https://bhaptics.notion.site/How-to-play-VRChat-with-bHaptics-1226d5724b8b80229ab9e0001ab70b61)
- [bHaptics Avatar World](https://vrchat.com/home/world/wrld_7b1fed5e-50da-4263-b68a-81344fab1ac7)
- [bHaptics Official Website](https://www.bhaptics.com)

### Featured Avatars
- V2(PC Only)
    - [Angry](https://vrchat.com/home/avatar/avtr_339ec708-e98b-4126-9d94-28a1bdc86a02)
    - [Kyle](https://vrchat.com/home/avatar/avtr_20a3eb95-3bed-4266-8d15-63fba1a621bb)
    - [Cool Banana](https://vrchat.com/home/avatar/avtr_e180f519-bb64-4f49-891a-4387d21fc722)
    - [Robin](https://vrchat.com/home/avatar/avtr_7dcf8a1d-eff9-4bc1-8cc8-d28b53d229c0)
    - [Sally](https://vrchat.com/home/avatar/avtr_ba91803b-b4ef-4a72-87db-e2cff5c583d2)
- V2M(Quest & PC)
    - [Angry](https://vrchat.com/home/avatar/avtr_30c7a479-2889-4713-a3c8-b81c21ef1543)
    - [Kyle](https://vrchat.com/home/avatar/avtr_bb7d5b0a-ad9f-4594-ab21-b2d0b51c87d9)
    - [Cool Banana](https://vrchat.com/home/avatar/avtr_a7ec00b0-e94a-4cec-b1fe-f5abff08683e)
    - [Robin](https://vrchat.com/home/avatar/avtr_086453dd-d275-4b46-94c9-02aa0d35272d)
    - [Sally](https://vrchat.com/home/avatar/avtr_7cf7dda5-59c4-47a5-8f81-6304290d3867)
