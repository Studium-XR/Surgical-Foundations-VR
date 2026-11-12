"""
Rigs and animates NVIDIA SimReady models in Blender, ready for a standalone headset.

    blender -b --factory-startup -P Tools/SimReady/rig_simready.py -- <in.glb> <out.glb> <out.joints.json> <category>

For each model it:
  1. imports the GLB, drops the stray duplicate meshes some SimReady exports carry, and flattens the hierarchy;
  2. finds the moving parts from their SimReady part names (wheel, drawer, door, lid, knob, button, arm, screen, pole,
     lever, jaw…) using RULES below plus per-asset overrides from rig_overrides.json;
  3. builds a rig: one joint node per moving part, placed at the part's hinge or slide origin and turned so its local Z
     is the joint axis, with the part (and anything that rides on it: its glass, the buttons on a door, the wheel on a
     caster, the screen on an arm) parented under it; joints nest where one rides on another;
  4. animates every joint (open/close, roll, turn, press…) as one NLA track per joint plus a combined "Showcase" track,
     exported as glTF animations;
  5. decimates to the triangle budget of its category and caps texture size, for the Quest frame budget;
  6. exports GLB and writes the joint manifest the Unity ArticulatedDevice component drives interactively.

Axis convention of the manifest: Unity space. Each joint node's Blender local +Z becomes Unity local +Y after the
glTF (Y-up) and glTFast (left-handed) conversions, and a positive rotation about it changes sign, which is already
applied to the stored limits.
"""
import bmesh  # noqa: F401  (keeps bpy's mesh utilities loaded in background mode)
import bpy
import json
import math
import os
import re
import sys
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

# ───────────────────────────── configuration ─────────────────────────────

# Triangle budget per category (Quest 3: whole scene ~750k–1M tris at 72 Hz; a theatre has 30–60 such models).
BUDGET = {
    "Instruments": 20000, "Consumables": 15000, "Sterile": 30000, "Devices": 40000, "Equipment": 120000,
    "Imaging": 120000, "Furniture": 60000, "Carts": 60000, "Lighting": 80000, "Architecture": 40000,
}
# Below this share of the source triangles, collapse decimation tears CAD meshes apart; stop there instead.
MIN_RATIO = 0.12
TEXTURE_MAX = {"Equipment": 2048, "Imaging": 2048, "Furniture": 2048}
DEFAULT_TEXTURE_MAX = 1024

# base part name (regex, case-insensitive) → joint kind. First match wins.
RULES = [
    (r"^(wheel|tire|sm_wheel\d*|sm_tire\d*|caster)$", "spin"),
    (r"^(wheelattachment|wheelbase|tirebase|wheelpin|casterbase)$", "swivel"),
    (r"^drawer$", "slide"),
    (r"^(door|locker|mainlocker)$", "door"),
    (r"^(lid|biglid|smalllid|cover|maskcap|cap|flap)$", "lid"),
    (r"(button|switch)$|^button", "press"),
    (r"^(knob|topknob|breathingsystemknob|dial)$", "turn"),
    (r"^(lever|pedal|brake|lock|trigger|handlelever)$", "lever"),
    (r"^(arm|leftarm|rightarm|screenarm|extentionarm|extensionarm|extentionhinge|column|boom)$", "swing"),
    (r"^(screen|screenhinge|screensupport|display|monitor)$", "tilt"),
    (r"^(toppole|middlepole|extension)$", "telescope"),
    (r"^(plunger|piston|forcer)$", "plunge"),
    (r"^seat$", "lift"),
]
# Parts that never move on their own but ride on their owner: "screenGlass_01" rides on "screen_01".
GLASS = re.compile(r"^(?P<owner>.+?)glass$", re.I)

