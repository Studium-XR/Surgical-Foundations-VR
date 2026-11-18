"""Build the Unity-ready male anatomy model (skin + organs in place) from the Z-Anatomy atlas.

Run from the repo root (takes a few minutes, the source file is 300 MB):

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --disable-autoexec ^
        Tools/Anatomy/Source/Startup.blend --python Tools/Anatomy/build_unity_anatomy.py -- [--budget 1.0] [--no-lod]

Pipeline
  1. extract  - inside Startup.blend: evaluate each organ's source meshes (subdivision applied), weld, box-UV,
                decimate to its triangle budget, write them to Output/_extract.blend. Z-Anatomy is built from
                BodyParts3D scan data, so every organ already sits at its true position inside the body.
  2. assemble - in an empty file: append the extract, generate the parts the atlas does not model as meshes
                (jejunum/ileum, oesophagus, bronchial tree) routed around the real organs, build the hierarchy
                (group > organ > organ_LOD0/_LOD1, which Unity turns into LODGroups), export the FBX, save the
                editable .blend and a JSON report.

Coordinates: Z-Anatomy is metres, Z up, the body faces -Y with the feet at Z=0. The FBX is exported Y-up with
transforms baked, so in Unity the body stands at the origin, 1 unit = 1 m, facing +Z.

Licence: the source data is CC BY-SA 4.0 (Z-Anatomy, from BodyParts3D CC BY-SA 2.1 JP). Everything exported here
is a derivative and must ship with attribution under the same licence; see ANATOMY_ATTRIBUTION.md.
"""
import bpy
import bmesh
import json
import math
import random
import sys
import time
from collections import defaultdict
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

TOOL_DIR = Path(__file__).resolve().parent
REPO = TOOL_DIR.parent.parent
OUT_DIR = TOOL_DIR / "Output"
EXTRACT_BLEND = OUT_DIR / "_extract.blend"
SOURCE_BLEND = OUT_DIR / "ANA_HumanMale.blend"
REPORT = OUT_DIR / "build_report.json"
FBX_PATH = REPO / "Assets/_Project/Art/Models/Anatomy/ANA_HumanMale.fbx"

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
BUDGET = float(ARGS[ARGS.index("--budget") + 1]) if "--budget" in ARGS else 1.0
MAKE_LODS = "--no-lod" not in ARGS
LOD1_RATIO = 0.3

# ───────────────────────────── materials (names match Assets/_Project/Art/Materials/Anatomy) ─────────────────────────────
# name: (base colour linear-ish sRGB, roughness, alpha, subsurface)
MATERIALS = {
    "M_Skin":          ((0.80, 0.58, 0.48), 0.55, 1.00, 0.15),
    "M_Hair":          ((0.10, 0.06, 0.04), 0.60, 1.00, 0.00),
    "M_Eye":           ((0.92, 0.90, 0.88), 0.10, 1.00, 0.10),
    "M_Iris":          ((0.25, 0.35, 0.45), 0.20, 1.00, 0.00),
    "M_Lung":          ((0.86, 0.56, 0.54), 0.50, 1.00, 0.20),
    "M_Pleura":        ((0.55, 0.45, 0.65), 0.20, 0.22, 0.00),
    "M_Cartilage":     ((0.84, 0.88, 0.92), 0.40, 1.00, 0.10),
    "M_Heart":         ((0.55, 0.13, 0.11), 0.40, 1.00, 0.15),
    "M_Vessel_Artery": ((0.70, 0.12, 0.10), 0.35, 1.00, 0.10),
    "M_Vessel_Vein":   ((0.20, 0.22, 0.55), 0.35, 1.00, 0.10),
    "M_Liver":         ((0.32, 0.12, 0.08), 0.30, 1.00, 0.10),
    "M_Gallbladder":   ((0.36, 0.42, 0.14), 0.25, 1.00, 0.10),
    "M_Stomach":       ((0.80, 0.36, 0.34), 0.35, 1.00, 0.15),
    "M_Bowel":         ((0.90, 0.60, 0.60), 0.30, 1.00, 0.20),
    "M_Colon":         ((0.80, 0.47, 0.37), 0.35, 1.00, 0.15),
    "M_Gland":         ((0.88, 0.66, 0.48), 0.45, 1.00, 0.15),
    "M_Spleen":        ((0.30, 0.08, 0.16), 0.30, 1.00, 0.10),
    "M_Kidney":        ((0.55, 0.15, 0.12), 0.30, 1.00, 0.10),
    "M_Duct":          ((0.92, 0.82, 0.55), 0.35, 1.00, 0.10),
    "M_Bladder":       ((0.58, 0.20, 0.16), 0.35, 1.00, 0.10),
    "M_Tissue_Pink":   ((0.86, 0.52, 0.48), 0.40, 1.00, 0.15),
    "M_Tissue_Wet":    ((0.72, 0.28, 0.26), 0.25, 1.00, 0.15),
    "M_Muscle":        ((0.55, 0.16, 0.14), 0.50, 1.00, 0.10),
    "M_Peritoneum":    ((0.92, 0.80, 0.62), 0.30, 0.60, 0.00),
    "M_Uterus":        ((0.85, 0.55, 0.55), 0.35, 1.00, 0.20),
    "M_Ovary":         ((0.93, 0.80, 0.74), 0.45, 1.00, 0.15),
    "M_Ligament":      ((0.88, 0.70, 0.62), 0.45, 1.00, 0.10),
    # endometriosis lesion appearances
    "M_Lesion_Red":    ((0.62, 0.05, 0.08), 0.25, 1.00, 0.10),
    "M_Lesion_Clear":  ((0.95, 0.85, 0.80), 0.05, 0.55, 0.00),
    "M_Lesion_Black":  ((0.12, 0.05, 0.08), 0.40, 1.00, 0.00),
    "M_Lesion_White":  ((0.93, 0.90, 0.86), 0.55, 1.00, 0.00),
}

MALE_ONLY = {"Penis", "Testes", "Prostate", "SeminalVesicles"}
FEMALE_GROUP = "Pelvis/Female"
LESION_GROUP = "Pelvis/Endometriosis"
FEMALE_FBX = REPO / "Assets/_Project/Art/Models/Anatomy/ANA_HumanFemale.fbx"

