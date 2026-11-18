# Human anatomy model pipeline (Blender → Unity)

The patient anatomy (skin, airway, thoracic, abdominal and pelvic organs) is built from the **Z-Anatomy** atlas,
which comes from the BodyParts3D scan data. The organs are not modelled by hand. Each one keeps its measured
shape and its true position inside the body, so in Unity the organs sit inside the skin exactly where they belong.

```
Tools/Anatomy/Source/Startup.blend   Z-Anatomy atlas (300 MB, git-ignored; unpack from Z-Anatomy.zip)
        │  build_unity_anatomy.py      extract → subdivide/decimate → generate missing parts → LODs → FBX
        ▼
Assets/_Project/Art/Models/Anatomy/ANA_HumanMale.fbx          (Git LFS)
Tools/Anatomy/Output/ANA_HumanMale.blend                      editable Blender source (git-ignored)
Tools/Anatomy/Output/build_report.json                        triangles per organ
        │  Unity: Surgical Foundations ▸ Build ▸ 1k · Human Anatomy Model (Z-Anatomy)
        ▼
Assets/_Project/Prefabs/Anatomy/ANA_HumanMale_Anatomy.prefab  translucent skin, peritoneum hidden
```

## Build

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --disable-autoexec Tools/Anatomy/Source/Startup.blend --python Tools/Anatomy/build_unity_anatomy.py
```

The build takes about 3 minutes. The options go after `--`:
- `--budget 0.6` scales every organ's triangle budget (use it for Quest-only builds).
- `--no-lod` skips the LOD1 meshes.
- `--assemble-only` reuses `Output/_extract.blend`, which is quicker when you only change the generated parts or the export.

Use `--disable-autoexec`. The atlas embeds a UI script that must not run headless.

Preview renders (written to `Docs/Images/Anatomy/`):

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b Tools/Anatomy/Output/ANA_HumanMale.blend --python Tools/Anatomy/render_previews.py
```

## What is in the model

| Group | Organs |
|---|---|
| Body | Skin (welded from all surface regions), Hair, Eyes (if present in the atlas) |
| Neck | Pharynx, Tongue, Salivary glands, Larynx (epiglottis, thyroid/cricoid/arytenoid cartilages), Thyroid, Parathyroids |
| Thorax | Trachea, **Bronchial tree***, five lung lobes, Pleura (translucent), Heart (4 chambers), Pulmonary trunk, Aorta, Venae cavae, Thymus, Diaphragm, **Oesophagus*** |
| Abdomen | Liver, Gallbladder, Stomach, Pancreas, Spleen, Duodenum, **Small intestine***, Colon (ascending, transverse, descending, sigmoid + rectum), Appendix, Taeniae, Kidneys, Renal pelvises, Ureters, Adrenals, Peritoneum (omenta, mesocolon) |
| Pelvis | Urinary bladder, Prostate, Seminal vesicles, Testes, Penis |

\* The atlas does not include these as meshes, so the script generates them:
- **Small intestine**: a 20-sided tube that runs from the duodenojejunal flexure to the caecum. It starts with the jejunum in the upper left and ends with the ileum in the lower right, packed into the infracolic space, and it is pushed out of the colon, kidneys, bladder, psoas, spine and abdominal wall. It is plausible, not patient-accurate. At about 1.4 m it is much shorter than a real 6 m small bowel, because the coils do not overlap.
- **Oesophagus**: runs from the cricoid level, behind the trachea and the left atrium, through the hiatus to the cardia of the stomach.
- **Bronchial tree**: grown by space colonisation from the carina into the volume of each lung's lobes. Branch calibres follow Murray's law, with main bronchi of about 12–13 mm in diameter.

Each organ is an empty at the organ's centre, which acts as the grab and pivot point, with `Organ_LOD0` and `Organ_LOD1` (30 %) children.
Unity turns the `_LODn` siblings into one LODGroup per organ. All meshes have a box-projected `UVMap`
(1 UV = 12.5 cm) for tiling tissue detail maps. The generated tubes have true along/around UVs.

## Coordinates and materials

- Blender: metres, Z-up, the body faces −Y, feet at Z = 0.
- Unity: 1 unit = 1 m, the body faces +Z, feet at Y = 0, no import rotation.
- Material slots are named `M_*` and remap onto `Assets/_Project/Art/Materials/Anatomy/M_*.mat`. The menu command creates any missing ones as URP Lit, plus `M_Skin_Ghost` for the see-through teaching prefab.

## Licence

The model is CC BY-SA 4.0. See `Assets/_Project/Art/Models/Anatomy/ANATOMY_ATTRIBUTION.md`. The credit lines must appear
in the app, and the model files and their derivatives stay under ShareAlike.
