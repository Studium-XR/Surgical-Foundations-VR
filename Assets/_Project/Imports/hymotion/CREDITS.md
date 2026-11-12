# HY-Motion staff animations

The `HYM_*.fbx` clips were generated with **Tencent HY-MOTION 1.0** (text-to-3D-human-motion), through the official
Hugging Face Space `tencent/HY-Motion-1.0` (https://huggingface.co/tencent/HY-Motion-1.0), on 2026-11-03.

Notice (required by the licence): "Tencent HY-MOTION 1.0 is licensed under the Tencent HY-MOTION 1.0 Community License
Agreement, Copyright © 2025 Tencent. All Rights Reserved. The trademark rights of "Tencent HY" are owned by Tencent or its
affiliate."

| Clip | Prompt | Length |
|---|---|---|
| HYM_nurse_idle | A person stands still with both hands clasped in front of the chest, shifting weight slightly from one foot to the other and calmly looking around. | 6 s |
| HYM_nurse_handover | A person stands, reaches forward with the right hand at waist height to hand over an object, then brings the hand back and clasps both hands in front of the chest. | 6 s |
| HYM_anaesthetist_monitor | A person stands, turns the head to the left to look at a screen, reaches out with the right hand to press buttons at chest height, then lowers the hand and looks forward. | 7 s |
| HYM_anaesthetist_idle | A person stands still with arms relaxed at the sides, breathing calmly and occasionally turning the head to look left and right. | 6 s |

| HYM_patient_lie_breathe | A person lies on their back with the arms by the sides, breathing slowly so the chest rises and falls, and slowly turns the head to the right and back to the middle. | 8 s |
| HYM_patient_lie_talk | A person lies on their back with the arms by the sides, turns the head to the right and talks, lifting the right hand a little from the bed and lowering it again. | 8 s |
| HYM_patient_sit_idle | A person sits on a chair with both hands resting on the thighs, breathing calmly, looking forward and now and then turning the head slightly to the side. | 8 s |
| HYM_patient_sit_talk | A person sits on a chair and talks to someone standing in front of them, nodding a little and gesturing gently with the right hand at chest height, then rests the hand on the thigh. | 8 s |
| HYM_patient_sit_listen | A person sits on a chair and listens attentively, nods the head twice, then keeps the hands resting on the thighs. | 6 s |
| HYM_patient_sit_pain | A person sits on a chair, leans forward and holds the lower right side of the belly with the right hand, then slowly sits up straight again. | 6 s |
| HYM_patient_sit_wave | A person sits on a chair, raises the right hand to wave hello, then lowers it back to the thigh. | 4 s |

The patient clips (generated 2026-11-12) play on CHR_Patient (Imports/patient/source/Patient_Rigged.fbx, rigged by
Tools/Characters/rig_patient.py) through Surgical Foundations → Build → 1j · Patient Character.

Seed 0, guidance scale 5.0. The clips play on CHR_ScrubNurse and CHR_Anaesthetist through Humanoid retargeting
(Surgical Foundations → Build → 1i · Staff Animations). Tencent claims no rights in the generated outputs.

**Territory:** the licence does not apply in the European Union, the United Kingdom or South Korea, and outputs may not be
used, distributed or displayed there. Check this before distributing the app in those regions; replace these clips with
another source for builds that ship there.

To make more clips: `Tools/HYMotion/gen_motion.py` (repo root; see its header), then add the clip to `StaffClips` in
StaffAnimationKit.cs and run Build → 1i.
