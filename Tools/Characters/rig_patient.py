"""
Gives the static patient model (Imports/patient/source/Patient.fbx, a nude adult lying supine with a towel) a
humanoid skeleton, so Unity can make it a Humanoid and play retargeted motion (HY-Motion clips: breathing, turning the
head, talking with the hands, holding the belly in pain).

    blender -b --factory-startup -P Tools/Characters/rig_patient.py -- <Patient.fbx> <Patient_Rigged.fbx> [<preview.png>]

Steps:
  1. Stand the body up (head +Z, facing −Y; the character's left is +X) and put the feet on the ground, because Unity's
     Humanoid avatar is defined from an upright rest pose.
  2. Measure the joints from the mesh itself: legs and arms are found as separate vertex clusters in horizontal slices
     (the arms hang by the sides, the legs are apart), the spine follows the torso's centre line, and the heights follow
     standard body proportions where the mesh gives no clear landmark.
  3. Build the armature with Unity-recognisable bone names, skin the body with automatic (heat) weights, and bind the
     towel to the pelvis and thighs only, so it never follows the hands.
  4. Export FBX (Unity axes); optionally render a test pose to check the skinning.
"""
import bpy
import math
import sys
from mathutils import Matrix, Vector


def log(*a):
    print("[patient]", *a, flush=True)


