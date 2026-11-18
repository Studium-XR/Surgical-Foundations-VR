"""Render review images of the built anatomy model (black backdrop, key + cool rim light, like the reference sheet).

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b Tools/Anatomy/Output/ANA_HumanMale.blend ^
        --python Tools/Anatomy/render_previews.py -- [--only torso_front,airway]

Writes Docs/Images/Anatomy/<view>.png.
"""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Vector

REPO = Path(__file__).resolve().parent.parent.parent
OUT = REPO / "Docs/Images/Anatomy"
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = set(ARGS[ARGS.index("--only") + 1].split(",")) if "--only" in ARGS else None

AIRWAY = {"Trachea", "BronchialTree", "Epiglottis", "ThyroidCartilage", "CricoidCartilage", "ArytenoidCartilages",
          "ThyroidGland", "Oesophagus", "Stomach", "Pharynx", "Liver", "Gallbladder", "Spleen"}
GUT = {"Stomach", "Duodenum", "SmallIntestine", "Colon_Ascending", "Colon_Transverse", "Colon_Descending",
       "Colon_SigmoidRectum", "Appendix", "Colon_Taeniae", "Pancreas", "Spleen", "Liver", "Gallbladder",
       "UrinaryBladder", "Kidney_Left", "Kidney_Right"}
HIDE_ALWAYS = {"GreaterOmentum", "LesserOmentum", "Mesocolon"}
FEMALE = {"Uterus", "Ovaries", "UterineTubes", "Ligaments"}
MALE_ONLY = {"Penis", "Testes", "Prostate", "SeminalVesicles"}
PELVIS_F = FEMALE | {"UrinaryBladder", "Colon_SigmoidRectum", "Ureters", "Appendix", "Colon_Ascending", "Colon_Descending"}


def is_female(org):
    return org in FEMALE or org.startswith("Lesion_")

# name: (resolution, view dir from target to eye, organ filter or None, skin mode, frame z-range or None)
VIEWS = {
    "full_body_ghost":    ((720, 1280), (0, -1, 0.05), None, "ghost", None),
    "torso_front":        ((1280, 720), (0, -1, 0.05), None, "hidden", (0.78, 1.62)),
    "torso_three_quarter": ((1280, 720), (-0.8, -1, 0.15), None, "hidden", (0.78, 1.62)),
    "thorax_no_pleura":   ((1280, 720), (0, -1, 0.05), "no_pleura", "hidden", (1.05, 1.55)),
    "airway_digestive":   ((1280, 720), (0, -1, 0.05), AIRWAY, "hidden", (1.02, 1.58)),
    "abdomen_gut":        ((1280, 720), (0, -1, 0.1), GUT, "hidden", (0.80, 1.26)),
    "skin":               ((720, 1280), (-0.5, -1, 0.1), set(), "solid", None),
    # laparoscopic-style view of the female pelvis from above the umbilicus, head-down tilt
    "female_pelvis_lap":  ((1280, 720), (0, -0.7, 0.75), "female", "hidden", (0.80, 0.98)),
    "female_pelvis_front": ((1280, 720), (0, -1, 0.15), "female", "hidden", (0.80, 0.98)),
}


def organ_of(obj):
    return obj.parent.name if obj.parent else obj.name


def setup_engine(scene):
    ids = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "CYCLES"):
        if eng in ids:
            try:
                scene.render.engine = eng
                break
            except TypeError:
                continue
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 48
        scene.cycles.use_denoising = True
    else:
        ee = scene.eevee
        for attr, val in (("taa_render_samples", 64), ("use_gtao", True), ("use_shadows", True)):
            if hasattr(ee, attr):
                setattr(ee, attr, val)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX" if "AgX" in [
        i.identifier for i in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] \
        else "Filmic"
    scene.view_settings.look = "None"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0, 0, 0, 1)
    bg.inputs["Strength"].default_value = 0.0


def add_light(name, loc, energy, color, size):
    ld = bpy.data.lights.new(name, "AREA")
    ld.energy, ld.color, ld.size = energy, color, size
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return ob


def aim(ob, target):
    ob.rotation_euler = (target - ob.location).to_track_quat("-Z", "Y").to_euler()


def ghost_skin_material():
    m = bpy.data.materials.new("PREVIEW_SkinGhost")
    try:
        m.use_nodes = True
    except AttributeError:
        pass
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.8, 0.58, 0.48, 1)
    b.inputs["Alpha"].default_value = 0.12
    b.inputs["Roughness"].default_value = 0.4
    if hasattr(m, "surface_render_method"):
        m.surface_render_method = "BLENDED"
    return m


def main():
    scene = bpy.context.scene
    setup_engine(scene)
    meshes = [o for o in scene.objects if o.type == "MESH"]
    for o in meshes:
        if o.name.endswith("_LOD1"):
            o.hide_render = True
    lod0 = [o for o in meshes if not o.name.endswith("_LOD1")]
    skin = next(o for o in lod0 if organ_of(o) == "Skin")
    skin_mat = skin.data.materials[0]
    ghost = ghost_skin_material()

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 85
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24
    cam_data.clip_start, cam_data.clip_end = 0.05, 50
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    key = add_light("Key", (-1.2, -2.0, 2.4), 450, (1.0, 0.95, 0.9), 1.5)
    rim = add_light("Rim", (1.6, 1.2, 2.0), 380, (0.6, 0.7, 1.0), 1.2)
    fill = add_light("Fill", (1.8, -1.8, 1.0), 120, (1.0, 0.9, 0.85), 2.0)
    OUT.mkdir(parents=True, exist_ok=True)

    for name, (res, direction, organs, skin_mode, zrange) in VIEWS.items():
        if ONLY and name not in ONLY:
            continue
        visible = []
        for o in lod0:
            org = organ_of(o)
            show = org not in HIDE_ALWAYS
            if org == "Skin":
                show = skin_mode != "hidden"
            elif org == "Hair":
                show = skin_mode == "solid"
            elif organs == "female":
                show = org in PELVIS_F or org.startswith("Lesion_")
            elif organs == "no_pleura":
                show = show and org != "Pleura" and not is_female(org)
            elif organs is not None:
                show = org in organs
            else:
                show = show and not is_female(org)
            o.hide_render = not show
            if show:
                visible.append(o)
        skin.data.materials[0] = ghost if skin_mode == "ghost" else skin_mat
        pts = [o.matrix_world @ Vector(c) for o in visible for c in o.bound_box]
        if zrange:
            pts = [p for p in pts if zrange[0] - 0.05 <= p.z <= zrange[1] + 0.05] or pts
        mn = Vector([min(p[i] for p in pts) for i in range(3)])
        mx = Vector([max(p[i] for p in pts) for i in range(3)])
        if zrange:
            mn.z, mx.z = zrange
        centre = (mn + mx) / 2
        h = (mx.z - mn.z) * 1.12
        w = max(mx.x - mn.x, mx.y - mn.y) * 1.12
        scene.render.resolution_x, scene.render.resolution_y = res
        aspect = res[0] / res[1]
        half_fov = math.atan(12 / cam_data.lens)
        dist = max(h, w / aspect) / 2 / math.tan(half_fov) + max(mx.y - mn.y, 0.2)
        d = Vector(direction).normalized()
        cam.location = centre + d * dist
        aim(cam, centre)
        for lt in (key, rim, fill):
            aim(lt, centre)
        scene.render.filepath = str(OUT / f"{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[preview] {name} -> {scene.render.filepath}", flush=True)
    skin.data.materials[0] = skin_mat


main()
