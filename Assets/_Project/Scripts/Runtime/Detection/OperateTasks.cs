using System;
using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// The Operate tasks, one after another: peg transfer, dissection, clip-and-cut. This is the sensor side: it
    /// watches instrument tips and jaws against the props in the cavity and reports to the rule classes
    /// (<see cref="PegTransfer"/>, <see cref="Dissection"/>, <see cref="ClipAndCut"/>). Props are found by name.
    /// </summary>
    public class OperateTasks
    {
        // Reach of the jaws, in metres.
        const float GraspRadius = 0.02f;
        const float PegSnapRadius = 0.014f;
        const float PointRadius = 0.012f;
        const float SiteRadius = 0.01f;
        const float DuctRadius = 0.035f;
        const float JawOpenAbove = 0.6f;
        const int DissectionPoints = 5;

        static readonly string[] Labels = { "Peg transfer", "Dissection", "Clip and cut" };

        class Tool
        {
            public LapInstrument instrument;
            public Vector3 lastTip;
            public bool closed;
            public Ring ring;
        }

        class Ring
        {
            public Transform transform;
            public Vector3 home;
            public bool placed;
            public Tool heldBy;
        }

        readonly ProtocolLog log;
        readonly List<Tool> tools = new List<Tool>();
        readonly List<Ring> rings = new List<Ring>();
        readonly List<Vector3> targetPegs = new List<Vector3>();
        readonly bool[] available = new bool[3];
        readonly bool[] skipped = new bool[3];
        bool[] pegTaken;

        Transform tissue, membrane;
        Collider tissueVolume;
        Vector3 membraneScale;
        Vector3[] dissectLocal;
        GameObject[] dissectMarkers;

        Transform structure, duct, cutPoint;
        Transform[] clipPoints;

        public OperateTasks(ProtocolLog log) => this.log = log;

        public event Action Changed;
        public PegTransfer Pegs { get; private set; }
        public Dissection Dissect { get; private set; }
        public ClipAndCut Clip { get; private set; }
        public OperateTask Current { get; private set; }
        public bool AllDone { get; private set; }
        public TaskMetrics Metrics { get; } = new TaskMetrics();
        public static string Label(OperateTask task) => Labels[(int)task];

        public void Setup(Scene scene, bool guided, float maxTipSpeed)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

            foreach (var instrument in all.Select(t => t.GetComponent<LapInstrument>()).Where(i => i != null && i.Jaws != null && i.Tip != null))
                tools.Add(new Tool { instrument = instrument, lastTip = instrument.Tip.position });

            SetupPegs(all);
            SetupTissue(all, guided, maxTipSpeed);
            SetupStructure(all, guided);

            Current = OperateTask.PegTransfer;
            Advance();
        }

        void SetupPegs(List<Transform> all)
        {
            var board = all.FirstOrDefault(t => t.name.StartsWith("PROP_PegBoard"));
            if (board == null) return;
            foreach (var t in board.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Ring_") && t.GetComponent<Rigidbody>() != null)
                {
                    // Inside the abdomen only instruments move the rings, and they are placed, not simulated:
                    // a 2 cm ring on a 6 mm peg is below what the physics engine handles reliably on a headset.
                    t.GetComponent<Rigidbody>().isKinematic = true;
                    var grab = t.GetComponent<XRGrabInteractable>();
                    if (grab != null) grab.enabled = false;
                    rings.Add(new Ring { transform = t, home = t.position });
                }
                else if (t.name.StartsWith("Peg_1_") && t.GetComponent<Collider>() != null)
                    targetPegs.Add(t.GetComponent<Collider>().bounds.center); // the peg's place is its collider's, not its transform's
            }
            if (rings.Count == 0 || targetPegs.Count == 0) return;
            pegTaken = new bool[targetPegs.Count];
            Pegs = new PegTransfer(rings.Count, log);
            Pegs.Changed += Raise;
            available[(int)OperateTask.PegTransfer] = true;
        }

        void SetupTissue(List<Transform> all, bool guided, float maxTipSpeed)
        {
            tissue = all.FirstOrDefault(t => t.name.StartsWith("ANA_DissectionTissue"));
            if (tissue == null) return;
            tissueVolume = tissue.GetComponentInChildren<Collider>(true);
            membrane = tissue.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Membrane");
            if (membrane != null) membraneScale = membrane.localScale;

            // The plane to open runs along the membrane's edge.
            dissectLocal = new Vector3[DissectionPoints];
            dissectMarkers = new GameObject[DissectionPoints];
            for (int i = 0; i < DissectionPoints; i++)
            {
                dissectLocal[i] = new Vector3(Mathf.Lerp(-0.02f, 0.02f, i / (DissectionPoints - 1f)), 0.011f, 0f);
                if (!guided) continue; // the points are marked in Guided mode only (FR-19)
                var marker = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Sphere, new Color(0.36f, 0.88f, 0.78f), "DissectPoint_" + (i + 1));
                marker.transform.SetParent(tissue, false);
                marker.transform.localPosition = dissectLocal[i];
                marker.transform.localScale = Vector3.one * 0.005f;
                marker.SetActive(false);
                dissectMarkers[i] = marker;
            }
            Dissect = new Dissection(DissectionPoints, maxTipSpeed, log);
            Dissect.Changed += Raise;
            available[(int)OperateTask.Dissection] = true;
        }

        void SetupStructure(List<Transform> all, bool guided)
        {
            structure = all.FirstOrDefault(t => t.name.StartsWith("ANA_ClipCutStructure"));
            if (structure == null) return;
            var parts = structure.GetComponentsInChildren<Transform>(true);
            clipPoints = parts.Where(t => t.name.StartsWith("ClipPoint_")).OrderBy(t => t.name).ToArray();
            cutPoint = parts.FirstOrDefault(t => t.name == "CutPoint");
            duct = parts.FirstOrDefault(t => t.name == "Duct");
            var guides = parts.FirstOrDefault(t => t.name == "GuidedMarkers");
            if (guides != null) guides.gameObject.SetActive(guided);
            if (clipPoints.Length == 0 || cutPoint == null) return;
            Clip = new ClipAndCut(clipPoints.Length, log);
            Clip.Changed += Raise;
            available[(int)OperateTask.ClipAndCut] = true;
        }

        public void Tick(float dt)
        {
            if (AllDone || dt <= 0f) return;
            Metrics.seconds += dt;

            var fastestInTissue = 0f;
            var anyInTissue = false;
            foreach (var tool in tools)
            {
                if (tool.instrument == null) continue;
                var tip = tool.instrument.Tip.position;
                var moved = (tip - tool.lastTip).magnitude;
                tool.lastTip = tip;
                if (tool.instrument.InPort) Metrics.pathMetres += moved;

                if (tissueVolume != null && tissueVolume.bounds.Contains(tip))
                {
                    anyInTissue = true;
                    fastestInTissue = Mathf.Max(fastestInTissue, moved / dt);
                }

                var jaws = tool.instrument.Jaws;
                if (!tool.closed && jaws.IsClosed) { tool.closed = true; OnClosed(tool, tip); }
                else if (tool.closed && jaws.Openness >= JawOpenAbove) { tool.closed = false; OnOpened(tool, tip); }

                if (tool.ring != null) tool.ring.transform.position = tip;
            }

            if (Current == OperateTask.Dissection && Dissect != null && Dissect.Contact(anyInTissue, fastestInTissue, dt))
                Metrics.errors++;

            Advance();
        }

        void OnClosed(Tool tool, Vector3 tip)
        {
            var name = tool.instrument.name;
            if (Current == OperateTask.PegTransfer && tool.ring == null)
            {
                var ring = rings.Where(r => !r.placed && r.heldBy == null && (r.transform.position - tip).magnitude <= GraspRadius)
                    .OrderBy(r => (r.transform.position - tip).sqrMagnitude).FirstOrDefault();
                if (ring == null) return;
                ring.heldBy = tool;
                tool.ring = ring;
            }
            else if (Current == OperateTask.ClipAndCut && name.Contains("ClipApplier"))
            {
                var site = Nearest(clipPoints.Select(p => p.position).ToList(), tip, SiteRadius, i => !Clip.IsClipped(i));
                if (site >= 0)
                {
                    if (Clip.PlaceClip(site)) ShowClip(tool.instrument, clipPoints[site]);
                }
                else if (NearDuct(tip, DuctRadius))
                {
                    Clip.ClipMisplaced();
                    Metrics.errors++;
                }
            }
            else if (Current == OperateTask.ClipAndCut && name.Contains("Scissors"))
            {
                if ((tip - cutPoint.position).magnitude <= SiteRadius)
                {
                    if (Clip.Cut(true))
                    {
                        if (Clip.CutWithoutClips) Metrics.errors++;
                        ShowCut();
                    }
                }
                else if (NearDuct(tip, SiteRadius))
                {
                    Clip.Cut(false);
                    Metrics.errors++;
                }
            }
        }

        void OnOpened(Tool tool, Vector3 tip)
        {
            if (tool.ring != null)
            {
                Release(tool, tip);
                return;
            }
            // Spreading the dissector's jaws in the plane is what opens it; a grasper's jaws would tear it.
            if (Current == OperateTask.Dissection && tool.instrument.name.Contains("Maryland"))
            {
                var points = dissectLocal.Select(p => tissue.TransformPoint(p)).ToList();
                var point = Nearest(points, tip, PointRadius, i => !Dissect.IsOpen(i));
                if (point >= 0 && Dissect.Open(point)) ShowDissection();
            }
        }

        void Release(Tool tool, Vector3 p)
        {
            var ring = tool.ring;
            tool.ring = null;
            ring.heldBy = null;

            var peg = Nearest(targetPegs, p, PegSnapRadius, i => !pegTaken[i], flat: true);
            if (peg >= 0 && p.y >= ring.home.y - 0.01f)
            {
                pegTaken[peg] = true;
                ring.placed = true;
                ring.transform.position = new Vector3(targetPegs[peg].x, ring.home.y, targetPegs[peg].z);
                Pegs.Placed();
                return;
            }
            // Put back where it came from is neither progress nor a drop.
            var backHome = new Vector2(p.x - ring.home.x, p.z - ring.home.z).magnitude <= PegSnapRadius;
            ring.transform.position = ring.home;
            if (backHome) return;
            Pegs.Dropped();
            Metrics.errors++;
        }

        static int Nearest(IList<Vector3> points, Vector3 from, float radius, Func<int, bool> allowed, bool flat = false)
        {
            int best = -1;
            var bestDistance = radius;
            for (int i = 0; i < points.Count; i++)
            {
                if (!allowed(i)) continue;
                var d = points[i] - from;
                if (flat) d.y = 0f;
                if (d.magnitude > bestDistance) continue;
                best = i;
                bestDistance = d.magnitude;
            }
            return best;
        }

        /// <summary>Distance from the duct's centre line (it runs along the structure's local X).</summary>
        bool NearDuct(Vector3 tip, float radius)
        {
            var local = structure.InverseTransformPoint(tip);
            var along = Mathf.Clamp(local.x, -0.045f, 0.045f);
            return (local - new Vector3(along, 0.004f, 0f)).magnitude <= radius;
        }

        void ShowClip(LapInstrument applier, Transform site)
        {
            var loaded = applier.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LoadedClip");
            if (loaded == null) return;
            var clip = UnityEngine.Object.Instantiate(loaded.gameObject, site.position, loaded.rotation, structure);
            clip.name = "Clip_" + site.name;
        }

        void ShowDissection()
        {
            for (int i = 0; i < DissectionPoints; i++)
                if (dissectMarkers[i] != null) dissectMarkers[i].SetActive(Current == OperateTask.Dissection && !Dissect.IsOpen(i));
            // The membrane peels back as the plane opens.
            if (membrane != null)
                membrane.localScale = Vector3.Scale(membraneScale, new Vector3(1f - 0.8f * Dissect.Opened / Dissect.Total, 1f, 1f));
        }

        void ShowCut()
        {
            if (duct == null) return;
            // Two stumps either side of the cut, a little apart, as the divided ends retract.
            var cutX = structure.InverseTransformPoint(cutPoint.position).x;
            var centreX = duct.localPosition.x;
            var half = 0.045f;
            foreach (var (start, end) in new[] { (centreX - half, cutX - 0.003f), (cutX + 0.003f, centreX + half) })
            {
                var stump = UnityEngine.Object.Instantiate(duct.gameObject, duct.parent);
                stump.name = "Duct_Stump";
                var scale = duct.localScale;
                scale.y *= (end - start) / (half * 2f);
                stump.transform.localScale = scale;
                var position = duct.localPosition;
                position.x = (start + end) * 0.5f;
                stump.transform.localPosition = position;
            }
            duct.gameObject.SetActive(false);
        }

        /// <summary>The next thing to reach for: a ring, the peg to put it on, a point to open, a site to clip or cut,
        /// or the instrument that has to be fetched from the Mayo stand first.</summary>
        public Vector3? GuideTarget
        {
            get
            {
                if (AllDone || !available[(int)Current]) return null;
                switch (Current)
                {
                    case OperateTask.PegTransfer:
                        if (tools.Any(t => t.ring != null))
                        {
                            for (int i = 0; i < targetPegs.Count; i++)
                                if (!pegTaken[i]) return targetPegs[i];
                            return null;
                        }
                        var ring = rings.FirstOrDefault(r => !r.placed);
                        return ring != null ? ring.transform.position : (Vector3?)null;
                    case OperateTask.Dissection:
                        for (int i = 0; i < DissectionPoints; i++)
                            if (!Dissect.IsOpen(i)) return Fetch("Maryland") ?? tissue.TransformPoint(dissectLocal[i]);
                        return null;
                    default:
                        for (int i = 0; i < clipPoints.Length; i++)
                            if (!Clip.IsClipped(i)) return Fetch("ClipApplier") ?? clipPoints[i].position;
                        return Fetch("Scissors") ?? cutPoint.position;
                }
            }
        }

        /// <summary>The named instrument's position if it still has to be brought in through a port; null once it is in.</summary>
        Vector3? Fetch(string instrumentName)
        {
            var tool = tools.FirstOrDefault(t => t.instrument != null && t.instrument.name.Contains(instrumentName));
            if (tool == null || tool.instrument.InPort) return null;
            return tool.instrument.Held ? FreePortEntry() : tool.instrument.transform.position;
        }

        static Vector3? FreePortEntry()
        {
            foreach (var port in InstrumentPort.All)
                if (port.Occupant == null) return port.Entry;
            // Every port is in use: show the one whose instrument has to come out (never the camera's).
            foreach (var port in InstrumentPort.All)
                if (port.Occupant != null && port.Occupant.CanWithdraw) return port.Entry;
            return null;
        }

        bool IsComplete(OperateTask task)
        {
            if (!available[(int)task]) return true;
            switch (task)
            {
                case OperateTask.PegTransfer: return Pegs.Complete;
                case OperateTask.Dissection: return Dissect.Complete;
                default: return Clip.Complete;
            }
        }

        void Advance()
        {
            var moved = false;
            while (!AllDone && IsComplete(Current))
            {
                if (available[(int)Current])
                    log(EventClass.Info, EventCodes.TaskComplete,
                        $"{Label(Current)}: {SessionManager.FormatClock(Metrics.seconds)}, path {Metrics.pathMetres:0.0} m, {Metrics.errors} error{(Metrics.errors == 1 ? "" : "s")}");
                if (Current == OperateTask.ClipAndCut) AllDone = true;
                else { Current++; Metrics.Reset(); }
                moved = true;
            }
            if (!moved) return;
            if (Dissect != null) ShowDissection();
            Raise();
        }

        /// <summary>The learner ended Operate early: every unfinished task is a logged deviation.</summary>
        public void SkipRemaining()
        {
            if (AllDone) return;
            for (var task = Current; task <= OperateTask.ClipAndCut; task++)
            {
                if (!available[(int)task] || IsComplete(task)) continue;
                skipped[(int)task] = true;
                log(EventClass.Deviation, EventCodes.TaskSkipped, $"{Label(task)} not completed");
            }
            AllDone = true;
            Raise();
        }

        public StepState State(OperateTask task)
        {
            if (skipped[(int)task]) return StepState.Skipped;
            if (AllDone || task < Current) return StepState.Done;
            return task == Current ? StepState.Active : StepState.Pending;
        }

        public string Progress(OperateTask task)
        {
            if (!available[(int)task]) return "";
            switch (task)
            {
                case OperateTask.PegTransfer: return $"{Pegs.Transferred}/{Pegs.Total}";
                case OperateTask.Dissection: return $"{Dissect.Opened}/{Dissect.Total}";
                default: return Clip.Complete ? "cut" : $"{Clip.Clips}/{Clip.Sites} clips";
            }
        }

        void Raise() => Changed?.Invoke();
    }
}