def main(src, dst, preview=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    body = next((o for o in meshes if o.name.lower().startswith("body")), max(meshes, key=lambda o: len(o.data.vertices)))
    towel = [o for o in meshes if o is not body]
    for o in list(bpy.context.scene.objects):
        if o.type not in ("MESH",):
            bpy.data.objects.remove(o, do_unlink=True)

    # 1 · Stand up: lying with the head at +Y and the face at +Z → head +Z, face −Y.
    stand = Matrix.Rotation(math.radians(90), 4, "X")
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.data.transform(stand @ mw)
        o.matrix_world = Matrix.Identity(4)
    vs = [v.co.copy() for v in body.data.vertices]
    lo = Vector([min(c) for c in zip(*vs)])
    hi = Vector([max(c) for c in zip(*vs)])
    shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
    for o in meshes:
        o.data.transform(Matrix.Translation(shift))
    vs = [v + shift for v in vs]
    H = hi.z - lo.z
    log(f"height {H:.3f} m, {len(vs)} vertices")

    # 2 · Landmarks.
    def slice_(z, tol=0.012):
        return [v for v in vs if abs(v.z - z) < tol]

    def centroid(pts):
        return sum(pts, Vector()) / len(pts) if pts else None

    def side_cluster(z, left, beyond=None):
        """Vertices of the limb on one side at height z: the outermost cluster past the widest gap in x."""
        pts = [v for v in slice_(z) if (v.x > 0) == left]
        if not pts:
            return None
        xs = sorted(abs(v.x) for v in pts)
        gaps = [(xs[i + 1] - xs[i], xs[i]) for i in range(len(xs) - 1)]
        if beyond is not None:
            return centroid([v for v in pts if abs(v.x) > beyond]) or centroid(pts)
        best = max(gaps, default=(0, 0))
        if best[0] > 0.008:
            cut = best[1]
            return centroid([v for v in pts if abs(v.x) > cut])
        return centroid([v for v in pts if abs(v.x) > xs[-1] - 0.06])

    # Crotch: scanning up from mid-thigh, the first height where the body is solid at the centre line.
    crotch = 0.47 * H
    for i in range(80):
        z = 0.38 * H + i * 0.002
        if any(abs(v.x) < 0.012 for v in slice_(z, 0.004)):
            crotch = z
            break
    log(f"crotch at {crotch / H:.3f} H")

    def leg(left, z):
        pts = [v for v in slice_(z) if (v.x > 0) == left]
        return centroid(pts)

    def torso(z, half=0.13):
        return centroid([v for v in slice_(z) if abs(v.x) < half])

    J = {}
    hipz = crotch + 0.035 * H
    for side, left in (("Left", True), ("Right", False)):
        thigh = leg(left, crotch - 0.03 * H) or Vector((0.09 if left else -0.09, 0, crotch))
        knee = leg(left, 0.285 * H)
        ankle = leg(left, 0.045 * H)
        J[side + "UpLeg"] = Vector((thigh.x, thigh.y, hipz))
        J[side + "Leg"] = Vector((knee.x, knee.y, 0.285 * H))
        J[side + "Foot"] = Vector((ankle.x, ankle.y + 0.02, 0.045 * H))
        foot = [v for v in vs if v.z < 0.03 * H and (v.x > 0) == left]
        front = min(v.y for v in foot) if foot else ankle.y - 0.15
        J[side + "ToeBase"] = Vector((ankle.x, ankle.y + 0.62 * (front - ankle.y), 0.018 * H))
        J[side + "ToeEnd"] = Vector((ankle.x, front, 0.015 * H))

        upper = side_cluster(0.74 * H, left)
        elbow = side_cluster(0.625 * H, left)
        wrist = side_cluster(0.49 * H, left, beyond=abs(J[side + "UpLeg"].x) + 0.07)
        J[side + "Shoulder"] = Vector((0.035 if left else -0.035, torso(0.815 * H).y, 0.815 * H))
        J[side + "Arm"] = Vector((upper.x, upper.y, 0.812 * H))
        J[side + "ForeArm"] = Vector((elbow.x, elbow.y, 0.625 * H))
        J[side + "Hand"] = Vector((wrist.x, wrist.y, 0.49 * H))
        # The hand continues the forearm (the fingertips sit against the towel, so slicing for them is unreliable).
        fore = (J[side + "Hand"] - J[side + "ForeArm"]).normalized()
        J[side + "HandEnd"] = J[side + "Hand"] + fore * 0.095 * H

    for name, f in (("Hips", None), ("Spine", 0.60), ("Spine1", 0.67), ("Spine2", 0.745), ("Neck", 0.825), ("Head", 0.87)):
        z = hipz + 0.01 * H if f is None else f * H
        c = torso(z, 0.10 if f and f > 0.8 else 0.13)
        J[name] = Vector((0.0, c.y if c else 0.0, z))
    J["HeadEnd"] = Vector((0.0, J["Head"].y, H))

    # 3 · Armature.
    arm_data = bpy.data.armatures.new("PatientRig")
    rig = bpy.data.objects.new("PatientRig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    def bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name)
        b.head, b.tail = head, tail
        if (b.tail - b.head).length < 0.01:
            b.tail = b.head + Vector((0, 0, 0.03))
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    bone("Hips", J["Hips"], J["Spine"])
    bone("Spine", J["Spine"], J["Spine1"], "Hips", True)
    bone("Spine1", J["Spine1"], J["Spine2"], "Spine", True)
    bone("Spine2", J["Spine2"], J["Neck"], "Spine1", True)
    bone("Neck", J["Neck"], J["Head"], "Spine2", True)
    bone("Head", J["Head"], J["HeadEnd"], "Neck", True)
    for s in ("Left", "Right"):
        bone(s + "UpLeg", J[s + "UpLeg"], J[s + "Leg"], "Hips")
        bone(s + "Leg", J[s + "Leg"], J[s + "Foot"], s + "UpLeg", True)
        bone(s + "Foot", J[s + "Foot"], J[s + "ToeBase"], s + "Leg", True)
        bone(s + "ToeBase", J[s + "ToeBase"], J[s + "ToeEnd"], s + "Foot", True)
        bone(s + "Shoulder", J[s + "Shoulder"], J[s + "Arm"], "Spine2")
        bone(s + "Arm", J[s + "Arm"], J[s + "ForeArm"], s + "Shoulder", True)
        bone(s + "ForeArm", J[s + "ForeArm"], J[s + "Hand"], s + "Arm", True)
        bone(s + "Hand", J[s + "Hand"], J[s + "HandEnd"], s + "ForeArm", True)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Skin the body with heat weights. Weld duplicate vertices first: split seams make the heat solver fail.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
    bm.to_mesh(body.data)
    bm.free()
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    fill_unweighted(body)
    separate_hands_from_thighs(body, rig)

    # The towel copies the weights of the body under it, limited to the pelvis and thighs so it never follows the hands.
    allowed = {"Hips", "LeftUpLeg", "RightUpLeg", "Spine"}
    for o in towel:
        for name in allowed:
            if name not in o.vertex_groups:
                o.vertex_groups.new(name=name)
        dt = o.modifiers.new("Weights", "DATA_TRANSFER")
        dt.object = body
        dt.use_vert_data = True
        dt.data_types_verts = {"VGROUP_WEIGHTS"}
        dt.vert_mapping = "POLYINTERP_NEAREST"
        dt.layers_vgroup_select_src = "ALL"
        dt.layers_vgroup_select_dst = "NAME"
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=dt.name)
        for g in list(o.vertex_groups):
            if g.name not in allowed:
                o.vertex_groups.remove(g)
        hips = o.vertex_groups["Hips"]
        for v in o.data.vertices:
            if sum(g.weight for g in v.groups) < 1e-4:
                hips.add([v.index], 1.0, "REPLACE")
        bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        o.parent = rig

    to_t_pose(rig, [body] + towel)

    # 4 · Export.
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL", bake_anim=False,
                             mesh_smooth_type="FACE", use_armature_deform_only=True, path_mode="AUTO")
    log("exported", dst)

    if preview:
        render_test(rig, [body] + towel, preview, H)


