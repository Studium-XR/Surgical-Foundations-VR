import bpy, sys, math
from collections import defaultdict
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=a[0])
rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
body = max((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
print("DBG meshes", [(o.name, len(o.data.vertices)) for o in bpy.context.scene.objects if o.type == "MESH"])
mw = body.matrix_world
H = max((mw @ v.co).z for v in body.data.vertices)
rest = {v.index: mw @ v.co for v in body.data.vertices}
pb = rig.pose.bones["LeftArm"]; pb.rotation_mode = "XYZ"; pb.rotation_euler = (0, 0, math.radians(-70))
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg).to_mesh()
names = {g.index: g.name for g in body.vertex_groups}
moved = []
for v in body.data.vertices:
    p0 = rest[v.index]; p1 = mw @ ev.vertices[v.index].co
    if (p1 - p0).length > 0.05 and p0.z < 0.6 * H:
        moved.append(v)
print("DBG H", round(H, 3), "moved low verts", len(moved))
agg = defaultdict(float)
for v in moved:
    for g in v.groups: agg[names[g.group]] += g.weight
print("DBG groups", sorted(((k, round(x, 1)) for k, x in agg.items()), key=lambda t: -t[1])[:10])
if moved:
    zs = sorted(rest[v.index].z / H for v in moved); xs = sorted(rest[v.index].x for v in moved)
    print("DBG rest z/H range", round(zs[0], 3), round(zs[-1], 3), "x range", round(xs[0], 3), round(xs[-1], 3))