# ───────────────────────────── organ manifest ─────────────────────────────
# (organ, group path, Z-Anatomy source objects, material, LOD0 triangle budget, extra options)
# Options: weld (merge distance in m), no_solidify (drop wall-thickness modifiers: single surface shells),
#          optional (missing sources are fine), subsurf (subdivision level to evaluate before decimating).
SKIN = "__SKIN__"
ORGANS = [
    # Body surface
    ("Skin", "Body", [SKIN], "M_Skin", 70000, dict(weld=0.0006, no_solidify=True, subsurf=1)),
    ("Hair", "Body", ["Hairs of head", "Hairs of eyebrow.l", "Hairs of eyebrow.r", "Eyelashes.l", "Eyelashes.r"],
     "M_Hair", 12000, dict(subsurf=0)),
    ("Eyes", "Body", ["Sclera.l", "Sclera.r", "Cornea.l", "Cornea.r"], "M_Eye", 4000, dict(optional=True)),
    ("Irises", "Body", ["Iris.l", "Iris.r"], "M_Iris", 2000, dict(optional=True)),
    # Neck
    ("Pharynx", "Neck", ["Nasopharynx", "Oropharynx", "Laryngopharynx"], "M_Tissue_Wet", 8000, {}),
    ("Tongue", "Neck", ["Tongue"], "M_Tissue_Pink", 6000, {}),
    ("SalivaryGlands", "Neck", ["Parotid gland.l", "Parotid gland.r", "(Accessory parotid gland).l",
                                "(Accessory parotid gland).r", "Sublingual gland.l", "Sublingual gland.r",
                                "Submandibular gland.l", "Submandibular gland.r"], "M_Gland", 6000, {}),
    ("Epiglottis", "Neck/Larynx", ["Epiglottis"], "M_Cartilage", 3000, {}),
    ("ThyroidCartilage", "Neck/Larynx", ["Thyroid cartilage"], "M_Cartilage", 6000, {}),
    ("CricoidCartilage", "Neck/Larynx", ["Cricoid cartilage"], "M_Cartilage", 3000, {}),
    ("ArytenoidCartilages", "Neck/Larynx", ["Arytenoid cartilage.l", "Arytenoid cartilage.r"], "M_Cartilage", 2000, {}),
    ("ThyroidGland", "Neck", ["Thyroid gland"], "M_Gland", 6000, {}),
    ("ParathyroidGlands", "Neck", ["Superior parathyroid gland.l", "Superior parathyroid gland.r",
                                   "Inferior parathyroid gland.l", "Inferior parathyroid gland.r"], "M_Gland", 800, {}),
    # Thorax
    ("Trachea", "Thorax/Airway", ["Trachea"], "M_Cartilage", 8000, {}),
    ("Lung_Right_Superior", "Thorax/Lungs", ["Superior lobe of right lung"], "M_Lung", 12000, {}),
    ("Lung_Right_Middle", "Thorax/Lungs", ["Middle lobe of right lung"], "M_Lung", 10000, {}),
    ("Lung_Right_Inferior", "Thorax/Lungs", ["Inferior lobe of right lung"], "M_Lung", 12000, {}),
    ("Lung_Left_Superior", "Thorax/Lungs", ["Superior lobe of left lung"], "M_Lung", 12000, {}),
    ("Lung_Left_Inferior", "Thorax/Lungs", ["Inferior lobe of left lung"], "M_Lung", 12000, {}),
    ("Pleura", "Thorax", ["Pleura"], "M_Pleura", 16000, dict(no_solidify=True)),
    ("RightAtrium", "Thorax/Heart", ["Right atrium"], "M_Heart", 10000, {}),
    ("RightVentricle", "Thorax/Heart", ["Right ventricle"], "M_Heart", 10000, {}),
    ("LeftAtrium", "Thorax/Heart", ["Left atrium"], "M_Heart", 10000, {}),
    ("LeftVentricle", "Thorax/Heart", ["Left ventricle"], "M_Heart", 10000, {}),
    ("PulmonaryTrunk", "Thorax/Heart", ["Pulmonary trunk"], "M_Vessel_Artery", 3000, {}),
    ("Aorta", "Thorax/GreatVessels", ["Ascending aorta", "Arch of aorta", "Aortic arch", "Thoracic aorta",
                                      "Descending thoracic aorta", "Abdominal aorta"], "M_Vessel_Artery", 8000,
     dict(optional=True)),
    ("VenaeCavae", "Thorax/GreatVessels", ["Superior vena cava", "Inferior vena cava"], "M_Vessel_Vein", 6000,
     dict(optional=True)),
    ("Thymus", "Thorax", ["Right lobe of thymus", "Left lobe of thymus"], "M_Gland", 6000, {}),
    ("Diaphragm", "Thorax", ["Diaphragm"], "M_Muscle", 16000, {}),
    # Abdomen
    ("Liver", "Abdomen", ["Liver"], "M_Liver", 30000, {}),
    ("Gallbladder", "Abdomen", ["Gallbladder"], "M_Gallbladder", 4000, {}),
    ("Stomach", "Abdomen", ["Stomach"], "M_Stomach", 16000, {}),
    ("Duodenum", "Abdomen/Intestines", ["Duodenum"], "M_Bowel", 8000, {}),
    ("Pancreas", "Abdomen", ["Pancreas"], "M_Gland", 10000, {}),
    ("Spleen", "Abdomen", ["Spleen"], "M_Spleen", 8000, {}),
    ("Colon_Ascending", "Abdomen/Intestines", ["Ascending colon"], "M_Colon", 8000, {}),
    ("Colon_Transverse", "Abdomen/Intestines", ["Transverse colon"], "M_Colon", 8000, {}),
    ("Colon_Descending", "Abdomen/Intestines", ["Descending colon"], "M_Colon", 8000, {}),
    ("Colon_SigmoidRectum", "Abdomen/Intestines", ["Sigmoid colon"], "M_Colon", 10000, {}),
    ("Appendix", "Abdomen/Intestines", ["Vermiform appendix"], "M_Colon", 2000, {}),
    ("Colon_Taeniae", "Abdomen/Intestines", ["Free taenia", "Mesocolic taenia", "Omental taenia"], "M_Colon", 3000, {}),
    ("Kidney_Left", "Abdomen/Urinary", ["Kidney.l"], "M_Kidney", 8000, {}),
    ("Kidney_Right", "Abdomen/Urinary", ["Kidney.r"], "M_Kidney", 8000, {}),
    ("RenalPelvis_Left", "Abdomen/Urinary", ["Renal pelvis.l"], "M_Duct", 3000, {}),
    ("RenalPelvis_Right", "Abdomen/Urinary", ["Renal pelvis.r"], "M_Duct", 3000, {}),
    ("Ureters", "Abdomen/Urinary", ["Ureter.l", "Ureter.r", "Abdominal part of ureter.l", "Abdominal part of ureter.r",
                                    "Pelvic part of ureter.l", "Pelvic part of ureter.r"], "M_Duct", 3000,
     dict(optional=True)),
    ("Adrenal_Left", "Abdomen/Urinary", ["Suprarenal gland.l"], "M_Gland", 3000, {}),
    ("Adrenal_Right", "Abdomen/Urinary", ["Suprarenal gland.r"], "M_Gland", 3000, {}),
    ("GreaterOmentum", "Abdomen/Peritoneum", ["Greater omentum"], "M_Peritoneum", 8000, {}),
    ("LesserOmentum", "Abdomen/Peritoneum", ["Lesser omentum"], "M_Peritoneum", 2000, {}),
    ("Mesocolon", "Abdomen/Peritoneum", ["Mesocolon", "Meso-appendix"], "M_Peritoneum", 4000, {}),
    # Pelvis
    ("UrinaryBladder", "Pelvis", ["Urinary bladder"], "M_Bladder", 6000, {}),
    ("Prostate", "Pelvis", ["Prostate"], "M_Gland", 3000, {}),
    ("SeminalVesicles", "Pelvis", ["Seminal gland.l", "Seminal gland.r"], "M_Gland", 3000, {}),
    ("Testes", "Pelvis", ["Testis.l", "Testis.r", "Epididymis.l", "Epididymis.r"], "M_Tissue_Pink", 4000, {}),
    ("Penis", "Pelvis", ["Glans penis", "Corpus cavernosum of penis", "Corpus spongiosum of penis"],
     "M_Tissue_Pink", 10000, {}),
]

