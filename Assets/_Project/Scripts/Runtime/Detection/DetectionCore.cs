using System;
using System.Linq;
using SurgicalFoundations.Backend;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Scenario;
using SurgicalFoundations.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// The learner's head and hands, whichever way they are tracked: a controller, or the hand-tracking pinch pose when
    /// the headset has switched to hands. Detectors read these instead of knowing about the XR rig.
    /// </summary>
    public static class LearnerRig
    {
        static XROrigin origin;
        static Transform trackingSpace;
        static readonly Transform[] controller = new Transform[2];
        static readonly Transform[] handPose = new Transform[2];

        public static Transform Head => Resolve() && origin.Camera != null ? origin.Camera.transform : null;

        public static Transform Hand(bool left)
        {
            if (!Resolve()) return null;
            int i = left ? 0 : 1;
            if (handPose[i] != null && handPose[i].gameObject.activeInHierarchy) return handPose[i];
            return controller[i] != null ? controller[i] : handPose[i];
        }

        /// <summary>
        /// Where this hand's pointing ray starts, aimed the way it points: the cast origin of the hand's Near-Far
        /// interactor, i.e. the ray the learner sees. The grip pose (<see cref="Hand"/>) points 30–40° off that ray on
        /// Quest controllers, so aiming along it put port marks and counted items somewhere other than where the
        /// visible ray was. Falls back to the hand itself.
        /// </summary>
        public static Transform Aim(bool left)
        {
            var hand = Hand(left);
            if (hand == null) return null;
            var root = hand == controller[left ? 0 : 1] || hand.parent == null ? hand : hand.parent;
            foreach (var caster in root.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.CurveInteractionCaster>())
            {
                if (!caster.isActiveAndEnabled) continue;
                var origin = caster.effectiveCastOrigin != null ? caster.effectiveCastOrigin : caster.castOrigin;
                if (origin != null) return origin;
            }
            return hand;
        }

        static readonly Vector3?[] firstSeen = new Vector3?[2];
        static readonly bool[] moved = new bool[2];

        /// <summary>
        /// Whether this hand is really being tracked. With no headset (or a controller that is asleep) the hand sits
        /// frozen wherever the rig left it, often on the floor, which must not read as "dropped below the sterile
        /// field". A hand counts as tracked once it has moved.
        /// </summary>
        public static bool Tracked(bool left)
        {
            var hand = Hand(left);
            if (hand == null) return false;
            int i = left ? 0 : 1;
            if (moved[i]) return true;
            // Measured in tracking space: the rig being placed, or its floor offset settling, is not the hand moving.
            var local = trackingSpace.InverseTransformPoint(hand.position);
            if (firstSeen[i] == null) firstSeen[i] = local;
            else if ((local - firstSeen[i].Value).sqrMagnitude > 1e-6f) moved[i] = true;
            return moved[i];
        }

        static bool Resolve()
        {
            if (origin != null) return true;
            firstSeen[0] = firstSeen[1] = null;
            moved[0] = moved[1] = false;
            origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (origin == null) return false;
            var offset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            trackingSpace = offset;
            controller[0] = offset.Find("Left Controller");
            controller[1] = offset.Find("Right Controller");
            handPose[0] = offset.Find("Left Hand/Pinch Grab Pose");
            handPose[1] = offset.Find("Right Hand/Pinch Grab Pose");
            return true;
        }
    }

    /// <summary>Detection thresholds come from the versioned scoring config (FR-18), with built-in fallbacks.</summary>
    public static class DetectionThresholds
    {
        public static float Get(string key, float fallback)
        {
            var thresholds = BackendServices.Instance?.Scoring?.Current?.thresholds;
            var match = thresholds?.FirstOrDefault(t => t.key == key);
            return match != null ? match.value : fallback;
        }
    }

    /// <summary>Where every detected event goes: the session's event log, plus the feedback cue that belongs to it.</summary>
    public static class DetectionLog
    {
        public static void Write(EventClass eventClass, string code, string message)
        {
            if (EventLogger.Instance != null) EventLogger.Instance.Log(eventClass, code, message);
            else Debug.Log($"[Detection] {eventClass} {code}: {message}");

            var audio = Audio.AudioManager.Instance;
            if (audio == null) return;
            var guided = SessionManager.Instance == null || SessionManager.Instance.Settings.mode == TrainingMode.Guided;
            var cue = Cue(code);
            // Guided mode: every event without a cue of its own still sounds right or wrong, so the learner hears at
            // once whether what they just did was on protocol. Assessment keeps only the specific cues above.
            if (cue == Audio.SoundId.None && guided) cue = ClassCue(eventClass);
            if (cue != Audio.SoundId.None) audio.Play(cue);
            // Coaching lines are a Guided-mode aid; Assessment only logs.
            var voice = Voice(code);
            if (voice != Audio.SoundId.None && guided) audio.Play(voice);
        }

        static Audio.SoundId ClassCue(EventClass eventClass)
        {
            switch (eventClass)
            {
                case EventClass.OnProtocol: return Audio.SoundId.FB_OnProtocol;
                case EventClass.Delayed: return Audio.SoundId.FB_Delayed;
                case EventClass.Deviation: return Audio.SoundId.FB_Deviation;
                default: return Audio.SoundId.None;
            }
        }

        static Audio.SoundId Voice(string code)
        {
            switch (code)
            {
                case EventCodes.Contamination: return Audio.SoundId.VO_Prep_SterilityBroken;
                case EventCodes.TrocarAngle: return Audio.SoundId.VO_Access_AngleShallow;
                default: return Audio.SoundId.None;
            }
        }

        static Audio.SoundId Cue(string code)
        {
            switch (code)
            {
                case EventCodes.Contamination: return Audio.SoundId.FB_Contamination;
                case EventCodes.TrocarAngle:
                case EventCodes.TrocarForce: return Audio.SoundId.FB_TechniqueFlag;
                case EventCodes.Drift: return Audio.SoundId.FB_DriftPing;
                case EventCodes.TrayItemConfirmed:
                case EventCodes.SwabCounted:
                case EventCodes.SwabFound: return Audio.SoundId.FB_CountTick;
                case EventCodes.TrayComplete:
                case EventCodes.CountComplete: return Audio.SoundId.FB_CountMatch;
                default: return Audio.SoundId.None;
            }
        }
    }

    /// <summary>
    /// One per stage scene. Reads the learner's hands, instruments and camera, feeds the stage's detection logic, and
    /// moves the stage's <see cref="StepSequence"/> on when a step is really done. Finds its props by name, so stage
    /// scenes need no extra wiring.
    /// </summary>
    public abstract class StageController : MonoBehaviour
    {
        /// <summary>The controller of the stage being played (for the Skip buttons).</summary>
        public static StageController Current { get; private set; }

        public event Action Changed;

        protected StepSequence Sequence { get; private set; }
        protected int Step => Sequence != null ? Sequence.Index : -1;
        /// <summary>The step being played, for components outside the stage (−1 before the sequence starts).</summary>
        public int CurrentStep => Step;
        protected static bool Guided => SessionManager.Instance == null || SessionManager.Instance.Settings.mode == TrainingMode.Guided;

        protected virtual void OnEnable() => Current = this;

        protected virtual void OnDisable()
        {
            if (Current == this) Current = null;
        }

        protected virtual void Start()
        {
            Sequence = FindObjectsByType<StepSequence>(FindObjectsInactive.Include).FirstOrDefault(s => s.gameObject.scene == gameObject.scene);
            if (Sequence == null) Debug.LogWarning($"[{GetType().Name}] No StepSequence in {gameObject.scene.name}; detection can't advance the stage.");
            else Sequence.StepShown += OnStepShown;
            Setup();
            RaiseChanged();
        }

        protected virtual void OnDestroy()
        {
            if (Sequence != null) Sequence.StepShown -= OnStepShown;
        }

        /// <summary>Find props and create the detection logic.</summary>
        protected abstract void Setup();

        /// <summary>
        /// The learner chose to move on without completing the current step ("Skip step" on the panels). Whatever was
        /// left undone is logged as deviations, so skipping is never a way around the assessment.
        /// </summary>
        public abstract void SkipStep();

        protected virtual void OnStepShown(int index) => RaiseChanged();

        /// <summary>Where the learner should be looking or reaching next; shown as a highlight in Guided mode (FR-19).</summary>
        public virtual Vector3? GuideTarget => null;

        protected void RaiseChanged() => Changed?.Invoke();

        protected static void Log(EventClass eventClass, string code, string message)
        {
            DetectionLog.Write(eventClass, code, message);
        }

        /// <summary>First transform with this exact name in this stage scene, else in any loaded scene (e.g. theatre equipment in 10_OR_Base).</summary>
        protected Transform Find(string exactName) => FindWhere(t => t.name == exactName);

        protected Transform FindWhere(Func<Transform, bool> match)
        {
            Transform fallback = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!match(t)) continue;
                    if (scene == gameObject.scene) return t;
                    if (fallback == null) fallback = t;
                }
            }
            return fallback;
        }
    }

    /// <summary>Adds each stage's controller (and the theatre's sterility monitor) when its scene loads.</summary>
    static class DetectionInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            switch (scene.name)
            {
                case SceneIds.OperatingTheatre: Ensure<SterilityMonitor>(scene); Ensure<GuidedHighlight>(scene); Ensure<GuidedCoach>(scene); Ensure<ScrubNurse>(scene); break;
                // The muted steps had a voice line scripted for the old click-through demo ("sterility broken", "angle too
                // shallow", "a swab is missing"); those now play when detection sees the event itself.
                case SceneIds.StagePrep: Ensure<PrepController>(scene); MuteVoice(scene, PrepController.GloveStepIndex); break;
                case SceneIds.StageAccess: Ensure<AccessController>(scene); MuteVoice(scene, AccessController.EntryStepIndex); break;
                case SceneIds.StageOperate: Ensure<OperateController>(scene); break;
                case SceneIds.StageClose: Ensure<CloseController>(scene); MuteVoice(scene, CloseController.CountStepIndex); break;
            }
        }

        static void MuteVoice(Scene scene, int step)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var sequence in root.GetComponentsInChildren<StepSequence>(true))
                sequence.MuteVoice(step);
        }

        static void Ensure<T>(Scene scene) where T : MonoBehaviour
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<T>(true) != null) return;
            var go = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<T>();
        }
    }
}
