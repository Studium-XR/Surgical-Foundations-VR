using System;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Core;
using UnityEngine;
using SurgicalFoundations.Contracts;

namespace SurgicalFoundations.Scenario
{
    /// <summary>
    /// Lives in 10_OR_Base. Drives Prep → Access → Operate → Close → Summary by swapping the additive stage scenes.
    /// Stage-specific logic sits in each stage scene; this only sequences them.
    /// </summary>
    public class ScenarioDirector : MonoBehaviour
    {
        public static ScenarioDirector Instance { get; private set; }

        [SerializeField] GameObject summaryPanel;

        public ScenarioStage Stage { get; private set; } = ScenarioStage.Prep;
        public event Action<ScenarioStage> StageChanged;

        /// <summary>Raised for every stage the learner enters, including Summary. Static because listeners in 00_Bootstrap
        /// (the session recorder) outlive the director, which is recreated with 10_OR_Base.</summary>
        public static event Action<ScenarioStage> StageEntered;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        System.Collections.IEnumerator Start()
        {
            if (summaryPanel != null) summaryPanel.SetActive(false);

            // Services live in 00_Bootstrap; wait for them and for any transition that is loading our stage.
            while (SceneLoader.Instance == null || SessionManager.Instance == null) yield return null;
            while (SceneLoader.Instance.IsBusy) yield return null;

            if (!SessionManager.Instance.IsRunning) SessionManager.Instance.BeginSession();

            // A procedure lab runs its own steps in this theatre: no Prep → Close sequence.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name == SceneIds.ProcedureLab) yield break;

            // A stage already loaded (normal flow, or opened directly in the editor)? Adopt it.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var name = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name;
                int idx = Array.IndexOf(SceneIds.Stages, name);
                if (idx >= 0) { SetStage((ScenarioStage)idx); yield break; }
            }
            GoTo(ScenarioStage.Prep);
        }

        public void GoTo(ScenarioStage stage)
        {
            if (stage == ScenarioStage.Summary) { ShowSummary(); return; }
            SetStage(stage);
            if (summaryPanel != null) summaryPanel.SetActive(false);
            SceneLoader.Instance?.LoadStage(SceneIds.Stages[(int)stage]);
        }

        public void Next()
        {
            AudioManager.Instance?.Play(SoundId.FB_StageComplete);
            EventLogger.Instance?.Log(EventClass.Info, "stage.complete", $"{Stage} complete");
            // A course lesson that is one stage of the scenario ends with that stage's result.
            GoTo(Curriculum.CourseSession.SingleStage ? ScenarioStage.Summary : Stage + 1);
        }

        public void RetryStage(int stage) => GoTo((ScenarioStage)stage);

        public void RetryScenario()
        {
            EventLogger.Instance?.Clear();
            SessionManager.Instance?.BeginSession();
            GoTo(ScenarioStage.Prep);
        }

        public void BackToLobby()
        {
            SessionManager.Instance?.EndSession();
            SceneLoader.Instance?.LoadFrontend(SceneIds.Lobby);
        }

        void ShowSummary()
        {
            // Summary first: the session recorder scores the run on StageEntered, before the panel below reads the result.
            SetStage(ScenarioStage.Summary);
            SessionManager.Instance?.EndSession();
            SceneLoader.Instance?.LoadTheatreOnly();
            if (summaryPanel != null) summaryPanel.SetActive(true);
            AudioManager.Instance?.Play(SoundId.FB_SummaryPass);
        }

        void SetStage(ScenarioStage stage)
        {
            Stage = stage;
            if (EventLogger.Instance != null) EventLogger.Instance.CurrentStage = stage;
            StageChanged?.Invoke(stage);
            StageEntered?.Invoke(stage);
        }
    }
}
