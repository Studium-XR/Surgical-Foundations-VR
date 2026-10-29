using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Rendering;
using SurgicalFoundations.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Open-surgery foundations station in the Skills Lab: five exercises on a layered practice pad and a vessel trainer,
    /// done in order and reviewed at the end.
    ///  1. Incision: trace the dashed line with the scalpel; the blade cuts where it touches the skin.
    ///  2. Grasp and retract: lift a wound edge with the toothed forceps (or hook it with the retractor) and hold it
    ///     open without pulling too hard.
    ///  3. Clamp: lock the haemostat across the bleeding artery at the marked point.
    ///  4. Suture: with the needle in the needle holder, pass it through both bites of each stitch.
    ///  5. Cut: close the Mayo scissors on each suture tail.
    /// Every exercise records time, accuracy and errors; the review shows them all. Instrument mechanics come from the
    /// instruments themselves (InstrumentJaws, RatchetLock); this only reads where their tips are and what they do.
    /// Puts itself on the Skills Lab back table when that scene loads (Resources/OpenSurgery/OpenSkillsStation).
    /// </summary>
    public class OpenSkillsStation : MonoBehaviour
    {
        enum Task { Incision, Retract, Clamp, Suture, Cut, Review }

        static readonly string[] Titles = { "Incision", "Grasp and retract", "Clamp the vessel", "Suture", "Cut the tails", "Review" };
        static readonly string[] Instructions =
        {
            "Pick up the scalpel and draw the blade along the dashed line, touching the skin. Stay on the line.",
            "Pick up the toothed forceps (or the retractor). Close the jaws on a cut edge and lift it gently to open the wound. Hold it for 2 seconds.",
            "The artery is bleeding. Pick up the haemostat, place the jaws across the marked point and squeeze fully to lock it.",
            "Pick up the needle holder. Pass the needle through the marked bite on one side of the wound, then the other. Two stitches.",
            "Pick up the Mayo scissors. Close them on each suture tail to cut it.",
            "",
        };

        // Tuning (metres, seconds, degrees).
        const float CutContactAbove = 0.004f, CutContactBelow = 0.012f;
        const float OnLine = 0.006f, BinSize = 0.005f, IncisionDoneCoverage = 0.85f;
        const float GrabRadius = 0.014f, MaxLiftAngle = 40f, TearAngle = 52f, HoldAngle = 15f, HoldSeconds = 2f, RestOpenAngle = 3f;
        const float ClampRadius = 0.012f, ClampNearMiss = 0.035f;
        const float BiteRadius = 0.007f, BiteWindow = 25f;
        const float TailRadius = 0.012f;

        Task task;
        float taskStart, sessionStart;
        readonly Dictionary<Task, float> taskTimes = new Dictionary<Task, float>();
        readonly Dictionary<Task, int> taskErrors = new Dictionary<Task, int>();

        // Parts.
        Transform pad, edgeA, edgeB, targetStart, targetEnd, clampPoint, bleedPoint;
        readonly Transform[,] bites = new Transform[2, 2];
        Transform scalpelTip, forcepsTip, scissorsTip, hemostatTip, retractorTip, needleTip;
        InstrumentJaws forceps, scissors, hemostat;
        RatchetLock ratchet;
        readonly List<(Transform t, Vector3 pos, Quaternion rot)> homes = new List<(Transform, Vector3, Quaternion)>();
        float skinTop;

        // Incision state.
        LineRenderer cutLine;
        readonly List<Vector3> cutPoints = new List<Vector3>();
        bool[] bins;
        float deviationSum;
        int deviationSamples;
        float offLineTravel;

        // Retraction state.
        Transform heldEdge;
        float heldFor;
        float angleA = RestOpenAngle, angleB = RestOpenAngle;

        // Clamp / bleed state.
        bool clamped;
        bool nearMissLogged;
        Transform drip;
        float dripT;

        // Suture state.
        int stitch;
        float biteATime = -1f;
        readonly List<GameObject> stitches = new List<GameObject>();
        readonly List<GameObject> tails = new List<GameObject>();
        bool scissorsWasClosed;

        // Guidance and panel.
        Transform marker;
        TextMeshProUGUI stepText, titleText, bodyText, statusText;
        Button restartButton;

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

        /// <summary>Puts the station on the scene's back table (once), replacing the display-only instrument set there.</summary>
        public static GameObject InstallIn(Scene scene)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var existing = all.FirstOrDefault(t => t.GetComponent<OpenSkillsStation>() != null);
            if (existing != null) return existing.gameObject;
            var table = all.FirstOrDefault(t => t.name.StartsWith("EQ_BackTable"));
            var prefab = Resources.Load<GameObject>("OpenSurgery/OpenSkillsStation");
            if (table == null || prefab == null) { Debug.LogWarning("[OpenSkillsStation] No back table or station prefab in the Skills Lab."); return null; }

            // The heavy display-only instrument set sat on this table; the working station replaces it.
            foreach (var t in all.Where(t => t.name.StartsWith("PROP_OpenSurgerySet"))) t.gameObject.SetActive(false);

            var renderers = table.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            // Learner on the room side of the table, facing the wall it stands against: of the table's four sides,
            // the one that best faces away from the middle of the room, so the station lines up with the table edges.
            var away = Vector3.ProjectOnPlane(top - Vector3.zero, Vector3.up).normalized;
            var axes = new[] { table.forward, -table.forward, table.right, -table.right };
            var facing = axes.OrderByDescending(v => Vector3.Dot(Vector3.ProjectOnPlane(v, Vector3.up).normalized, away)).First();
            var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up).normalized, Vector3.up);
            var station = Instantiate(prefab, top + rot * new Vector3(0.05f, 0f, -0.02f), rot);
            station.name = "OpenSkillsStation";
            SceneManager.MoveGameObjectToScene(station, scene);
            return station;
        }

        // ─────────────────────────────── setup ───────────────────────────────

        void Start()
        {
            Transform Find(string n) => GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            Transform Under(Transform root, string n) => root == null ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

            pad = Find("PROP_SkinPad");
            edgeA = Under(pad, "SkinEdge_A");
            edgeB = Under(pad, "SkinEdge_B");
            targetStart = Under(pad, "Target_Start");
            targetEnd = Under(pad, "Target_End");
            for (int i = 0; i < 2; i++) { bites[i, 0] = Under(pad, $"Bite_{i}_A"); bites[i, 1] = Under(pad, $"Bite_{i}_B"); }
            var vessel = Find("PROP_VesselTrainer");
            clampPoint = Under(vessel, "ClampPoint");
            bleedPoint = Under(vessel, "BleedPoint");

            var scalpel = Find("INST_Scalpel");
            var fc = Find("INST_TissueForceps");
            var sc = Find("INST_MayoScissors");
            var hm = Find("INST_Hemostat");
            var nh = Find("INST_NeedleHolder");
            var rt = Find("INST_Retractor");
            scalpelTip = Under(scalpel, "Pivot_Tip");
            forcepsTip = Under(fc, "Pivot_Tip");
            scissorsTip = Under(sc, "Pivot_Tip");
            hemostatTip = Under(hm, "Pivot_Tip");
            retractorTip = Under(rt, "Pivot_Tip");
            var needle = Under(nh, "Needle");
            needleTip = needle != null && needle.childCount > 0 ? needle.GetChild(needle.childCount - 1) : Under(nh, "JawHinge");
            forceps = fc != null ? fc.GetComponent<InstrumentJaws>() : null;
            scissors = sc != null ? sc.GetComponent<InstrumentJaws>() : null;
            hemostat = hm != null ? hm.GetComponent<InstrumentJaws>() : null;
            ratchet = hm != null ? hm.GetComponent<RatchetLock>() : null;
            foreach (var t in new[] { scalpel, fc, sc, hm, nh, rt })
                if (t != null) homes.Add((t, t.localPosition, t.localRotation));

            if (pad == null || edgeA == null || targetStart == null || scalpelTip == null)
            {
                Debug.LogWarning("[OpenSkillsStation] Station parts missing; rebuild it with Surgical Foundations → Build → 1g.");
                enabled = false;
                return;
            }
            skinTop = pad.InverseTransformPoint(targetStart.position).y;

            cutLine = new GameObject("CutLine").AddComponent<LineRenderer>();
            cutLine.transform.SetParent(pad, false);
            cutLine.useWorldSpace = false;
            cutLine.widthMultiplier = 0.0016f;
            cutLine.numCapVertices = 2;
            cutLine.sharedMaterial = RuntimeMaterials.Lit(new Color(0.45f, 0.05f, 0.06f));
            cutLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var m = RuntimeMaterials.Primitive(PrimitiveType.Sphere, new Color(0.36f, 0.88f, 0.78f), "OpenSkills_Marker");
            marker = m.transform;
            marker.SetParent(transform, false);
            marker.localScale = Vector3.one * 0.012f;

            drip = RuntimeMaterials.Primitive(PrimitiveType.Sphere, new Color(0.55f, 0.02f, 0.04f), "Bleed").transform;
            drip.SetParent(transform, false);
            drip.localScale = Vector3.one * 0.006f;

            BuildPanel();
            Restart();
        }

        void Restart()
        {
            foreach (var (t, pos, rot) in homes)
            {
                t.localPosition = pos;
                t.localRotation = rot;
            }
            if (hemostat != null) hemostat.Locked = false;
            cutPoints.Clear();
            cutLine.positionCount = 0;
            bins = new bool[Mathf.CeilToInt(2f * HalfLength / BinSize)];
            deviationSum = 0f; deviationSamples = 0; offLineTravel = 0f;
            LetGo();
            angleA = angleB = 0f;
            SetEdge(edgeA, 0f); SetEdge(edgeB, 0f);
            clamped = false; nearMissLogged = false;
            stitch = 0; biteATime = -1f;
            foreach (var g in stitches.Concat(tails)) RuntimeMaterials.Remove(g);
            stitches.Clear(); tails.Clear();
            taskTimes.Clear(); taskErrors.Clear();
            sessionStart = Time.time;
            Begin(Task.Incision);
        }

        float HalfLength => Mathf.Abs(pad.InverseTransformPoint(targetEnd.position).x);

        void Begin(Task t)
        {
            if (t > Task.Incision) taskTimes[task] = Time.time - taskStart;
            task = t;
            taskStart = Time.time;
            if (!taskErrors.ContainsKey(t)) taskErrors[t] = 0;
            RefreshPanel();
        }

        void Complete(string code, string message)
        {
            DetectionLog.Write(EventClass.OnProtocol, code, message);
            var next = task + 1;
            if (next == Task.Review)
            {
                taskTimes[task] = Time.time - taskStart;
                task = Task.Review;
                AudioManager.Instance?.Play(SoundId.FB_StageComplete);
                RefreshPanel();
                return;
            }
            Begin(next);
        }

        void Error(string code, string message)
        {
            taskErrors[task] = (taskErrors.TryGetValue(task, out var n) ? n : 0) + 1;
            DetectionLog.Write(EventClass.Deviation, code, message);
            RefreshPanel();
        }

        // ─────────────────────────────── per frame ───────────────────────────────

        void Update()
        {
            switch (task)
            {
                case Task.Incision: TickIncision(); break;
                case Task.Retract: TickRetract(); break;
                case Task.Clamp: TickClamp(); break;
                case Task.Suture: TickSuture(); break;
                case Task.Cut: TickCut(); break;
            }
            TickEdges();
            TickBleed();
            TickMarker();
            if (task != Task.Review && statusText != null) statusText.text = $"{Time.time - taskStart:0}s · errors {Errors(task)}";
        }

        int Errors(Task t) => taskErrors.TryGetValue(t, out var n) ? n : 0;

        // 1 ─ Incision: the blade cuts where it touches the skin; accuracy is the distance from the dashed line.
        void TickIncision()
        {
            var p = pad.InverseTransformPoint(scalpelTip.position);
            bool contact = p.y <= skinTop + CutContactAbove && p.y >= skinTop - CutContactBelow &&
                           Mathf.Abs(p.x) <= HalfLength + 0.02f && Mathf.Abs(p.z) <= 0.07f;
            if (!contact) return;

            var onSkin = new Vector3(p.x, skinTop + 0.0006f, p.z);
            if (cutPoints.Count > 0 && (cutPoints[cutPoints.Count - 1] - onSkin).sqrMagnitude < 0.0015f * 0.0015f) return;
            float step = cutPoints.Count > 0 ? (cutPoints[cutPoints.Count - 1] - onSkin).magnitude : 0f;
            if (step > 0.03f) step = 0f; // a new stroke, not a jump
            cutPoints.Add(onSkin);
            cutLine.positionCount = cutPoints.Count;
            cutLine.SetPosition(cutPoints.Count - 1, onSkin);

            float dev = Mathf.Abs(p.z);
            deviationSum += dev;
            deviationSamples++;
            if (dev <= OnLine && Mathf.Abs(p.x) <= HalfLength)
            {
                int bin = Mathf.Clamp(Mathf.FloorToInt((p.x + HalfLength) / BinSize), 0, bins.Length - 1);
                bins[bin] = true;
            }
            else
            {
                offLineTravel += step;
                if (offLineTravel >= 0.01f)
                {
                    offLineTravel = 0f;
                    Error("skills.incision.offline", "Incision strayed off the marked line");
                }
            }

            if (Coverage() >= IncisionDoneCoverage)
            {
                angleA = angleB = RestOpenAngle; // the cut skin parts a little
                Complete("skills.incision.done", $"Incision complete, mean deviation {MeanDeviationMm():0.0} mm");
            }
        }

        float Coverage() => bins == null || bins.Length == 0 ? 0f : bins.Count(b => b) / (float)bins.Length;
        float MeanDeviationMm() => deviationSamples == 0 ? 0f : deviationSum / deviationSamples * 1000f;

        // 2 ─ Grasp and retract: forceps closed on an edge (or the retractor hooked behind it) lifts it; too far tears free.
        Transform holder;     // the tool tip holding the edge
        bool holderIsForceps;

        void TickRetract()
        {
            if (heldEdge == null)
            {
                foreach (var edge in new[] { edgeA, edgeB })
                {
                    if (forceps != null && forceps.IsClosed && NearEdge(forcepsTip.position, edge))
                    { heldEdge = edge; holder = forcepsTip; holderIsForceps = true; break; }
                    // The retractor's lip has to be hooked just under the edge.
                    if (retractorTip != null && NearEdge(retractorTip.position, edge) && pad.InverseTransformPoint(retractorTip.position).y < skinTop)
                    { heldEdge = edge; holder = retractorTip; holderIsForceps = false; break; }
                }
                return;
            }

            // Forceps hold while closed; the retractor holds while its lip stays at the edge.
            bool still = holderIsForceps ? forceps.IsClosed : NearEdge(holder.position, heldEdge);
            if (!still) { LetGo(); return; }

            // Angle that brings the edge's free side up to the tool tip, about its hinge on the outer side.
            var hinge = pad.InverseTransformPoint(heldEdge.position);
            var tip = pad.InverseTransformPoint(holder.position);
            float run = Mathf.Max(0.005f, Mathf.Abs(hinge.z - tip.z));
            float angle = Mathf.Atan2(tip.y - hinge.y, run) * Mathf.Rad2Deg;
            if (angle > TearAngle)
            {
                LetGo();
                Error("skills.retract.force", "Too much force: the tissue edge tore free");
                return;
            }
            angle = Mathf.Clamp(angle, RestOpenAngle, MaxLiftAngle);
            if (heldEdge == edgeA) angleA = angle; else angleB = angle;
            heldFor = angle >= HoldAngle ? heldFor + Time.deltaTime : 0f;
            if (heldFor >= HoldSeconds)
            {
                LetGo();
                Complete("skills.retract.done", "Wound edge grasped and retracted");
            }
        }

        void LetGo()
        {
            heldEdge = null;
            holder = null;
            heldFor = 0f;
        }

        bool NearEdge(Vector3 world, Transform edge)
        {
            // The free side of the edge runs along the incision line, at its current lift.
            var p = pad.InverseTransformPoint(world);
            float sign = edge == edgeA ? 1f : -1f;
            float angle = (edge == edgeA ? angleA : angleB) * Mathf.Deg2Rad;
            var hinge = pad.InverseTransformPoint(edge.position);
            float half = Mathf.Abs(hinge.z);
            var free = new Vector3(p.x, hinge.y + Mathf.Sin(angle) * half, hinge.z - sign * Mathf.Cos(angle) * half);
            return Mathf.Abs(p.x) <= HalfLength && (new Vector2(p.y, p.z) - new Vector2(free.y, free.z)).magnitude <= GrabRadius;
        }

        void TickEdges()
        {
            // Released edges settle back to slightly open (cut) or shut (not yet cut).
            float rest = task > Task.Incision ? RestOpenAngle : 0f;
            if (heldEdge != edgeA) angleA = Mathf.MoveTowards(angleA, rest, 60f * Time.deltaTime);
            if (heldEdge != edgeB) angleB = Mathf.MoveTowards(angleB, rest, 60f * Time.deltaTime);
            SetEdge(edgeA, angleA);
            SetEdge(edgeB, -angleB);
        }

        static void SetEdge(Transform edge, float angle)
        {
            if (edge != null) edge.localRotation = Quaternion.Euler(angle, 0, 0);
        }

        // 3 ─ Clamp: locked haemostat at the clamp point stops the bleeding.
        void TickClamp()
        {
            if (ratchet == null || hemostatTip == null || clampPoint == null) return;
            float d = (hemostatTip.position - clampPoint.position).magnitude;
            if (!ratchet.IsLocked) { nearMissLogged = false; return; }
            if (d <= ClampRadius)
            {
                clamped = true;
                Complete("skills.clamp.done", "Haemostat locked across the vessel; bleeding controlled");
            }
            else if (d <= ClampNearMiss && !nearMissLogged)
            {
                nearMissLogged = true;
                Error("skills.clamp.offtarget", "Clamp locked beside the vessel, not across it");
            }
        }

        void TickBleed()
        {
            bool bleeding = !clamped && bleedPoint != null;
            if (drip.gameObject.activeSelf != bleeding) drip.gameObject.SetActive(bleeding);
            if (!bleeding) return;
            dripT = (dripT + Time.deltaTime * 1.6f) % 1f;
            drip.position = bleedPoint.position + Vector3.down * (dripT * dripT * 0.03f) + bleedPoint.right * 0.002f;
        }

        // 4 ─ Suture: needle tip through bite A, then bite B, within the window.
        void TickSuture()
        {
            if (needleTip == null || stitch >= 2) return;
            var a = bites[stitch, 0];
            var b = bites[stitch, 1];
            if (biteATime < 0f)
            {
                if ((needleTip.position - a.position).magnitude <= BiteRadius) biteATime = Time.time;
                return;
            }
            if (Time.time - biteATime > BiteWindow) { biteATime = -1f; Error("skills.suture.timeout", "Stitch not completed: the needle never came out at the second bite"); return; }
            if ((needleTip.position - b.position).magnitude > BiteRadius) return;

            AddStitch(a.position, b.position);
            stitch++;
            biteATime = -1f;
            if (stitch >= 2) Complete("skills.suture.done", "Two stitches placed");
            else DetectionLog.Write(EventClass.OnProtocol, "skills.suture.stitch", "Stitch placed");
        }

        void AddStitch(Vector3 a, Vector3 b)
        {
            var mid = (a + b) * 0.5f + pad.up * 0.0015f;
            var s = RuntimeMaterials.Primitive(PrimitiveType.Cylinder, new Color(0.36f, 0.24f, 0.55f), "Stitch");
            s.transform.SetParent(pad, true);
            s.transform.position = mid;
            s.transform.up = (b - a).normalized;
            s.transform.localScale = new Vector3(0.0012f, (b - a).magnitude * 0.5f, 0.0012f);
            stitches.Add(s);
            // Two tails rising from the knot at bite A, to be cut.
            for (int k = 0; k < 2; k++)
            {
                var t = RuntimeMaterials.Primitive(PrimitiveType.Cylinder, new Color(0.36f, 0.24f, 0.55f), "Tail");
                t.transform.SetParent(pad, true);
                var dir = (pad.up + pad.right * (k == 0 ? 0.6f : -0.6f)).normalized;
                t.transform.position = a + dir * 0.015f;
                t.transform.up = dir;
                t.transform.localScale = new Vector3(0.0011f, 0.015f, 0.0011f);
                tails.Add(t);
            }
        }

        // 5 ─ Cut: scissors closing near a tail cuts it.
        void TickCut()
        {
            if (scissors == null || scissorsTip == null) return;
            bool closed = scissors.IsClosed;
            if (closed && !scissorsWasClosed)
            {
                // The blades cut along their length, not just at the very tip.
                var tip = scissorsTip.position;
                var back = scissors.transform.position;
                var hit = tails.FirstOrDefault(t => t != null && DistanceToSegment(t.transform.position, back + (tip - back) * 0.6f, tip) <= TailRadius);
                if (hit != null)
                {
                    tails.Remove(hit);
                    RuntimeMaterials.Remove(hit);
                    AudioManager.Instance?.PlayAt(SoundId.INST_JawClose, tip);
                    if (tails.All(t => t == null)) Complete("skills.cut.done", "Suture tails cut");
                }
            }
            scissorsWasClosed = closed;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return (a + ab * t - p).magnitude;
        }

        // ─────────────────────────────── guidance + panel ───────────────────────────────

        Vector3? GuideTarget()
        {
            switch (task)
            {
                case Task.Incision:
                    for (int i = 0; i < bins.Length; i++)
                        if (!bins[i]) return pad.TransformPoint(new Vector3(-HalfLength + (i + 0.5f) * BinSize, skinTop + 0.004f, 0));
                    return null;
                case Task.Retract: return pad.TransformPoint(new Vector3(0, skinTop + 0.006f, 0.004f));
                case Task.Clamp: return clampPoint != null ? clampPoint.position + Vector3.up * 0.012f : (Vector3?)null;
                case Task.Suture: return stitch < 2 ? bites[stitch, biteATime < 0f ? 0 : 1].position + Vector3.up * 0.004f : (Vector3?)null;
                case Task.Cut:
                    var t = tails.FirstOrDefault(x => x != null);
                    return t != null ? t.transform.position + Vector3.up * 0.01f : (Vector3?)null;
                default: return null;
            }
        }

        void TickMarker()
        {
            var target = Guided ? GuideTarget() : null;
            marker.gameObject.SetActive(target.HasValue);
            if (!target.HasValue) return;
            marker.position = target.Value;
            marker.localScale = Vector3.one * (0.01f + 0.003f * Mathf.Sin(Time.time * 6f));
        }

        void RefreshPanel()
        {
            if (titleText == null) return;
            if (task == Task.Review)
            {
                stepText.text = "REVIEW";
                titleText.text = "Open surgery skills";
                int total = 0;
                var sb = new System.Text.StringBuilder();
                for (var t = Task.Incision; t < Task.Review; t++)
                {
                    int e = Errors(t);
                    total += e;
                    sb.AppendLine($"{Titles[(int)t],-20} {(taskTimes.TryGetValue(t, out var s) ? s : 0f),5:0}s   errors {e}");
                }
                sb.AppendLine();
                sb.AppendLine($"Incision: {Coverage() * 100f:0}% of the line, mean deviation {MeanDeviationMm():0.0} mm");
                sb.AppendLine($"Total {Time.time - sessionStart:0}s, {total} error{(total == 1 ? "" : "s")}.");
                sb.Append("Practice scores only: criteria to be set with surgical educators.");
                bodyText.text = sb.ToString();
                statusText.text = "";
                restartButton.gameObject.SetActive(true);
                return;
            }
            stepText.text = $"EXERCISE {(int)task + 1} OF 5";
            titleText.text = Titles[(int)task];
            bodyText.text = Instructions[(int)task];
            restartButton.gameObject.SetActive(false);
        }

        void BuildPanel()
        {
            var go = new GameObject("OpenSkills_Panel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(620, 430);
            rt.localScale = Vector3.one * 0.0006f;
            // Behind the pad, raised and tipped back towards the learner standing at −Z.
            rt.localPosition = new Vector3(0.0f, 0.36f, 0.3f);
            rt.localRotation = Quaternion.Euler(15f, 0, 0); // top tipped away, so the face looks up at the learner
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var bg = Panel(rt, "Background", UISprites.Rounded, new Color(0.06f, 0.11f, 0.12f, 0.96f), Vector2.zero, rt.sizeDelta);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.4f;
            stepText = Text(rt, 18, new Color(0.36f, 0.88f, 0.78f), new Vector2(0, 175), new Vector2(560, 30), FontStyles.Bold);
            titleText = Text(rt, 34, new Color(0.91f, 0.94f, 0.94f), new Vector2(0, 138), new Vector2(560, 44), FontStyles.Bold);
            bodyText = Text(rt, 22, new Color(0.75f, 0.81f, 0.81f), new Vector2(0, -10), new Vector2(560, 240), FontStyles.Normal);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.alignment = TextAlignmentOptions.TopLeft;
            statusText = Text(rt, 20, new Color(0.6f, 0.68f, 0.68f), new Vector2(0, -180), new Vector2(560, 30), FontStyles.Normal);

            var btn = Panel(rt, "Restart", UISprites.Rounded, new Color(0.36f, 0.88f, 0.78f), new Vector2(0, -175), new Vector2(220, 56));
            btn.type = Image.Type.Sliced;
            Text(btn.rectTransform, 22, new Color(0.04f, 0.1f, 0.1f), Vector2.zero, new Vector2(200, 50), FontStyles.Bold).text = "Practise again";
            restartButton = btn.gameObject.AddComponent<Button>();
            restartButton.onClick.AddListener(Restart);

            canvas.worldCamera = Camera.main;
        }

        static Image Panel(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Text(RectTransform parent, float size, Color color, Vector2 pos, Vector2 box, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }
    }
}
