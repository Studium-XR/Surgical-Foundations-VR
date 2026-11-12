# Studium XR platform: courses, palm UI, headset quality and the SimReady library

This page covers the platform layer added on top of the Surgical Foundations scenario: the course catalogue, the
palm-anchored interface, per-headset quality, and the rigged NVIDIA SimReady equipment library.

## 1 · Course catalogue

| | |
|---|---|
| Source of truth | `Tools/Curriculum/build_catalog.py` (compact course definitions) |
| Generated | `Assets/_Project/Resources/Curriculum/catalog.json` (shipped in the app) and `Studium-XR-Backend/src/SurgicalFoundations.Api/Catalog/catalog.json` (embedded in the API) |
| Shape | programs → courses → modules → lessons (Reading, Video, Quiz, VR lab, VR assessment, Explore in 3D) |
| Size | 6 programs, 32 courses, 158 lessons |

Programs: Surgical Foundations (8 courses), Minimally Invasive Surgery (5), General Surgery Procedures (5), Trauma and
Emergency Surgery (4), Surgical Specialties (8: orthopaedics, vascular, plastics, ENT, urology, cardiothoracic,
neurosurgery, obstetrics), Perioperative Practice (2 + shared).

Each course carries outcomes, skills, the frameworks it maps to, hours, CME credits and prerequisites. Edit the Python
file and run `py -3 -I Tools/Curriculum/build_catalog.py`; bump `VERSION` so headsets pick up the API's newer copy.

**Clinical sign-off:** the readings and questions were drafted from published guidance (WHO checklist, WHO/NICE SSI,
FLS/FUSE outlines, ACS/APDS/ASE, ISCP). They must be reviewed by a surgical educator before learners earn credit.

### What runs today

| Lesson kind | Status |
|---|---|
| Reading, Quiz | Work in the lobby course browser, offline, with feedback per answer |
| Explore in 3D | Work: the skills lab lays out the lesson's SimReady equipment with working parts |
| VR lab / assessment: `lap-prep`, `lap-access`, `lap-operate`, `lap-close`, `lap-foundations`, `skills-lab` | Launch the existing theatre stages; a single-stage lesson ends at that stage's summary |
| VR lab / assessment: the other 19 (suturing, minor ops, open and laparoscopic appendicectomy, hernia, laparotomy, anastomosis, trauma bay, chest drain, eFAST, vascular access, catheterisation, ENT, orthopaedic fixation, vascular anastomosis, skin graft, burr hole, caesarean, cardiac tamponade) | Procedure labs (below) |

### Procedure labs

`Tools/Curriculum/build_procedures.py` → `Resources/Procedures/<simulationId>.json` (19 labs, 199 steps), played by
`Procedures/ProcedureRunner` in scene `15_Stage_Procedure` on top of the operating theatre. The runner lays the lab's
SimReady equipment on an instrument trolley beside the learner and around the table, marks targets on the patient
(measured from the umbilicus), and checks each step from the learner's hands:

| Step | Checked by |
|---|---|
| pick | The right instrument grabbed (a listed wrong one is an error) |
| apply | The held instrument kept on the right spot on the patient for the hold time; the right instrument on another target is a wrong-site error |
| operate | A device's moving part opened or closed (ArticulatedDevice) |
| choose | A clinical decision; unsafe options fail the lab |
| talk | Answering the patient; the patient's lines are subtitled and spoken by the headset's text-to-speech |
| plug | A cable or tube plugged in: into a device port or onto the patient |
| confirm | A step not simulated in detail, ticked off |

Guided mode shows the coach's hint and a ring on the target; Assessment shows neither. Score = 100 − 10 per error −
3 per hint asked for; the lesson's pass mark applies and an unsafe step fails the lab. Results go to the course
progress (and the API, which accepts procedure-lab results without a theatre session id).

