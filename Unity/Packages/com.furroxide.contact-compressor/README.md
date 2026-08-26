# Contact Compressor

Collapses a dense grid of VRChat Contact Receivers into a handful of box receivers that encode
**where** the contact happened, as floats, instead of **whether** each point was hit.

You keep authoring one receiver per point. At build time they are replaced. A 40-point vest panel
becomes 6 receivers; a 136-receiver haptic rig becomes about 30.

Nothing in this package knows what a haptic motor is. It works for any dense contact grid whose
values leave the avatar over OSC.

---

## Why

VRChat's avatar performance ranks allow **8 contacts for Excellent and 32 for Poor** on PC (2 and 16
on mobile). Anything that needs per-point resolution — a haptic suit, a DIY vibration rig, a
touch-reactive shader — blows straight past that. The usual answer is one receiver per point, which
also runs into a [tracked, unfixed
bug](https://feedback.vrchat.com/bug-reports/p/having-too-many-contacts-near-each-other-causes-receivers-to-output-the-wrong-va)
where many contacts clustered together start reporting **wrong values**.

Encoding the position instead of the point is strictly more information for far fewer receivers, and
it is continuous rather than on/off.

## How it works

VRChat SDK 3.10.4 (June 2026) added box-shaped contacts, and for box *receivers* an option called
**Use Face Proximity**. A receiver with it enabled reports a value that is perfectly linear in how
far the sender is from the box's +Z face:

```
P = clamp01(t + r / L)
```

where `t` is the sender's normalised position along the box depth `L`, and `r` is the sender's
radius. That `r` is the toucher's collider, which you cannot know — so a single box is wrong by `r`
on every axis.

Two boxes covering the same volume, one rotated 180°, cancel it exactly:

```
P₊ = t + r/L
P₋ = (1 − t) + r/L

t = (P₊ − P₋ + 1) / 2      exact position, independent of the toucher's size
r = L · (P₊ + P₋ − 1) / 2  the toucher's collider size, for free
```

Three opposed pairs give an exact 3D contact point from six receivers. The pair sum also gives
**presence** (both read 0 when nothing is touching) and **multi-touch detection** (VRChat combines
overlapping senders with `math.max`, so `P₊` locks onto whichever sender is nearest its own face and
`P₋` onto the other — the excess is exactly their separation).

### Padding

Proximity clamps at 1.0 once a sender reaches a face, and the maths only holds below saturation.
Substituting the saturation condition gives a rule with no free variables:

> **padding ≥ the radius of the largest collider you expect to be touched with**

It does not depend on region size, which is why padding here is in **metres**, not a percentage. A
torso is only ~0.26 m front-to-back; 30% padding would leave 0.078 m and a stock hand collider would
peg against the chest. The default of 0.10 m covers VRChat's stock hand and foot colliders.

### Verified, not assumed

The above is derived, but it is also measured. `VRC.Dynamics.dll` was decompiled and its
`CalcProximity` ported literally into the test suite, so every assertion runs against what VRChat
actually computes rather than against the algebra this was derived from:

| Property | Measured |
|---|---|
| Linearity of `P₊` | exact to float epsilon (`6e-8`) |
| Opposed-pair position, sphere senders | exact (`3.6e-8 m`) |
| Opposed-pair position, capsule senders | 0.00–0.64 mm at realistic VRChat collider sizes |
| Single-sided box, same conditions | `r·√3` — **173 mm** for a 0.10 m collider |
| Multi-touch excess | exactly equals sender separation |

## Usage

1. Add a **Contact Compressor Group** to a GameObject whose children carry the receivers you already
   author. Give it a region id.
2. The inspector shows the before/after receiver count, the fitted box, the largest collider it can
   resolve, and how far apart your two closest points are. Fix anything it complains about.
3. Build. A `IVRCSDKPreprocessAvatarCallback` at order **−1100** fits the box, emits the receivers,
   deletes the originals, and registers the float parameters as **unsynced** — costing none of the
   avatar's 256 synced bits.
4. **Export manifest…** writes a JSON describing every region and where each of your original points
   sits inside it. That is what the consumer reads.

Your scene is untouched. Only the uploaded avatar is different.

## The manifest is the calibration

This is the part worth understanding. The tool never asks you to describe your layout, because you
already did — by placing the receivers. It records each one's normalised position inside the fitted
box, so a consumer turning a decoded position back into "which point" needs no hard-coded table.
Move a receiver and everything downstream follows.

## Parameters

```
/avatar/parameters/<prefix>/<RegionId>/<Axis><Sign>    float
```

for example `/avatar/parameters/bOSC/v3/Torso/Xp`. Six per fully-encoded region.

## Decoding

[`Furroxide.ContactCompressor.Decoder`](../../../Decoder) is a `netstandard2.0` library that takes
parameter name/value pairs and gives back contact positions and weights over your authored points.
It has no OSC-library and no device dependency.

```csharp
var decoder = new ContactCompressorDecoder(manifest);
decoder.Accept("/avatar/parameters/bOSC/v3/Torso/Xp", 0.71f);   // ignores anything it doesn't own

foreach (var point in decoder.Sample("Torso"))
    Console.WriteLine($"{point.Id} at {point.Weight:P0}");
```

`Sample` spreads the contact across the nearest points using the *decoded collider size* as the
falloff width, so a fingertip lands tightly on one point and a palm spreads across several — which a
per-point on/off receiver cannot express at all. When it detects two people touching one region it
returns every point at equal weight rather than firing a phantom point between them.

The decoder compiles the *same source files* as this package's `Core` assembly, which is
deliberately engine-free (`noEngineReferences`). The maths has to agree with what the avatar emits;
a reimplementation would be free to drift.

## Trade-offs

- **Multi-touch.** Per-point receivers handle simultaneous touches independently; this does not. One
  region resolves one contact point. It is *detected* rather than silently wrong, and separate
  regions are independent. Keep the per-point path if you need true multi-touch within one region.
- **Local Only.** Defaults to preserving whatever the source receivers used. Forcing it on takes the
  avatar's Contacts metric to zero — the SDK's performance scanner skips local-only contacts — but
  remote clients stop evaluating the receivers, so anything driven off them becomes invisible to
  other players.
- **Build order.** The hook must run after receiver-generating tools (VRCFury builds at −10000) and
  before the VRCSDK strips `IEditorOnly` components at −1024. If another tool generates receivers
  later than −1100, they will not be collected.

## Looking ahead

VRChat's [13 August 2026 Developer
Update](https://ask.vrchat.com/t/developer-update-13-august-2026/48800) announced contact receivers
that report the sender's local position directly, which would reduce each region from six receivers
to one. It is announced only — not in 3.10.4, no version committed. The encoder backend is separable
from the OSC contract, so when it ships, consumers should not need to change.

## Licence

GPL-3.0-only.
