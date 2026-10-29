using SurgicalFoundations.Contracts;
using SurgicalFoundations.Interaction;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Prep stage. Step 0: the six-step surgical scrub at the sink, detected from where the hands are and how they move.
    /// Step 1: gloving at the back table (taking the glove packet). Step 2: the opening count, confirming every
    /// instrument and swab on the back table by pointing at it, then confirming the sterile field. Sterility breaks
    /// are watched by <see cref="SterilityMonitor"/> from gloving onwards.
    /// </summary>
    public class PrepController : StageController
    {
        public const int ScrubStepIndex = 0, GloveStepIndex = 1, TrayStepIndex = 2;

        // Sensor tuning, in metres / metres per second.
        // Sized for tracked hands on a real headset: the hand pose sits ~10 cm off the palm and jitters a few cm.
        const float HandsTogetherDistance = 0.22f;
        const float RubSpeed = 0.08f;
        const float WaterZoneRadius = 0.32f;
        const float WaterZoneDepth = 0.6f;
        const float TowelZoneRadius = 0.35f;
        const float InspectDistance = 0.75f;
        const float InspectMinDistance = 0.12f;
        const float InspectAngle = 55f;
        /// <summary>How far off the pointing ray an item may be and still be the one pointed at.</summary>
        const float PointRadius = 0.07f;
        const float PointRange = 2.5f;

        // The lines of the opening count (screen 07) and the prefab each one counts.
        static readonly (string label, string prefab)[] TrayLines =
        {
            ("Trocar, 12 mm", "INST_Trocar12"), ("Trocar, 5 mm", "INST_Trocar5"), ("Laparoscope, 30°", "INST_Laparoscope30"),
            ("Grasper", "INST_AtraumaticGrasper"), ("Scissors", "INST_LapScissors"), ("Clip applier", "INST_ClipApplier"),
            ("Swabs", "INST_Swab")
        };

        class TrayItem
        {
            public Transform transform;
            public int line;
            public bool confirmed;
        }

        readonly List<TrayItem> trayItems = new List<TrayItem>();
        InputAction[] trigger;

        Transform tap;
        readonly List<Transform> taps = new List<Transform>();
        Transform towels;
        Vector3 lastOffset;
        float rubSmoothed;
        float glovedTime;

        public ScrubSequence Scrub { get; private set; }
        public TrayCheck Tray { get; private set; }
        public Vector3 WaterZoneCentre => tap != null ? tap.position + Vector3.down * (WaterZoneDepth * 0.5f) : Vector3.zero;
        public Vector3 TowelPosition => towels != null ? towels.position : Vector3.zero;

        public override Vector3? GuideTarget
        {
            get
            {
                if (Step == ScrubStepIndex && Scrub != null)
                {
                    switch ((ScrubStep)Scrub.Expected)
                    {
                        case ScrubStep.PreWash:
                        case ScrubStep.CleanNails:
                        case ScrubStep.Rinse: return tap != null ? WaterZoneCentre : (Vector3?)null;
                        case ScrubStep.Dry: return towels != null ? TowelPosition : (Vector3?)null;
                        default: return null; // inspecting and scrubbing happen in the learner's own hands
                    }
                }
                if (Step == GloveStepIndex)
                {
                    var state = SterilityMonitor.Instance != null ? SterilityMonitor.Instance.State : null;
                    var packet = SterilityMonitor.FindGlovePacket();
                    if (state != null && !state.Sterile && packet != null) return packet.transform.position;
                }
                if (Step == TrayStepIndex)
                {
                    var next = trayItems.FirstOrDefault(i => !i.confirmed && i.transform != null);
                    if (next != null) return next.transform.position;
                }
                return null;
            }
        }

        protected override void Setup()
        {
            // The scenes used to force a contaminated glove when the second panel appeared; real state now drives the hands.
            foreach (var cue in FindObjectsByType<HandLookCue>(FindObjectsInactive.Include))
                if (cue.gameObject.scene == gameObject.scene) Destroy(cue);

            // The sink has several spouts; the learner may use any of them, so the water zone follows the nearest one.
            taps.AddRange(gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>())
                .Where(t => t.name.StartsWith("WaterSocket_")));
            tap = taps.FirstOrDefault(t => t.name == "WaterSocket_C") ?? taps.FirstOrDefault();
            towels = Find("PROP_SterileTowels");
            if (towels == null) towels = CreateTowels();

            Scrub = new ScrubSequence(DetectionThresholds.Get(ThresholdKeys.ScrubMinStepSeconds, 15f), Log);
            Scrub.Changed += RaiseChanged;

            SetupTray();
        }

        void SetupTray()
        {
            var roots = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var lines = new List<(string, int)>();
            foreach (var (label, prefab) in TrayLines)
            {
                // Instances keep the prefab's name, sometimes with a " (1)" suffix.
                var found = roots.Where(t => t.name == prefab || t.name.StartsWith(prefab + " ")).ToList();
                if (found.Count == 0) continue;
                foreach (var t in found) trayItems.Add(new TrayItem { transform = t, line = lines.Count });
                lines.Add((label, found.Count));
            }
            if (lines.Count == 0) { Debug.LogWarning("[PrepController] Nothing on the back table to count."); return; }
            Tray = new TrayCheck(lines.ToArray(), Log);
            Tray.Changed += RaiseChanged;
            trigger = new[] { AccessController.TriggerAction("LeftHand"), AccessController.TriggerAction("RightHand") };
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (trigger != null) foreach (var a in trigger) a.Dispose();
        }

        void Update()
        {
            if (Sequence == null || Scrub == null) return;
            if (Step == ScrubStepIndex) TickScrub();
            else if (Step == GloveStepIndex) TickGloving();
            else if (Step == TrayStepIndex) TickTray();
        }

        // ───────────── Step 2: opening count ─────────────

        void TickTray()
        {
            if (Tray == null || trigger == null) return;
            for (int h = 0; h < 2; h++)
            {
                if (!trigger[h].WasPressedThisFrame()) continue;
                var hand = LearnerRig.Aim(h == 0); // the visible ray, not the grip pose
                if (hand != null) ConfirmAlongRay(hand.position, hand.forward);
            }
        }

        /// <summary>Confirms the uncounted item nearest to a pointing ray. False if the ray points at nothing countable.</summary>
        public bool ConfirmAlongRay(Vector3 origin, Vector3 direction)
        {
            TrayItem best = null;
            var bestOff = PointRadius;
            foreach (var item in trayItems)
            {
                if (item.confirmed || item.transform == null) continue;
                var to = item.transform.position - origin;
                var along = Vector3.Dot(to, direction);
                if (along < 0.05f || along > PointRange) continue;
                var off = (to - direction * along).magnitude;
                if (off > bestOff) continue;
                best = item;
                bestOff = off;
            }
            return best != null && Confirm(best);
        }

        /// <summary>Confirms one item on the back table. Public so input other than the trigger (and tests) can count.</summary>
        public bool ConfirmItem(Transform item)
        {
            var found = trayItems.FirstOrDefault(i => i.transform == item);
            return found != null && !found.confirmed && Confirm(found);
        }

        public IEnumerable<Transform> UncountedItems => trayItems.Where(i => !i.confirmed && i.transform != null).Select(i => i.transform);

        bool Confirm(TrayItem item)
        {
            if (Tray == null || Step != TrayStepIndex || !Tray.Confirm(item.line)) return false;
            item.confirmed = true;

            // A mint tick above the item, so the learner can see what has been counted.
            var tick = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Sphere, new Color(0.36f, 0.88f, 0.78f), "Counted_" + item.transform.name);
            tick.transform.SetParent(item.transform, false);
            tick.transform.position = item.transform.position + Vector3.up * 0.04f;
            var s = item.transform.lossyScale;
            tick.transform.localScale = new Vector3(0.014f / Mathf.Max(s.x, 1e-4f), 0.014f / Mathf.Max(s.y, 1e-4f), 0.014f / Mathf.Max(s.z, 1e-4f));
            return true;
        }

        void TickScrub()
        {
            var left = LearnerRig.Hand(true);
            var right = LearnerRig.Hand(false);
            var head = LearnerRig.Head;
            if (left == null || right == null || head == null) return;

            Scrub.Tick(Sample(left, right, head, Time.deltaTime), Time.deltaTime);
            if (Scrub.Complete) FinishScrub();
        }

        /// <summary>Turns hand poses into "what are the hands doing": the only place that knows about distances and angles.</summary>
        public ScrubSample Sample(Transform left, Transform right, Transform head, float dt)
        {
            if (taps.Count > 1)
            {
                var mid = (left.position + right.position) * 0.5f;
                tap = taps.OrderBy(t => (t.position - mid).sqrMagnitude).First();
            }
            var offset = right.position - left.position;
            // Rubbing = the hands moving relative to each other, whatever the body is doing.
            var relativeSpeed = dt > 0f ? (offset - lastOffset).magnitude / dt : 0f;
            lastOffset = offset;
            rubSmoothed = Mathf.Lerp(rubSmoothed, relativeSpeed, Mathf.Clamp01(dt * 8f));

            var tableTop = Core.SessionManager.Instance != null ? Core.SessionManager.Instance.Settings.tableHeightCm / 100f : 0.92f;
            return new ScrubSample
            {
                inWater = tap != null && InWater(left.position) && InWater(right.position),
                handsTogether = offset.magnitude <= HandsTogetherDistance,
                rubbing = rubSmoothed >= RubSpeed,
                fingertipsUp = left.forward.y > 0.35f && right.forward.y > 0.35f,
                atTowel = towels != null && Near(left.position, towels.position, TowelZoneRadius) && Near(right.position, towels.position, TowelZoneRadius),
                inspecting = InView(left.position, head) && InView(right.position, head),
                handsDropped = left.position.y < tableTop || right.position.y < tableTop
            };
        }

        bool InWater(Vector3 p)
        {
            var d = p - tap.position;
            return d.y <= 0.05f && d.y >= -WaterZoneDepth && new Vector2(d.x, d.z).magnitude <= WaterZoneRadius;
        }

        static bool Near(Vector3 a, Vector3 b, float radius) => (a - b).sqrMagnitude <= radius * radius;

        static bool InView(Vector3 hand, Transform head)
        {
            var to = hand - head.position;
            // An untracked controller reports the head's own position; a real hand is never closer than this.
            return to.magnitude >= InspectMinDistance && to.magnitude <= InspectDistance && Vector3.Angle(head.forward, to) <= InspectAngle && hand.y > head.position.y - 0.6f;
        }

        void FinishScrub()
        {
            SterilityMonitor.Instance?.State.MarkScrubbed();
            Sequence.Next();
        }

        void TickGloving()
        {
            var monitor = SterilityMonitor.Instance;
            if (monitor == null) return;
            var state = monitor.State;

            if (!state.Sterile && !state.AnyContaminated)
            {
                var packet = SterilityMonitor.FindGlovePacket();
                if (packet != null && packet.isSelected) state.Glove();
            }

            // Move on once gloved and still sterile a moment later (a break at this point shows the recovery card instead).
            glovedTime = state.Sterile ? glovedTime + Time.deltaTime : 0f;
            if (glovedTime >= 1.5f) { glovedTime = 0f; Sequence.Next(); }
        }

        public override void SkipStep()
        {
            if (Sequence == null) return;
            if (Step == ScrubStepIndex)
            {
                Scrub.SkipRemaining();
                FinishScrub();
            }
            else if (Step == GloveStepIndex)
            {
                var state = SterilityMonitor.Instance != null ? SterilityMonitor.Instance.State : null;
                if (state != null && !state.Sterile)
                {
                    Log(EventClass.Deviation, EventCodes.GlovingSkipped,
                        state.AnyContaminated ? "Continued without changing a contaminated glove" : "Continued without gloving");
                    state.StartGloved();
                }
                Sequence.Next();
            }
            else
            {
                // Confirming the sterile field: any line of the opening count still unchecked is logged.
                Tray?.SkipRemaining();
                Log(EventClass.OnProtocol, EventCodes.FieldConfirmed, "Sterile field confirmed");
                Sequence.Next();
            }
        }

        /// <summary>A stack of sterile towels beside the sink, for scenes built before it was added to the scene builder.</summary>
        Transform CreateTowels()
        {
            if (tap == null) return null;
            var go = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Cube, new Color(0.35f, 0.62f, 0.78f), "PROP_SterileTowels");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            // To the learner's right of the tap, at chest height, clear of the splash zone.
            go.transform.position = tap.position + new Vector3(-0.55f, -0.1f, 0.25f);
            go.transform.localScale = new Vector3(0.24f, 0.08f, 0.18f);
            return go.transform;
        }
    }
}
