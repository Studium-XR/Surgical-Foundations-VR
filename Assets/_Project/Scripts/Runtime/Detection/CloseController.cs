using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Core;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Lighting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Close stage. Step 0: the closing swab count (FR-15). Every swab from the opening count has to go into the kick
    /// bucket; one was left in the field at a seeded spot (US-CLS-05) and must be found first. Step 1: the ports come
    /// out under vision, camera port last, and each port site is closed with a stitch.
    /// </summary>
    public class CloseController : StageController
    {
        public const int CountStepIndex = 0, RemovalStepIndex = 1;
        const int OpeningCount = 5;

        /// <summary>A port lifted this far from where it sat is out.</summary>
        const float RemovedDistance = 0.06f;
        const float BiteRadius = 0.02f;
        const float BiteOffset = 0.008f;
        const float ViewMargin = 0.04f;

        /// <summary>Where the missed swab can be: on the floor around the table and on the drapes. The session seed picks one.</summary>
        static readonly Vector3[] HiddenSpots =
        {
            new Vector3(0.45f, 0.01f, 0.38f), new Vector3(-0.55f, 0.01f, -0.45f), new Vector3(0.75f, 0.01f, -0.35f),
            new Vector3(-0.35f, 1.18f, 0.22f), new Vector3(0.3f, 1.18f, -0.2f)
        };

        // Working ports first, camera port last.
        static readonly (string trocar, string label)[] PortOrder =
        {
            ("Trocar_Right", "Right working"), ("Trocar_Left", "Left working"), ("Trocar_Camera", "Camera")
        };

        class Site
        {
            public InstrumentPort port;
            public Vector3 start;
            public Vector3 skin;
            public GameObject[] markers = new GameObject[2];
        }

        readonly List<Transform> swabs = new List<Transform>();
        readonly List<Site> sites = new List<Site>();
        Transform hidden;
        Collider bucket;
        Camera scope;
        Transform needle;
        InstrumentJaws needleJaws;
        bool needleClosed;
        float stepStarted;
        float completeTime;
        float closedTime;

        public SwabCount Count { get; private set; }
        public PortRemoval Removal { get; private set; }
        public PortClosure Closure { get; private set; }

        public override Vector3? GuideTarget
        {
            get
            {
                if (Step == CountStepIndex && Count != null && !Count.Complete)
                {
                    // A swab in the hand goes to the bucket; otherwise the next swab in sight. The hidden one is not
                    // pointed out: finding it is the exercise.
                    foreach (var swab in swabs)
                    {
                        var grab = swab != null ? swab.GetComponent<XRGrabInteractable>() : null;
                        if (grab != null && grab.isSelected) return bucket != null ? bucket.bounds.center : (Vector3?)null;
                    }
                    foreach (var swab in swabs)
                        if (swab != null && swab != hidden) return swab.position;
                    return null;
                }
                if (Step == RemovalStepIndex && Removal != null)
                {
                    // Finish one site (stitch it) before the next port comes out.
                    for (int i = 0; i < sites.Count; i++)
                        if (Removal.IsRemoved(i) && !Closure.IsClosed(i))
                            return BitePoint(i, Closure.HasBite(i, 0) ? 1 : 0);
                    for (int i = 0; i < sites.Count; i++)
                        if (!Removal.IsRemoved(i) && sites[i].port != null) return sites[i].port.transform.position;
                }
                return null;
            }
        }

        protected override void Setup()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                swabs.AddRange(root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("INST_Swab") || t.name.StartsWith("Swab_Hidden")));
            hidden = swabs.FirstOrDefault(s => s.name.StartsWith("Swab_Hidden"));

            // Same seed, same hiding place: an assessment can be repeated or compared fairly (FR-04).
            if (hidden != null)
            {
                var seed = SessionManager.Instance != null ? SessionManager.Instance.Seed : 0;
                var spot = HiddenSpots[(int)((uint)seed % (uint)HiddenSpots.Length)];
                hidden.position = spot;
            }

            var target = FindWhere(t => t.name == "DropTarget");
            bucket = target != null ? target.GetComponent<Collider>() : null;
            if (bucket == null) Debug.LogWarning("[CloseController] No kick bucket DropTarget in the scene; swabs can't be counted in.");

            Count = new SwabCount(OpeningCount, Log);
            Count.Changed += RaiseChanged;
            stepStarted = Time.time;

            SetupPorts();
        }

        void SetupPorts()
        {
            var labels = new List<string>();
            foreach (var (trocar, label) in PortOrder)
            {
                var port = InstrumentPort.All.FirstOrDefault(p => p.name == trocar && p.gameObject.scene == gameObject.scene);
                if (port == null) continue;
                sites.Add(new Site { port = port, start = port.transform.position, skin = port.Fulcrum });
                labels.Add(label);
            }
            if (sites.Count == 0) { Debug.LogWarning("[CloseController] No ports in the scene; port removal can't be detected."); return; }

            var names = labels.ToArray();
            Removal = new PortRemoval(names, sites.FindIndex(s => s.port.name == "Trocar_Camera"), Log);
            Removal.Changed += RaiseChanged;
            Closure = new PortClosure(names, Log);
            Closure.Changed += RaiseChanged;

            var feed = FindObjectsByType<LaparoscopeFeed>(FindObjectsInactive.Include).FirstOrDefault(f => f.gameObject.scene == gameObject.scene);
            scope = feed != null ? feed.GetComponentInChildren<Camera>(true) : null;

            var holder = FindWhere(t => t.name.StartsWith("INST_NeedleHolder"));
            if (holder != null)
            {
                needleJaws = holder.GetComponent<InstrumentJaws>();
                needle = holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Needle");
            }
            if (needle == null || needleJaws == null) Debug.LogWarning("[CloseController] No needle holder in the scene; port sites can't be closed.");
        }

        protected override void OnStepShown(int index)
        {
            if (index == CountStepIndex) stepStarted = Time.time;
            base.OnStepShown(index);
        }

        void Update()
        {
            if (Sequence == null || Count == null) return;
            if (Step == CountStepIndex) TickCount();
            if (Removal != null) TickPorts();
        }

        // ───────────── Step 0: swab count ─────────────

        void TickCount()
        {
            if (bucket != null)
                for (int i = swabs.Count - 1; i >= 0; i--)
                {
                    var swab = swabs[i];
                    if (swab == null) { swabs.RemoveAt(i); continue; }
                    var grab = swab.GetComponent<XRGrabInteractable>();
                    // Counted when it is let go inside the bucket, not while it is still in the hand.
                    if (grab != null && grab.isSelected) continue;
                    if (bucket.bounds.Contains(swab.position)) CountSwab(swab);
                }

            completeTime = Count.Complete ? completeTime + Time.deltaTime : 0f;
            if (completeTime >= 1f) { completeTime = 0f; Sequence.Next(); }
        }

        /// <summary>Counts a swab into the bucket. Public so a test (or another drop detector) can count one.</summary>
        public void CountSwab(Transform swab)
        {
            if (!swabs.Remove(swab)) return;
            var grab = swab.GetComponent<XRGrabInteractable>();
            if (grab != null) grab.enabled = false;
            // Rest it at the bottom of the bucket.
            if (bucket != null) swab.position = new Vector3(bucket.bounds.center.x, bucket.bounds.min.y - 0.2f, bucket.bounds.center.z);
            Count.Count(swab == hidden, Time.time - stepStarted);
            // Every swab in sight is in and the count is still one short: tell the learner (Guided) to go and look.
            if (Guided && !Count.Complete && swabs.Count == 1 && swabs[0] == hidden)
                Audio.AudioManager.Instance?.Play(Audio.SoundId.VO_Close_SwabMissing);
        }

        public IReadOnlyList<Transform> RemainingSwabs => swabs;
        public Transform HiddenSwab => hidden;

        // ───────────── Step 1: ports out, sites closed ─────────────

        void TickPorts()
        {
            // A port pulled early (during the count) is still a port removed, so this runs in both steps.
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (Removal.IsRemoved(i) || site.port == null) continue;
                if ((site.port.transform.position - site.start).magnitude >= RemovedDistance) RemovePort(i);
            }

            if (needle != null && needleJaws != null)
            {
                // One bite per squeeze of the needle holder.
                if (!needleClosed && needleJaws.IsClosed) { needleClosed = true; TryBite(needle.position); }
                else if (needleClosed && needleJaws.Openness >= 0.6f) needleClosed = false;
            }

            if (Step != RemovalStepIndex) return;
            closedTime = Removal.Complete && Closure.Complete ? closedTime + Time.deltaTime : 0f;
            if (closedTime >= 1f) { closedTime = 0f; Sequence.Next(); }
        }

        /// <summary>Takes a port out. Whether its site was on the monitor at that moment is measured here.</summary>
        public void RemovePort(int index)
        {
            if (Removal == null || Removal.IsRemoved(index)) return;
            var site = sites[index];
            if (!Removal.Remove(index, InView(site.skin))) return;

            // The port is handed off; whatever was through it comes out with it (the scope, for the camera port).
            var occupant = site.port.Occupant;
            if (occupant != null) occupant.gameObject.SetActive(false);
            site.port.gameObject.SetActive(false);
            ShowBitePoints(index);
        }

        public Vector3 PortSite(int index) => sites[index].skin;
        public Vector3 BitePoint(int index, int side) => sites[index].skin + Vector3.right * (side == 0 ? -BiteOffset : BiteOffset);

        bool InView(Vector3 worldPoint)
        {
            if (scope == null || !scope.isActiveAndEnabled) return false;
            var v = scope.WorldToViewportPoint(worldPoint);
            return v.z > 0f && v.x > ViewMargin && v.x < 1f - ViewMargin && v.y > ViewMargin && v.y < 1f - ViewMargin;
        }

        void TryBite(Vector3 needleTip)
        {
            int bestSite = -1, bestSide = 0;
            var best = BiteRadius;
            for (int i = 0; i < sites.Count; i++)
            {
                if (!Removal.IsRemoved(i) || Closure.IsClosed(i)) continue;
                for (int side = 0; side < 2; side++)
                {
                    if (Closure.HasBite(i, side)) continue;
                    var d = (BitePoint(i, side) - needleTip).magnitude;
                    if (d > best) continue;
                    best = d; bestSite = i; bestSide = side;
                }
            }
            if (bestSite < 0) return;

            var closed = Closure.Bite(bestSite, bestSide);
            var marker = sites[bestSite].markers[bestSide];
            if (marker != null) marker.SetActive(false);
            if (closed) ShowStitch(bestSite);
        }

        void ShowBitePoints(int index)
        {
            if (!Guided) return; // where to place the stitch is marked in Guided mode only (FR-19)
            for (int side = 0; side < 2; side++)
            {
                var marker = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Sphere, new Color(0.36f, 0.88f, 0.78f), $"BitePoint_{index}_{side}");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, gameObject.scene);
                marker.transform.position = BitePoint(index, side) + Vector3.up * 0.003f;
                marker.transform.localScale = Vector3.one * 0.006f;
                sites[index].markers[side] = marker;
            }
        }

        void ShowStitch(int index)
        {
            var stitch = Rendering.RuntimeMaterials.Primitive(PrimitiveType.Cylinder, new Color(0.45f, 0.3f, 0.7f), "Stitch_" + index); // suture violet
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stitch, gameObject.scene);
            stitch.transform.position = sites[index].skin + Vector3.up * 0.002f;
            stitch.transform.rotation = Quaternion.Euler(0f, 0f, 90f); // lying across the wound
            stitch.transform.localScale = new Vector3(0.0015f, BiteOffset, 0.0015f);
        }

        public override void SkipStep()
        {
            if (Sequence == null) return;
            if (Step == CountStepIndex) Count.CloseWithMismatch();
            else if (Removal != null)
            {
                Removal.SkipRemaining();
                Closure.SkipRemaining();
            }
            Sequence.Next();
        }
    }
}