**Cables and tubes** (`Interaction/Cables.cs`). Labs list their cables: the fixed end leaves one device, the plug goes
into a port on another device (mains sockets, oxygen and vacuum outlets on the ceiling supply pendant) or onto the
patient (diathermy plate on the thigh, oximeter on a finger, ECG on the chest, blood pressure cuff, the laparoscope's
light, camera and CO2 lines at the camera port). They are made easy to use: a large grab volume on each plug, a 12 cm
snap zone that lines the plug up in its socket, the matching socket glowing (brighter as the plug nears) in Guided mode,
the plug returning to its coil if dropped anywhere else, and cables drawn with a natural sag that never tangles. The lab
"Setting up the theatre equipment" (SF-101, MIS-201) is all set-up; open appendicectomy, hernia, laparotomy, laparoscopic
appendicectomy, caesarean, trauma bay and cardiac labs start with their own set-up. 20 labs, 253 steps, 45 plug-ins.

These are procedural walkthroughs with real instruments and targets, not tissue simulation: incisions, sutures and
dissection are judged by instrument, site and sequence.

### Headset runtime (`Assets/_Project/Scripts/Runtime/Curriculum`)

- `CourseCatalogStore`: bundled catalogue, replaced by the API's when its `version` is higher (cached offline).
- `CourseProgressStore`: enrolments and lesson results per user on the headset; queued to the API through the sync queue.
- `CourseSession`: the current course/lesson; launches lessons; records a scored VR session against the lesson.
- `UI/CourseBrowser`: the lobby panel (programs, courses, lessons, readings, quizzes), world-locked to the learner's right.

### API (`Studium-XR-Backend`)

| Route | |
|---|---|
| `GET /catalog`, `GET /catalog/courses/{id}` | Public catalogue |
| `GET /me/enrollments`, `PUT /me/enrollments/{courseId}` | Enrolments (idempotent) |
| `POST /me/lesson-attempts` | Lesson result. The server applies the pass mark; VR lessons must name the learner's own completed session, whose server-side score is used |
| `GET /me/certificates`, `GET /certificates/{id}` | Certificates, issued when every lesson is complete; public verification |

Migration `Courses` adds `CourseEnrollments`, `LessonRecords`, `CourseCertificates`. Tests: `tests/.../CourseTests.cs`.

## 2 · Palm interface (nothing follows the head)

- **Palm hub** (`UI/Palm/PalmHub`): turn the left palm towards your face. A card sits above the palm with the clock,
  stage, mode, course and lesson, and six buttons: Settings, Pause, Graphics, Subtitles, Recenter, Objectives. Press them
  with the other hand. Lower the hand and the card goes.
- **Settings dial and pause card** dock on the palm (`UI/Palm/PalmDock`): they ride on the hand while the palm is up and
  stay put in the room when it is lowered; they never attach to the head. Y (left controller), the menu button, F2 and
  Esc still open them.
- **HUD and subtitles** are world-locked (`UI/WorldLockedPanel`): placed in front of the learner when a scene loads or
  they recenter, then left in the room. Any `LazyFollow` still in a built scene is converted at runtime.

## 3 · Quality per headset (`Rendering/DeviceQuality`)

The headset is recognised from its model name and GPU and given a profile; the learner can pick Auto, Performance or
Fidelity on the palm (saved).

| Tier | Headsets | Eye buffer | MSAA | Refresh | Foveation | Shadows | Post FX |
|---|---|---|---|---|---|---|---|
| Entry | Quest 1/2, Pico Neo 3 | 1.0× | 2× | 72 Hz | high | off | off |
| Standard | Quest Pro, Pico 4 | 1.1× | 4× | 72 Hz | medium | 1k, 5 m | off |
| High | Quest 3, Quest 3S, Pico 4 Ultra | 1.25× | 4× | 90 Hz | low | 2k, 8 m, 2 cascades | on |
| Ultra | Android XR (Galaxy XR), PC VR with ≥ 8 GB | 1.4× | 4× | 90 Hz | minimal | 4k, 12 m, 4 cascades | on, HDR |

