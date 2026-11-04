using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Peg-board rings in the Skills Lab can be picked up with an instrument, not only by hand: close the jaws of a held
    /// grasper on a ring and it rides on the tip; open them over a free peg and it drops onto the peg, anywhere else and
    /// it falls. (Inside the patient the Operate stage does this itself, in OperateTasks.) Added when the Skills Lab loads.
    /// </summary>
    public class RingGrasp : MonoBehaviour
    {
        const float GraspRadius = 0.025f;
        const float PegSnapRadius = 0.02f;
        const float JawOpenAbove = 0.6f;
        /// <summary>A ring's centre above the board when it sits on a peg (board-local, as the prefab places them).</summary>
        const float RingRestY = 0.024f;

        class Tool
        {
            public InstrumentJaws jaws;
            public Transform tip;
            public bool closed;
            public Transform ring;
        }

        readonly List<Tool> tools = new List<Tool>();
        readonly List<Transform> rings = new List<Transform>();
        readonly List<Transform> pegs = new List<Transform>();
        Transform board;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneIds.SkillsLab) return;
            var go = new GameObject("RingGrasp");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<RingGrasp>();
        }

        void Start()
        {
            var all = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            board = all.FirstOrDefault(t => t.name.StartsWith("PROP_PegBoard"));
            if (board == null) { enabled = false; return; }
            var boardParts = board.GetComponentsInChildren<Transform>(true);
            rings.AddRange(boardParts.Where(t => t.name.StartsWith("Ring_")));
            pegs.AddRange(boardParts.Where(t => t.name.StartsWith("Peg_") && t.GetComponent<Collider>() != null));
            foreach (var jaws in all.Select(t => t.GetComponent<InstrumentJaws>()).Where(j => j != null))
            {
                var tip = jaws.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pivot_Tip");
                if (tip != null) tools.Add(new Tool { jaws = jaws, tip = tip });
            }
        }

        void Update()
        {
            foreach (var tool in tools)
            {
                if (tool.jaws == null) continue;
                if (!tool.closed && tool.jaws.IsClosed) { tool.closed = true; Grasp(tool); }
                else if (tool.closed && tool.jaws.Openness >= JawOpenAbove) { tool.closed = false; Release(tool); }

                if (tool.ring == null) continue;
                // Taken out of the jaws by hand: the hand has it now.
                var grab = tool.ring.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected) { tool.ring = null; continue; }
                tool.ring.SetPositionAndRotation(tool.tip.position, board.rotation); // lies flat, as on the board
            }
        }

        void Grasp(Tool tool)
        {
            if (tool.ring != null) return;
            var held = tools.Where(t => t.ring != null).Select(t => t.ring);
            var ring = rings.Where(r => r != null && !held.Contains(r) && (r.position - tool.tip.position).magnitude <= GraspRadius)
                .Where(r => { var g = r.GetComponent<XRGrabInteractable>(); return g == null || !g.isSelected; })
                .OrderBy(r => (r.position - tool.tip.position).sqrMagnitude).FirstOrDefault();
            if (ring == null) return;
            tool.ring = ring;
            SetPhysics(ring, kinematic: true);
            Audio.AudioManager.Instance?.PlayAt(Audio.SoundId.INST_JawClose, ring.position);
        }

        void Release(Tool tool)
        {
            var ring = tool.ring;
            if (ring == null) return;
            tool.ring = null;

            var p = ring.position;
            var peg = pegs.Where(g => !Occupied(g, ring))
                .OrderBy(g => Flat(g.position - p)).FirstOrDefault(g => Flat(g.position - p) <= PegSnapRadius);
            if (peg != null && p.y >= board.TransformPoint(new Vector3(0, RingRestY, 0)).y - 0.01f)
            {
                var local = board.InverseTransformPoint(peg.position);
                ring.SetPositionAndRotation(board.TransformPoint(new Vector3(local.x, RingRestY, local.z)), board.rotation);
                SetPhysics(ring, kinematic: true); // seated on its peg
            }
            else SetPhysics(ring, kinematic: false); // dropped: it falls
        }

        bool Occupied(Transform peg, Transform except) =>
            rings.Any(r => r != null && r != except && Flat(r.position - peg.position) <= PegSnapRadius * 0.5f);

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static void SetPhysics(Transform ring, bool kinematic)
        {
            var body = ring.GetComponent<Rigidbody>();
            if (body == null) return;
            if (!kinematic) { body.isKinematic = false; body.useGravity = true; return; }
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}
