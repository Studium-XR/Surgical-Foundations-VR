using SurgicalFoundations.Core;
using SurgicalFoundations.Detection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// A panel that is placed in the room relative to where the learner is facing, and then stays there. It is placed
    /// again only at moments the learner expects the world to change (a scene or stage loads, they recenter), or when a
    /// caller asks (a new subtitle line while the old spot is out of view). It never follows the head.
    /// Replaces the old LazyFollow head-tracked HUD and subtitles; <see cref="Install"/> converts any LazyFollow left in
    /// built scenes, so no scene has to be rebuilt.
    /// </summary>
    public class WorldLockedPanel : MonoBehaviour
    {
        [Tooltip("Offset from the head when placed: x right, y up, z forward (yaw only, so it stays upright).")]
        public Vector3 offset = new Vector3(0f, -0.3f, 1.3f);

        bool placed;

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneLoader.Instance != null) SceneLoader.Instance.SceneReady += OnSceneReady;
            placed = false;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (SceneLoader.Instance != null) SceneLoader.Instance.SceneReady -= OnSceneReady;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => placed = false;
        void OnSceneReady(string _) => placed = false;

        void LateUpdate()
        {
            if (!placed) Place();
        }

        /// <summary>Places the panel in front of the learner now. Returns false when there is no head yet.</summary>
        public bool Place()
        {
            var head = LearnerRig.Head != null ? LearnerRig.Head : (Camera.main != null ? Camera.main.transform : null);
            if (head == null) return false;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var pos = head.position + right * offset.x + Vector3.up * offset.y + fwd * offset.z;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            placed = true;
            return true;
        }

        /// <summary>Places it again only if the learner can't currently see it (outside ~35° of where they look).</summary>
        public void PlaceIfOutOfView()
        {
            var head = LearnerRig.Head != null ? LearnerRig.Head : (Camera.main != null ? Camera.main.transform : null);
            if (head == null) return;
            var to = (transform.position - head.position).normalized;
            if (!placed || Vector3.Dot(to, head.forward) < 0.82f) Place();
        }

        public void MarkForPlacement() => placed = false;

        // ─────────────────────────────── installer ───────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded += (s, m) => ConvertHeadLocked();
            ConvertHeadLocked();
        }

        /// <summary>Every LazyFollow (head-tracked UI) becomes a world-locked panel at the same offset.</summary>
        public static void ConvertHeadLocked()
        {
            foreach (var lf in FindObjectsByType<LazyFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var go = lf.gameObject;
                var off = lf.targetOffset;
                Destroy(lf);
                if (!go.TryGetComponent<WorldLockedPanel>(out var wl)) wl = go.AddComponent<WorldLockedPanel>();
                // The old HUD sat 0.45 m above eye level and drifted with the head; locked in the room it reads better
                // a little lower and further away, like a wall board.
                wl.offset = off.y > 0.2f ? new Vector3(off.x, 0.35f, Mathf.Max(off.z, 1.8f)) : off;
            }
        }
    }
}
