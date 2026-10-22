# Release readiness audit — 2026-10-22

The reviewed fixes are prepared as release candidate `3.0.1`, with Contact Compressor `0.1.2`.
Local automated checks pass. Hosted CI and the physical smoke test are the remaining release
gates; see [the candidate checklist](release-smoke-test.md).

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
| .NET decoder and companion filesystem suite, Release | **107 passed**, 0 failed, 0 skipped |
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

## Before publication

1. **Version preparation:** `VERSION`, companion build/legacy metadata, the avatar manifest and
   Setup Assistant fallback are `3.0.1`; Contact Compressor is `0.1.2`. The changelog records the
   fixes and upgrade steps. [v3.0.0 is already published](https://github.com/Furroxide/bHapticsVRChat/releases/tag/v3.0.0).
2. **Source preparation:** the companion fixes are pushed at
   [`01583e1`](https://github.com/Furroxide/bHapticsVRChatLegacyLauncher/commit/01583e1a0bcc87a0b3dec2fe8f0ac2fb6aef2d1a)
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