A dynamic-resolution loop lowers the eye buffer in 5 % steps when frames run over budget and restores it when there is
headroom. Refresh-rate requests need **Meta Quest Display Utilities** enabled under XR Plug-in Management → OpenXR
(otherwise the headset keeps its default rate). The pipeline asset is cloned at runtime, so play mode never edits it.

## 4 · SimReady equipment library (Blender rig pipeline)

| Step | Tool | Output |
|---|---|---|
| Download | `py -3 -I Tools/SimReady/download_simready.py` | `Tools/SimReady/_raw/<Category>/*.glb` (≈ 250 models, 1.6 GB, git-ignored) |
| Rig + animate | `py -3 -I Tools/SimReady/rig_all.py` (Blender 5.2, headless, parallel) | `Assets/_Project/Imports/simready_rigged/<Category>/<name>.glb` + `.joints.json` |
| Prefabs | Unity: **Surgical Foundations → Build → 1i · SimReady Library (rigged)** | `Prefabs/SimReady/**`, `Resources/SimReadyLibrary.asset` |

Source: the GLB conversion of the SimReady library (`codepawl/simready-glb` on Hugging Face, CC BY 4.0), the same
assets, part names and hierarchy as the USD files on SimReady Central at about 1/20 of the size. The USD originals
(50 MB–1.2 GB each) would not fit on this machine's disk.

The rigger reads SimReady part names (`wheel`, `drawer`, `door`, `lid`, `knob`, `button`, `arm`, `screen`, `pole`,
`lever`, `handleLeft/Right`, `plunger`, …) and builds one joint node per moving part at its hinge or slide origin, nests
riders (glass on its screen, wheel on its caster, buttons on a door, screen on its arm), keys an animation per joint plus
a combined `Showcase`, welds and decimates to a per-category triangle budget, caps textures at 1–2k and writes the joint
manifest. Per-asset fixes go in `Tools/SimReady/rig_overrides.json`. `preview_rig.py` renders rest/open views.

At runtime `ArticulatedDevice` drives the joints through a Unity **Animator Controller** per device
(`Animation/SimReady/<Category>/AC_<id>.controller`): one layer per moving part whose motion time is a 0–1 parameter
named after the joint, and a `Showcase` layer playing the Blender animation. Grab a drawer and pull, swing a door, twist
a knob, press a button; a quick tap opens or closes a part. Wheels and casters are posed directly. Hand-sized items also
get a grab interactable.

Clip preview: selecting a clip inside a rigged .glb previews it on its own model (`Editor/Tools/GltfClipPreview`).

**Attribution** (CC BY 4.0, required wherever the app is distributed): "Contains NVIDIA SimReady assets, licensed under
CC BY 4.0."

**Known limits of automatic rigging:** joints come from names and geometry, so some are approximate (hinge side of an
unusual door, the opening angle of an instrument). Check a model with `preview_rig.py` and fix it in
`rig_overrides.json` before it is used in an assessed lesson.

## 5 · Rigged equipment in the main scenes

**Surgical Foundations → Build → 1k · Dress Scenes with SimReady Equipment** puts working SimReady devices into the
theatre (and so into every stage played on top of it) and the skills lab: ventilator, diathermy generator on its cart,
suction pump, infusion pump stand, emergency trolley with defibrillator; utility cart, wheelchair, emergency trolley,
bedside lamp and blood pressure monitor. Each is turned so its controls face into the room, and its drawers, doors,
knobs and buttons work by hand exactly as in the equipment lab. Positions are in `SceneEquipmentDressing.cs`.

## 6 · Theatre staff

The scrub nurse and anaesthetist play their HY-Motion idle and action loops (their Animators had been left switched
off, so they stood in the model's T-pose). The nurse's "hands clasped at the chest" idle retargets with the hands too
high for her proportions; it needs a better clip or hand IK.

