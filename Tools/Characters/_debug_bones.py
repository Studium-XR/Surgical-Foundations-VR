import bpy, sys
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=a[0])
rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
for b in rig.data.bones:
    if b.name.startswith("Left") or b.name in ("Hips", "Spine2", "Head"):
        h = rig.matrix_world @ b.head_local; t = rig.matrix_world @ b.tail_local
        print("BONE", b.name, tuple(round(x, 3) for x in h), "->", tuple(round(x, 3) for x in t))
