using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Access stage. Step 0: mark the three port sites (point at the abdomen, pull the trigger); each mark is measured
    /// against its landmark. Step 1: insert a trocar at each site; angle, depth through the wall layers and a force
    /// estimate are judged live. Going too deep takes the vessel-injury branch (step 2): draw the trocar back, hold
    /// pressure on the site, watch it stay dry. Then entry carries on with the remaining ports.
    /// </summary>
    public class AccessController : StageController
    {
        public const int PortsStepIndex = 0, EntryStepIndex = 1, BranchStepIndex = 2, EndIndex = 3;

        /// <summary>How close (on the skin) a trocar tip must be to a port site to count as entering there.</summary>
        const float EntryRadius = 0.05f;
        const float MarkRayLength = 1.2f;
        /// <summary>A hand this close to the bleeding site is pressing on it.</summary>
        const float PressRadius = 0.07f;
        /// <summary>Looking within this angle of the site counts as watching it.</summary>
        const float WatchAngle = 25f;

        static readonly (string target, string name)[] SiteNames =
        {
            ("Target_Camera_Umbilical", "Camera"), ("Target_LeftWorking", "Left working"), ("Target_RightWorking", "Right working")
        };

        readonly List<Transform> trocars = new List<Transform>();
        readonly Dictionary<Transform, Transform> tips = new Dictionary<Transform, Transform>();
        readonly List<GameObject> markVisuals = new List<GameObject>();
        readonly TrocarLimits limits = new TrocarLimits();
        InputAction[] trigger;
        Transform markersRoot;
        Vector3 cavityCentre;
        float skinY;
        int portIndex;
        float allOnTargetTime;
        Transform bleedTip;

        public PortSitePlan Plan { get; private set; }
        public TrocarEntry Entry { get; private set; }
        public int PortIndex => portIndex;
        public int PortCount => Plan != null ? Plan.Sites.Count : 0;
        public TrocarLimits Limits => limits;
        /// <summary>The vessel injury being managed (null until an unsafe entry happens).</summary>
        public BleedControl Bleed { get; private set; }
        public Vector3 BleedPoint { get; private set; }

        public override Vector3? GuideTarget
        {
            get
            {
                if (Plan == null) return null;
                if (Step == PortsStepIndex)
                {
                    foreach (var site in Plan.Sites)
                        if (!site.marked || !site.onTarget) return site.target;
                }
                else if (Step == EntryStepIndex && portIndex < Plan.Sites.Count)
                {
                    var site = Plan.Sites[portIndex];
                    return site.marked ? site.mark : site.target;
                }
                else if (Step == BranchStepIndex && Bleed != null && !Bleed.Controlled) return BleedPoint;
                return null;
            }
        }

        protected override void Setup()
        {
            var targets = new List<(string, Vector3)>();
            foreach (var (target, name) in SiteNames)
            {
                var t = Find(target);
                if (t != null) targets.Add((name, t.position));
            }
            if (targets.Count == 0) { Debug.LogWarning("[AccessController] No port-site targets (PROP_PortSiteMarkers) in the scene."); return; }

            skinY = targets[0].Item2.y;
            // Working ports aim at the middle of the operative field under the umbilicus; the camera port goes straight in.
            cavityCentre = targets[0].Item2 + Vector3.down * 0.16f;
            Plan = new PortSitePlan(targets, DetectionThresholds.Get(ThresholdKeys.PortTargetRadiusCm, 2f) / 100f, Log);
            Plan.Changed += RaiseChanged;

            limits.angleToleranceDeg = DetectionThresholds.Get(ThresholdKeys.TrocarAngleToleranceDeg, limits.angleToleranceDeg);
            limits.maxForceN = DetectionThresholds.Get(ThresholdKeys.TrocarMaxForceN, limits.maxForceN);
            limits.entryDepth = DetectionThresholds.Get(ThresholdKeys.TrocarEntryDepthMm, limits.entryDepth * 1000f) / 1000f;
            limits.safeDepth = DetectionThresholds.Get(ThresholdKeys.TrocarSafeDepthMm, limits.safeDepth * 1000f) / 1000f;
            limits.unsafeDepth = DetectionThresholds.Get(ThresholdKeys.TrocarUnsafeDepthMm, limits.unsafeDepth * 1000f) / 1000f;

            // Landmark rings are a Guided-mode aid only (FR-19): in Assessment the learner finds the sites unaided.
            markersRoot = FindWhere(t => t.name.StartsWith("PROP_PortSiteMarkers"));
            if (markersRoot != null)
                foreach (var marker in markersRoot.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Marker_")))
                    marker.gameObject.SetActive(Guided);

            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var trocar in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("INST_Trocar")))
            {
                var tip = trocar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pivot_Tip");
                if (tip == null) continue;
                trocars.Add(trocar);
                tips[trocar] = tip;
            }

            trigger = new[] { TriggerAction("LeftHand"), TriggerAction("RightHand") };
        }

        internal static InputAction TriggerAction(string hand)
        {
            var action = new InputAction("Mark " + hand, InputActionType.Button);
            action.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
            action.AddBinding($"<XRController>{{{hand}}}/{{TriggerButton}}");
            action.Enable();
            return action;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (trigger != null) foreach (var a in trigger) a.Dispose();
        }

        void Update()
        {
            if (Sequence == null || Plan == null) return;
            if (Step == PortsStepIndex) TickMarking();
            else if (Step == EntryStepIndex) TickEntry();
            else if (Step == BranchStepIndex) TickBleed();
        }

        // ───────────── Step 0: port sites ─────────────

        void TickMarking()
        {
            for (int h = 0; h < 2; h++)
            {
                if (trigger == null || !trigger[h].WasPressedThisFrame()) continue;
                var hand = LearnerRig.Aim(h == 0); // the visible ray, not the grip pose
                if (hand != null && PointOnSkin(hand, out var point)) MarkAt(point);
            }

            // All three on target: move on by itself. Off-target marks wait for the learner to re-mark or continue.
            allOnTargetTime = Plan.AllOnTarget ? allOnTargetTime + Time.deltaTime : 0f;
            if (allOnTargetTime >= 1.5f) { allOnTargetTime = 0f; Sequence.Next(); }
        }

        /// <summary>Where the hand's pointing ray meets the skin.</summary>
        bool PointOnSkin(Transform hand, out Vector3 point)
        {
            point = default;
            var direction = hand.forward;
            if (direction.y > -0.05f) return false; // not pointing down at the patient
            var distance = (skinY - hand.position.y) / direction.y;
            if (distance < 0f || distance > MarkRayLength) return false;
            point = hand.position + direction * distance;
            return true;
        }

        /// <summary>Marks a port site at a point on the skin. Public so input other than the trigger (and tests) can mark.</summary>
        public PortSite MarkAt(Vector3 point)
        {
            if (Plan == null || Step != PortsStepIndex) return null;
            point.y = skinY;
            var site = Plan.Mark(point);
            if (site != null) ShowMark(Plan.Sites.IndexOf(site), site);
            return site;
        }

        void ShowMark(int index, PortSite site)
        {
            while (markVisuals.Count <= index) markVisuals.Add(null);
            if (markVisuals[index] == null)
            {
                var go = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Cylinder, new Color(0.35f, 0.2f, 0.6f), "PortMark_" + site.name);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
                go.transform.localScale = new Vector3(0.014f, 0.0008f, 0.014f);
                markVisuals[index] = go;
            }
            markVisuals[index].transform.position = site.mark + Vector3.up * 0.004f;
            // Skin-marker violet; in Guided mode an off-target mark turns amber so the learner knows to re-mark.
            var colour = Guided && !site.onTarget ? new Color(0.96f, 0.71f, 0.29f) : new Color(0.35f, 0.2f, 0.6f);
            markVisuals[index].GetComponent<Renderer>().material.color = colour;
        }

        // ───────────── Step 1: trocar entry ─────────────

        void TickEntry()
        {
            if (portIndex >= Plan.Sites.Count) return;
            var site = Plan.Sites[portIndex];
            if (Entry == null || Entry.Port != site.name)
            {
                Entry = new TrocarEntry(site.name, limits, Log);
                RaiseChanged();
            }

            var entryPoint = site.marked ? site.mark : site.target;
            if (!NearestTrocar(entryPoint, out var tip, out var axis))
            {
                Entry.Tick(0f, 0f, Time.deltaTime);
                return;
            }

            var ideal = portIndex == 0 ? Vector3.down : (cavityCentre - entryPoint).normalized;
            Entry.Tick(skinY - tip.position.y, Vector3.Angle(axis, ideal), Time.deltaTime);
            RaiseChanged();

            if (Entry.Phase == TrocarPhase.Placed) PortPlaced(tip);
            else if (Entry.Phase == TrocarPhase.Unsafe) StartBleed(tip, entryPoint);
        }

        /// <summary>The trocar whose tip is over (or in) the entry site, with its long axis.</summary>
        bool NearestTrocar(Vector3 entryPoint, out Transform tip, out Vector3 axis)
        {
            tip = null;
            axis = Vector3.down;
            var best = EntryRadius;
            foreach (var trocar in trocars)
            {
                if (trocar == null || !trocar.gameObject.activeInHierarchy) continue;
                var t = tips[trocar];
                var flat = new Vector2(t.position.x - entryPoint.x, t.position.z - entryPoint.z).magnitude;
                if (flat > best || t.position.y > skinY + 0.05f) continue;
                best = flat;
                tip = t;
                axis = trocar.forward; // instruments are built long-axis +Z, tip at +Z
            }
            return tip != null;
        }

        void PortPlaced(Transform tip)
        {
            // The port stays where it was placed: let go of it and stop it being picked up again.
            var trocar = tip != null ? trocars.FirstOrDefault(t => t != null && tip.IsChildOf(t)) : null;
            if (trocar != null)
            {
                var grab = trocar.GetComponent<XRGrabInteractable>();
                if (grab != null) grab.enabled = false;
                var body = trocar.GetComponent<Rigidbody>();
                if (body != null) body.isKinematic = true; // held by the abdominal wall, not by gravity
                trocars.Remove(trocar);
            }
            portIndex++;
            Entry = null;
            RaiseChanged();
            if (portIndex >= Plan.Sites.Count) Sequence.GoTo(EndIndex);
            else if (Step != EntryStepIndex) Sequence.GoTo(EntryStepIndex); // back from the bleed branch to the next port
        }

        // ───────────── Step 2: vessel injury at the port site ─────────────

        void StartBleed(Transform tip, Vector3 site)
        {
            bleedTip = tip;
            BleedPoint = site;
            Bleed = new BleedControl(Plan.Sites[portIndex].name,
                DetectionThresholds.Get(ThresholdKeys.BleedPressureSeconds, 5f),
                DetectionThresholds.Get(ThresholdKeys.BleedObserveSeconds, 3f), Log);
            Bleed.Changed += RaiseChanged;

            // The blood pools where the injury is, not at a fixed spot.
            var pool = FindWhere(t => t.name == "BleedPool_FX");
            if (pool != null) pool.position = site + Vector3.up * 0.006f;
            Sequence.GoTo(BranchStepIndex);
        }

        void TickBleed()
        {
            if (Bleed == null || Bleed.Controlled || Bleed.Skipped) return;
            // Gone from the scene counts as drawn back: there is nothing left in the wound.
            var withdrawn = bleedTip == null || skinY - bleedTip.position.y <= limits.safeDepth;
            var head = LearnerRig.Head;
            var watching = head != null && Vector3.Angle(head.forward, BleedPoint - head.position) <= WatchAngle;
            Bleed.Tick(withdrawn, HandOnSite(true) || HandOnSite(false), watching, Time.deltaTime);
            if (Bleed.Controlled) LeaveBleed();
        }

        bool HandOnSite(bool left)
        {
            var hand = LearnerRig.Hand(left);
            return hand != null && LearnerRig.Tracked(left) && (hand.position - BleedPoint).magnitude <= PressRadius;
        }

        /// <summary>The injured port is left as it is and entry moves on to the next one (or the stage ends).</summary>
        void LeaveBleed()
        {
            var tip = bleedTip;
            bleedTip = null;
            PortPlaced(tip);
        }

        public override void SkipStep()
        {
            if (Sequence == null) return;
            if (Step == PortsStepIndex)
            {
                Plan?.SkipUnmarked();
                Sequence.Next();
            }
            else if (Step == EntryStepIndex)
            {
                if (Plan != null)
                    for (int i = portIndex; i < Plan.Sites.Count; i++)
                        Log(EventClass.Deviation, EventCodes.TrocarSkipped, $"{Plan.Sites[i].name} port not placed");
                Sequence.GoTo(EndIndex);
            }
            else if (Step == BranchStepIndex && Bleed != null)
            {
                Bleed.Skip();
                LeaveBleed();
            }
            else Sequence.GoTo(EndIndex);
        }
    }
}
