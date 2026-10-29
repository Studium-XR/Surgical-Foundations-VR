using System.Collections.Generic;
using SurgicalFoundations.Detection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Keeps world-space panels, hint cards and labels out of the props. A panel is clear when its box overlaps no
    /// object and nothing stands between it and the learner's eyes; otherwise it is lifted in small steps and, if that
    /// is not enough, brought towards the learner. Room shells (walls, floor, ceiling) and other UI never count as
    /// obstacles. Obstacles are gathered from the loaded scenes and refreshed on scene loads.
    /// </summary>
    public static class PanelClearance
    {
        const float Step = 0.03f;
        const float MaxLift = 0.6f;
        const float MaxPull = 0.5f;
        const float Margin = 0.015f;
        /// <summary>Anything bigger than this in every direction it spans is room shell, not a prop.</summary>
        const float ShellSize = 2.5f;

        static readonly List<Renderer> obstacles = new List<Renderer>();
        static bool dirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            obstacles.Clear();
            dirty = true;
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneChanged(Scene s, LoadSceneMode m) => dirty = true;
        static void OnSceneUnloaded(Scene s) => dirty = true;

        /// <summary>
        /// Where to put a panel of <paramref name="size"/> metres (width, height) facing the viewer so it is clear of
        /// props, starting from <paramref name="desired"/>. <paramref name="ignore"/> is left out of the obstacles
        /// (the panel itself, or the object a label belongs to).
        /// </summary>
        public static Vector3 Clear(Vector3 desired, Vector2 size, Vector3 viewer, params Transform[] ignore)
        {
            Refresh();
            var toViewer = viewer - desired;
            toViewer.y = 0f;
            var towards = toViewer.sqrMagnitude > 1e-4f ? toViewer.normalized : Vector3.zero;

            for (float pull = 0f; pull <= MaxPull + 1e-4f; pull += Step * 3f)
            for (float lift = 0f; lift <= MaxLift + 1e-4f; lift += Step)
            {
                var p = desired + Vector3.up * lift + towards * pull;
                if (IsClear(p, size, viewer, ignore)) return p;
            }
            return desired + Vector3.up * MaxLift; // nothing clear nearby: at least get above the clutter
        }

        /// <summary>
        /// Moves a world-space canvas (keeping its rotation) until it is clear of props and visible from the viewer.
        /// Returns true if it had to move.
        /// </summary>
        public static bool ClearCanvas(RectTransform rt, Vector3 viewer)
        {
            Refresh();
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;
            var toViewer = viewer - centre;
            toViewer.y = 0f;
            var towards = toViewer.sqrMagnitude > 1e-4f ? toViewer.normalized : Vector3.zero;
            var ignore = new Transform[] { rt };

            for (float pull = 0f; pull <= MaxPull + 1e-4f; pull += Step * 3f)
            for (float lift = 0f; lift <= MaxLift + 1e-4f; lift += Step)
            {
                var offset = Vector3.up * lift + towards * pull;
                var box = new Bounds(corners[0] + offset, Vector3.zero);
                for (int i = 1; i < 4; i++) box.Encapsulate(corners[i] + offset);
                box.Expand(Margin * 2f);
                if (!IsClearBox(box, centre + offset, viewer, ignore)) continue;
                if (offset == Vector3.zero) return false;
                rt.position += offset;
                return true;
            }
            return false; // nowhere clear within reach: leave the authored layout alone
        }

        /// <summary>World size of a RectTransform-based canvas, in metres.</summary>
        public static Vector2 SizeOf(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Vector2((c[3] - c[0]).magnitude, (c[1] - c[0]).magnitude);
        }

        static bool IsClear(Vector3 centre, Vector2 size, Vector3 viewer, Transform[] ignore)
        {
            var forward = centre - viewer;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            var right = rot * Vector3.right * (size.x * 0.5f + Margin);
            var up = rot * Vector3.up * (size.y * 0.5f + Margin);
            var depth = rot * Vector3.forward * Margin;
            var ext = new Vector3(
                Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(depth.x),
                Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(depth.y),
                Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(depth.z));
            return IsClearBox(new Bounds(centre, ext * 2f), centre, viewer, ignore);
        }

        static bool IsClearBox(Bounds box, Vector3 centre, Vector3 viewer, Transform[] ignore)
        {
            var forward = centre - viewer;
            var sight = new Ray(viewer, forward.normalized);
            var sightLength = forward.magnitude - 0.05f;

            foreach (var r in obstacles)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || Ignored(r.transform, ignore)) continue;
                var b = r.bounds;
                if (b.Intersects(box)) return false;
                if (b.IntersectRay(sight, out var d) && d > 0.1f && d < sightLength) return false;
            }
            return true;
        }

        static bool Ignored(Transform t, Transform[] ignore)
        {
            foreach (var i in ignore)
                if (i != null && (t == i || t.IsChildOf(i))) return true;
            return false;
        }

        static void Refresh()
        {
            if (!dirty) { obstacles.RemoveAll(r => r == null); return; }
            dirty = false;
            obstacles.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer || r is SpriteRenderer) continue;
                    if (r.GetComponentInParent<Canvas>() != null || r.GetComponent<TMPro.TMP_Text>() != null) continue;
                    if (r.GetComponentInParent<XROrigin>() != null) continue; // the learner's own hands and rays
                    var size = r.bounds.size;
                    if (size.x > ShellSize && size.z > ShellSize || size.y > ShellSize && Mathf.Max(size.x, size.z) > ShellSize) continue;
                    if (r.name.StartsWith("GuidedHighlight") || r.name.StartsWith("Label_") || r.name.StartsWith("FX") || r.name.Contains("LaminarFlow")) continue;
                    obstacles.Add(r);
                }
            }
        }

        /// <summary>The learner's eyes, or a standing eye height at the origin before the rig exists.</summary>
        public static Vector3 Viewer()
        {
            var head = LearnerRig.Head;
            return head != null ? head.position : new Vector3(0f, 1.6f, -0.85f);
        }
    }
}