# Defaults per joint kind: (unit, min, max). Rotations in degrees (Blender sense), translations as a share of the part.
LIMITS = {
    "spin": ("deg", 0, 360), "swivel": ("deg", -180, 180), "slide": ("m", 0, 0.6), "door": ("deg", 0, 100),
    "lid": ("deg", 0, 100), "press": ("m", 0, 0.003), "turn": ("deg", 0, 270), "lever": ("deg", 0, 20),
    "swing": ("deg", -60, 60), "tilt": ("deg", -25, 25), "telescope": ("m", 0, 0.45), "plunge": ("m", 0, 0.8),
    "lift": ("m", 0, 0.12), "jaw": ("deg", 0, 30),
}
FPS = 30
# Largest dimension (m) a part may have and still be the kind its name says; bigger parts stay fixed. Stops e.g. a
# base frame named "wheelBase" from being swivelled like a caster.
MAX_SIZE = {"spin": 0.3, "swivel": 0.2, "press": 0.06, "turn": 0.15, "lever": 0.5, "slide": 1.2, "door": 2.4,
            "lid": 1.6, "plunge": 0.4, "jaw": 0.6}


def log(*a):
    print("[rig]", *a, flush=True)


# ───────────────────────────── helpers ─────────────────────────────

def base_and_index(name):
    """'screenGlass_01_mesh_00.001' -> ('screenglass', '01')"""
    n = re.sub(r"\.\d+$", "", name)
    n = re.sub(r"_mesh(_\d+)*$", "", n)
    n = re.sub(r"_obj(_\d+)*$", "", n)
    m = re.match(r"^(?P<b>.*?)_?(?P<i>\d+)?$", n)
    return (m.group("b") or n).lower(), (m.group("i") or "")


_BOUNDS = {}


def world_bounds(objs):
    """Axis-aligned bounds of the objects' vertices in world space (measured once per object, before rigging)."""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        b = _BOUNDS.get(o.name)
        if b is None:
            mw = o.matrix_world
            vs = [mw @ v.co for v in o.data.vertices] or [mw.translation.copy()]
            cols = list(zip(*vs))  # (xs, ys, zs)
            b = (Vector([min(c) for c in cols]), Vector([max(c) for c in cols]))
            _BOUNDS[o.name] = b
        lo = Vector(map(min, lo, b[0]))
        hi = Vector(map(max, hi, b[1]))
    return lo, hi


def volume(objs):
    lo, hi = world_bounds(objs)
    s = hi - lo
    return max(s.x, 1e-4) * max(s.y, 1e-4) * max(s.z, 1e-4)


def axis_vec(i):
    return [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))][i]


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def world_verts(o):
    mw = o.matrix_world
    return [mw @ v.co for v in o.data.vertices]


# ───────────────────────────── import & clean ─────────────────────────────

def import_clean(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]

    def depth(o):
        d = 0
        while o.parent:
            d += 1
            o = o.parent
        return d

    # Stray duplicates: the same mesh data placed at the model origin as well as under Geometry/. Keep the deepest.
    by_data = {}
    for o in meshes:
        by_data.setdefault(o.data.name, []).append(o)
    for objs in by_data.values():
        objs.sort(key=depth, reverse=True)
        for extra in objs[1:]:
            bpy.data.objects.remove(extra, do_unlink=True)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:  # flatten, keeping world transforms
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    # Bake object transforms into the meshes so bounds and pivots are measured in one space.
    for o in meshes:
        if o.data.users > 1:
            o.data = o.data.copy()
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    return meshes


# ───────────────────────────── joints ─────────────────────────────

class Joint:
    def __init__(self, name, kind, parts):
        self.name, self.kind, self.parts = name, kind, list(parts)
        self.pivot = Vector()
        self.axis = Vector((0, 0, 1))
        self.unit, self.min, self.max = LIMITS[kind]
        self.parent = None
        self.empty = None


def classify(meshes, overrides):
    """Groups parts into joints. Returns (joints, static parts)."""
    by_key = {}
    for o in meshes:
        b, i = base_and_index(o.name)
        by_key.setdefault((b, i), []).append(o)

    forced = {k.lower(): v for k, v in overrides.get("parts", {}).items()}
    joints, followers, static = [], [], []
    for (b, i), objs in sorted(by_key.items()):
        key = f"{b}_{i}" if i else b
        kind = forced.get(key) or forced.get(b)
        if kind is None:
            if GLASS.match(b):
                followers.append((GLASS.match(b).group("owner"), i, objs))
                continue
            kind = next((k for rx, k in RULES if re.search(rx, b, re.I)), None)
        if kind is not None and kind in MAX_SIZE and not forced.get(key) and not forced.get(b):
            lo, hi = world_bounds(objs)
            if max(hi - lo) > MAX_SIZE[kind]:
                kind = None
        if kind in (None, "static"):
            static.extend(objs)
        else:
            joints.append(Joint(key, kind, objs))
    # Glass follows its owner if the owner moves.
    for owner, i, objs in followers:
        target = next((j for j in joints if base_and_index(j.parts[0].name) == (owner, i)), None)
        (target.parts if target else static).extend(objs)
    return joints, static


