# Open-H-Embodiment recording

`Assets/_Project/Resources/OpenH/hamlyn_peg_transfer_000.csv` is derived from the
**Open-H-Embodiment** dataset (`nvidia/PhysicalAI-Robotics-Open-H-Embodiment` on Hugging Face),
subset `Surgical/hamlyn/peg_transfer`, episode 0 ("Move the orange cylinder peg to right."), recorded on a dVRK
(da Vinci Research Kit) by the Hamlyn Centre. Licensed under
[Creative Commons Attribution 4.0 (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/).

- Source: https://huggingface.co/datasets/nvidia/PhysicalAI-Robotics-Open-H-Embodiment
- Changes made: only the per-arm tip position and jaw angle were kept (from `observation.state.*_arm_cartesian`),
  written as CSV. At runtime `ExpertGhostReplay` retargets the path onto the Skills Lab peg board (turned, scaled and
  moved), so the ghost shows the expert's motion, not the original board layout.

Attribution for the store listing or about screen:
"Contains data from Open-H-Embodiment (Hamlyn Centre peg transfer), licensed under CC BY 4.0."
