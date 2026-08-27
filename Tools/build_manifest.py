"""
Builds the default Contact Compressor manifest for the stock bHaptics prefabs.

The desktop app ships this so a consolidated avatar works with no export step. Custom layouts
still export their own manifest from the Unity inspector, which overrides this.

Mirrors ContactRegionFitter: fit a box to the receivers, pad it, and record each point's
normalised position inside it.
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from prefab_layout import load, root_anchor, world_of  # noqa: E402

PREFABS = Path("Unity/Packages/com.furroxide.bhaptics-vrchat/Runtime/Prefabs/With Mesh")
PADDING = 0.10

# Collapse a self/others pair at one spot into a single logical point, matching the
# pointIdPattern the bHaptics package configures on each group.
POINT_ID_RE = re.compile(r"^bOSC/v2/(.+)/(?:self|others)$")

REGIONS = [
    ("Torso",    "Vest.prefab",     r"^bOSC/v2/(?:VestFront|VestBack)/\d+/(?:self|others)$", "XYZ"),
    ("Head",     "Head.prefab",     r"^bOSC/v2/Head/\d+/(?:self|others)$",                   "X"),
    ("ForearmL", "ArmLeft.prefab",  r"^bOSC/v2/ForearmL/\d+/(?:self|others)$",               "XYZ"),
    ("ForearmR", "ArmRight.prefab", r"^bOSC/v2/ForearmR/\d+/(?:self|others)$",               "XYZ"),
]


def build_region(region_id, prefab, pattern, axes):
    transforms, receivers, go_to_transform = load(PREFABS / prefab)
    rx = re.compile(pattern)
    root = root_anchor(transforms)

    # parameter -> position; then collapse to logical points by averaging.
    raw = {}
    for r in receivers:
        if not rx.match(r["parameter"]):
            continue
        anchor = r["rootTransform"] if r["rootTransform"] != "0" else go_to_transform.get(r["gameObject"])
        if not anchor:
            continue
        raw[r["parameter"]] = (world_of(anchor, transforms, r["offset"], root), r["radius"])

    if not raw:
        raise SystemExit(f"{region_id}: no receivers matched {pattern!r} in {prefab}")

    collapsed = {}
    for param, (pos, radius) in raw.items():
        m = POINT_ID_RE.match(param)
        pid = m.group(1) if m else param
        collapsed.setdefault(pid, []).append((pos, radius))

    points = {}
    for pid, members in collapsed.items():
        n = len(members)
        points[pid] = (
            tuple(sum(m[0][i] for m in members) / n for i in range(3)),
            sum(m[1] for m in members) / n,
        )

    lo = tuple(min(p[0][i] for p in points.values()) for i in range(3))
    hi = tuple(max(p[0][i] for p in points.values()) for i in range(3))
    # Only clamp axes that are actually encoded; the fitter leaves unused axes at zero.
    enabled = {0: "X" in axes, 1: "Y" in axes, 2: "Z" in axes}
    extents = tuple(max(hi[i] - lo[i], 0.02) if enabled[i] else (hi[i] - lo[i]) for i in range(3))
    box = tuple(extents[i] + 2 * PADDING for i in range(3))

    region = {
        "id": region_id,
        "axes": axes,
        "boxExtents": [round(b, 6) for b in box],
        "regionExtents": [round(e, 6) for e in extents],
        "points": [],
    }

    def sort_key(pid):
        m = re.search(r"/(\d+)$", pid)
        return (pid.rsplit("/", 1)[0], int(m.group(1)) if m else 0)

    for pid in sorted(points, key=sort_key):
        pos, radius = points[pid]
        # An unused axis has zero extent; the fitter reports 0.5 there rather than dividing.
        def norm(i):
            return round((pos[i] - lo[i]) / extents[i], 6) if extents[i] > 0 else 0.5

        region["points"].append({
            "id": pid,
            "u": norm(0),
            "v": norm(1),
            "w": norm(2),
            "radius": round(radius, 6),
        })

    print(f"  {region_id:<10} {len(points):>2} points  "
          f"extents {extents[0]:.3f} x {extents[1]:.3f} x {extents[2]:.3f}  "
          f"(from {len(raw)} receivers)")
    return region


def main(out_path):
    manifest = {
        "version": 1,
        "prefix": "bOSC/v3",
        "generator": "bHaptics VRChatOSC default layout (stock prefabs)",
        "regions": [build_region(*r) for r in REGIONS],
    }

    Path(out_path).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    total_points = sum(len(r["points"]) for r in manifest["regions"])
    emitted = sum(len(r["axes"]) * 2 for r in manifest["regions"])
    print(f"\n  {len(manifest['regions'])} regions, {total_points} points, "
          f"{emitted} emitted receivers")
    print(f"  -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1])