def hinged_instrument(meshes):
    """
    Scissor-type instruments (scissors, artery forceps, rongeurs, pliers, clamps): the parts split into a left and a
    right arm ("handleLeft" + "jawLeft" + "springLeft", or "body_01" / "body_02"); the right arm turns about the point
    where the two arms cross, in the instrument's flat plane.
    """
    def side(o):
        b, i = base_and_index(o.name)
        if "right" in b or "rght" in b:
            return 1
        if "left" in b:
            return 0
        return None

    sides = [side(o) for o in meshes]
    if all(x is not None for x in sides) and 0 in sides and 1 in sides:
        left = [o for o, x in zip(meshes, sides) if x == 0]
        right = [o for o, x in zip(meshes, sides) if x == 1]
    elif len(meshes) == 2:
        left, right = [meshes[0]], [meshes[1]]
    else:
        return None
    va = [v for o in left for v in world_verts(o)]
    vb = [v for o in right for v in world_verts(o)]
    if not va or not vb:
        return None
    kd = KDTree(len(vb))
    for k, v in enumerate(vb):
        kd.insert(v, k)
    kd.balance()
    lo, hi = world_bounds(meshes)
    size = hi - lo
    close = [v for v in va[:: max(1, len(va) // 4000)] if kd.find(v)[2] < max(size) * 0.01]
    if len(close) < 3:
        return None
    j = Joint("jaw", "jaw", right)
    j.pivot = sum(close, Vector()) / len(close)
    thin = min(range(3), key=lambda k: size[k])  # the arms open in the instrument's flat plane
    j.axis = axis_vec(thin)
    return j


def place(j, all_lo, all_hi, body_centre, body_lo, body_hi):
    """Pivot and axis for a joint from its part bounds and the whole model's bounds."""
    lo, hi = world_bounds(j.parts)
    c, s = (lo + hi) / 2, hi - lo
    centre = body_centre  # the cabinet the part belongs to, not the whole model (side arms would skew it)
    k = j.kind
    thin = min(range(3), key=lambda i: s[i])
    if k == "spin":
        j.pivot, j.axis = c, axis_vec(thin if thin != 2 else max((0, 1), key=lambda i: -s[i]))
    elif k == "swivel":
        j.pivot, j.axis = Vector((c.x, c.y, hi.z)), Vector((0, 0, 1))
    elif k == "slide":
        # A drawer's geometry is concentrated in its front panel and handle: it leaves towards that side. When the
        # drawer is a plain box, fall back to the cabinet face its front is flush with.
        vs = [v for o in j.parts for v in world_verts(o)]
        mean = sum(vs, Vector()) / max(1, len(vs))
        off = [(abs(mean[a] - c[a]) / max(s[a], 1e-4), a, 1 if mean[a] >= c[a] else -1) for a in (0, 1)]
        best = max(off)
        if best[0] > 0.03:
            _, h, sign = best
        else:
            faces = [(abs(body_hi[a] - hi[a]), a, 1) for a in (0, 1)] + [(abs(lo[a] - body_lo[a]), a, -1) for a in (0, 1)]
            _, h, sign = min(faces)
        d = axis_vec(h) * sign
        j.pivot, j.axis = c, d
        j.max = round(s[h] * j.max, 4)
    elif k in ("door", "lid"):
        if k == "door":
            w = 0 if s.x >= s.y else 1           # door width runs along w; hinge is a vertical edge
            depth = 1 - w
            edge_lo, edge_hi = lo[w], hi[w]
            # The hinge is on the side nearer the model's outline.
            side = edge_lo if (edge_lo - all_lo[w]) < (all_hi[w] - edge_hi) else edge_hi
            front = hi[depth] if c[depth] >= centre[depth] else lo[depth]
            p = c.copy(); p[w] = side; p[depth] = front
            j.pivot, j.axis = p, Vector((0, 0, 1))
        else:
            # Lid: hinge along the longer horizontal side, at the back edge, on top.
            longer = 0 if s.x >= s.y else 1
            shorter = 1 - longer
            p = c.copy(); p[shorter] = hi[shorter]; p.z = hi.z
            j.pivot, j.axis = p, axis_vec(longer)
        # Pick the rotation sense that swings the part outward/upward.
        test = (Matrix.Rotation(math.radians(45), 3, j.axis) @ (c - j.pivot)) + j.pivot
        if k == "door":
            moves_out = (test - centre).length > (c - centre).length
        else:
            moves_out = test.z > c.z
        if not moves_out:
            j.axis = -j.axis
    elif k == "press" or k == "plunge":
        out = c - centre
        a = thin if k == "press" else max(range(3), key=lambda i: s[i])
        d = axis_vec(a) * (1 if out[a] >= 0 else -1)
        j.pivot, j.axis = c, -d if k == "press" else d   # buttons go in, plungers go along the barrel
        if k == "plunge":
            j.max = round(s[a] * j.max, 4)
    elif k in ("turn", "lever"):
        j.pivot, j.axis = c, axis_vec(thin)
    elif k == "swing":
        # Arms turn about a vertical axis at the end nearest the model's centre line (their mount).
        long_ax = 0 if s.x >= s.y else 1
        p = c.copy()
        p[long_ax] = lo[long_ax] if abs(lo[long_ax] - centre[long_ax]) < abs(hi[long_ax] - centre[long_ax]) else hi[long_ax]
        j.pivot, j.axis = p, Vector((0, 0, 1))
    elif k == "tilt":
        wide = 0 if s.x >= s.y else 1
        j.pivot, j.axis = c, axis_vec(wide)
    elif k in ("telescope", "lift"):
        j.pivot, j.axis = Vector((c.x, c.y, lo.z)), Vector((0, 0, 1))
        if k == "telescope":
            j.max = round(s.z * j.max, 4)


COMPATIBLE = {
    "spin": ("swivel",),
    "swing": ("swing", "telescope", "lift"),
    "tilt": ("swing", "telescope", "lift"),
}


def nest(joints, static, model_volume):
    """Who rides on whom: wheels on their casters, screens on their arms, buttons and knobs on doors/drawers/lids."""
    def inside(part_objs, host, pad=0.01):
        lo, hi = world_bounds(host.parts)
        plo, phi = world_bounds(part_objs)
        c = (plo + phi) / 2
        return all(lo[i] - pad <= c[i] <= hi[i] + pad for i in range(3))

    def index(j):
        return base_and_index(j.parts[0].name)[1]

    carriers = [j for j in joints if j.kind in ("door", "lid", "slide", "swing", "tilt", "telescope", "lift", "swivel")
                and volume(j.parts) < 0.5 * model_volume]  # a "drawer" as big as the machine is mislabelled
    for j in joints:
        jlo, jhi = world_bounds(j.parts)
        jc = (jlo + jhi) / 2
        jv = volume(j.parts)
        cands = []
        for h in carriers:
            if h is j or volume(h.parts) <= jv * 0.5 and j.kind not in COMPATIBLE:
                continue
            allowed = COMPATIBLE.get(j.kind)
            if allowed is not None:
                if h.kind not in allowed:
                    continue
                # Arm on arm only within one assembly (screenArm_02 on screenArm_01, extentionArm on extentionHinge).
                if j.kind == "swing" and h.kind == "swing" and base_and_index(j.parts[0].name)[0][:6] != base_and_index(h.parts[0].name)[0][:6]:
                    continue
                hlo, hhi = world_bounds(h.parts)
                d = (jc - (hlo + hhi) / 2).length
                if d > 0.6 * max((jhi - jlo).length, (hhi - hlo).length) + 0.05:
                    continue
                same = index(h) == index(j) and index(j) != ""
                cands.append((0 if same else 1, d, h))
            elif j.kind not in ("swivel", "slide", "door", "lid") and h.kind in ("door", "lid", "slide") and inside(j.parts, h):
                hlo, hhi = world_bounds(h.parts)
                cands.append((1, (jc - (hlo + hhi) / 2).length, h))
        cands.sort(key=lambda t: (t[0], t[1]))
        best = cands[0][2] if cands else None
        p = best
        while p is not None and p is not j:  # no cycles
            p = p.parent
        if p is None:
            j.parent = best
    # Small static parts inside a moving carrier ride on it (a tray in a drawer, a handle on a door).
    for o in list(static):
        for h in carriers:
            if h.kind in ("door", "lid", "slide") and volume([o]) < 0.5 * volume(h.parts) and inside([o], h, pad=0.002):
                h.parts.append(o)
                static.remove(o)
                break


def build_rig(joints):
    order = []
    seen = set()

    def visit(j):
        if id(j) in seen:
            return
        if j.parent:
            visit(j.parent)
        seen.add(id(j))
        order.append(j)

    for j in joints:
        visit(j)
    for j in order:
        e = bpy.data.objects.new("J_" + j.name, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.05
        bpy.context.scene.collection.objects.link(e)
        z = j.axis.normalized()
        x = z.orthogonal().normalized()
        y = z.cross(x)
        rot = Matrix((x, y, z)).transposed().to_4x4()
        e.matrix_world = Matrix.Translation(j.pivot) @ rot
        if j.parent:
            e.parent = j.parent.empty
            e.matrix_parent_inverse = j.parent.empty.matrix_world.inverted()
        j.empty = e
        for p in j.parts:
            p.parent = e
            p.matrix_parent_inverse = e.matrix_world.inverted()
    return order


# ───────────────────────────── animation ─────────────────────────────

REST = {}


def keyframe(e, kind, value, frame):
    """Poses joint node e at value (degrees about, or metres along, its local Z) and keys it."""
    rest = REST[e.name]
    if LIMITS[kind][0] == "deg":
        e.rotation_mode = "XYZ"
        m = rest @ Matrix.Rotation(math.radians(value), 4, "Z")
        e.rotation_euler = m.to_euler("XYZ", e.rotation_euler)  # compatible euler keeps a full 360° turn
        e.keyframe_insert("rotation_euler", frame=frame)
    else:
        m = rest @ Matrix.Translation((0, 0, value))
        e.location = m.translation
        e.keyframe_insert("location", frame=frame)


def animate(order):
    for j in order:
        REST[j.empty.name] = j.empty.matrix_basis.copy()
        j.empty.rotation_mode = "XYZ"
    tracks = []
    for j in order:
        e = j.empty
        lo, hi = j.min, j.max
        if j.kind == "spin":
            seq = [(1, 0), (13, 90), (25, 180), (37, 270), (49, 360)]
        elif j.kind == "swivel":
            seq = [(1, 0), (25, 45), (61, -45), (85, 0)]
        elif j.kind in ("swing", "tilt"):
            seq = [(1, 0), (25, hi * 0.6), (61, lo * 0.6), (85, 0)]
        else:
            seq = [(1, lo), (31, hi), (61, hi), (91, lo)]
        e.animation_data_create()
        act = bpy.data.actions.new(f"{j.name}_operate")
        e.animation_data.action = act
        e.rotation_euler = REST[e.name].to_euler("XYZ")
        for f, v in seq:
            keyframe(e, j.kind, v, f)
        if j.kind == "spin":
            for fc in fcurves(act):
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
        e.animation_data.action = None
        t = e.animation_data.nla_tracks.new()
        t.name = f"{j.name}_operate"
        t.strips.new(act.name, 1, act)
        s = e.animation_data.nla_tracks.new()
        s.name = "Showcase"
        s.strips.new(act.name + "_show", 1, act)
        e.matrix_basis = REST[e.name]
        tracks.append(t.name)
    return tracks


def fcurves(act):
    """F-curves of an action, for both the legacy and the slotted (Blender 4.4+) action layouts."""
    if hasattr(act, "fcurves") and len(act.fcurves):
        return list(act.fcurves)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                out.extend(bag.fcurves)
    return out


# ───────────────────────────── optimisation ─────────────────────────────

def optimise(meshes, category):
    budget = BUDGET.get(category, 40000)
    # CAD exports split vertices along every hard edge; weld them first so decimation does not open seams.
    import bmesh as _bm
    for o in meshes:
        bm = _bm.new()
        bm.from_mesh(o.data)
        _bm.ops.remove_doubles(bm, verts=bm.verts, dist=0.0002)
        bm.to_mesh(o.data)
        bm.free()
    total = sum(tris(o) for o in meshes)
    if total > budget:
        ratio = max(MIN_RATIO, budget / total)
        for o in meshes:
            if tris(o) < 200:
                continue  # small parts (buttons, knobs) keep their shape
            m = o.modifiers.new("Decimate", "DECIMATE")
            m.ratio = ratio
            m.use_collapse_triangulate = True
    cap = TEXTURE_MAX.get(category, DEFAULT_TEXTURE_MAX)
    for img in bpy.data.images:
        w, h = img.size[:]
        if max(w, h) > cap and w > 0 and h > 0:
            k = cap / max(w, h)
            img.scale(max(1, int(w * k)), max(1, int(h * k)))
    return total


# ───────────────────────────── manifest ─────────────────────────────

def manifest(name, category, order, tri_in, all_lo, all_hi):
    def unity(v):
        return [-v.x, v.z, -v.y]  # Blender (Z-up, RH) → glTF (Y-up) → Unity (LH)

    out = []
    for j in order:
        unit, lo, hi = j.unit, j.min, j.max
        if unit == "deg":
            lo, hi = -hi, -lo  # rotation sense flips with handedness
        out.append({
            "name": j.name, "kind": j.kind, "node": "J_" + j.name, "parent": ("J_" + j.parent.name) if j.parent else None,
            "axis": [0, 1, 0], "unit": unit, "min": round(lo, 4), "max": round(hi, 4),
            "pivot": [round(x, 4) for x in unity(j.pivot)], "parts": sorted({re.sub(r'\.\d+$', '', p.name) for p in j.parts}),
            "clip": f"{j.name}_operate",
        })
    size = all_hi - all_lo
    return {"asset": name, "category": category, "trianglesSource": tri_in, "size": [round(size.x, 3), round(size.z, 3), round(size.y, 3)],
            "joints": out, "showcaseClip": "Showcase" if out else None}


# ───────────────────────────── main ─────────────────────────────

def main(src, dst, joints_path, category):
    name = os.path.splitext(os.path.basename(src))[0]
    ov_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "rig_overrides.json")
    overrides = {}
    if os.path.exists(ov_path):
        with open(ov_path) as f:
            overrides = json.load(f).get(name, {})

    meshes = import_clean(src)
    if not meshes:
        log("no meshes in", src)
        return 1
    all_lo, all_hi = world_bounds(meshes)

    joints, static = classify(meshes, overrides)
    if category == "Instruments" and overrides.get("jaw", True) and not any(j.kind != "press" for j in joints):
        j = hinged_instrument(meshes)
        if j:
            joints = [j]
            static = [o for o in meshes if o not in j.parts]
    body = max(static, key=lambda o: volume([o])) if static else None
    if body is not None:
        blo, bhi = world_bounds([body])
        body_centre = (blo + bhi) / 2
    else:
        blo, bhi = all_lo, all_hi
        body_centre = (all_lo + all_hi) / 2
    for j in joints:
        if j.kind != "jaw":
            place(j, all_lo, all_hi, body_centre, blo, bhi)
        o = overrides.get("limits", {}).get(j.name)
        if o:
            j.min, j.max = o
    nest(joints, static, volume(meshes))
    order = build_rig(joints)
    animate(order)
    tri_in = optimise(meshes, category)

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.context.scene.render.fps = FPS
    kwargs = dict(filepath=dst, export_format="GLB", export_yup=True, export_apply=True, export_animations=bool(order),
                  export_animation_mode="NLA_TRACKS", export_image_format="AUTO")
    try:
        bpy.ops.export_scene.gltf(**kwargs)
    except TypeError:
        kwargs.pop("export_animation_mode")
        bpy.ops.export_scene.gltf(**kwargs)

    with open(joints_path, "w") as f:
        json.dump(manifest(name, category, order, tri_in, all_lo, all_hi), f, indent=1)
    log(f"{name}: {len(meshes)} parts, {len(order)} joints ({', '.join(j.kind + ':' + j.name for j in order)}), {tri_in} tris in")
    return 0


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(*argv[:4]))
