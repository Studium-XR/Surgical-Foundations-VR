using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Detection;
using SurgicalFoundations.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.Replay
{
    /// <summary>
    /// Expert ghost at the Skills Lab peg board: a recorded da Vinci peg transfer (Open-H-Embodiment, Hamlyn Centre,
    /// CC BY 4.0; Resources/OpenH) replayed in a loop as a ghost laparoscopic instrument, so the learner can watch how
    /// little an expert's tip wanders. Guided mode only.
    /// The recording gives each robot arm's tip position relative to its remote centre of motion, which is the port the
    /// shaft pivots through, so the ghost pivots about a virtual port above the board just as a real instrument would.
    /// Each robot's base sat differently from this board, so the path is retargeted, not reproduced: turned about the
    /// vertical so the carry (jaws closing to jaws opening) runs from the near row of pegs to the far row, scaled to that
    /// distance, with the grasp on the first ring. Arms that barely move in the recording are left out.
    /// </summary>
    public class ExpertGhostReplay : MonoBehaviour
    {
        const string Recording = "OpenH/hamlyn_peg_transfer_000";
        const float ShaftLength = 0.32f, ShaftRadius = 0.002f, JawLength = 0.014f;
        const float MinTravel = 0.01f;        // arms whose tip moves less than this stay out
        const float TransferDistance = 0.06f; // near row of pegs to far row on PROP_PegBoard
        const float RingTop = 0.03f, BoardTop = 0.022f;
        const float JawOpen = 0.1f;           // radians; above this the jaws count as open
        const float LoopPause = 1.5f;
        static readonly Color GhostColor = new Color(0.36f, 0.88f, 0.78f);

        class Arm
        {
            public Vector3[] tip;   // board-local, per frame
            public float[] jaw;     // opening, radians
            public Vector3 port;    // board-local
            public int grasp, release; // frames the jaws close on the ring and let it go
            public Transform shaft, jawA, jawB, ring;
        }

        float[] times;
        readonly List<Arm> arms = new List<Arm>();
        float clock;
        Transform root;
        TextMeshPro label;

        static bool Guided => SessionManager.Instance == null || SessionManager.Instance.Settings.mode == TrainingMode.Guided;

        // ─────────────────────────────── installation ───────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == SceneIds.SkillsLab) InstallIn(scene);
        }

        /// <summary>Adds the ghost to the scene's peg board (once).</summary>
        public static ExpertGhostReplay InstallIn(Scene scene)
        {
            var board = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name.StartsWith("PROP_PegBoard"));
            if (board == null) return null;
            var existing = board.GetComponentInChildren<ExpertGhostReplay>(true);
            if (existing != null) return existing;
            var go = new GameObject("ExpertGhost");
            go.transform.SetParent(board, false);
            return go.AddComponent<ExpertGhostReplay>();
        }

        // ─────────────────────────────── setup ───────────────────────────────

        void Awake()
        {
            var csv = Resources.Load<TextAsset>(Recording);
            if (csv == null) { Debug.LogWarning("[ExpertGhostReplay] Recording missing: Resources/" + Recording); enabled = false; return; }
            var raw = Parse(csv.text, out times);
            if (times.Length < 2) { enabled = false; return; }

            // First ring on the near row, else the board's near-left corner.
            var board = transform.parent;
            var ring = board != null ? board.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Ring_1") : null;
            var start = ring != null ? board.InverseTransformPoint(ring.position) : new Vector3(-0.08f, 0f, -0.03f);
            start.y = RingTop;

            root = new GameObject("Visuals").transform; // shown and hidden as one
            root.SetParent(transform, false);
            foreach (var path in raw)
            {
                var arm = Retarget(path, start);
                if (arm != null) { Build(arm); arms.Add(arm); }
            }
            if (arms.Count == 0) { enabled = false; return; }
            BuildLabel();
        }

        /// <summary>Per arm: tip in the arm's port frame turned to Unity axes (x, z, y), and the jaw angle.</summary>
        static List<(Vector3[] tip, float[] jaw)> Parse(string text, out float[] t)
        {
            var rows = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("t,"))
                .Select(l => l.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            t = rows.Select(r => r[0]).ToArray();
            var result = new List<(Vector3[], float[])>();
            for (int a = 0; a < 2; a++)
            {
                int c = 1 + a * 4;
                result.Add((rows.Select(r => new Vector3(r[c], r[c + 2], r[c + 1])).ToArray(), rows.Select(r => r[c + 3]).ToArray()));
            }
            return result;
        }

        static Arm Retarget((Vector3[] tip, float[] jaw) path, Vector3 start)
        {
            var p = path.tip;
            var flat = new Vector3(1, 0, 1);
            if (p.Max(v => Vector3.Scale(v - p[0], flat).magnitude) < MinTravel) return null;

            // Grasp: the jaws close after having been open. Release: they open again. Without both, the start and
            // the point farthest from it stand in for them.
            int grasp = -1, release = -1;
            for (int i = 1; i < p.Length; i++)
            {
                if (grasp < 0 && path.jaw[i] < 0f && path.jaw.Take(i).Any(j => j > JawOpen)) grasp = i;
                else if (grasp >= 0 && path.jaw[i] > 0f) { release = i; break; }
            }
            if (grasp < 0 || release < 0)
            {
                grasp = 0;
                release = System.Array.IndexOf(p, p.OrderByDescending(v => Vector3.Scale(v - p[0], flat).sqrMagnitude).First());
            }
            var reach = Vector3.Scale(p[release] - p[grasp], flat);
            if (reach.magnitude < 0.001f) return null;

            var turn = Quaternion.FromToRotation(reach.normalized, Vector3.forward);
            var scale = Mathf.Clamp(TransferDistance / reach.magnitude, 0.5f, 2.5f);
            Vector3 Map(Vector3 v) => start + turn * ((v - p[grasp]) * scale);
            var tips = p.Select(Map).ToArray();
            for (int i = 0; i < tips.Length; i++) tips[i].y = Mathf.Max(tips[i].y, BoardTop); // never through the board
            return new Arm { tip = tips, jaw = path.jaw, port = Map(Vector3.zero), grasp = grasp, release = release };
        }

        void Build(Arm arm)
        {
            arm.shaft = RuntimeMaterials.Primitive(PrimitiveType.Cylinder, GhostColor, "GhostShaft").transform;
            arm.shaft.SetParent(root, false);
            arm.shaft.localScale = new Vector3(ShaftRadius * 2f, ShaftLength * 0.5f, ShaftRadius * 2f);
            arm.jawA = Jaw("GhostJawA");
            arm.jawB = Jaw("GhostJawB");
            arm.ring = RuntimeMaterials.Primitive(PrimitiveType.Cylinder, GhostColor, "GhostRing").transform;
            arm.ring.SetParent(root, false);
            arm.ring.localScale = new Vector3(0.02f, 0.0025f, 0.02f); // a ring as carried, 2 cm across

            var trail = new GameObject("GhostPath").AddComponent<LineRenderer>();
            trail.transform.SetParent(root, false);
            trail.useWorldSpace = false;
            trail.positionCount = arm.tip.Length;
            trail.SetPositions(arm.tip);
            trail.widthMultiplier = 0.0012f;
            trail.sharedMaterial = RuntimeMaterials.Unlit(GhostColor * 0.8f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Transform Jaw(string name)
        {
            var hinge = new GameObject(name).transform;
            hinge.SetParent(root, false);
            var blade = RuntimeMaterials.Primitive(PrimitiveType.Cube, GhostColor, "Blade").transform;
            blade.SetParent(hinge, false);
            blade.localScale = new Vector3(0.0018f, 0.0018f, JawLength);
            blade.localPosition = new Vector3(0f, 0f, JawLength * 0.5f);
            return hinge;
        }

        void BuildLabel()
        {
            var go = new GameObject("GhostLabel");
            go.transform.SetParent(root, false);
            label = go.AddComponent<TextMeshPro>();
            label.text = "Expert demo: recorded robotic peg transfer\n<size=60%>Open-H-Embodiment, Hamlyn Centre (CC BY 4.0)</size>";
            label.fontSize = 0.22f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = GhostColor;
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(5, 20, 24, 255);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(0.4f, 0.05f);
            go.transform.localPosition = new Vector3(0f, arms.Max(a => a.port.y) + 0.04f, 0f);
        }

        // ─────────────────────────────── playback ───────────────────────────────

        void Update()
        {
            var show = Guided;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            var length = times[times.Length - 1];
            clock = (clock + Time.deltaTime) % (length + LoopPause);
            Pose(Mathf.Min(clock, length));

            var head = LearnerRig.Head;
            if (head != null && label != null)
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - head.position, Vector3.up);
        }

        /// <summary>Puts every ghost at time <paramref name="t"/> of the recording.</summary>
        public void Pose(float t)
        {
            int i = System.Array.BinarySearch(times, t);
            if (i < 0) i = Mathf.Clamp(~i - 1, 0, times.Length - 2);
            i = Mathf.Min(i, times.Length - 2);
            var k = Mathf.InverseLerp(times[i], times[i + 1], t);
            foreach (var arm in arms)
            {
                var tip = Vector3.Lerp(arm.tip[i], arm.tip[i + 1], k);
                var jaw = Mathf.Lerp(arm.jaw[i], arm.jaw[i + 1], k);
                var dir = (tip - arm.port).normalized;                       // port → tip
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                arm.shaft.localPosition = tip - dir * (ShaftLength * 0.5f + 0.002f);
                arm.shaft.localRotation = rot * Quaternion.Euler(90f, 0f, 0f); // cylinder's long axis is Y
                var half = Mathf.Max(0f, jaw) * 0.5f * Mathf.Rad2Deg;
                foreach (var (j, s) in new[] { (arm.jawA, 1f), (arm.jawB, -1f) })
                {
                    j.localPosition = tip - dir * 0.002f;
                    j.localRotation = rot * Quaternion.Euler(s * half, 0f, 0f);
                }
                var carrying = arm.grasp > 0 && i >= arm.grasp && i < arm.release;
                if (arm.ring.gameObject.activeSelf != carrying) arm.ring.gameObject.SetActive(carrying);
                arm.ring.localPosition = tip - Vector3.up * 0.004f;
            }
        }
    }
}
