using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using SurgicalFoundations.Detection;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Scenario;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.Procedures
{
    /// <summary>
    /// Runs a VR procedure lab in the operating theatre (scene 15_Stage_Procedure, loaded on top of 10_OR_Base).
    /// It reads the lesson's procedure (Resources/Procedures/&lt;simulationId&gt;.json), lays the SimReady equipment out on
    /// an instrument trolley beside the learner and on the floor around the table, marks the targets on the patient, and
    /// walks the learner through the steps, checking each one from what their hands do:
    ///   pick (take the right instrument) · apply (hold it on the right spot on the patient) · operate (open or close a
    ///   device's moving part) · choose (a clinical decision) · talk (answer the patient) · confirm (a step that is not
    ///   simulated in detail, ticked off).
    /// Guided mode shows the coach's hint and highlights the target; Assessment shows neither. Wrong instruments, wrong
    /// sites and wrong answers cost points; unsafe choices fail the lab. The result is recorded against the lesson.
    /// </summary>
    public class ProcedureRunner : MonoBehaviour
    {
        [Tooltip("Editor testing: run this procedure when no course lesson asked for one.")]
        [SerializeField] string fallbackProcedure = "open-appendicectomy";

        ProcedureDefinition def;
        readonly Dictionary<string, GameObject> items = new Dictionary<string, GameObject>();
        readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>();
        readonly Dictionary<string, AnchorSpec> anchorSpecs = new Dictionary<string, AnchorSpec>();
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly Dictionary<string, CablePlug> plugs = new Dictionary<string, CablePlug>();
        readonly Dictionary<string, CableSocket> sockets = new Dictionary<string, CableSocket>();
        Transform patient, umbilicus;
        Vector3 cranial, right, anterior;

        int stepIndex = -1;
        StepSpec step;
        string heldKey;
        float holdTime, startTime;
        int errors, hints;
        bool critical, finished, hintShown;
        readonly List<string> issues = new List<string>();

        // UI
        RectTransform panel;
        TextMeshProUGUI titleText, stepText, hintText, feedbackText, statusText;
        RectTransform choiceArea;
        GameObject marker;

        bool Guided => SessionManager.Instance == null || SessionManager.Instance.Settings.mode == TrainingMode.Guided;

        IEnumerator Start()
        {
            while (SceneLoader.Instance != null && SceneLoader.Instance.IsBusy) yield return null;
            yield return Begin();
        }

        /// <summary>Clears everything this lab spawned and starts it again from the first step.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear(); items.Clear(); anchors.Clear(); anchorSpecs.Clear(); issues.Clear(); plugs.Clear(); sockets.Clear();
            foreach (Transform c in transform) Destroy(c.gameObject);
            marker = null; panel = null; umbilicus = null; patient = null;
            stepIndex = -1; step = null; heldKey = null; errors = hints = 0; critical = finished = false;
            SessionManager.Instance?.BeginSession();
            StartCoroutine(Begin());
        }

        IEnumerator Begin()
        {
            yield return null;
            var id = CourseSession.Lesson != null && !string.IsNullOrEmpty(CourseSession.Lesson.simulationId) ? CourseSession.Lesson.simulationId : fallbackProcedure;
            def = Load(id);
            if (def == null) { Debug.LogError("[Procedure] No procedure definition for " + id); yield break; }
            Speech.Warm();
            FindPatient();
            LayOut();
            SpawnCables();
            BuildPanel();
            startTime = Time.time;
            EventLogger.Instance?.Log(EventClass.Info, "procedure.start", def.title);
            Next();
        }

        public static ProcedureDefinition Load(string id)
        {
            var ta = Resources.Load<TextAsset>("Procedures/" + id);
            return ta == null ? null : JsonUtility.FromJson<ProcedureDefinition>(ta.text);
        }

        public static bool Exists(string id) => !string.IsNullOrEmpty(id) && Resources.Load<TextAsset>("Procedures/" + id) != null;

        void OnDestroy()
        {
            Speech.Stop();
            foreach (var g in spawned) if (g != null) Destroy(g);
        }

        // ─────────────────────────────── set-up ───────────────────────────────

        void FindPatient()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith("ANA_PatientBody")) patient = t;
                if (t.name == "Umbilicus" && umbilicus == null) umbilicus = t;
            }
            // The patient lies on the table along its X axis, head towards −X, face up; the patient's right is −Z.
            var basis = patient != null ? patient : transform;
            cranial = basis.TransformDirection(Vector3.left).normalized;
            right = basis.TransformDirection(Vector3.back).normalized;
            anterior = basis.TransformDirection(Vector3.up).normalized;
            if (umbilicus == null)
            {
                var go = new GameObject("Umbilicus (fallback)");
                go.transform.position = new Vector3(-0.02f, 1.16f, 0f);
                umbilicus = go.transform;
                spawned.Add(go);
            }
        }

        Vector3 OnPatient(float c, float r, float a) => umbilicus.position + cranial * c + right * r + anterior * a;

        Vector3 Floor(Vector3 p) => new Vector3(p.x, 0f, p.z);

        // ─────────────────────────────── tidy layout ───────────────────────────────
        // Fixed spots in the operating theatre (table at the origin, head −X, learner at (0, 0, −0.85) facing +Z), chosen
        // clear of the theatre's own furniture so a lab never scatters equipment across the floor:
        //   head — beside the anaesthesia end; foot — beyond the foot of the table; bay — the free slots in the equipment
        //   bay on the far wall; spare — along the near wall, behind the learner, if a lab needs even more.
        static readonly Vector3[] HeadSlots = { new Vector3(-2.45f, 0f, 1.75f), new Vector3(-1.55f, 0f, 2.2f) };
        static readonly Vector3[] FootSlots = { new Vector3(1.8f, 0f, 0.15f), new Vector3(1.8f, 0f, 1.0f) };
        static readonly Vector3[] BaySlots = { new Vector3(-0.15f, 0f, 3.1f), new Vector3(1.55f, 0f, 3.1f) };
        static readonly Vector3[] SpareSlots = { new Vector3(-0.9f, 0f, -3.0f), new Vector3(0.0f, 0f, -3.0f), new Vector3(0.9f, 0f, -3.0f) };
        static readonly Vector3 TrolleySpot = new Vector3(0.75f, 0f, -1.7f);

        /// <summary>The theatre's own copy of a device, if it has one: built-in props and the SimReady equipment bay.</summary>
        static readonly Dictionary<string, string> TheatreProps = new Dictionary<string, string>
        {
            { "monitor_patient_a01", "EQ_PatientMonitor" }, { "video_system_a01", "EQ_LapTower" }, { "IV_pole_a01", "EQ_IVStand" },
            { "anesthesia_machine_a01", "EQ_AnaesthesiaMachine" }, { "table_surgical_a01", "EQ_ORTable" },
        };

        GameObject FindInTheatre(string id)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name == "SR_" + id && t.parent != null && t.parent.name.StartsWith("--- SimReady Equipment")) return t.gameObject;
                if (TheatreProps.TryGetValue(id, out var prop) && t.name == prop) return t.gameObject;
            }
            return null;
        }

        /// <summary>Turns a device so its controls face <paramref name="dir"/>, squared to the room (multiples of 90°).</summary>
        static void FaceControls(GameObject go, Vector3 dir)
        {
            dir.y = 0f;
            var dev = go.GetComponentInChildren<ArticulatedDevice>();
            if (dev != null && dev.Joints.Count > 0 && dir.sqrMagnitude > 1e-4f)
            {
                var controls = dev.Joints.Where(j => j.spec.kind is "press" or "turn" or "slide" or "door" or "lever").Select(j => j.node.position).ToList();
                if (controls.Count > 0)
                {
                    var front = controls.Aggregate(Vector3.zero, (acc, c) => acc + c) / controls.Count - Bounds(go).center;
                    front.y = 0f;
                    if (front.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.FromToRotation(front.normalized, dir.normalized) * go.transform.rotation;
                }
            }
            go.transform.rotation = Quaternion.Euler(0f, Mathf.Round(go.transform.eulerAngles.y / 90f) * 90f, 0f);
        }

        /// <summary>Stands a device on the floor with its footprint centred on <paramref name="spot"/>.</summary>
        static void StandAt(GameObject go, Vector3 spot)
        {
            var b = Bounds(go);
            go.transform.position += new Vector3(spot.x - b.center.x, -b.min.y, spot.z - b.center.z);
        }

        void LayOut()
        {
            var lib = SimReadyLibrary.Instance;
            var head = new Queue<Vector3>(HeadSlots);
            var foot = new Queue<Vector3>(FootSlots);
            var bay = new Queue<Vector3>(BaySlots);
            var spare = new Queue<Vector3>(SpareSlots);
            Vector3 tableCentre = new Vector3(umbilicus.position.x, 0f, umbilicus.position.z);

            // Instrument trolley beside the learner, long side towards them; reuse the theatre's if it has one.
            var trolley = Spawn(lib?.Find("trolley_instrument_b01"), TrolleySpot, Quaternion.identity);
            if (trolley != null)
            {
                FaceControls(trolley, Vector3.forward);
                var tb0 = Bounds(trolley);
                if (tb0.size.z > tb0.size.x) trolley.transform.rotation *= Quaternion.Euler(0f, 90f, 0f); // long side to the learner
                StandAt(trolley, TrolleySpot);
                foreach (var rb in trolley.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            }
            var trayItems = new List<(EquipmentSpec spec, SimReadyLibrary.Entry entry)>();

            foreach (var e in def.equipment)
            {
                var key = string.IsNullOrEmpty(e.key) ? e.id : e.key;
                var entry = lib?.Find(e.id);
                if (e.place == "tray" || string.IsNullOrEmpty(e.place))
                {
                    if (entry != null) trayItems.Add((e, entry));
                    else Debug.LogWarning($"[Procedure] {def.id}: '{e.id}' is not in the SimReady library");
                    continue;
                }

                // Floor equipment: borrow the theatre's own device when it has one, so nothing appears twice.
                var go = FindInTheatre(e.id);
                bool borrowed = go != null;
                bool builtInProp = borrowed && TheatreProps.ContainsKey(e.id);
                if (!borrowed)
                {
                    if (entry == null) { Debug.LogWarning($"[Procedure] {def.id}: '{e.id}' is not in the SimReady library"); continue; }
                    go = Spawn(entry, Vector3.zero, Quaternion.identity);
                    if (go == null) continue;
                }
                if (!builtInProp) // built-in theatre props already have their place; everything else goes to a tidy slot
                {
                    var queue = e.place == "head" ? head : foot;
                    var spot = queue.Count > 0 ? queue.Dequeue() : bay.Count > 0 ? bay.Dequeue() : spare.Count > 0 ? spare.Dequeue() : TrolleySpot + Vector3.right * 1.2f;
                    FaceControls(go, tableCentre - spot);
                    StandAt(go, spot);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; rb.useGravity = false; }
                }
                items[key] = go;
            }

            LayTray(trolley, trayItems);
            foreach (var a in def.anchors)
            {
                var t = new GameObject("Target_" + a.name).transform;
                t.position = OnPatient(a.cranial, a.right, a.anterior);
                t.SetParent(transform, true);
                anchors[a.name] = t;
                anchorSpecs[a.name] = a;
            }
        }

        /// <summary>
        /// The hand-held items on the trolley top in neat rows: instruments first (laid parallel, long axis across the
        /// trolley), then consumables and devices; each row filled left to right with even gaps, rows front to back.
        /// </summary>
        void LayTray(GameObject trolley, List<(EquipmentSpec spec, SimReadyLibrary.Entry entry)> trayItems)
        {
            if (trayItems.Count == 0) return;
            Bounds top;
            if (trolley != null)
            {
                var tb = Bounds(trolley);
                top = new Bounds(new Vector3(tb.center.x, tb.max.y, tb.center.z), new Vector3(tb.size.x - 0.06f, 0f, tb.size.z - 0.06f));
            }
            else top = new Bounds(TrolleySpot + Vector3.up * 0.9f, new Vector3(0.8f, 0f, 0.45f));

            var ordered = trayItems.OrderBy(t => t.entry.category == "Instruments" ? 0 : t.entry.category == "Consumables" ? 1 : 2)
                                   .ThenByDescending(t => Mathf.Max(t.entry.size.x, t.entry.size.z)).ToList();
            const float gap = 0.035f;
            float x = top.min.x, z = top.max.z, rowDepth = 0f; // start at the back-left; rows come towards the learner
            foreach (var (spec, entry) in ordered)
            {
                var go = Spawn(entry, Vector3.zero, Quaternion.identity);
                if (go == null) continue;
                var b = Bounds(go);
                if (b.size.z > b.size.x) { go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); b = Bounds(go); } // long axis across
                if (x + b.size.x > top.max.x && x > top.min.x) { x = top.min.x; z -= rowDepth + gap; rowDepth = 0f; }
                var spot = new Vector3(x + b.size.x / 2f, top.max.y + 0.003f, z - b.size.z / 2f);
                go.transform.position += new Vector3(spot.x - b.center.x, spot.y - b.min.y, spot.z - b.center.z);
                x += b.size.x + gap;
                rowDepth = Mathf.Max(rowDepth, b.size.z);
                Register(go, string.IsNullOrEmpty(spec.key) ? spec.id : spec.key);
            }
        }

        /// <summary>A hand-held item: kinematic on its spot, picked up by hand, returned to its spot when let go.</summary>
        void Register(GameObject go, string key)
        {
            items[key] = go;
            if (!go.TryGetComponent<XRGrabInteractable>(out var grab)) return;
            if (go.TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = true; rb.useGravity = false; }
            grab.throwOnDetach = false;
            go.AddComponent<ReturnToRest>(); // records this spot now; the item comes back here when let go
            grab.selectEntered.AddListener(_ => OnPicked(key));
            grab.selectExited.AddListener(_ => { if (heldKey == key) heldKey = null; });
        }

        // ─────────────────────────────── cables ───────────────────────────────

        Vector3 LearnerPoint => LearnerRig.Head != null ? LearnerRig.Head.position : OnPatient(0f, 0.85f, 0.45f);

        /// <summary>A point on the face of a device that looks towards the learner: height and sideways offset as fractions.</summary>
        (Vector3 point, Vector3 outward) DeviceFace(GameObject device, float height, float side)
        {
            var b = Bounds(device);
            var toLearner = Vector3.ProjectOnPlane(LearnerPoint - b.center, Vector3.up);
            if (toLearner.sqrMagnitude < 1e-4f) toLearner = -right;
            toLearner.Normalize();
            // Distance from the centre to the bounds face along that direction.
            float reach = Mathf.Abs(toLearner.x) * b.extents.x + Mathf.Abs(toLearner.z) * b.extents.z;
            var across = Vector3.Cross(Vector3.up, toLearner);
            float width = Mathf.Abs(across.x) * b.extents.x + Mathf.Abs(across.z) * b.extents.z;
            var p = b.center + toLearner * (reach + 0.005f) + across * (side * width);
            p.y = b.min.y + Mathf.Clamp01(height) * b.size.y;
            return (p, toLearner);
        }

        void SpawnCables()
        {
            foreach (var c in def.cables)
            {
                if (!items.TryGetValue(c.from ?? "", out var fromDev)) { Debug.LogWarning($"[Procedure] {def.id}: cable {c.key} starts at missing {c.from}"); continue; }
                var (start, outward) = DeviceFace(fromDev, c.fromHeight, c.fromSide);
                var originGo = new GameObject("CableOrigin_" + c.key);
                originGo.transform.position = start;
                originGo.transform.SetParent(fromDev.transform, true);

                // Socket: on the target device, or on the patient.
                CableSocket socket = null;
                if (!string.IsNullOrEmpty(c.toAnchor) && anchors.TryGetValue(c.toAnchor, out var a))
                    socket = CableSocket.Create(c.key, c.label, a.position, anterior, transform);
                else if (!string.IsNullOrEmpty(c.to) && items.TryGetValue(c.to, out var toDev))
                {
                    var (sp, sOut) = DeviceFace(toDev, c.toHeight, c.toSide);
                    socket = CableSocket.Create(c.key, c.label, sp, sOut, toDev.transform);
                }
                if (socket == null) { Debug.LogWarning($"[Procedure] {def.id}: cable {c.key} has nowhere to plug in"); continue; }
                sockets[c.key] = socket;
                spawned.Add(socket.gameObject);

                // The plug hangs just in front of the device it comes from, within easy reach.
                var coil = start + outward * 0.14f + Vector3.down * 0.12f;
                coil.y = Mathf.Max(coil.y, 0.75f);
                ColorUtility.TryParseHtmlString(c.color, out var col);
                var plug = CablePlug.Create(c.key, c.label, originGo.transform, coil, col, c.length, transform);
                plug.PluggedIn += OnPlugged;
                plugs[c.key] = plug;
                spawned.Add(plug.gameObject);
            }
        }

        void OnPlugged(CablePlug plug, CableSocket socket)
        {
            if (finished || step == null) return;
            AudioManager.Instance?.Play(SoundId.UI_Click);
            if (step.type == "plug" && plug.key == step.item) Complete($"{plug.label} connected.");
        }

        GameObject Spawn(SimReadyLibrary.Entry e, Vector3 pos, Quaternion rot)
        {
            if (e == null || e.prefab == null) return null;
            var go = Instantiate(e.prefab, pos, rot);
            go.name = e.prefab.name;
            spawned.Add(go);
            return go;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ─────────────────────────────── steps ───────────────────────────────

        void Next()
        {
            stepIndex++;
            holdTime = 0f;
            hintShown = false;
            if (stepIndex >= def.steps.Count) { Finish(); return; }
            step = def.steps[stepIndex];
            feedbackText.text = "";
            if (step.type == "talk")
            {
                AudioManager.Instance?.Say(step.prompt);
                Speech.Say(step.prompt, 0.95f);
            }
            if (Guided) ShowHint(free: true);
            foreach (var kv in sockets) kv.Value.Guide = Guided && step.type == "plug" && kv.Key == step.item;
            Refresh();
            PlaceMarker();
            if (step.type == "plug" && plugs.TryGetValue(step.item ?? "", out var already) && already.Plugged != null) Complete();
            else if (step.type == "plug" && !plugs.ContainsKey(step.item ?? "")) Complete(); // cable not available on this build
        }

        void Complete(string message = null)
        {
            AudioManager.Instance?.Play(SoundId.UI_Confirm);
            EventLogger.Instance?.Log(EventClass.OnProtocol, "procedure.step", $"{def.id}: {step.title}");
            if (!string.IsNullOrEmpty(message)) feedbackText.text = message;
            StartCoroutine(AdvanceSoon());
        }

        IEnumerator AdvanceSoon()
        {
            var s = step;
            yield return new WaitForSeconds(0.8f);
            if (s == step && !finished) Next();
        }

        void Error(string what, bool isCritical = false)
        {
            errors++;
            issues.Add(what);
            if (isCritical) critical = true;
            feedbackText.text = "<color=#F0706A>" + what + "</color>";
            AudioManager.Instance?.Play(SoundId.UI_Denied);
            EventLogger.Instance?.Log(isCritical ? EventClass.Deviation : EventClass.Delayed, "procedure.error", $"{def.id}: {what}");
        }

        void OnPicked(string key)
        {
            heldKey = key;
            if (finished || step == null) return;
            if (step.type == "pick")
            {
                if (key == step.item) Complete("Correct instrument.");
                else if (step.wrongItems.Contains(key)) Error($"Not the {Readable(key)}: {step.title}.");
            }
        }

        void Update()
        {
            if (finished || step == null) return;
            statusText.text = $"Step {stepIndex + 1} of {def.steps.Count} · {SessionManager.FormatClock(Time.time - startTime)} · errors {errors}";
            switch (step.type)
            {
                case "apply": CheckApply(); break;
                case "operate": CheckOperate(); break;
            }
        }

        void CheckApply()
        {
            if (!anchors.TryGetValue(step.target, out var target)) { Complete(); return; }
            var spec = anchorSpecs[step.target];
            Vector3? tip = null;
            if (!string.IsNullOrEmpty(step.item))
            {
                if (heldKey != step.item || !items.TryGetValue(step.item, out var go)) { holdTime = 0f; return; }
                var b = Bounds(go);
                tip = b.ClosestPoint(target.position);
                // Wrong site: the right instrument held on a different target.
                foreach (var kv in anchors)
                {
                    if (kv.Key == step.target) continue;
                    if (Vector3.Distance(b.ClosestPoint(kv.Value.position), kv.Value.position) < anchorSpecs[kv.Key].radius * 0.6f
                        && Vector3.Distance(tip.Value, target.position) > spec.radius)
                    {
                        if (holdTime > -0.5f) { Error($"Wrong site: that is the {anchorSpecs[kv.Key].label}.", step.critical); holdTime = -3f; }
                        return;
                    }
                }
            }
            else
            {
                foreach (var left in new[] { true, false })
                {
                    var h = LearnerRig.Hand(left);
                    if (h != null && (tip == null || Vector3.Distance(h.position, target.position) < Vector3.Distance(tip.Value, target.position))) tip = h.position;
                }
            }
            if (tip == null) return;
            if (Vector3.Distance(tip.Value, target.position) <= spec.radius)
            {
                holdTime = Mathf.Max(0f, holdTime) + Time.deltaTime;
                feedbackText.text = $"Hold… {Mathf.Clamp01(holdTime / Mathf.Max(0.1f, step.hold)) * 100f:0}%";
                if (holdTime >= step.hold) Complete();
            }
            else if (holdTime > 0f) holdTime = 0f;
            else holdTime = Mathf.Min(0f, holdTime + Time.deltaTime);
        }

        void CheckOperate()
        {
            if (!items.TryGetValue(step.item ?? "", out var go)) { Complete(); return; }
            var dev = go.GetComponentInChildren<ArticulatedDevice>();
            if (dev == null || dev.Joints.Count == 0) { Complete(); return; }
            foreach (var j in dev.Joints)
            {
                if (!string.IsNullOrEmpty(step.joint) && j.spec.name != step.joint) continue;
                if (j.Continuous) continue;
                float span = Mathf.Abs(j.Open - j.Rest);
                if (span < 1e-5f) continue;
                float openness = Mathf.Abs(j.value - j.Rest) / span;
                if (step.open ? openness >= 0.8f : openness <= 0.1f) { Complete(); return; }
            }
        }

        void Choose(ChoiceSpec c)
        {
            if (finished) return;
            if (step.type == "talk" && !string.IsNullOrEmpty(c.reply))
            {
                AudioManager.Instance?.Say(c.reply);
                Speech.Say(c.reply, 0.95f);
            }
            if (c.correct)
            {
                Complete(string.IsNullOrEmpty(c.reply) ? c.feedback : $"Patient: “{c.reply}”" + (string.IsNullOrEmpty(c.feedback) ? "" : "\n" + c.feedback));
            }
            else
            {
                Error(c.feedback ?? "Not the best choice.", c.critical);
                if (!string.IsNullOrEmpty(c.reply)) feedbackText.text += $"\nPatient: “{c.reply}”";
            }
        }

        void ShowHint(bool free)
        {
            if (hintShown || string.IsNullOrEmpty(step.hint)) return;
            hintShown = true;
            if (!free) hints++;
            hintText.text = step.hint;
        }

        void Finish()
        {
            finished = true;
            if (marker != null) marker.SetActive(false);
            foreach (var kv in sockets) kv.Value.Guide = false;
            float score = Mathf.Clamp(100f - errors * def.errorPenalty - hints * def.hintPenalty, 0f, 100f);
            float pass = CourseSession.Lesson != null ? CourseSession.Lesson.passScore : 70f;
            bool passed = score >= pass && !critical;
            CourseSession.Complete(score, passed);
            EventLogger.Instance?.Log(EventClass.Info, "procedure.complete", $"{def.id}: {score:0} ({(passed ? "pass" : "not yet")})");
            AudioManager.Instance?.Play(passed ? SoundId.FB_StageComplete : SoundId.UI_Denied);

            stepText.text = passed ? "<color=#5CE0C8>Procedure complete</color>" : critical ? "<color=#F0706A>Stopped: unsafe step</color>" : "<color=#F5B54B>Not yet</color>";
            hintText.text = $"Score {score:0}% (pass mark {pass:0}%) · {errors} error(s) · {hints} hint(s) · {SessionManager.FormatClock(Time.time - startTime)}";
            feedbackText.text = issues.Count == 0 ? "No errors. Well done." : "To work on:\n• " + string.Join("\n• ", issues.Distinct().Take(4));
            Clear(choiceArea);
            Button(choiceArea, "Try again", new Vector2(-170, 0), new Vector2(300, 64), Restart);
            Button(choiceArea, "Back to courses", new Vector2(170, 0), new Vector2(300, 64), () =>
            {
                SessionManager.Instance?.EndSession();
                SceneLoader.Instance?.LoadFrontend(SceneIds.Lobby);
            });
        }

        // ─────────────────────────────── UI ───────────────────────────────

        static string Readable(string key) => SimReadyLibrary.Readable(key);

        void PlaceMarker()
        {
            if (marker == null)
            {
                marker = new GameObject("TargetMarker", typeof(RectTransform));
                var c = marker.AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)marker.transform;
                rt.sizeDelta = new Vector2(100, 100);
                var ring = new GameObject("Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ring.transform.SetParent(marker.transform, false);
                ((RectTransform)ring.transform).sizeDelta = new Vector2(100, 100);
                var img = ring.GetComponent<Image>();
                img.sprite = UI.UISprites.Ring(0.82f);
                img.color = new Color(0.36f, 0.88f, 0.78f, 0.9f);
                img.raycastTarget = false;
                spawned.Add(marker);
            }
            bool show = Guided && step.type == "apply" && anchors.ContainsKey(step.target ?? "");
            marker.SetActive(show);
            if (!show) return;
            var spec = anchorSpecs[step.target];
            marker.transform.position = anchors[step.target].position + anterior * 0.004f;
            marker.transform.rotation = Quaternion.LookRotation(-anterior, cranial);
            marker.transform.localScale = Vector3.one * (spec.radius * 2f / 100f);
        }

        void Refresh()
        {
            stepText.text = $"<b>{step.title}</b>";
            if (!hintShown) hintText.text = Guided ? "" : "<color=#8A9097>Assessment: no hints.</color>";
            Clear(choiceArea);
            switch (step.type)
            {
                case "choose":
                case "talk":
                    var prompt = step.type == "talk" ? $"<i>{def.patientName.Split(' ').Last()}:</i> “{step.prompt}”" : step.prompt;
                    stepText.text += "\n" + prompt;
                    // Stable shuffle so the right answer is not always first.
                    var opts = step.choices.OrderBy(c => (c.text.GetHashCode() ^ step.id.GetHashCode()) & 0x7fffffff).ToList();
                    for (int i = 0; i < opts.Count; i++)
                    {
                        var c = opts[i];
                        Button(choiceArea, c.text, new Vector2(0, 110 - i * 74), new Vector2(980, 66), () => Choose(c), left: true);
                    }
                    break;
                case "confirm":
                    Button(choiceArea, "Done", new Vector2(0, 0), new Vector2(300, 64), () => Complete());
                    break;
            }
        }

        void BuildPanel()
        {
            // Across the table from the learner, above the patient's far side: fixed in the room.
            var go = new GameObject("ProcedurePanel", typeof(RectTransform));
            spawned.Add(go);
            panel = (RectTransform)go.transform;
            panel.sizeDelta = new Vector2(1100, 760);
            panel.localScale = Vector3.one * 0.0011f;
            var head = LearnerRig.Head;
            var basePos = OnPatient(0.25f, -0.75f, 0f);
            panel.position = new Vector3(basePos.x, 1.62f, basePos.z);
            var look = head != null ? panel.position - head.position : right;
            look.y = 0f;
            panel.rotation = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : right, Vector3.up);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var bg = Img(panel, UI.UISprites.Rounded, new Color(0.09f, 0.094f, 0.11f, 0.95f), Vector2.zero, panel.sizeDelta);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f; // 24 px corners: restrained, not bubbly

            titleText = Text(panel, $"{def.title}", 34, new Vector2(0, 330), new Vector2(1020, 46), FontStyles.Bold);
            statusText = Text(panel, "", 20, new Vector2(0, 290), new Vector2(1020, 30), FontStyles.Normal, new Color(0.54f, 0.56f, 0.59f));
            stepText = Text(panel, "", 28, new Vector2(0, 205), new Vector2(1020, 130), FontStyles.Normal);
            hintText = Text(panel, "", 22, new Vector2(0, 112), new Vector2(1020, 70), FontStyles.Italic, new Color(0.36f, 0.88f, 0.78f));
            feedbackText = Text(panel, "", 22, new Vector2(0, -330), new Vector2(1020, 80), FontStyles.Normal, new Color(0.96f, 0.71f, 0.29f));
            var area = new GameObject("Choices", typeof(RectTransform));
            choiceArea = (RectTransform)area.transform;
            choiceArea.SetParent(panel, false);
            choiceArea.anchoredPosition = new Vector2(0, -120);
            choiceArea.sizeDelta = new Vector2(1020, 300);
            if (!Guided) return;
            Button(panel, "Hint", new Vector2(470, 330), new Vector2(120, 50), () => ShowHint(free: false));
        }

        static void Clear(RectTransform rt)
        {
            foreach (Transform c in rt) Destroy(c.gameObject);
        }

        static Image Img(RectTransform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Text(RectTransform parent, string s, float size, Vector2 pos, Vector2 box, FontStyles style, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = s;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? new Color(0.84f, 0.85f, 0.87f);
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        static void Button(RectTransform parent, string label, Vector2 pos, Vector2 size, System.Action onClick, bool left = false)
        {
            var img = Img(parent, UI.UISprites.Rounded, new Color(0.13f, 0.14f, 0.165f), pos, size);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2f; // 12 px corners on cards and buttons
            var b = img.gameObject.AddComponent<Button>();
            var c = b.colors;
            c.highlightedColor = new Color(0.75f, 0.95f, 0.9f);
            c.pressedColor = new Color(0.6f, 0.85f, 0.8f);
            c.fadeDuration = 0.06f;
            b.colors = c;
            b.onClick.AddListener(() => { AudioManager.Instance?.Play(SoundId.UI_Click); onClick(); });
            var t = Text(img.rectTransform, label, 24, Vector2.zero, size - new Vector2(40, 8), FontStyles.Normal);
            t.alignment = left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        }
    }
}
