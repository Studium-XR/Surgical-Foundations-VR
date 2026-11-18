using SurgicalFoundations.Audio;
using SurgicalFoundations.Scenario;
using UnityEngine;
using SurgicalFoundations.Contracts;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Steps through the panels of one stage (e.g. Prep: scrub → sterility → tray & drape). The last step hands
    /// over to the ScenarioDirector. Detection logic will call <see cref="Next"/> instead of the buttons later.
    /// </summary>
    public class StepSequence : MonoBehaviour
    {
        [SerializeField] GameObject[] steps;
        [SerializeField] SoundId[] voiceOnStep;
        [SerializeField, Tooltip("Optional: where the learner stands for each step. Null = stay put.")] Transform[] stepSpawns;
        [SerializeField] bool advanceScenarioAtEnd = true;

        int index = -1;

        /// <summary>The step being shown (−1 before the first).</summary>
        public int Index => index;
        public int StepCount => steps != null ? steps.Length : 0;
        public event System.Action<int> StepShown;

        void Start() { if (index < 0) Show(0); }

        /// <summary>Drops a step's scripted voice line: detection plays it when the thing it describes really happens.</summary>
        public void MuteVoice(int step)
        {
            if (voiceOnStep != null && step >= 0 && step < voiceOnStep.Length) voiceOnStep[step] = SoundId.None;
        }

        public void Next() => GoTo(index + 1);

        /// <summary>
        /// Shows the step after <paramref name="step"/>. Unlike <see cref="Next"/> this does not depend on which step is
        /// showing, so a panel can move on from its own OnEnable, before this sequence's Start has shown step 0.
        /// </summary>
        public void ShowAfter(GameObject step)
        {
            int i = steps != null ? System.Array.IndexOf(steps, step) : -1;
            if (i < 0) { Next(); return; }
            GoTo(i + 1);
        }

        /// <summary>Jump to a step (branches). Past the last step ends the stage.</summary>
        public void GoTo(int i)
        {
            if (i < steps.Length) Show(i);
            else if (advanceScenarioAtEnd) ScenarioDirector.Instance?.Next();
        }

        public void Show(int i)
        {
            int previous = index;
            index = Mathf.Clamp(i, 0, steps.Length - 1);
            // Step 0's spot is the stage spawn point (placed by SceneLoader); later steps may move the learner.
            if (previous >= 0 && stepSpawns != null && index < stepSpawns.Length && stepSpawns[index] != null)
                Core.SceneLoader.Instance?.TeleportTo(stepSpawns[index]);
            for (int s = 0; s < steps.Length; s++)
                if (steps[s] != null) steps[s].SetActive(s == index);
            if (previous != index && voiceOnStep != null && index < voiceOnStep.Length && voiceOnStep[index] != SoundId.None)
                AudioManager.Instance?.Play(voiceOnStep[index]);
            if (previous != index) StepShown?.Invoke(index);
        }

        public void LogOnProtocol(string message) => EventLogger.Instance?.Log(EventClass.OnProtocol, "demo.onprotocol", message);
        public void LogDelayed(string message) => EventLogger.Instance?.Log(EventClass.Delayed, "demo.delayed", message);
        public void LogDeviation(string message) => EventLogger.Instance?.Log(EventClass.Deviation, "demo.deviation", message);
    }
}