def fill_unweighted(mesh):
    """
    Vertices the heat solver could not reach (fingertips resting on the thigh, for instance) take the average weights of
    their already-weighted neighbours along the mesh edges, spreading inwards ring by ring. Following the surface, not
    straight-line distance, keeps a fingertip with its hand instead of the thigh it touches.
    """
    n = len(mesh.data.vertices)
    weights = [dict() for _ in range(n)]
    for v in mesh.data.vertices:
        for g in v.groups:
            if g.weight > 1e-4:
                weights[v.index][g.group] = g.weight
    empty = {i for i in range(n) if not weights[i]}
    log(f"heat weights: {len(empty)} of {n} vertices unweighted; filling along the surface")
    nbrs = [[] for _ in range(n)]
    for e in mesh.data.edges:
        i, j = e.vertices
        nbrs[i].append(j)
        nbrs[j].append(i)
    while empty:
        ring = {}
        for i in empty:
            src = [weights[j] for j in nbrs[i] if weights[j]]
            if not src:
                continue
            acc = {}
            for w in src:
                for k, x in w.items():
                    acc[k] = acc.get(k, 0.0) + x / len(src)
            ring[i] = acc
        if not ring:
            break  # islands with no weighted vertex at all
        for i, w in ring.items():
            weights[i] = w
        empty -= set(ring)
    if empty:
        # Separate small pieces (nails, eyes): each piece goes wholly to the bone nearest its centre, measured to the
        # bone's segment, so a fingernail lying against the thigh still follows the hand.
        rig = mesh.find_armature()
        bones = [(b.name, rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones]

        def seg_dist(p, a, b):
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            return (a + ab * t - p).length

        seen = set()
        islands = 0
        for start in list(empty):
            if start in seen:
                continue
            comp, stack = [], [start]
            seen.add(start)
            while stack:
                i = stack.pop()
                comp.append(i)
                for j in nbrs[i]:
                    if j in empty and j not in seen:
                        seen.add(j)
                        stack.append(j)
            c = sum((mesh.data.vertices[i].co for i in comp), Vector()) / len(comp)
            name = min(bones, key=lambda bb: seg_dist(c, bb[1], bb[2]))[0]
            g = mesh.vertex_groups.get(name) or mesh.vertex_groups.new(name=name)
            for i in comp:
                weights[i] = {g.index: 1.0}
            islands += 1
        log(f"{len(empty)} vertices on {islands} separate pieces bound to their nearest bone")
    for i in range(n):
        for k, x in weights[i].items():
            mesh.vertex_groups[k].add([i], x, "REPLACE")


def separate_hands_from_thighs(mesh, rig):
    """
    The hands rest against the thighs in the model's pose, so heat weighting leaks hand weights into the thigh skin (and
    the other way round): lifting the arm then drags a spike out of the thigh. Each vertex is given to the limb it lies
    inside, judged by its distance to each bone divided by that limb's thickness; arm weights are removed from vertices
    that belong to the leg or pelvis, and leg weights from vertices that belong to the arm.
    """
    radius = {"Hips": 0.16, "UpLeg": 0.085, "Leg": 0.06, "Arm": 0.05, "ForeArm": 0.042, "Hand": 0.04}
    arm_parts, leg_parts = ("Arm", "ForeArm", "Hand"), ("UpLeg", "Leg", "Hips")
    segs = {}
    for b in rig.data.bones:
        part = b.name.replace("Left", "").replace("Right", "")
        if part in radius:
            segs[b.name] = (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local, radius[part], part)

    def seg_dist(p, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (a + ab * t - p).length

    names = {g.index: g.name for g in mesh.vertex_groups}
    changed = 0
    for v in mesh.data.vertices:
        groups = [(names[g.group], g) for g in v.groups if g.weight > 1e-4]
        has_arm = any(n.replace("Left", "").replace("Right", "") in arm_parts for n, _ in groups)
        has_leg = any(n.replace("Left", "").replace("Right", "") in leg_parts for n, _ in groups)
        if not (has_arm and has_leg):
            continue
        best = min(segs.items(), key=lambda kv: seg_dist(v.co, kv[1][0], kv[1][1]) / kv[1][2])
        owner_is_arm = best[1][3] in arm_parts
        drop = [n for n, _ in groups if n.replace("Left", "").replace("Right", "") in (leg_parts if owner_is_arm else arm_parts)]
        keep = [(n, g.weight) for n, g in groups if n not in drop]
        if not keep:
            keep = [(best[0], 1.0)]
        total = sum(w for _, w in keep)
        for n in drop:
            mesh.vertex_groups[n].remove([v.index])
        for n, w in keep:
            (mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)).add([v.index], w / total, "REPLACE")
        changed += 1
    log(f"{changed} vertices shared between a hand and a thigh: given to the limb they belong to")


def to_t_pose(rig, meshes):
    """
    Unity builds a Humanoid avatar from the rest pose and expects a T-pose: arms straight out to the sides. The patient
    was modelled with the arms by the sides, so the arms are raised to horizontal, the deformed mesh is kept, and that
    pose becomes the new rest pose.
    """
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for side, sign in (("Left", 1), ("Right", -1)):
        target = Vector((sign, 0, 0))
        for name in (side + "Arm", side + "ForeArm", side + "Hand"):
            bpy.context.view_layer.update()
            pb = rig.pose.bones[name]
            head = rig.matrix_world @ pb.head
            d = (rig.matrix_world @ pb.tail) - head
            q = d.normalized().rotation_difference(target)
            world = rig.matrix_world @ pb.matrix
            world = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ world
            pb.matrix = rig.matrix_world.inverted() @ world
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    # Bake the posed shape into the meshes, make the pose the rest pose, and bind again.
    for o in meshes:
        mod = next((m for m in o.modifiers if m.type == "ARMATURE"), None)
        if mod is None:
            continue
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        m = o.modifiers.new("Armature", "ARMATURE")
        m.object = rig
    log("rest pose is now a T-pose")


def render_test(rig, meshes, path, H):
    """Rest pose and a test pose (arms raised, head turned, one knee bent) side by side."""
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    sc.display.shading.light = "STUDIO"
    sc.render.resolution_x, sc.render.resolution_y = 700, 700
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = H * 1.15
    cam.location = Vector((0.6, -3.0, H / 2))
    cam.rotation_euler = (Vector((0, 0, H / 2)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    for name, pose in (("rest", {}), ("pose", {"LeftArm": ("Z", 70), "RightForeArm": ("Z", 80), "Head": ("Y", 35), "LeftLeg": ("X", 60), "Spine1": ("X", 10)})):
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (0, 0, 0)
        for b, (ax, deg) in pose.items():
            pb = rig.pose.bones[b]
            e = [0, 0, 0]
            e["XYZ".index(ax)] = math.radians(deg)
            pb.rotation_euler = e
        bpy.context.view_layer.update()
        sc.render.filepath = path.replace(".png", f"_{name}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(*argv[:3])
