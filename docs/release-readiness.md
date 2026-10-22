# Release readiness audit — 2026-10-22

The reviewed fixes are prepared as release candidate `3.0.1`, with Contact Compressor `0.1.2`.
Automated checks and expanded offline smoke tests pass. Require hosted CI on the final candidate
and actual-avatar, VRChat, and physical smoke-test sign-off; see
[the candidate checklist](release-smoke-test.md).

## Scope

Reviewed the decoder and manifest parser, shared contact math, Unity fitting/emission and avatar
preprocessing, setup/undo and generated assets, companion discovery/installation, the companion's
OSC and configuration boundaries, package contents and metadata, documentation, and CI/release
workflows. The companion is a separate repository at `External/bHapticsOSC`.

## Findings and fixes

| Priority | Finding | Resolution and evidence |
| --- | --- | --- |
| P1 | OSC avatar IDs were used as paths when deleting cached configs. Absolute paths or traversal could escape the avatar folder. A missing OSC directory also stopped the haptics worker. | Companion cleanup accepts only `avtr_` plus a canonical GUID and tolerates missing/inaccessible folders and deletion failures. Nine filesystem tests use isolated temporary profiles. |
| P1 | The installer rejected files below 1 MB, including the actual 602,112-byte published 3.0.0 executable. | Lowered the coarse size floor to 64 KB while retaining PE, exact-version, product, and content-length checks. Windows Unity tests verify the actual built executable and reject a mismatched version. |
| P1 | Releases and artifact-ready comments did not depend on regression tests. | Added a shared workflow running decoder/companion and both Unity package suites. Publication and the comment depend on it. Result validation rejects absent, failed, inconclusive, or empty test assemblies. |
| P1 | Malformed JSON, overflowing floats, deeply nested data, invalid geometry/points, and duplicate region IDs could corrupt decoding or terminate initialization. | Added strict JSON syntax/type/finite-number checks, bounded nesting, and decoder layout validation. Regression tests and merged-executable smoke tests cover rejection and graceful startup. |
| P1 | Malformed TOML configuration prevented startup or terminated the file-watcher thread during live reload. | Reject invalid files without overwriting them, retain usable configuration, restore watcher handling, and accept a subsequent correction. Regression and isolated merged-executable tests cover startup and reload. |
| P1 | The bundled Player WebSocket parser assumed one TCP read returned a complete header, mask, length or payload, dropping valid segmented traffic. | Patch the compatible vendored transport to read until complete or EOF, retaining upstream attribution. Capped-stream sync/async regressions and a segmented simulated Player test cover the actual failure. |
| P1 | Multiple groups could claim the same receivers; source/frame references could point outside the avatar. | Preprocessing rejects these layouts before emitting or removing receivers. Unity regressions check duplicate regions, overlapping roots, and external references. |
| P1 | A generated FX controller could remain disabled; reused expression parameters could retain saved/default active contacts. | Enable custom animation layers and expressions, clear saved/default contact values, and preserve the original expression asset. Verified against real Unity/SDK objects. |
| P2 | A failed setup could include an earlier operation in its undo rollback. | Start a new undo group for both setup entry points. Existing cleanup/undo regression coverage remains passing. |
| P2 | Legacy installations did not detect an independently installed Contact Compressor. | Add its project-define fallback alongside VRCFury; document the optional dependency. Recompiled and exported the legacy layout. |
| P2 | Contact Compressor VPM and legacy distribution paths omitted a license copy. | Include GPL text and Unity metadata. The legacy license uses a distinct stable GUID to avoid a VPM migration collision. |
| P2 | Release lookup treated authentication/service failures as evidence that a release was absent. | Only HTTP 404 starts a new release. Mocked checks cover missing release, draft, published release, HTTP 403, and network failure. |
| P2 | The fork-PR artifact comment omitted the compressor dependency ZIP. | Include the same dependency download offered by same-repository PR artifacts. Updated upgrade documentation. |

Existing local fixes were preserved and included in validation: installer update-pump registration,
atomic executable replacement and exact version validation, support for smaller valid device plans,
invalid/duplicate group rejection, complete manifest export, and safe handling of newer manifests
during companion initialization. These changes were already present when the audit started.

## Verification

| Check | Result |
| --- | --- |
| .NET decoder, companion configuration/filesystem and transport suite, Release | **198 passed**, 0 failed, 0 skipped |
| Unity 2022.3.22f1, SDK 3.10.4, both package EditMode assemblies on Windows | **239 passed**, 0 failed, 12 expected prefab-column skips |
| Installer request completion/cancellation and actual executable verification | Passed in the Windows Unity run |
| Companion Release build and ILRepack | Passed; single executable, expected product/version, no loose DLLs |
| Merged executable initialization: missing, valid, newer-version, deeply nested manifest | Passed in isolated .NET Framework processes; no OSC or hardware connection started |
| NuGet known-vulnerability lookup, companion solution and test project, including transitive dependencies | No known vulnerable packages reported by the configured NuGet source |
| Both VPM archives | Built twice; repeated builds were byte-identical |
| Legacy Unity package | Compiled and exported, 203 assets; extracted archive confirms the final installer, complete license, and exclusion of generated assets/SDK packages |
| Default decoder manifest | Regeneration matches the shipped prefab geometry |
| GitHub Actions workflows | `actionlint` passed; workflow failure-path fixtures passed |
| Release metadata | Candidate `3.0.1` passes release validation against `origin/main`; the avatar dependency requires compressor `>=0.1.2 <1.0.0` |
| Whitespace, package Unity metadata, tracked build-artifact and targeted credential-pattern checks | Passed; these checks are not a comprehensive secret or dependency security assessment |

