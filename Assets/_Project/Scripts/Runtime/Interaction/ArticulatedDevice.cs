using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// The working parts of a rigged SimReady model: drawers slide, doors and lids swing, knobs turn, buttons press,
    /// casters swivel, wheels roll, arms and screens move. The rig (joint nodes "J_*", each turning about or sliding
    /// along its local +Y) is made in Blender by Tools/SimReady/rig_simready.py; its manifest (the .joints.json next to
    /// the model) lists every joint's kind and limits. Each joint gets a grab handle on its part: pull or push it with
    /// either hand to move it within its limits, or tap it to open/close it smoothly. Showcase() runs every joint once,
    /// for the equipment lab.
    /// When the model has an Animator whose controller was built by SimReadyLibraryBuilder, the Animator poses the parts:
    /// each joint has a layer whose state's motion time is a float parameter named after the joint (0 = its minimum,
    /// 1 = its maximum), and a "Showcase" layer plays the animation authored in Blender. Joints without a parameter
    /// (rolling wheels, swivelling casters) are posed directly.
    /// </summary>
    public class ArticulatedDevice : MonoBehaviour
    {
        [Serializable]
        public class JointSpec
        {
            public string name;
            public string kind;
            public string node;
            public string parent;
            public float[] axis;
            public string unit;
            public float min;
            public float max;
            public string[] parts;
            public string clip;
        }

        [Serializable]
        class Manifest
        {
            public string asset;
            public string category;
            public JointSpec[] joints;
        }

        public class Joint
        {
            public JointSpec spec;
            public Transform node;
            public Quaternion restRot;
            public Vector3 restPos;
            public float value;   // current position, in degrees or metres
            public float target;  // where it is easing to
            public bool Rotary => spec.unit == "deg";
            public bool Continuous => spec.kind == "spin";
            /// <summary>The "closed" or rest value: 0 where it lies inside the limits, else the nearer limit.</summary>
            public float Rest => Mathf.Clamp(0f, Mathf.Min(spec.min, spec.max), Mathf.Max(spec.min, spec.max));
            /// <summary>The "open" value: the limit further from rest.</summary>
            public float Open => Mathf.Abs(spec.max - Rest) >= Mathf.Abs(spec.min - Rest) ? spec.max : spec.min;
        }

        [SerializeField] TextAsset manifest;
        [Tooltip("Degrees or metres per second while a joint eases to a tapped position.")]
        [SerializeField] float easeSpeed = 4f;
        [SerializeField] bool interactive = true;

        readonly List<Joint> joints = new List<Joint>();
        readonly Dictionary<string, Joint> byName = new Dictionary<string, Joint>();
        bool showcasing;
        float showcaseT;

        Animator animator;
        readonly HashSet<string> animParams = new HashSet<string>();
        int showcaseLayer = -1;
        float showcaseLayerUntil = -1f;

        public IReadOnlyList<Joint> Joints => joints;
        public string Category { get; private set; }
        public event Action<Joint> Moved;

        public TextAsset ManifestAsset { get => manifest; set { manifest = value; Bind(); } }

        void Awake() => Bind();

        void Bind()
        {
            joints.Clear();
            byName.Clear();
            if (manifest == null) return;
            Manifest m;
            try { m = JsonUtility.FromJson<Manifest>(manifest.text); }
            catch (Exception e) { Debug.LogError($"[ArticulatedDevice] {name}: bad manifest: {e.Message}", this); return; }
            Category = m.category;
            var nodes = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) nodes.TryAdd(t.name, t);
            foreach (var spec in m.joints ?? Array.Empty<JointSpec>())
            {
                if (!nodes.TryGetValue(spec.node, out var node)) continue;
                var j = new Joint { spec = spec, node = node, restRot = node.localRotation, restPos = node.localPosition };
                joints.Add(j);
                byName[spec.name] = j;
            }
            BindAnimator();
            if (interactive && Application.isPlaying) AddHandles();
        }

        void BindAnimator()
        {
            animParams.Clear();
            showcaseLayer = -1;
            animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || !Application.isPlaying) return;
            foreach (var p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Float) animParams.Add(p.name);
            showcaseLayer = animator.GetLayerIndex("Showcase");
            animator.applyRootMotion = false;
        }

        bool Animated(Joint j) => animParams.Contains(j.spec.name);

        // ─────────────────────────────── driving ───────────────────────────────

        /// <summary>Sets a joint immediately (clamped to its limits, except wheels which roll freely).</summary>
        public void Set(Joint j, float value)
        {
            if (!j.Continuous) value = Mathf.Clamp(value, Mathf.Min(j.spec.min, j.spec.max), Mathf.Max(j.spec.min, j.spec.max));
            j.value = j.target = value;
            Apply(j);
        }

        /// <summary>Eases a joint towards a value.</summary>
        public void MoveTo(Joint j, float value)
        {
            j.target = j.Continuous ? value : Mathf.Clamp(value, Mathf.Min(j.spec.min, j.spec.max), Mathf.Max(j.spec.min, j.spec.max));
        }

        /// <summary>Open if closed, close if open (wheels: a quarter turn).</summary>
        public void Toggle(Joint j)
        {
            if (j.Continuous) { MoveTo(j, j.target + 90f); return; }
            bool open = Mathf.Abs(j.target - j.Rest) > Mathf.Abs(j.Open - j.Rest) * 0.5f;
            MoveTo(j, open ? j.Rest : j.Open);
        }

        public bool TryGet(string jointName, out Joint j) => byName.TryGetValue(jointName, out j);

        /// <summary>Opens every joint and closes it again, one after another, over a few seconds.</summary>
        public void Showcase()
        {
            if (showcaseLayer >= 0)
            {
                // The Blender-authored animation on its own layer, laid over the per-joint layers while it plays.
                animator.SetLayerWeight(showcaseLayer, 1f);
                animator.Play("Showcase", showcaseLayer, 0f);
                showcaseLayerUntil = Time.time + ShowcaseLength();
                return;
            }
            showcasing = joints.Count > 0;
            showcaseT = 0f;
        }

        float ShowcaseLength()
        {
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c.name == "Showcase") return c.length;
            return 3f;
        }

        public void CloseAll()
        {
            showcasing = false;
            foreach (var j in joints) MoveTo(j, j.Continuous ? j.target : j.Rest);
        }

        /// <summary>0 at the joint's minimum, 1 at its maximum (wheels: the fraction of a turn).</summary>
        public static float Normalized(Joint j)
        {
            if (j.Continuous) return Mathf.Repeat(j.value, 360f) / 360f;
            float span = j.spec.max - j.spec.min;
            return Mathf.Abs(span) < 1e-6f ? 0f : Mathf.Clamp01((j.value - j.spec.min) / span);
        }

        void Apply(Joint j)
        {
            if (Animated(j)) animator.SetFloat(j.spec.name, Normalized(j)); // the Animator poses it this frame
            else dirty.Add(j);                                              // posed in LateUpdate, after the Animator
            Moved?.Invoke(j);
        }

        readonly HashSet<Joint> dirty = new HashSet<Joint>();

        static void Pose(Joint j)
        {
            if (j.Rotary) j.node.localRotation = j.restRot * Quaternion.AngleAxis(j.value, Vector3.up);
            else j.node.localPosition = j.restPos + j.restRot * (Vector3.up * j.value);
        }

        void Update()
        {
            if (showcasing) RunShowcase();
            if (showcaseLayerUntil > 0f && Time.time >= showcaseLayerUntil)
            {
                animator.SetLayerWeight(showcaseLayer, 0f);
                showcaseLayerUntil = -1f;
            }
            foreach (var j in joints)
            {
                if (Mathf.Approximately(j.value, j.target)) continue;
                float range = j.Continuous ? 360f : Mathf.Max(1e-4f, Mathf.Abs(j.spec.max - j.spec.min));
                j.value = Mathf.MoveTowards(j.value, j.target, range * easeSpeed * 0.25f * Time.deltaTime);
                Apply(j);
            }
        }

        void LateUpdate()
        {
            if (dirty.Count == 0) return;
            foreach (var j in dirty) Pose(j);
            dirty.Clear();
        }

        // Each joint gets 1.2 s: open in the first half, close in the second; wheels turn once.
        void RunShowcase()
        {
            const float per = 1.2f;
            showcaseT += Time.deltaTime;
            int i = Mathf.FloorToInt(showcaseT / per);
            if (i >= joints.Count) { showcasing = false; return; }
            var j = joints[i];
            float local = (showcaseT - i * per) / per;
            if (j.Continuous) MoveTo(j, Mathf.Lerp(0f, 360f, local));
            else MoveTo(j, local < 0.5f ? j.Open : j.Rest);
        }

        // ─────────────────────────────── hands ───────────────────────────────

        void AddHandles()
        {
            foreach (var j in joints)
            {
                if (j.spec.kind == "spin" || j.spec.kind == "swivel") continue; // wheels and casters follow the cart
                var renderers = j.node.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var go = new GameObject("Handle_" + j.spec.name);
                go.transform.SetParent(j.node, false);
                go.transform.position = b.center;
                go.transform.rotation = j.node.rotation;
                var col = go.AddComponent<BoxCollider>();
                col.size = Quaternion.Inverse(j.node.rotation) * b.size;
                col.size = new Vector3(Mathf.Max(Mathf.Abs(col.size.x), 0.02f), Mathf.Max(Mathf.Abs(col.size.y), 0.02f), Mathf.Max(Mathf.Abs(col.size.z), 0.02f));
                var h = go.AddComponent<JointHandle>();
                h.Init(this, j);
            }
        }

        /// <summary>
        /// One joint's handle. Select (grip) and move the hand: slides follow the hand along the axis, hinges and knobs
        /// follow the hand's angle about the axis. A short select without movement toggles the joint instead.
        /// </summary>
        public class JointHandle : MonoBehaviour
        {
            ArticulatedDevice device;
            Joint joint;
            XRSimpleInteractable interactable;
            Transform hand;
            Vector3 grabPoint;
            float grabValue, grabTime, moved;

            public void Init(ArticulatedDevice d, Joint j)
            {
                device = d;
                joint = j;
                interactable = gameObject.AddComponent<XRSimpleInteractable>();
                interactable.selectEntered.AddListener(OnSelect);
                interactable.selectExited.AddListener(OnRelease);
            }

            void OnSelect(SelectEnterEventArgs e)
            {
                hand = e.interactorObject.GetAttachTransform(interactable);
                grabPoint = hand.position;
                grabValue = joint.value;
                grabTime = Time.time;
                moved = 0f;
                haveGrabRot = false;
            }

            void OnRelease(SelectExitEventArgs e)
            {
                hand = null;
                if (Time.time - grabTime < 0.35f && moved < 0.01f) device.Toggle(joint);
            }

            void Update()
            {
                if (hand == null) return;
                var parent = joint.node.parent;
                var axisWorld = (parent != null ? parent.rotation : Quaternion.identity) * (joint.restRot * Vector3.up);
                var pivotWorld = parent != null ? parent.TransformPoint(joint.restPos) : joint.restPos;
                moved = Mathf.Max(moved, (hand.position - grabPoint).magnitude);
                float value;
                if (!joint.Rotary)
                {
                    value = grabValue + Vector3.Dot(hand.position - grabPoint, axisWorld);
                }
                else if (joint.spec.kind == "turn" || joint.spec.kind == "press" || joint.spec.kind == "lever")
                {
                    // Knobs: turn with the wrist (hand roll about the axis).
                    value = grabValue + WristTwist(axisWorld);
                }
                else
                {
                    var a = Vector3.ProjectOnPlane(grabPoint - pivotWorld, axisWorld);
                    var b = Vector3.ProjectOnPlane(hand.position - pivotWorld, axisWorld);
                    value = grabValue + (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(a, b, axisWorld) : 0f);
                }
                device.Set(joint, value);
            }

            Quaternion grabRot;
            bool haveGrabRot;

            float WristTwist(Vector3 axis)
            {
                if (!haveGrabRot) { grabRot = hand.rotation; haveGrabRot = true; }
                var delta = hand.rotation * Quaternion.Inverse(grabRot);
                delta.ToAngleAxis(out float angle, out var ax);
                if (angle > 180f) angle -= 360f;
                return angle * Vector3.Dot(ax.normalized, axis);
            }

            void OnDisable() => haveGrabRot = false;
        }
    }
}
