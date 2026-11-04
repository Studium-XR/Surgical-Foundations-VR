using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Lighting;
using UnityEngine;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Operate stage. Runs the three tasks in order (<see cref="OperateTasks"/>) and watches every inserted
    /// instrument's tip in the laparoscope's view: once it has been on the monitor, out of view for longer than the
    /// threshold is a drift (FR-13). While
    /// something is drifting the stage shows its drift panel and monitor arrow (step 1); once it is back in view the
    /// task panel (step 0) returns. The stage ends when the last task is done.
    /// </summary>
    public class OperateController : StageController
    {
        public const int TasksStepIndex = 0, DriftStepIndex = 1, EndIndex = 2;

        /// <summary>Viewport margin: a tip this close to the edge of the picture already counts as leaving it.</summary>
        const float ViewMargin = 0.04f;
        const float AbdomenRadius = 0.25f;

        class Tracked
        {
            public string label;
            public Transform tip;
            /// <summary>On the monitor at least once since it last came into the abdomen.</summary>
            public bool seen;
            public float insideFor;
        }

        /// <summary>Inside this long without ever being brought into view, it counts as out of view anyway.</summary>
        const float FindItSeconds = 6f;

        readonly List<Tracked> instruments = new List<Tracked>();
        Camera scope;
        float skinY = 1.165f;
        Vector3 abdomenCentre;
        bool hasPorts;

        bool ended;

        public DriftMonitor Drift { get; private set; }
        public OperateTasks Tasks { get; private set; }

        // While an instrument is out of view the job is to bring it back, not to carry on with the task.
        public override Vector3? GuideTarget => ended || Tasks == null || (Drift != null && Drift.Drifting != null) ? null : Tasks.GuideTarget;

        protected override void Setup()
        {
            var feed = FindObjectsByType<LaparoscopeFeed>(FindObjectsInactive.Include).FirstOrDefault(f => f.gameObject.scene == gameObject.scene);
            scope = feed != null ? feed.GetComponentInChildren<Camera>(true) : null;
            if (scope == null) Debug.LogWarning("[OperateController] No laparoscope camera in the scene; drift can't be detected.");

            // Ports are placed 9 cm above the skin mark (SceneBuilder.Trocars); together they outline the abdomen.
            var ports = gameObject.scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith("Trocar_")).ToList();
            if (ports.Count > 0)
            {
                hasPorts = true;
                skinY = ports[0].position.y - 0.09f;
                abdomenCentre = ports.Aggregate(Vector3.zero, (sum, p) => sum + p.position) / ports.Count;
            }

            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var tip in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Pivot_Tip"))
            {
                var instrument = InstrumentRoot(tip);
                // The scope is the camera, and trocars are ports, not working instruments.
                if (instrument == null || instrument.name.Contains("Laparoscope") || instrument.name.Contains("Trocar")) continue;
                instruments.Add(new Tracked { tip = tip, label = Label(instrument.name) });
            }

            Drift = new DriftMonitor(DetectionThresholds.Get(ThresholdKeys.DriftOutOfViewSeconds, 1f), Log);
            Drift.Changed += OnDriftChanged;

            // Holds the laparoscope and moves it on request, so both of the learner's hands are free for instruments.
            gameObject.AddComponent<CameraAssistant>();

            Tasks = new OperateTasks(Log);
            Tasks.Changed += RaiseChanged;
            Tasks.Setup(gameObject.scene, Guided, DetectionThresholds.Get(ThresholdKeys.TissueMaxTipSpeedCmPerS, 25f) / 100f);
        }

        void Update()
        {
            if (Sequence == null || Drift == null || scope == null) return;
            if (ended || (Step != TasksStepIndex && Step != DriftStepIndex)) return;

            Tasks.Tick(Time.deltaTime);
            if (Tasks.AllDone)
            {
                ended = true;
                Sequence.GoTo(EndIndex);
                return;
            }

            foreach (var i in instruments)
            {
                if (i.tip == null) continue;
                var inserted = Inserted(i.tip.position);
                var inView = InView(i.tip.position);
                // Going in through a port, the tip passes under the abdominal wall where the scope can't see it: that
                // is the way in, not a drift. Only an instrument that has been on the monitor can drift off it.
                i.insideFor = inserted ? i.insideFor + Time.deltaTime : 0f;
                if (!inserted) i.seen = false;
                else if (inView || i.insideFor >= FindItSeconds) i.seen = true;
                Drift.Tick(i.label, !inserted || inView || !i.seen, Time.deltaTime);
            }
        }

        /// <summary>Only instruments inside the patient can drift; one lying on the Mayo stand (which is lower than the
        /// abdomen) is simply not in use. Inside = below the skin and within the abdomen's footprint.</summary>
        public bool Inserted(Vector3 tip)
        {
            if (tip.y >= skinY - 0.01f) return false;
            if (!hasPorts) return true;
            return new Vector2(tip.x - abdomenCentre.x, tip.z - abdomenCentre.z).magnitude <= AbdomenRadius;
        }

        public bool InView(Vector3 worldPoint)
        {
            var v = scope.WorldToViewportPoint(worldPoint);
            return v.z > 0f && v.x > ViewMargin && v.x < 1f - ViewMargin && v.y > ViewMargin && v.y < 1f - ViewMargin;
        }

        void OnDriftChanged()
        {
            if (Sequence != null && !ended && (Step == TasksStepIndex || Step == DriftStepIndex))
                Sequence.Show(Drift.Drifting != null ? DriftStepIndex : TasksStepIndex);
            RaiseChanged();
        }

        /// <summary>Ending Operate from the panel: unfinished tasks are logged as deviations.</summary>
        public override void SkipStep()
        {
            if (Sequence == null || ended) return;
            ended = true;
            Tasks?.SkipRemaining();
            Sequence.GoTo(EndIndex);
        }

        static Transform InstrumentRoot(Transform tip)
        {
            for (var t = tip.parent; t != null; t = t.parent)
                if (t.name.StartsWith("INST_")) return t;
            return null;
        }

        static string Label(string prefabName)
        {
            if (prefabName.Contains("Grasper")) return "Grasper";
            if (prefabName.Contains("Maryland")) return "Dissector";
            if (prefabName.Contains("Scissors")) return "Scissors";
            if (prefabName.Contains("ClipApplier")) return "Clip applier";
            return prefabName.Replace("INST_", "");
        }
    }
}
