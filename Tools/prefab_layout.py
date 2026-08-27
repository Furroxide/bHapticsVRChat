"""
Extracts contact receiver positions from a Unity prefab by composing the transform hierarchy.

This mirrors what ContactRegionFitter does inside Unity, so it can produce the default manifest
that ships with the desktop app without needing the Editor open - and, usefully, gives an
independent check on the fitter's numbers.
"""
import json
import math
import re
import sys
from pathlib import Path

CONTACT_RECEIVER_GUID = "80f1b8067b0760e4bb45023bc2e9de66"


def parse_vec(text, default=(0.0, 0.0, 0.0)):
    m = re.search(r"\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+)", text)
    return (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else default


def parse_quat(text):
    m = re.search(
        r"\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+),\s*w:\s*([-\d.eE+]+)", text)
    return (float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4))) if m else (0., 0., 0., 1.)


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v); v' = v + w*t + cross(q.xyz, t)
    tx = 2 * (y * vz - z * vy)
    ty = 2 * (z * vx - x * vz)
    tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


def blocks(text):
    """Yield (class_id, anchor, body) for each YAML document in a Unity asset."""
    parts = re.split(r"^--- !u!(\d+) &(\d+).*$", text, flags=re.M)
    for i in range(1, len(parts), 3):
        yield parts[i], parts[i + 1], parts[i + 2]


def load(path):
    text = Path(path).read_text(encoding="utf-8", errors="replace")

    transforms = {}   # anchor -> dict
    receivers = []
    go_to_transform = {}

    for class_id, anchor, body in blocks(text):
        if class_id == "4":                                   # Transform
            go = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)\}", body)
            father = re.search(r"m_Father:\s*\{fileID:\s*(\d+)\}", body)
            pos = re.search(r"m_LocalPosition:\s*(\{[^}]*\})", body)
            rot = re.search(r"m_LocalRotation:\s*(\{[^}]*\})", body)
            scale = re.search(r"m_LocalScale:\s*(\{[^}]*\})", body)
            transforms[anchor] = {
                "gameObject": go.group(1) if go else None,
                "father": father.group(1) if father else "0",
                "pos": parse_vec(pos.group(1)) if pos else (0., 0., 0.),
                "rot": parse_quat(rot.group(1)) if rot else (0., 0., 0., 1.),
                "scale": parse_vec(scale.group(1), (1., 1., 1.)) if scale else (1., 1., 1.),
            }
            if go:
                go_to_transform[go.group(1)] = anchor

        elif class_id == "114" and CONTACT_RECEIVER_GUID in body:   # MonoBehaviour
            go = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)\}", body)
            param = re.search(r"^\s*parameter:\s*(.*)$", body, flags=re.M)
            radius = re.search(r"^\s*radius:\s*([-\d.eE+]+)", body, flags=re.M)
            offset = re.search(r"^\s*position:\s*(\{[^}]*\})", body, flags=re.M)
            root_tf = re.search(r"rootTransform:\s*\{fileID:\s*(\d+)\}", body)
            rtype = re.search(r"^\s*receiverType:\s*(\d+)", body, flags=re.M)
            allow_self = re.search(r"^\s*allowSelf:\s*(\d+)", body, flags=re.M)
            allow_others = re.search(r"^\s*allowOthers:\s*(\d+)", body, flags=re.M)
            receivers.append({
                "gameObject": go.group(1) if go else None,
                "parameter": param.group(1).strip() if param else "",
                "radius": float(radius.group(1)) if radius else 0.0,
                "offset": parse_vec(offset.group(1)) if offset else (0., 0., 0.),
                "rootTransform": root_tf.group(1) if root_tf else "0",
                "receiverType": int(rtype.group(1)) if rtype else -1,
                "allowSelf": allow_self.group(1) == "1" if allow_self else False,
                "allowOthers": allow_others.group(1) == "1" if allow_others else False,
            })

    return transforms, receivers, go_to_transform


def root_anchor(transforms):
    """The transform with no parent, i.e. the prefab root."""
    for anchor, t in transforms.items():
        if t["father"] == "0":
            return anchor
    return None


def world_of(anchor, transforms, local=(0., 0., 0.), stop_at=None):
    """
    Compose a local point up the transform chain into `stop_at`'s local space.

    `stop_at` matters. ContactRegionFitter measures in the group's frame via
    Transform.InverseTransformPoint, which is the frame's *local* space - the root's own
    rotation and scale are not applied, because the emitted encoder box lives under that
    frame and inherits them. Composing through the root instead tilts the whole point cloud:
    the stock vest root carries a 13.7 degree X rotation, which inflated the measured height
    by 1.29x and put this generator out of step with the fitter.
    """
    pos = local
    node = anchor
    guard = 0
    while node and node != "0" and node in transforms and node != stop_at:
        t = transforms[node]
        pos = (pos[0] * t["scale"][0], pos[1] * t["scale"][1], pos[2] * t["scale"][2])
        pos = qrot(t["rot"], pos)
        pos = (pos[0] + t["pos"][0], pos[1] + t["pos"][1], pos[2] + t["pos"][2])
        node = t["father"]
        guard += 1
        if guard > 64:
            raise RuntimeError("transform chain too deep - cycle?")
    return pos


def main(prefab_path, pattern, region_id, out_path=None, padding=0.10):
    transforms, receivers, go_to_transform = load(prefab_path)
    rx = re.compile(pattern)

    points = {}
    skipped = 0
    for r in receivers:
        if not rx.match(r["parameter"]):
            skipped += 1
            continue
        anchor = r["rootTransform"] if r["rootTransform"] != "0" else go_to_transform.get(r["gameObject"])
        if not anchor:
            continue
        p = world_of(anchor, transforms, r["offset"])
        points[r["parameter"]] = {"pos": p, "radius": r["radius"]}

    if not points:
        print(f"  no receivers matched {pattern!r}", file=sys.stderr)
        return None

    xs = [v["pos"][0] for v in points.values()]
    ys = [v["pos"][1] for v in points.values()]
    zs = [v["pos"][2] for v in points.values()]
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs), max(ys), max(zs))
    extents = tuple(max(hi[i] - lo[i], 0.02) for i in range(3))
    box = tuple(extents[i] + 2 * padding for i in range(3))

    region = {
        "id": region_id,
        "axes": "XYZ",
        "boxExtents": [round(b, 6) for b in box],
        "regionExtents": [round(e, 6) for e in extents],
        "points": [],
    }
    for name in sorted(points):
        p = points[name]["pos"]
        norm = [(p[i] - lo[i]) / extents[i] if extents[i] > 0 else 0.5 for i in range(3)]
        region["points"].append({
            "id": name,
            "u": round(norm[0], 6),
            "v": round(norm[1], 6),
            "w": round(norm[2], 6),
            "radius": round(points[name]["radius"], 6),
        })

    print(f"  {region_id}: {len(points)} points, "
          f"extents {extents[0]:.3f} x {extents[1]:.3f} x {extents[2]:.3f} m "
          f"(skipped {skipped} non-matching receivers)")

    if out_path:
        Path(out_path).write_text(json.dumps(region, indent=2), encoding="utf-8")
    return region


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3],
         sys.argv[4] if len(sys.argv) > 4 else None)
