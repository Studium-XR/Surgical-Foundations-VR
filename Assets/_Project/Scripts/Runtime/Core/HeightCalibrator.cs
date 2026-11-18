using SurgicalFoundations.Contracts;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace SurgicalFoundations.Core
{
    /// <summary>
    /// Puts the learner's eyes at a working height over the OR table. The rig runs in Floor space, so a standing adult
    /// already gets their real height; this only lifts the camera offset when the tracked head is lower than that:
    /// a seated learner (Posture.Seated) is raised to a standing eye height, and a standing one whose head reads below
    /// <see cref="minStandingEyeHeight"/> (short learner, or a floor level set too high in the guardian) is raised to it.
    /// Calibrates once the head is tracked, again when the posture changes, and again after a recenter resets the offset.
    /// On top of that the learner can raise or lower their eyes by hand (<see cref="UserOffset"/>, from the quick-settings
    /// dial), which is remembered between sessions.
    /// </summary>
    public class HeightCalibrator : MonoBehaviour
    {
        [SerializeField] float minStandingEyeHeight = 1.55f;
        [SerializeField] float seatedEyeHeight = 1.65f;

        public const float MinUserOffset = -0.2f, MaxUserOffset = 0.4f;
        const string OffsetPref = "sf.eyeHeightOffset";

        public static HeightCalibrator Instance { get; private set; }

        static float userOffset;
        static bool userOffsetLoaded;

        /// <summary>Extra eye height chosen by the learner, in metres (negative lowers). Applied at once, kept in PlayerPrefs on <see cref="SaveUserOffset"/>.</summary>
        public static float UserOffset
        {
            get
            {
                if (!userOffsetLoaded) { userOffset = PlayerPrefs.GetFloat(OffsetPref, 0f); userOffsetLoaded = true; }
                return userOffset;
            }
        }

        public static void SetUserOffset(float metres)
        {
            userOffset = Mathf.Clamp(metres, MinUserOffset, MaxUserOffset);
            userOffsetLoaded = true;
            if (Instance != null) Instance.MarkDirty();
        }

        public static void SaveUserOffset()
        {
            PlayerPrefs.SetFloat(OffsetPref, UserOffset);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => userOffsetLoaded = false; // re-read on each play (domain reload may be off)

        XROrigin origin;
        Transform offset;
        float applied = float.NaN;
        bool dirty = true;
        float stableTime;
        float lastHeadY;

        void Awake() => Instance = this;

        void Start()
        {
            if (!TryGetComponent(out origin)) origin = FindAnyObjectByType<XROrigin>();
            if (SessionManager.Instance != null) SessionManager.Instance.SettingsChanged += MarkDirty;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (SessionManager.Instance != null) SessionManager.Instance.SettingsChanged -= MarkDirty;
        }

        public void MarkDirty() => dirty = true;

        float lastGoodY;
        bool sceneJustLoaded;

        void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => sceneJustLoaded = true;

        /// <summary>
        /// Puts the camera offset back where it was before a scene load. XROrigin only ever changes its height on a
        /// recenter, but the XRI Interaction Simulator (desktop testing) sets the offset's world position to a stale
        /// value on every scene load, even when XR Simulation is not the active loader. Combined with the rig dropping
        /// under gravity while the old floor is unloaded, that left the camera metres away from the rig, often above
        /// the theatre ceiling (a black view). Called by the scene loader before it places the rig, and every frame
        /// here. On a headset the offset is already where it was, so this changes nothing.
        /// </summary>
        public void RestoreOffset()
        {
            if (origin == null || origin.CameraFloorOffsetObject == null) return;
            var t = origin.CameraFloorOffsetObject.transform;
            var p = t.localPosition;
            bool moved = Mathf.Abs(p.x) > 0.001f || Mathf.Abs(p.z) > 0.001f;
            if (!moved && !sceneJustLoaded) return;
            sceneJustLoaded = false;
            t.localPosition = new Vector3(0f, lastGoodY, 0f);
        }

        static bool SimulatorRunning() =>
            Application.isEditor &&
            FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>() != null;

        void LateUpdate()
        {
            if (origin == null || origin.Camera == null || origin.CameraFloorOffsetObject == null) return;
            offset = origin.CameraFloorOffsetObject.transform;
            RestoreOffset();
            lastGoodY = offset.localPosition.y;

            // XROrigin puts the offset back to 0 on a recenter or tracking-origin change; that needs a fresh calibration.
            if (!float.IsNaN(applied) && Mathf.Abs(offset.localPosition.y - applied) > 0.001f) dirty = true;
            if (!dirty) return;

            // Head height in tracking space, without our own lift. Wait until it is real and has settled for half a second.
            var headY = origin.Camera.transform.localPosition.y;
            stableTime = Mathf.Abs(headY - lastHeadY) < 0.02f ? stableTime + Time.unscaledDeltaTime : 0f;
            lastHeadY = headY;
            // A real head below 30 cm is not tracked yet. The desktop XR Interaction Simulator's head starts at 0, though,
            // and never "arrives", so with the simulator running the learner is lifted from wherever the head is.
            if (stableTime < 0.5f) return;
            if (headY < 0.3f && !SimulatorRunning()) return;

            var seated = SessionManager.Instance != null && SessionManager.Instance.Settings.posture == Posture.Seated;
            var target = seated ? seatedEyeHeight : minStandingEyeHeight;
            var lift = Mathf.Max(0f, target - headY) + UserOffset;

            var p = offset.localPosition;
            p.y = lift;
            offset.localPosition = p;
            applied = lift;
            dirty = false;
        }
    }
}
