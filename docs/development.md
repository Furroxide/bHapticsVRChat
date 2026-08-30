# Development

This guide covers the local build, test, packaging, and release checks for bHaptics VRChatOSC. Run commands from the repository root in PowerShell unless a section says otherwise.

## Prerequisites

- Git with submodule support
- .NET 8 SDK for the Windows companion build
- .NET 9 SDK for the Contact Compressor decoder tests
- Unity `2022.3.22f1` for package compilation, EditMode tests, and legacy package export
- PowerShell for the repository tooling

## Clone the complete repository

The Windows companion is a submodule, so clone recursively:

```powershell
git clone --recurse-submodules https://github.com/furroxide/bHapticsVRChat.git
Set-Location bHapticsVRChat
```

For an existing clone, initialize or refresh its submodules before building:

```powershell
git submodule update --init --recursive
```

`External/bHapticsOSC` is a separate repository. Commit companion changes inside that submodule before updating its commit pointer in this repository.

## Build and test

### Windows companion (.NET 8 SDK)

```powershell
dotnet msbuild External\bHapticsOSC\bHapticsOSC.sln /m /restore /p:Configuration=Release /p:Platform="Any CPU"
```

The packaged executable is written to `External\bHapticsOSC\Output\Release\bHapticsOSC.exe`.

### Contact Compressor decoder (.NET 9 SDK)

```powershell
dotnet test Decoder\Furroxide.ContactCompressor.Decoder.Tests\Furroxide.ContactCompressor.Decoder.Tests.csproj
```

### Unity EditMode tests

1. Open the `Unity` project in Unity `2022.3.22f1` and allow package resolution and compilation to finish.
2. Open **Window → General → Test Runner**.
3. Select **EditMode** and run all tests.
4. Confirm both `bHapticsOSC.VRChat.Editor.Tests` and `Furroxide.ContactCompressor.Editor.Tests` complete successfully.

## Validate release metadata

Ordinary documentation, CI, tooling, and feature pull requests keep the current release version. Validate them with the unchanged-version allowance:

```powershell
.\Tools\Validate-ReleaseMetadata.ps1 -BaseRef origin/main -AllowUnchangedVersion -SkipRemoteReleaseCheck
```

For a pull request that intentionally publishes a new release, omit `-AllowUnchangedVersion`:

```powershell
.\Tools\Validate-ReleaseMetadata.ps1 -BaseRef origin/main -SkipRemoteReleaseCheck
```

The validator checks strict versions, mirrored companion and package metadata, dependency ranges, package identities, the matching changelog entry, and release baselines. CI performs the remote release-state check that `-SkipRemoteReleaseCheck` omits locally.

## Build distribution packages

### VCC/VPM packages

Build the avatar package:

```powershell
.\Tools\Build-VpmPackage.ps1
```

Build its Contact Compressor dependency:

```powershell
.\Tools\Build-VpmPackage.ps1 `
  -PackagePath Unity/Packages/com.furroxide.contact-compressor `
  -ExpectedPackageName com.furroxide.contact-compressor
```

Both scripts write deterministic ZIP archives under `dist`.

### Legacy Unity package

Close the Unity project before running the exporter, then run:

```powershell
.\Tools\Export-UnityPackage.ps1
```

The script stages the canonical VPM content in a temporary legacy `Assets` layout and invokes Unity `2022.3.22f1`. If Unity cannot be found automatically, pass `-UnityPath 'C:\Path\To\Unity.exe'`.

The legacy `.unitypackage` is a fallback for projects that cannot use VCC/VPM. Do not install it alongside the VPM package.

## Expected artifacts

| Artifact | Local path or source |
| --- | --- |
| Windows companion | `External\bHapticsOSC\Output\Release\bHapticsOSC.exe` |
| Avatar VPM package | `dist\com.furroxide.bhaptics-vrchat-<version>.zip` |
| Contact Compressor VPM package | `dist\com.furroxide.contact-compressor-<version>.zip` |
| Standalone VPM manifest | `Unity\Packages\com.furroxide.bhaptics-vrchat\package.json` |
| Legacy Unity package | `dist\bHapticsOSC-VRChat.unitypackage` |
| Unity export log | `dist\export-unitypackage.log` |

Pull requests targeting `main` build temporary review artifacts and update a sticky pull-request comment with their download links. Published releases contain the executable, both VPM archives, the avatar package manifest, and the legacy Unity package.

## Release-only version bumps

Only bump `VERSION` and mirrored release metadata when a pull request is intentionally publishing a new release. Ordinary code, documentation, CI, and tooling changes must leave the release version unchanged and use `-AllowUnchangedVersion` during validation.

For a release pull request:

1. Set `VERSION` to a new strict `X.Y.Z` version.
2. Add the matching top entry to `CHANGELOG.md`.
3. Keep the companion build metadata, legacy Unity package metadata, VPM manifest version, and Setup Assistant fallback requirement aligned with `VERSION`.
4. If `Unity/Packages/com.furroxide.contact-compressor` changed, bump that package's independent version; otherwise leave it unchanged.
5. Run the release form of `Validate-ReleaseMetadata.ps1`, all tests, and both packaging workflows before merge.

A push to `main` publishes only when the metadata describes a new, unreleased version. A push that keeps the current version does not create another release.
