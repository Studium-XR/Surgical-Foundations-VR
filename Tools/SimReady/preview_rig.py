"""Renders a rigged GLB at rest and mid-showcase, side by side, for a quick visual check.
blender -b --factory-startup -P preview_rig.py -- <rigged.glb> <out.png>"""
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a[0])
sc = bpy.context.scene
objs = [o for o in sc.objects if o.type == "MESH"]
# Pose joints straight from the manifest (fully open), rather than relying on NLA playback after re-import.
import json
man = json.load(open(a[0][:-4] + ".joints.json")) if len(a) < 3 else json.load(open(a[2]))
for o in sc.objects:
    if o.animation_data:
        o.animation_data.action = None
        for t in o.animation_data.nla_tracks: t.mute = True
REST = {o.name: o.matrix_basis.copy() for o in sc.objects}
def pose(open_):
    from mathutils import Matrix
    for j in man["joints"]:
        o = sc.objects.get(j["node"])
        if not o: continue
        lo, hi = j["min"], j["max"]
        v = (hi if abs(hi) >= abs(lo) else lo) if open_ else 0
        if j["kind"] == "spin": v = 0
        if j["unit"] == "deg": o.matrix_basis = REST[o.name] @ Matrix.Rotation(math.radians(-v), 4, "Z")
        else: o.matrix_basis = REST[o.name] @ Matrix.Translation((0, 0, v))
lo = Vector((1e9,)*3); hi = Vector((-1e9,)*3)
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
c = (lo + hi) / 2; r = (hi - lo).length
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
d = Vector((1.0, -1.3, 0.7)).normalized() * r * 1.35
cam.location = c + d
cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.rotation_euler = (0.8, 0.2, 0.6); sc.collection.objects.link(sun)
try: sc.render.engine = "BLENDER_WORKBENCH"
except TypeError: pass
sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 640, 640
import os
base = os.path.splitext(os.path.abspath(a[1]))[0]
for f, open_ in ((1, False), (31, True)):  # rest, and every joint fully open
    pose(open_); bpy.context.view_layer.update()
    sc.render.filepath = f"{base}_f{f}.png"; bpy.ops.render.render(write_still=True)
