# Contact Compression (internal testing preview)

Replaces the per-motor contact receivers with a positional encoder. The avatar stops saying
*"motor 7 is being touched"* and starts saying *"you are being touched here"*, and the companion app
works out which motors that means.

**Status: preview.** The maths and the decoder are covered by tests and validated against VRChat's
own collision code. The Unity editor code compiles against the real SDK but has not yet been run
through an actual avatar build. Expect to find things.

## Why

| | Before | After |
|---|---|---|
| Vest receivers | 80 | 6 |
| Head receivers | 8 | 2 |
| Arm receivers | 12 each | 6 each |
| **Total (vest, head, arms)** | **112** | **20** |
| Motor resolution | one motor, on or off | continuous position, spread across neighbours |

VRChat rates an avatar Very Poor past 32 contacts, and has a
[known bug](https://feedback.vrchat.com/bug-reports/p/having-too-many-contacts-near-each-other-causes-receivers-to-output-the-wrong-va)
where clustered receivers report wrong values — which a 40-receiver vest reliably triggers.

Hands and feet are left alone deliberately: three motors behind six receivers, so encoding them
would cost as much as it saves. Punch receivers are also untouched.

## Setting it up

1. Install both packages from the VCC listing — `bHaptics VRChatOSC` now depends on
   `Contact Compressor`.
2. On the avatar's **bHapticsOSC Integration** inspector, tick **Consolidate contact receivers**.
   It shows the before/after receiver count for your current device selection.
3. Click **CREATE VRCFURY SETUP** as usual.
4. Select the `Vest` object under `bHapticsOSC VRCFury`. It now has a **Contact Compressor Group**.
   Press **Run** under *Round-trip check* — it simulates a touch on every motor using VRChat's own
   proximity maths and tells you whether each one decodes back to itself. It should report all
   points resolving cleanly.
5. Press **Export manifest…** and save it as `contact-compressor.json` into the companion app's
   `Config` folder (next to `Devices.cfg`).
6. Upload the avatar. Start `bHapticsOSC.exe` — it prints
   `[ContactCompressor] Loaded 4 region(s) driving 56 motor(s)` when it picks the manifest up.

If you used the stock prefabs without resizing anything, you can skip steps 4–5 and copy
[`Decoder/manifests/bhaptics-default.json`](../Decoder/manifests/bhaptics-default.json) instead —
it is generated from those prefabs. Scaling the vest to fit your avatar does **not** invalidate it,
because the manifest stores proportions rather than absolute sizes.

Without a manifest the app behaves exactly as before, so an uncompressed avatar is unaffected.

## What to look for

- **Position accuracy.** Have someone touch a known spot. Does the right part of the vest fire?
- **The new bit.** A touch should now feel like it lands *between* motors when it is between them.
  Previously it snapped to one. A palm should feel broader than a fingertip — collider size is
  recovered from the encoding and widens the spread.
- **Front vs back.** These share one region and are separated by depth; check a back touch does not
  fire the chest.
- **Two people at once.** One region resolves one contact point. When it detects two, it deliberately
  falls back to a region-wide buzz rather than inventing a phantom point between them. Confirm it
  feels like that rather than like a wrong location.
- **Performance rank.** Should drop substantially. Setting *Local Only* on the group takes contacts
  out of the rank entirely, at the cost of other players no longer seeing your TouchView meshes.

## Known gaps

- The Unity editor path has not been run yet. The build hook, the fitter and the emitter compile
  but are unexercised.
- Punch still uses its own 80 receivers. Consolidating those needs the app to correlate an impact
  with the live position, which is not built yet.
- Hands and feet stay on the per-motor path.
- Padding is specified in the prefab's local units. If you scale a device *down* a lot, the padding
  shrinks with it and large colliders may saturate — the inspector's round-trip check will say so.
- The companion app lives in a submodule (`External/bHapticsOSC`), so its changes are a separate
  commit in that repository.

## How it works

See the [Contact Compressor README](../Unity/Packages/com.furroxide.contact-compressor/README.md)
for the mechanism and the measurements behind it.