# Meshes that only steer the generated tubes (never exported).
OBSTACLE_PREFIX = "OBST_"
OBSTACLE_SOURCES = ["Sacrum", "Psoas major.l", "Psoas major.r"] + \
    [f"Vertebra C{i}" for i in (6, 7)] + [f"Vertebra T{i}" for i in range(1, 13)] + [f"Vertebra L{i}" for i in range(1, 6)]

# Generated organs: (organ, group, material, LOD0 budget)
GENERATED = [
    ("Oesophagus", "Thorax", "M_Tissue_Wet"),
    ("BronchialTree", "Thorax/Airway", "M_Cartilage"),
    ("SmallIntestine", "Abdomen/Intestines", "M_Bowel"),
]

ROOT_NAME = "ANA_HumanMale"
EXTRACT_COLLECTION = "UNITY_EXTRACT"
EXTRACT_PREFIX = "UX_"


def log(*a):
    print("[anatomy]", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


# ═════════════════════════════════════ shared mesh helpers ═════════════════════════════════════

def ensure_material(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    color, rough, alpha, sss = MATERIALS[name]
    try:
        mat.use_nodes = True  # no-op / deprecated on Blender 5.x, required on 4.x
    except AttributeError:
        pass
    nodes = mat.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        nodes.clear()
        bsdf = nodes.new("ShaderNodeBsdfPrincipled")
        out = nodes.new("ShaderNodeOutputMaterial")
        mat.node_tree.links.new(bsdf.outputs[0], out.inputs[0])
    lin = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in color)  # node colours are linear
    bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Alpha"].default_value = alpha
    if "Subsurface Weight" in bsdf.inputs:
        bsdf.inputs["Subsurface Weight"].default_value = sss
        bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.25)
        bsdf.inputs["Subsurface Scale"].default_value = 0.004
    mat.diffuse_color = (*color, alpha)  # what the FBX exporter writes as the diffuse colour
    if alpha < 1.0 and hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "BLENDED"
    return mat


def box_uv(bm, scale=8.0):
    """Tri-planar box projection (1 UV unit = 12.5 cm) so tiling tissue detail maps work on every organ."""
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for loop in f.loops:
            co = loop.vert.co
            loop[uv].uv = (co[a] * scale, co[b] * scale)


def bmesh_to_mesh(bm, name, mat_name):
    me = bpy.data.meshes.new(name)
    # keep only the UV layer we wrote
    for layer in list(bm.loops.layers.uv):
        if layer.name != "UVMap":
            bm.loops.layers.uv.remove(layer)
    for f in bm.faces:
        f.material_index = 0
        f.smooth = True
    bm.to_mesh(me)
    me.materials.clear()
    me.materials.append(ensure_material(mat_name))
    return me


def decimated_copy(me, ratio, name):
    """Collapse-decimate a mesh via a temporary object; returns a new mesh."""
    tmp = bpy.data.objects.new("__dec", me)
    bpy.context.scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new("dec", "DECIMATE")
    mod.ratio = max(0.01, min(1.0, ratio))
    mod.use_collapse_triangulate = False
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    out = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), depsgraph=dg)
    out.name = name
    bpy.data.objects.remove(tmp)
    for p in out.polygons:
        p.use_smooth = True
    return out


def fit_budget(me, budget):
    tris = tri_count(me)
    if tris <= budget:
        return me
    out = decimated_copy(me, budget / tris, me.name)
    bpy.data.meshes.remove(me)
    return out


# ═════════════════════════════════════ phase 1: extract from Z-Anatomy ═════════════════════════════════════

def skin_sources():
    col = bpy.data.collections.get("9: Regions of human body")
    if col is None:
        raise RuntimeError("Collection '9: Regions of human body' not found - is this the Z-Anatomy Startup.blend?")
    names = []
    for o in col.all_objects:
        n = o.name.lower()
        if o.type != "MESH" or len(o.data.vertices) <= 2 or o.name.endswith((".g", ".j")):
            continue
        if "hair" in n or "eyelash" in n:
            continue
        names.append(o.name)
    return sorted(set(names))


def prepare_source(o, src_col, opts):
    if o.name not in src_col.objects:
        src_col.objects.link(o)
    o.hide_viewport = False
    try:
        o.hide_set(False)
    except RuntimeError:
        pass
    sub = opts.get("subsurf")
    if o.type == "MESH" and sub is None and not any(m.type == "SUBSURF" for m in o.modifiers):
        # low-res BodyParts3D meshes with no smoothing of their own: subdivide, then let the budget decimate
        if len(o.data.vertices) < 6000:
            m = o.modifiers.new("unity_smooth", "SUBSURF")
            m.render_levels = 2 if len(o.data.vertices) < 1500 else 1
    for m in o.modifiers:
        m.show_viewport = m.show_render
        if m.type == "SOLIDIFY" and opts.get("no_solidify"):
            m.show_viewport = False
        if m.type == "SUBSURF":
            if sub is not None:
                m.levels = sub
            else:
                m.levels = max(m.render_levels, 2) if len(o.data.vertices) < 4000 else max(m.render_levels, 1)


