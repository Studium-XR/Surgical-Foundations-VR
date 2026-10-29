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

        void LateUpdate()
        {
            if (origin == null || origin.Camera == null || origin.CameraFloorOffsetObject == null) return;
            offset = origin.CameraFloorOffsetObject.transform;

            // XROrigin puts the offset back to 0 on a recenter or tracking-origin change; that needs a fresh calibration.
            if (!float.IsNaN(applied) && Mathf.Abs(offset.localPosition.y - applied) > 0.001f) dirty = true;
            if (!dirty) return;

            // Head height in tracking space, without our own lift. Wait until it is real and has settled for half a second.
            var headY = origin.Camera.transform.localPosition.y;
            stableTime = Mathf.Abs(headY - lastHeadY) < 0.02f ? stableTime + Time.unscaledDeltaTime : 0f;
            lastHeadY = headY;
            if (headY < 0.3f || stableTime < 0.5f) return;

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
