# NVIDIA SimReady assets

The `.glb` models in this folder are from **NVIDIA SimReady Assets**, © NVIDIA Corporation,
licensed under [Creative Commons Attribution 4.0 (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/).

- Source: https://huggingface.co/datasets/nvidia/simready-assets (GLB mirror: https://huggingface.co/datasets/codepawl/simready-glb)
- Changes made: meshopt compression removed, WebP textures re-encoded as PNG, and the heavier meshes
  simplified for the Meta Quest triangle budget (gltf-transform + meshoptimizer). Geometry was not otherwise edited.
  The prefabs built from them (Surgical Foundations → Build → 1h) re-orient and pivot the models for the simulator.

| File | Used for |
|---|---|
| instrument_scalpel_handle_a01.glb | INST_Scalpel |
| instrument_forceps_a01.glb | INST_TissueForceps |
| instrument_scissors_a01.glb | INST_MayoScissors |
| instrument_artery_forceps_b01.glb | INST_Hemostat |
| instrument_retractor_a01.glb | INST_Retractor |
| instrument_suction_a01.glb | INST_Suction |
| tray_sterilization_instrument_c02.glb | EQ_SterilizationTray |
| trolley_instrument_b01.glb | EQ_InstrumentTrolley |
| needle_surgical_a01.glb | PROP_NeedleBox |
| drape_surgical_a01.glb | PPE_DrapePack |
| light_surgical_ceiling_arm_b01.glb | EQ_SurgicalLightArm |
| table_surgical_a01.glb | EQ_ORTable |
| anesthesia_machine_a01.glb | EQ_AnaesthesiaMachine |
| monitor_patient_a01.glb | EQ_PatientMonitor |
| IV_pole_a01.glb | EQ_IVStand |
| video_system_a01.glb | EQ_LapTower (cart and one monitor; its camera boom and second monitor are left off) |
| drape_surgical_a02.glb | surgical drape fabric (M_Drape_Teal): its measured colour and `Art/Textures/SimReady/T_SimReadyDrape_Normal.png`, a mirrored tile of its normal map |

The theatre models were simplified harder (to 2–7 % of their triangles) and their textures capped at 1024 px.
Some SimReady GLBs also carry stray top-level copies of their meshes at the model origin; the builders keep only
the placed copy under `Geometry/`.

CC BY 4.0 needs this attribution wherever the app is distributed (store listing, about or credits screen):
"Contains NVIDIA SimReady assets, licensed under CC BY 4.0."
