# Context images: Surgical Foundations VR

Images for explaining the product: what the learner sees, how the simulator was built up, and before / after
comparisons. Renders are taken from the Unity editor with post-processing off, which is how the Meta Quest build
shows the scene. Numbers group the images by topic.

Third-party content shown: NVIDIA SimReady models (CC BY 4.0, see `Assets/_Project/Imports/simready/CREDITS.md`)
and an Open-H-Embodiment recording (CC BY 4.0, see `Assets/_Project/Imports/openh/CREDITS.md`). Credit both
wherever these images are published.

## 0x · Reference screenshots (before)

| File | Shows |
|---|---|
| `01_reference_theatre_before_simready.png` | The operating theatre before the update: flat teal drape and simple placeholder equipment. |
| `02_reference_laparoscopic_tower_before.png` | The old placeholder laparoscopic tower (box with a monitor on a pole). |

## 1x · Open-surgery skills station (Skills Lab)

| File | Shows |
|---|---|
| `10_simready_instrument_lineup.png` | The six NVIDIA SimReady instruments from above and from the side: tissue forceps, Mayo scissors, haemostat, rake retractor, suction tip. |
| `11_simready_instruments_closeup.png` | The instruments as prefabs, with their working tips marked (red dots) and the hinged instruments shown open. |
| `12_open_skills_station_first_build.png` | First build of the station, at the end of a run: the review panel with time, errors and incision accuracy per exercise. |
| `13_open_skills_station_in_skills_lab.png` | The station on the Skills Lab back table, and the theatre calibration panel (height, dominant hand, table height). |
| `14_open_skills_station_in_skills_lab_2.png` | The station from the learner's position, with exercise 1 (Incision) on the instruction panel. |
| `15_open_skills_station_simready_instruments.png` | The station with the SimReady instruments, sterilisation tray, needle box and suction: learner's view and close-up. |

## 2x · Expert demonstration

| File | Shows |
|---|---|
| `20_expert_ghost_replay_peg_transfer.png` | Expert ghost replaying a recorded robotic peg transfer (Open-H-Embodiment, Hamlyn Centre) on the Skills Lab peg board: approach, grasp, carry, release. |

## 3x–5x · Operating theatre with real equipment

| File | Shows |
|---|---|
| `30_simready_theatre_props.png` | The SimReady theatre models as imported: IV pole, anaesthesia workstation, patient monitor, surgical table, drape pack. |
| `31_simready_theatre_props_four_sides.png` | The same models from four sides (used to work out each model's front). |
| `32_anaesthesia_machine_and_patient_monitor_vitals.png` | Anaesthesia workstation and patient monitor with the simulator's animated vital signs on their screens. |
| `33_theatre_simready_props_first_pass.png` | First pass in the theatre: SimReady drape fabric, table, anaesthesia machine, monitor and IV pole (before the lighting fix). |
| `40_laparoscopic_tower_simready_candidates.png` | The two SimReady candidates for the laparoscopic tower: a video tower cart and an endoscopy camera / light-source unit. |
| `41_laparoscopic_tower_simready.png` | The new laparoscopic tower: the video cart with one large monitor on the column, where the live laparoscope picture appears. |
| `50_theatre_final_overview.png` | Final theatre, overview: real equipment, blue surgical drapes and corrected lighting. Compare with `01`. |
| `51_theatre_final_anaesthesia_side.png` | Final theatre, head end: anaesthesia workstation moved clear of the anaesthetist, patient monitor and IV pole. |
| `52_theatre_final_laparoscopic_tower.png` | Final theatre from the surgeon's position, facing the new laparoscopic tower across the patient. Compare with `02`. |

## 6x · Product overview (README screenshots)

| File | Shows |
|---|---|
| `60_readme_lobby.png` | The lobby / main menu. |
| `60_readme_skills-lab.png` | The Skills Lab. |
| `60_readme_prep-scrub.png` | Preparation stage: scrubbing at the sink. |
| `60_readme_access-stage.png` | Access stage: port placement. |
| `60_readme_theatre-overview.png` | Theatre overview. |
| `60_readme_imported-models.png` | Imported model line-up. |
| `61_shader_gallery.png` | The custom shaders (tissue, steel, drape, glove, skin and effects). |

The `60_` images are copies of `Docs/Images` (the README links those originals). Re-render them with
**Surgical Foundations → Tools → Render README Screenshots**.