def evaluated_bmesh(names, dg):
    bm = bmesh.new()
    for name in names:
        o = bpy.data.objects[name]
        oe = o.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(oe, depsgraph=dg)
        me.transform(oe.matrix_world)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    return bm


def phase_extract():
    t0 = time.time()
    scene = bpy.context.scene
    src_col = bpy.data.collections.new("__SOURCES")
    scene.collection.children.link(src_col)
    out_col = bpy.data.collections.new(EXTRACT_COLLECTION)
    scene.collection.children.link(out_col)

    jobs = []  # (out name, names, material, budget, opts)
    missing = {}
    for organ, group, sources, mat, budget, opts in ORGANS:
        names = skin_sources() if sources == [SKIN] else sources
        found = [n for n in names if n in bpy.data.objects and bpy.data.objects[n].type in {"MESH", "CURVE"}]
        lost = [n for n in names if n not in found]
        if lost:
            missing[organ] = lost
        if not found:
            log(f"skip {organ}: none of {names} exist" + ("" if opts.get("optional") else "  <-- REQUIRED"))
            continue
        jobs.append((organ, found, mat, int(budget * BUDGET), opts, group))
    for src in OBSTACLE_SOURCES:
        if src in bpy.data.objects:
            jobs.append((OBSTACLE_PREFIX + src, [src], "M_Muscle", 4000, dict(subsurf=0), ""))

    for organ, found, mat, budget, opts, group in jobs:
        for n in found:
            prepare_source(bpy.data.objects[n], src_col, opts)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()

    for organ, found, mat, budget, opts, group in jobs:
        bm = evaluated_bmesh(found, dg)
        weld = opts.get("weld", 0.00002)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.normal_update()
        box_uv(bm)
        me = fit_budget(bmesh_to_mesh(bm, organ, mat), budget)
        bm.free()
        ob = bpy.data.objects.new(EXTRACT_PREFIX + organ, me)  # prefix: Z-Anatomy already owns e.g. "Stomach"
        ob["group"] = group
        ob["sources"] = ", ".join(found)
        out_col.objects.link(ob)
        log(f"{organ:24s} {tri_count(me):>7d} tris  <- {len(found)} source(s)")

    OUT_DIR.mkdir(exist_ok=True)
    bpy.data.libraries.write(str(EXTRACT_BLEND), {out_col}, fake_user=True)
    (OUT_DIR / "_missing_sources.json").write_text(json.dumps(missing, indent=2), encoding="utf-8")
    log(f"extract done in {time.time() - t0:.0f}s -> {EXTRACT_BLEND}")


# ═════════════════════════════════════ phase 2: generate, assemble, export ═════════════════════════════════════

def world_bvh(obj, fix_normals=True):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    if fix_normals:  # some atlas meshes are flipped; the inside/outside test needs outward normals
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def signed_dist(tree, p):
    loc, n, _, d = tree.find_nearest(p)
    if loc is None:
        return 1e9, None, None
    s = (p - loc).dot(n)
    return (d if s >= 0 else -d), loc, n


def inside(tree, p):
    loc, n, _, _ = tree.find_nearest(p)
    return loc is not None and (p - loc).dot(n) < 0


def chaikin(pts, iterations=3):
    for _ in range(iterations):
        out = [pts[0]]
        for a, b in zip(pts, pts[1:]):
            out += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
        out.append(pts[-1])
        pts = out
    return pts


def resample(pts, step):
    out = [pts[0].copy()]
    carry = 0.0
    for a, b in zip(pts, pts[1:]):
        seg = (b - a).length
        if seg < 1e-9:
            continue
        t = step - carry
        while t <= seg:
            out.append(a.lerp(b, t / seg))
            t += step
        carry = seg - (t - step)
    if (out[-1] - pts[-1]).length > step * 0.3:
        out.append(pts[-1].copy())
    else:
        out[-1] = pts[-1].copy()
    return out


def relax_path(pts, radius, obstacles, container=None, wall=0.0, iterations=10, fixed_ends=2):
    """Push a centreline out of obstacle organs (and inside the body wall), with Laplacian smoothing between passes."""
    clearance = radius * 1.08
    # the skin is stitched from many patches; check which way its normals face using a point we know is inside
    c_sign = 1.0
    if container is not None and signed_dist(container, Vector((0.0, -0.02, 1.0)))[0] > 0:
        c_sign = -1.0
    for it in range(iterations):
        for i in range(fixed_ends, len(pts) - fixed_ends):
            p = pts[i]
            for tree in obstacles:
                s, loc, n = signed_dist(tree, p)
                if loc is not None and -0.02 < s < clearance:  # deeper than 2 cm = bad normal, not a collision
                    p = loc + n * clearance
            if container is not None:
                # anterior abdominal wall: cast from the body's midplane forward (-Y) to the skin
                hit = container.ray_cast(Vector((p.x, 0.0, p.z)), Vector((0, -1, 0)))[0]
                if hit is not None and p.y < hit.y + radius + wall:
                    p = Vector((p.x, hit.y + radius + wall, p.z))
            pts[i] = p
        if it < iterations - 1:
            sm = pts[:]
            for i in range(fixed_ends, len(pts) - fixed_ends):
                sm[i] = pts[i] * 0.5 + (pts[i - 1] + pts[i + 1]) * 0.25
            pts = sm
    return pts


def tube_bmesh(bm, uv, pts, radii, segs, cap_start=False, cap_end=True):
    """Append a tube with parallel-transport frames; UV u = around, v = along (texel aspect ~1)."""
    n = len(pts)
    if n < 2:
        return
    tangents = []
    for i in range(n):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        t = (b - a)
        tangents.append(t.normalized() if t.length > 1e-12 else Vector((0, 0, 1)))
    ref = Vector((0, 0, 1)) if abs(tangents[0].z) < 0.9 else Vector((1, 0, 0))
    normal = tangents[0].cross(ref).normalized()
    rings = []
    v_coord = [0.0]
    for i in range(1, n):
        v_coord.append(v_coord[-1] + (pts[i] - pts[i - 1]).length)
    circ = 2 * math.pi * max(sum(radii) / len(radii), 1e-4)
    for i in range(n):
        if i > 0:
            q = tangents[i - 1].rotation_difference(tangents[i])
            normal = (q @ normal).normalized()
        t = tangents[i]
        binormal = t.cross(normal)
        ring = []
        for j in range(segs):
            ang = 2 * math.pi * j / segs
            ring.append(bm.verts.new(pts[i] + radii[i] * (math.cos(ang) * normal + math.sin(ang) * binormal)))
        rings.append(ring)
    for i in range(n - 1):
        for j in range(segs):
            j2 = (j + 1) % segs
            f = bm.faces.new((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]))
            u0, u1 = j / segs, (j + 1) / segs
            va, vb = v_coord[i] / circ, v_coord[i + 1] / circ
            for loop, co in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))):
                loop[uv].uv = co

    def cap(ring, center, flip):
        c = bm.verts.new(center)
        for j in range(segs):
            a, b = ring[j], ring[(j + 1) % segs]
            f = bm.faces.new((c, b, a) if not flip else (c, a, b))
            for loop in f.loops:
                loop[uv].uv = (0.5, 0.0)
    if cap_start:
        cap(rings[0], pts[0], False)
    if cap_end:
        cap(rings[-1], pts[-1], True)