ILRepack still reports duplicate icon/resource warnings from merged libraries. Product/version
inspection, standalone initialization, and executable verification all pass. The C# documentation
warnings encountered during the baseline decoder build were fixed.

Generated archives, executable, TRX/XML reports, vulnerability responses, smoke fixtures, and logs
are under the ignored `artifacts/release-audit/` directory. Files from the initial audit retain
versions `3.0.0`/`0.1.1`; use the `3.0.1` candidate CI run's artifacts for release smoke testing.

## Expanded candidate smoke tests

Tested the exact artifacts from [CI run 39812188376](https://github.com/Furroxide/bHapticsVRChat/actions/runs/39812188376),
parent `5d7672d`, companion `01583e1`, before the configuration-recovery follow-up. The unchanged
Unity package payloads passed the following checks in disposable projects:

- 38 artifact checks: all 445 VPM files matched committed sources; legacy payloads, versions,
  dependencies, unique GUIDs, full licenses, archive paths and executable identity passed.
- Hosted reports contained 219 Unity passes, 32 expected skips (20 Windows-only and 12 missing
  prefab-column cases), and 107 .NET passes. The separate local Windows suite covered the
  Windows-only paths as recorded above.
- 32 additional Unity smoke scenarios passed using a valid SDK robot humanoid: both desktop
  mesh variants, smaller/all-device selections, Undo/Redo, repeated placement, compression
  roundtrips, full SDK/VRCFury preprocessing and source-asset preservation. Native Windows and
  Android asset bundles built. Legacy import and setup passed with and without the compressor.
- 9 live-editor installer scenarios passed against a local HTTP server: new install and atomic
  replacement, wrong-version/length/non-PE rejection, truncated transport, in-flight cancel and
  cleanup, retry and locked-target preservation. Tests used workspace files and restored the
  exact remembered-path preference; no AppData installation or public release download ran.
- Live loopback OSC tests passed 201 assertions (including 44 baseline assertions), covering
  all 70 v2 motor addresses and all 56 shipped compressed-layout points, release clearing,
  panel/limb routing, punch controls, invalid packets, avatar reset and AFK behavior. A simulated
  Player passed 15 more assertions against actual serialized motor frames and zero releases.
  No real Player or hardware was connected by these tests.

The configuration startup/reload crash and segmented WebSocket read defect were discovered by
this expanded smoke run and fixed afterward. Full logs, isolated harnesses, package hashes, and
follow-up results are retained under the ignored `artifacts/smoke-3.0.1/` directory.
Offline preprocessing and bundle builds do not
prove private SDK upload, published-avatar OSC configuration, or contact behavior inside VRChat.

The follow-up adds 22 configuration regressions and 69 transport regressions to the original
107-test .NET suite. Config recovery preserves usable values and pending on-disk edits, including
edits not yet observed by the watcher. The compatible vendored transport retains all 98 upstream
public signatures and embeds its full MIT license/provenance in the merged executable.

## Before publication

1. **Version preparation:** `VERSION`, companion build/legacy metadata, the avatar manifest and
   Setup Assistant fallback are `3.0.1`; Contact Compressor is `0.1.2`. The changelog records the
   fixes and upgrade steps. [v3.0.0 is already published](https://github.com/Furroxide/bHapticsVRChat/releases/tag/v3.0.0).
2. **Source preparation:** the companion fixes are pushed at
   [`f0cbc7c`](https://github.com/Furroxide/bHapticsVRChatLegacyLauncher/commit/f0cbc7cf570c991d45e8752dd4def0f786e16a30)
   on `release/3.0.1` and pinned in the parent candidate. Companion CI uses the same .NET SDK and
   artifact actions as the parent pipeline. Filesystem regressions compile the pinned source.
3. **Hosted CI:** require green checks on the exact candidate being reviewed: decoder/companion,
   both Unity assemblies, executable/package builds, release metadata, and legacy export. Linux
   CI skips Windows-only tests; the full Windows suite is also checked locally.
4. **Manual sign-off:** complete [the VRChat/hardware checklist](release-smoke-test.md) before
   merging the release candidate. Automatic download and public VCC listing checks follow
   publication, because their matching-version URLs do not exist for an unpublished branch.

## Retained limits

- Contact compression remains an optional preview. One region resolves one contact point;
  multiple contacts can fall back to a region-wide response. Punch, hands, and feet retain their
  existing receivers.
- Padding follows the device's scale; heavily downscaled devices can saturate for large colliders.
- The companion loads one layout manifest. Switching between differently fitted avatars needs
  the matching file and a companion restart.
- The Windows companion requires the same PC as VRChat; Quest-compatible avatar assets do not
  provide a standalone Quest companion.
- Existing `Unity/ProjectSettings/ProjectSettings.asset` and
  `Unity/ProjectSettings/SceneTemplateSettings.json` changes were left as found.

The avatar-ID/config convention was checked against the current
[VRChat OSC avatar documentation](https://docs.vrchat.com/docs/osc-avatar-parameters).
