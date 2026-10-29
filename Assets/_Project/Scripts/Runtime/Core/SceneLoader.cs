using System;
using System.Collections;
using System.Collections.Generic;
using SurgicalFoundations.Audio;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.Core
{
    /// <summary>
    /// All scene changes go through here. 00_Bootstrap never unloads (NFR-09); frontend scenes swap one for another,
    /// theatre stages load additively on top of 10_OR_Base so room, lighting and patient stay put (NFR-03).
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }

        [SerializeField] ScreenFader fader;
        [SerializeField] XROrigin xrOrigin;

        public bool IsBusy { get; private set; }
        public event Action<string> SceneReady;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (fader == null) fader = GetComponentInChildren<ScreenFader>();
            if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
            // The rig ships as "Not Specified", which Quest resolves to Device space: every player gets a fixed
            // 1.36 m eye height regardless of how tall they are. Floor space uses the player's real height.
            if (xrOrigin != null)
            {
                xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                if (!xrOrigin.TryGetComponent<HeightCalibrator>(out _)) xrOrigin.gameObject.AddComponent<HeightCalibrator>();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public ScreenFader Fader => fader;

        /// <summary>Lobby, SkillsLab: one content scene at a time.</summary>
        public void LoadFrontend(string sceneName) => Run(new List<string> { sceneName });

        /// <summary>Loads (or swaps to) a theatre stage, keeping 10_OR_Base resident.</summary>
        public void LoadStage(string stageScene) => Run(new List<string> { SceneIds.OperatingTheatre, stageScene });

        /// <summary>Theatre with no stage loaded, e.g. for the summary.</summary>
        public void LoadTheatreOnly() => Run(new List<string> { SceneIds.OperatingTheatre });

        void Run(List<string> targets)
        {
            if (IsBusy) { Debug.LogWarning($"[SceneLoader] Busy, ignored request for {string.Join(", ", targets)}"); return; }
            StartCoroutine(Transition(targets));
        }

        IEnumerator Transition(List<string> targets)
        {
            IsBusy = true;
            AudioManager.Instance?.Play(SoundId.FB_Transition);
            if (fader != null && !fader.IsOpaque) yield return fader.FadeOut();

            // Unload everything that is neither persistent nor wanted.
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == SceneIds.Bootstrap || targets.Contains(s.name) || !s.isLoaded) continue;
                yield return SceneManager.UnloadSceneAsync(s);
            }

            foreach (var name in targets)
            {
                if (SceneManager.GetSceneByName(name).isLoaded) continue;
                var op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                while (op != null && !op.isDone) yield return null;
            }

            // The first target owns RenderSettings (ambient, fog, reflections), so it becomes the active scene.
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(targets[0]));
            PlaceRig(targets[targets.Count - 1]);

            yield return null; // let Start() run in the new scenes
            SceneReady?.Invoke(targets[targets.Count - 1]);
            if (fader != null) yield return fader.FadeIn();
            IsBusy = false;
        }

        /// <summary>Instant reposition under the fade. Uses the highest-priority spawn point in the given scene.</summary>
        public void PlaceRig(string sceneName)
        {
            PlayerSpawnPoint best = null;
            foreach (var sp in FindObjectsByType<PlayerSpawnPoint>())
            {
                if (sp.gameObject.scene.name != sceneName || !sp.isActiveAndEnabled) continue;
                if (best == null || sp.Priority > best.Priority) best = sp;
            }
            if (best != null) PlaceRigAt(best.transform);
        }

        public void PlaceRigAt(Transform t)
        {
            if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin == null || t == null) return;
            xrOrigin.MatchOriginUpCameraForward(Vector3.up, Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized);
            var cameraHeight = xrOrigin.CameraInOriginSpaceHeight;
            xrOrigin.MoveCameraToWorldLocation(t.position + Vector3.up * cameraHeight);
        }

        /// <summary>Fade → reposition → fade, for moving between work areas inside a stage (never a camera animation, NFR-05).</summary>
        public void TeleportTo(Transform t)
        {
            if (!IsBusy) StartCoroutine(TeleportRoutine(t));
        }

        IEnumerator TeleportRoutine(Transform t)
        {
            IsBusy = true;
            if (fader != null) yield return fader.FadeOut(0.25f);
            PlaceRigAt(t);
            yield return null;
            if (fader != null) yield return fader.FadeIn(0.25f);
            IsBusy = false;
        }
    }
}
