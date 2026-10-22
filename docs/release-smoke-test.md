# VRChat and hardware smoke test — 3.0.1

Test the exact commit built by the `release/3.0.1` CI run, using companion `3.0.1`, avatar package
`3.0.1`, and Contact Compressor `0.1.2`. Record the commit/run, Windows version, Unity/SDK/VRCFury
versions, bHaptics Player version, avatar ID, device models, and a pass/fail result for each item.
Mark unavailable gear or platforms as **not tested**.

The [offline smoke results](release-readiness.md#expanded-candidate-smoke-tests) already cover
clean local imports, setup/Undo, both mesh variants, compression math, SDK preprocessing,
Windows/Android bundle serialization, live-editor installer recovery, baseline/compressed OSC routing and simulated Player
frames. Repeat the relevant checks below on the actual avatar and paired devices: those offline
results do not establish fitting, uploaded-avatar behavior, tactile mapping or real recovery.

## Candidate installation, before publication

- [ ] Download the executable and both VPM ZIP artifacts from the same successful CI run. Extract
  the outer GitHub artifact downloads first; retain the actual package ZIPs and executable.
- [ ] Use a disposable VCC avatar project in Unity `2022.3.22f1`. Resolve Avatars SDK `3.10.x` and
  VRCFury `>=1.1341.0 <2.0.0` through VCC before opening the candidate packages.
- [ ] With Unity closed, extract the candidate package ZIPs into
  `Packages/com.furroxide.bhaptics-vrchat` and `Packages/com.furroxide.contact-compressor`, so each
  folder has its own `package.json` directly inside. Open Unity and confirm versions `3.0.1` and
  `0.1.2`, resolved dependencies, and no compilation errors.
- [ ] Open Setup Assistant and use **Locate existing app** for the CI executable. Confirm it
  identifies a supported `3.0.1` companion. Test the automatic GitHub download after publication;
  its matching-version URL is unavailable while `3.0.1` is only a branch candidate.
- [ ] Open bHaptics Player, pair the available devices, and confirm each device responds to its
  Player test. Start the candidate companion; confirm its banner says `3.0.1` and it stays running.

## Avatar setup and upload

- [ ] On a known humanoid avatar, run Setup Assistant with vest, head, and available arm devices.
  Confirm devices fit the intended bones and existing controller/menu content remains usable.
- [ ] Undo once: the generated setup is removed/restored as one operation, and the edit made
  immediately before setup remains. Redo or set up again successfully.
- [ ] Repeat with a smaller supported selection, such as one vest. Setup must succeed without
  requiring all default devices. Re-run an existing setup and check device placement is retained.
- [ ] Test both desktop **With Mesh** and **Without Mesh** variants on separate avatar copies.
  Confirm no duplicate generated roots, missing scripts, or unexpected receiver groups.
- [ ] Upload a private test avatar through the SDK and wear that uploaded avatar in VRChat.
  Build & Test alone does not exercise published-avatar OSC config generation.

## Baseline contacts, with compression off

- [ ] Enable OSC in VRChat and verify the companion observes the avatar/change and contact
  parameter traffic. Check that supported paired hardware responds while the avatar is worn.
- [ ] Touch the vest at front, back, upper/lower, and left/right positions. The correct panel and
  location respond; moving across positions does not leave the previous location active.
- [ ] Check head, each arm, and any supported hands/feet independently. Left/right and limb
  mappings must not swap. Record which device models were actually checked.
- [ ] Check both self-touch and touch by another player where the configured tags/permissions
  allow it. Remove contact and confirm output stops. TouchView feedback matches where applicable.
- [ ] Test light/hard punch input, front/back mapping, and the enabled/ripple/strength/duration
  controls. Disabling punch stops punch output while ordinary touch still works.

## Optional contact compression

- [ ] On a separate avatar copy, enable **Consolidate contact receivers**, create setup, and
  run the inspector's round-trip checks. Check receiver counts against the actual selected gear;
  punch, hands, and feet retain their existing receivers.
- [ ] Copy that avatar's generated `contact-compressor.json` into the candidate executable's
  `Config` folder and restart the companion. Confirm it reports the expected regions/motors.
- [ ] Upload and wear the compressed avatar. Repeat front/back, left/right, edge, and individual
  motor-location checks. A front touch must not activate the back panel.
- [ ] Sweep between neighboring points and compare a small fingertip collider with a larger
  hand collider. Output should move/spread as expected; check for edge saturation or wrong motors.
- [ ] Test simultaneous touches in one region and in separate regions. One region can fall back
  to a region-wide response for ambiguous touches; a precise phantom location is a failure.
- [ ] Switch avatars and return. Contacts reset, and no old motor remains active. Use the matching
  manifest and restart the companion when switching to a differently fitted layout.

## Recovery and alternate installation

- [ ] Restart the companion and bHaptics Player, reconnect a device, and toggle OSC off/on. Confirm
  recovery and inspect for stuck output, exceptions, duplicate processes, or a bound-port error.
  Also leave the real Player connection running during a normal play session. The simulated
  transport checks cover segmented TCP reads, but real connection loss or failed recovery still
  blocks sign-off.
- [ ] Test AFK/seated/in-station behavior against the configured reactivity settings, then return
  to normal play. Close the companion and confirm output stops.
- [ ] With a temporary backup of the candidate config, try a malformed/newer manifest. The
  companion reports the rejected manifest and remains running; restore the valid file and restart.
- [ ] In a separate project, import the legacy `.unitypackage` after resolving the SDK/VRCFury.
  Confirm setup works; install Contact Compressor `0.1.2` separately if testing legacy compression.
  Verify the archive includes its license. Keep legacy and VPM avatar formats in separate projects.
- [ ] If supporting a Quest-compatible avatar build, verify its upload/build and intended
  PC-connected use separately. These assets do not provide a standalone Quest companion.

## After publication

- [ ] From VCC, install/upgrade using the public listing. Confirm avatar `3.0.1` resolves compressor
  `0.1.2` or newer, and a project upgrading from `3.0.0` receives the updated dependency.
- [ ] Use Setup Assistant's automatic companion installation. Download progresses through checking,
  downloading, verification, and completion; the installed file identifies as `3.0.1`.
- [ ] Cancel a download and retry. The editor stays responsive and an existing working executable
  remains intact. Check stopped-app upgrade and the useful failure message for a running/locked app.
- [ ] A located `3.0.0` companion is reported as outdated. Launch the installed `3.0.1` app, repeat
  one front/back and left/right contact test, and confirm the public downloads/listing are usable.

Any failed core installation, upload, mapping, release-version, or stuck-output check blocks
sign-off. Keep optional compression failures labeled and resolve them before claiming that mode
is validated. Attach the recorded results and relevant companion/Unity logs to the release review.
