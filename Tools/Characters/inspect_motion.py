"""Prints pelvis/head height and the body's up direction at a few frames of a HY-Motion FBX (does it lie or sit?)."""
import bpy, sys
a = sys.argv[sys.argv.index("--") + 1:]
for path in a:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    act = arm.animation_data.action if arm.animation_data else None
    f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (1, 1)
    sc = bpy.context.scene
    out = []
    for f in (f0, (f0 + f1) // 2, f1):
        sc.frame_set(f)
        pel = arm.matrix_world @ arm.pose.bones["Pelvis"].head
        head = arm.matrix_world @ arm.pose.bones["Head"].head
        up = (head - pel).normalized()
        out.append(f"f{f}: pelvis z {pel.z:.2f}, head z {head.z:.2f}, up {tuple(round(x, 2) for x in up)}")
    print("MOTION", path.split("/")[-1].split("\\")[-1], f"frames {f0}-{f1}", " | ".join(out))