def tube_object(name, mat, paths):
    """paths: list of (pts, radii, segs, cap_start, cap_end)."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for pts, radii, segs, cs, ce in paths:
        tube_bmesh(bm, uv, pts, radii, segs, cs, ce)
    bm.normal_update()
    me = bmesh_to_mesh(bm, name, mat)
    bm.free()
    return bpy.data.objects.new(name, me)


def nearest_surface_point(obj, target, inset):
    tree = world_bvh(obj)
    loc, n, _, _ = tree.find_nearest(Vector(target))
    return loc - n * inset, n


def make_oesophagus(objs):
    stomach = objs["Stomach"]
    cardia, _ = nearest_surface_point(stomach, (0.030, 0.000, 1.212), 0.006)
    ctrl = [Vector(p) for p in [(0.0, 0.004, 1.458), (0.001, 0.014, 1.440), (0.003, 0.026, 1.400),
                                (0.005, 0.032, 1.355), (0.008, 0.036, 1.310), (0.012, 0.034, 1.270),
                                (0.019, 0.024, 1.240), (0.026, 0.010, 1.222)]] + [cardia]
    pts = resample(chaikin(ctrl, 3), 0.004)
    obstacles = [world_bvh(objs[k]) for k in ("Trachea", "LeftAtrium", "RightAtrium", "Liver") if k in objs]
    obstacles += [world_bvh(o) for k, o in objs.items() if k.startswith(OBSTACLE_PREFIX + "Vertebra")]
    radius = 0.0085
    pts = relax_path(pts, radius, obstacles, iterations=8, fixed_ends=3)
    radii = [radius * (0.85 if i < 4 else 1.0) for i in range(len(pts))]
    return tube_object("Oesophagus", "M_Tissue_Wet", [(pts, radii, 18, True, False)])


def make_small_intestine(objs):
    """Jejunum (upper left) to ileum (lower right) as packed meanders in the infracolic space,
    starting at the duodenojejunal flexure and ending at the caecum."""
    duo = objs["Duodenum"]
    asc = objs["Colon_Ascending"]
    start, _ = nearest_surface_point(duo, (0.035, -0.040, 1.085), 0.004)
    end, _ = nearest_surface_point(asc, (-0.060, -0.028, 0.945), 0.004)
    rng = random.Random(11)
    radius = 0.0115
    leg_gap = 0.027                       # x distance between hairpin legs (> tube diameter)
    rows = [1.050, 1.015, 0.980, 0.945, 0.910]
    y_front, y_back = -0.080, -0.036
    ctrl = [start, Vector((0.060, -0.060, 1.075))]
    for r, z in enumerate(rows):
        x_lo, x_hi = (-0.050, 0.082) if r < 3 else (-0.048, 0.060)
        xs = [x_hi - k * leg_gap for k in range(int((x_hi - x_lo) / leg_gap) + 1)]
        if r % 2 == 1:
            xs.reverse()
        front = r % 2 == 0
        for x in xs:
            jx, jz = rng.uniform(-0.002, 0.002), rng.uniform(-0.003, 0.003)
            ya = (y_front if front else y_back) + rng.uniform(-0.004, 0.004)
            yb = (y_back if front else y_front) + rng.uniform(-0.004, 0.004)
            ctrl.append(Vector((x + jx, ya, z + jz)))
            ctrl.append(Vector((x + jx, yb, z + jz + rng.uniform(-0.004, 0.004))))
            front = not front
    ctrl += [Vector((-0.050, -0.040, 0.925)), end]
    pts = resample(chaikin(ctrl, 3), 0.004)
    obstacle_keys = ["Colon_Ascending", "Colon_Transverse", "Colon_Descending", "Colon_SigmoidRectum", "Appendix",
                     "Duodenum", "Pancreas", "Kidney_Left", "Kidney_Right", "UrinaryBladder", "Stomach", "Liver",
                     "Spleen", OBSTACLE_PREFIX + "Psoas major.l", OBSTACLE_PREFIX + "Psoas major.r",
                     OBSTACLE_PREFIX + "Sacrum"] + [k for k in objs if k.startswith(OBSTACLE_PREFIX + "Vertebra L")]
    obstacles = [world_bvh(objs[k]) for k in obstacle_keys if k in objs]
    container = world_bvh(objs["Skin"], fix_normals=False)
    pts = relax_path(pts, radius, obstacles, container=container, wall=0.016, iterations=10, fixed_ends=3)
    radii = [radius * (0.9 + 0.1 * min(1.0, i / 40.0)) for i in range(len(pts))]
    log(f"small intestine length {sum((b - a).length for a, b in zip(pts, pts[1:])):.2f} m")
    return tube_object("SmallIntestine", "M_Bowel", [(pts, radii, 20, False, False)])


def make_bronchial_tree(objs):
    """Space-colonisation bronchial tree grown from the carina into each lung's lobes; radii by Murray's law."""
    trachea = objs["Trachea"]
    tv = [trachea.matrix_world @ v.co for v in trachea.data.vertices]
    zmin = min(v.z for v in tv)
    low = [v for v in tv if v.z < zmin + 0.006]
    carina = sum(low, Vector()) / len(low)
    rng = random.Random(5)
    step, d_influence, d_kill = 0.0045, 0.035, 0.007
    hila = {"Right": (Vector((-0.040, carina.y + 0.008, carina.z - 0.022)), 0.0062),
            "Left": (Vector((0.046, carina.y + 0.012, carina.z - 0.035)), 0.0055)}
    nodes, parent, growable, fixed_r = [], [], [], {}
    def add(p, par, grow):
        nodes.append(p)
        parent.append(par)
        growable.append(grow)
        return len(nodes) - 1
    root = add(carina, -1, False)
    fixed_r[root] = 0.0080
    attractors = []
    for side, (hilum, r_hilum) in hila.items():
        steps = max(2, int((hilum - carina).length / step))
        prev = root
        for k in range(1, steps + 1):
            p = carina.lerp(hilum, k / steps)
            prev = add(p, prev, k == steps)
            fixed_r[prev] = 0.0080 + (r_hilum - 0.0080) * k / steps
        lobes = [objs[k] for k in objs if k.startswith(f"Lung_{side}_")]
        trees = [world_bvh(o) for o in lobes]
        mn = Vector([min((o.matrix_world @ Vector(c))[i] for o in lobes for c in o.bound_box) for i in range(3)])
        mx = Vector([max((o.matrix_world @ Vector(c))[i] for o in lobes for c in o.bound_box) for i in range(3)])
        target, tries = 1400, 0
        side_pts = []
        while len(side_pts) < target and tries < target * 60:
            tries += 1
            p = Vector([rng.uniform(mn[i], mx[i]) for i in range(3)])
            if any(inside(t, p) for t in trees):
                # stay off the pleural surface so twigs end inside the lung
                if min(abs(signed_dist(t, p)[0]) for t in trees) > 0.006:
                    side_pts.append(p)
        attractors += side_pts
    for _ in range(160):
        if not attractors:
            break
        kd = KDTree(len(nodes))
        for i, p in enumerate(nodes):
            if growable[i]:
                kd.insert(p, i)
        kd.balance()
        pull = defaultdict(Vector)
        for a in attractors:
            co, idx, d = kd.find(a)
            if idx is not None and d < d_influence:
                pull[idx] += (a - co).normalized()
        if not pull:
            break
        added = 0
        for idx, v in pull.items():
            if v.length < 1e-6:
                v = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
            p = nodes[idx] + v.normalized() * step
            _, _, dn = kd.find(p)
            if dn is not None and dn < step * 0.45:
                continue
            add(p, idx, True)
            added += 1
        if not added:
            break
        kd_all = KDTree(len(nodes))
        for i, p in enumerate(nodes):
            kd_all.insert(p, i)
        kd_all.balance()
        attractors = [a for a in attractors if kd_all.find(a)[2] > d_kill]
    children = defaultdict(list)
    for i, par in enumerate(parent):
        if par >= 0:
            children[par].append(i)
    radius = [0.0] * len(nodes)
    r_leaf = 0.00055
    for i in reversed(range(len(nodes))):
        kids = children[i]
        radius[i] = r_leaf if not kids else sum(radius[k] ** 3 for k in kids) ** (1 / 3)
    for side, (hilum, r_hilum) in hila.items():
        hid = next(i for i in fixed_r if (nodes[i] - hilum).length < 1e-6)
        scale = r_hilum / max(radius[hid], 1e-6)
        stack = [hid]
        while stack:
            i = stack.pop()
            radius[i] = max(r_leaf, radius[i] * scale)
            stack += children[i]
    for i, r in fixed_r.items():
        radius[i] = r
    # smooth interior chain nodes
    for _ in range(2):
        new = nodes[:]
        for i in range(len(nodes)):
            if i in fixed_r or len(children[i]) != 1 or parent[i] < 0:
                continue
            new[i] = nodes[i] * 0.5 + (nodes[parent[i]] + nodes[children[i][0]]) * 0.25
        nodes = new
    # chains: break at branch points
    paths = []
    starts = [root]
    while starts:
        s = starts.pop()
        for c in children[s]:
            chain = [s, c]
            while len(children[chain[-1]]) == 1:
                chain.append(children[chain[-1]][0])
            if len(children[chain[-1]]) > 1:
                starts.append(chain[-1])
            pts = [nodes[i] for i in chain]
            radii = [radius[i] for i in chain]
            radii[0] = radii[1]  # the joint uses the child's calibre so it tucks inside the parent
            segs = max(5, min(16, round(2 * math.pi * max(radii) / 0.0028)))
            paths.append((pts, radii, segs, False, not children[chain[-1]]))
    log(f"bronchial tree: {len(nodes)} nodes, {len(paths)} branches")
    return tube_object("BronchialTree", "M_Cartilage", paths)


# ─────────────── female pelvis (the atlas is a male specimen: these are generated to textbook dimensions) ───────────────
# Adult nulliparous sizes: uterus 7.5 x 5 x 3 cm (body:cervix 2:1, anteverted, anteflexed over the bladder), ovaries
# 3 x 2 x 1.5 cm in the ovarian fossae, uterine tubes ~10 cm arching over the ovaries to fimbriated ends.

def ellipsoid_bm(bm, uv, centre, radii, rot=Quaternion(), segs=24, rings=14):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    verts = geom["verts"]
    m = Matrix.Translation(Vector(centre)) @ rot.to_matrix().to_4x4() @ Matrix.Diagonal((*radii, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        for loop in f.loops:
            co = loop.vert.co
            loop[uv].uv = (co.x * 8, co.z * 8)
    return verts


def obj_from_bm(name, mat, bm):
    bm.normal_update()
    me = bmesh_to_mesh(bm, name, mat)
    bm.free()
    return bpy.data.objects.new(name, me)


def make_female_pelvis(objs):
    out = {}
    bladder = objs["UrinaryBladder"]
    bb = [bladder.matrix_world @ Vector(c) for c in bladder.bound_box]
    b_top = max(v.z for v in bb)
    b_back = max(v.y for v in bb)
    # uterus: cervix behind the bladder neck, body tipped forward over the bladder dome
    cervix = Vector((0.0, b_back + 0.004, b_top - 0.032))
    fundus = Vector((0.0, b_back - 0.040, b_top + 0.034))
    axis = (fundus - cervix)
    n = 26
    pts, radii = [], []
    for i in range(n):
        t = i / (n - 1)
        bend = math.sin(t * math.pi) * 0.006              # anteflexion
        pts.append(cervix.lerp(fundus, t) + Vector((0, -bend, 0)))
        # cervix 2.5 cm wide, isthmus narrow, body widening to a 5 cm fundus, closed dome at the top
        profile = 0.0125 + 0.004 * math.sin(min(t, 0.33) / 0.33 * math.pi / 2) if t < 0.33 else 0.0165 + 0.0085 * math.sin((t - 0.33) / 0.67 * math.pi * 0.6)
        if t > 0.86:
            profile *= math.sqrt(max(0.0, 1 - ((t - 0.86) / 0.14) ** 2)) * 0.9 + 0.1
        radii.append(profile)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    tube_bmesh(bm, uv, pts, radii, 28, cap_start=True, cap_end=True)
    # flatten front-to-back (3 cm deep vs 5 cm wide)
    side = Vector((1, 0, 0))
    depth = axis.normalized().cross(side).normalized()
    for v in bm.verts:
        c = cervix + axis * max(0.0, min(1.0, (v.co - cervix).dot(axis) / axis.length_squared))
        d = v.co - c
        v.co = c + d - depth * d.dot(depth) * 0.38
    out["Uterus"] = obj_from_bm("Uterus", "M_Uterus", bm)
    cornu = {s: fundus + Vector((s * 0.022, 0.004, -0.006)) for s in (-1, 1)}
    # ovaries in the ovarian fossae, long axis vertical
    ovary_c = {s: Vector((s * 0.062, b_back + 0.012, b_top + 0.004)) for s in (-1, 1)}
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for s in (-1, 1):
        ellipsoid_bm(bm, uv, ovary_c[s], (0.009, 0.0075, 0.015), Quaternion((0, 1, 0), math.radians(-25 * s)))
    out["Ovaries"] = obj_from_bm("Ovaries", "M_Ovary", bm)
    # uterine tubes: isthmus -> ampulla arching over the ovary -> fimbriae draped on its lateral pole
    paths = []
    for s in (-1, 1):
        o = ovary_c[s]
        ctrl = [cornu[s], cornu[s] + Vector((s * 0.018, 0.002, 0.004)), Vector((o.x - s * 0.010, o.y - 0.012, o.z + 0.026)),
                Vector((o.x + s * 0.012, o.y - 0.004, o.z + 0.024)), Vector((o.x + s * 0.016, o.y + 0.010, o.z + 0.008))]
        p = resample(chaikin(ctrl, 3), 0.0025)
        r = [0.0018 + 0.0032 * (i / (len(p) - 1)) ** 1.6 for i in range(len(p))]
        paths.append((p, r, 12, False, False))
        end, tdir = p[-1], (p[-1] - p[-2]).normalized()
        for k in range(9):  # fimbriae: short fingers fanning from the infundibulum
            ang = 2 * math.pi * k / 9
            perp = tdir.cross(Vector((0, 0, 1)) if abs(tdir.z) < 0.9 else Vector((1, 0, 0))).normalized()
            q = Quaternion(tdir, ang)
            spread = (q @ perp) * 0.6 + tdir
            f = [end + spread.normalized() * d for d in (0.0, 0.004, 0.008, 0.011)]
            paths.append((f, [0.0012, 0.0011, 0.0009, 0.0006], 6, False, True))
    out["UterineTubes"] = tube_object("UterineTubes", "M_Uterus", paths)
    # ligaments: ovarian (ovary -> uterus), round (cornu -> deep inguinal ring), uterosacral (cervix -> sacrum)
    lig = []
    for s in (-1, 1):
        lig.append(([ovary_c[s] + Vector((-s * 0.008, 0, 0)), cornu[s] + Vector((s * 0.004, 0.006, -0.008))], [0.0028, 0.0028], 10, True, True))
        rc = [cornu[s] + Vector((0, -0.006, -0.004)), Vector((s * 0.055, b_back - 0.075, b_top + 0.012)), Vector((s * 0.085, -0.07, b_top + 0.02))]
        lig.append((resample(chaikin(rc, 2), 0.004), [0.0022] * 20, 8, True, True))
        us = [cervix + Vector((s * 0.008, 0.010, 0.004)), Vector((s * 0.024, cervix.y + 0.035, cervix.z + 0.010)), Vector((s * 0.022, cervix.y + 0.058, cervix.z + 0.030))]
        lig.append((resample(chaikin(us, 2), 0.004), [0.0032] * 30, 10, True, True))
    lig = [(p, [r[0]] * len(p), sg, a, b) for p, r, sg, a, b in lig]  # constant calibre per ligament
    out["Ligaments"] = tube_object("Ligaments", "M_Ligament", lig)
    return out, dict(cervix=cervix, fundus=fundus, ovary=ovary_c, b_top=b_top, b_back=b_back)


def make_endometriosis_lesions(objs, female, land):
    """Lesions of the four appearances at the classic sites; each its own object so a lab can target it."""
    rng = random.Random(3)
    sites = [  # name, organ, aim point, appearance, size (m)
        ("PouchOfDouglas", "Colon_SigmoidRectum", land["cervix"] + Vector((0.004, 0.030, 0.006)), "Black", 0.004),
        ("UterosacralLeft", "Ligaments", land["cervix"] + Vector((0.020, 0.040, 0.016)), "Red", 0.0035),
        ("UterosacralRight", "Ligaments", land["cervix"] + Vector((-0.020, 0.040, 0.016)), "Clear", 0.0025),
        ("OvaryLeft", "Ovaries", land["ovary"][1] + Vector((0.006, -0.010, 0.004)), "Black", 0.0045),
        ("OvarianFossaLeft", "Ovaries", land["ovary"][1] + Vector((0.012, 0.010, -0.010)), "Red", 0.004),
        ("BladderPeritoneum", "UrinaryBladder", Vector((0.006, land["b_back"] - 0.03, land["b_top"] + 0.004)), "Clear", 0.0025),
        ("UterusPosterior", "Uterus", land["fundus"] + Vector((0.008, 0.030, -0.020)), "White", 0.004),
    ]
    lesions = {}
    for name, organ, aim, look, size in sites:
        host = female.get(organ) or objs.get(organ)
        if host is None:
            continue
        tree = world_bvh(host)
        loc, nrm, _, _ = tree.find_nearest(aim)
        if loc is None:
            continue
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new("UVMap")
        rot = Vector((0, 0, 1)).rotation_difference(nrm)
        if look == "Clear":  # cluster of small translucent vesicles
            for k in range(6):
                off = rot @ Vector((rng.uniform(-1, 1) * size * 1.8, rng.uniform(-1, 1) * size * 1.8, 0))
                r = size * rng.uniform(0.35, 0.6)
                ellipsoid_bm(bm, uv, loc + off + nrm * r * 0.5, (r, r, r), rot, 10, 6)
        elif look == "Red":  # flame-like: an elongated raised patch
            ellipsoid_bm(bm, uv, loc + nrm * size * 0.15, (size * 1.6, size * 0.8, size * 0.35), rot, 14, 8)
        else:  # black 'powder burn' puckered spot, or white scar plaque
            ellipsoid_bm(bm, uv, loc + nrm * size * 0.1, (size, size * 0.85, size * 0.25), rot, 14, 8)
        lesions[f"Lesion_{name}_{look}"] = obj_from_bm(f"Lesion_{name}_{look}", f"M_Lesion_{look}", bm)
    return lesions


def recentre(obj):
    """Origin at the bounding-box centre (grab pivot in VR) without moving the geometry in world space."""
    me = obj.data
    if not me.vertices:
        return
    mn = Vector([min(v.co[i] for v in me.vertices) for i in range(3)])
    mx = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])
    c = (mn + mx) * 0.5
    me.transform(Matrix.Translation(-c))
    obj.matrix_world = obj.matrix_world @ Matrix.Translation(c)


def phase_assemble():
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    with bpy.data.libraries.load(str(EXTRACT_BLEND), link=False) as (src, dst):
        dst.collections = [c for c in src.collections if c == EXTRACT_COLLECTION]
    extract_col = dst.collections[0]
    scene.collection.children.link(extract_col)
    objs = {}
    for o in extract_col.objects:
        o.name = o.name[len(EXTRACT_PREFIX):]
        objs[o.name] = o
    groups_of = {organ: group for organ, group, *_ in ORGANS}

    for name, group, mat in GENERATED:
        maker = {"Oesophagus": make_oesophagus, "BronchialTree": make_bronchial_tree,
                 "SmallIntestine": make_small_intestine}[name]
        ob = maker(objs)
        extract_col.objects.link(ob)
        objs[name] = ob
        groups_of[name] = group
        log(f"{name:24s} {tri_count(ob.data):>7d} tris  (generated)")

    female, land = make_female_pelvis(objs)
    lesions = make_endometriosis_lesions(objs, female, land)
    for group, made in ((FEMALE_GROUP, female), (LESION_GROUP, lesions)):
        for name, ob in made.items():
            extract_col.objects.link(ob)
            objs[name] = ob
            groups_of[name] = group
            log(f"{name:24s} {tri_count(ob.data):>7d} tris  (generated, female)")

    for k in [k for k in objs if k.startswith(OBSTACLE_PREFIX)]:
        bpy.data.objects.remove(objs.pop(k))

    # hierarchy: ROOT / group / sub-group / organ (empty at organ centre) / organ_LOD0, organ_LOD1
    final_col = bpy.data.collections.new(ROOT_NAME)
    scene.collection.children.link(final_col)
    root = bpy.data.objects.new(ROOT_NAME, None)
    final_col.objects.link(root)
    empties = {"": root}

    def group_empty(path):
        if path in empties:
            return empties[path]
        parent_path, _, leaf = path.rpartition("/")
        e = bpy.data.objects.new(leaf, None)
        e.empty_display_size = 0.02
        final_col.objects.link(e)
        e.parent = group_empty(parent_path)
        empties[path] = e
        return e

    report = {"budget_scale": BUDGET, "organs": {}, "lod0_tris": 0, "lod1_tris": 0}
    for name in sorted(objs, key=lambda n: (groups_of.get(n, ""), n)):
        src = objs[name]
        extract_col.objects.unlink(src)
        recentre(src)
        src.name = f"{name}_LOD0" if MAKE_LODS else f"{name}_Mesh"  # free the bare name for the holder
        holder = bpy.data.objects.new(name, None)
        holder.empty_display_size = 0.01
        final_col.objects.link(holder)
        holder.parent = group_empty(groups_of.get(name, "Other"))
        holder.matrix_world = Matrix.Translation(src.matrix_world.translation)
        src.name = f"{name}_LOD0" if MAKE_LODS else f"{name}_Mesh"
        src.data.name = src.name
        final_col.objects.link(src)
        src.parent = holder
        src.matrix_parent_inverse = Matrix.Identity(4)
        src.location = (0, 0, 0)
        entry = {"group": groups_of.get(name, "Other"), "lod0": tri_count(src.data),
                 "material": src.data.materials[0].name, "centre_m": [round(c, 4) for c in holder.location]}
        report["lod0_tris"] += entry["lod0"]
        if MAKE_LODS:
            lod1_me = decimated_copy(src.data, LOD1_RATIO, f"{name}_LOD1")
            lod1 = bpy.data.objects.new(f"{name}_LOD1", lod1_me)
            final_col.objects.link(lod1)
            lod1.parent = holder
            entry["lod1"] = tri_count(lod1_me)
            report["lod1_tris"] += entry["lod1"]
        report["organs"][name] = entry
    bpy.data.collections.remove(extract_col)

    # export: the male body (no female organs), and the female variant (no male genital organs)
    female_names = {n for n, g in groups_of.items() if g in (FEMALE_GROUP, LESION_GROUP)}

    def excluded_by(skip_names, skip_groups):
        out = set()
        for o in final_col.objects:
            organ = o.parent.name if o.type == "MESH" and o.parent else o.name
            if organ in skip_names or o.name in skip_names:
                out.add(o.name)
        for path in skip_groups:
            e = empties.get(path)
            if e:
                out.add(e.name)
        return out

    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    for path, skip in ((FBX_PATH, excluded_by(female_names, (FEMALE_GROUP, LESION_GROUP))),
                       (FEMALE_FBX, excluded_by(MALE_ONLY, ()))):
        for o in scene.objects:
            o.select_set(o.name in final_col.objects and o.name not in skip)
        bpy.ops.export_scene.fbx(
            filepath=str(path), use_selection=True, object_types={"EMPTY", "MESH"},
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", global_scale=1.0,
            axis_forward="-Z", axis_up="Y", bake_space_transform=False,  # Unity bakes the axis (bakeAxisConversion)
            mesh_smooth_type="FACE", use_mesh_modifiers=True, use_tspace=False,
            add_leaf_bones=False, bake_anim=False, path_mode="STRIP", use_custom_props=False)
        log(f"exported {path.name}: {sum(1 for o in scene.objects if o.select_get())} objects")
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND), compress=True)
    report["fbx"] = str(FBX_PATH.relative_to(REPO)).replace("\\", "/")
    report["fbx_mb"] = round(FBX_PATH.stat().st_size / 1e6, 1)
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    log(f"assembled {len(report['organs'])} organs: LOD0 {report['lod0_tris']:,} tris, "
        f"LOD1 {report['lod1_tris']:,} tris, FBX {report['fbx_mb']} MB in {time.time() - t0:.0f}s")


if __name__ == "__main__":
    if "--assemble-only" not in ARGS:
        if "Startup.blend" not in bpy.data.filepath:
            raise SystemExit("Open Tools/Anatomy/Source/Startup.blend (see the module docstring for the command).")
        phase_extract()
    phase_assemble()
