using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Where a cable or tube goes in: a port on a device (the diathermy generator's pad socket, the suction canister inlet,
    /// the light source) or a point on the patient (the return-electrode pad site, a finger for the oximeter). A ring marks
    /// it; in Guided mode the ring glows for the cable that belongs here and brightens as that plug comes close.
    /// </summary>
    public class CableSocket : MonoBehaviour
    {
        public string key;
        public string label;
        public CablePlug Occupant { get; internal set; }
        public bool Guide { get; set; }

        Image ring;
        float glow;

        public static readonly List<CableSocket> All = new List<CableSocket>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static CableSocket Create(string key, string label, Vector3 position, Vector3 outward, Transform parent)
        {
            var go = new GameObject("Socket_" + key);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(outward.sqrMagnitude > 1e-6f ? outward : Vector3.forward));
            go.transform.SetParent(parent, true);
            var s = go.AddComponent<CableSocket>();
            s.key = key;
            s.label = label;
            s.BuildRing();
            return s;
        }

        void BuildRing()
        {
            var c = new GameObject("Ring", typeof(RectTransform));
            c.transform.SetParent(transform, false);
            c.transform.localPosition = Vector3.forward * 0.003f;
            c.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)c.transform;
            rt.sizeDelta = new Vector2(100, 100);
            rt.localScale = Vector3.one * 0.0006f; // 6 cm ring
            var img = new GameObject("Img", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            img.transform.SetParent(c.transform, false);
            ((RectTransform)img.transform).sizeDelta = new Vector2(100, 100);
            ring = img.GetComponent<Image>();
            ring.sprite = UI.UISprites.Ring(0.72f);
            ring.raycastTarget = false;
        }

        void Update()
        {
            // Dim grey when idle; mint when it is the target; bright as the matching plug approaches; solid when plugged.
            float target = Occupant != null ? 0.35f : Guide ? 0.6f : 0.18f;
            if (Occupant == null && Guide)
                foreach (var p in CablePlug.All)
                    if (p.key == key && p.Held)
                        target = Mathf.Max(target, Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(0.05f, 0.4f, Vector3.Distance(p.Tip, transform.position))));
            glow = Mathf.MoveTowards(glow, target, Time.deltaTime * 3f);
            var col = Occupant != null ? new Color(0.36f, 0.88f, 0.78f) : Guide ? new Color(0.36f, 0.88f, 0.78f) : new Color(0.7f, 0.72f, 0.75f);
            ring.color = new Color(col.r, col.g, col.b, glow);
            float pulse = Guide && Occupant == null ? 1f + 0.08f * Mathf.Sin(Time.time * 5f) : 1f;
            ring.transform.localScale = Vector3.one * pulse;
        }
    }

    /// <summary>
    /// The plug end of a cable or tube, held in the hand. The cable runs from its fixed end on the device to the plug with
    /// a gentle sag (drawn, not simulated, so it never tangles). Let go within <see cref="SnapRadius"/> of a socket with the
    /// same key and it snaps in, lined up; anywhere else it goes back to its coil. Grab it again to unplug.
    /// </summary>
    public class CablePlug : MonoBehaviour
    {
        public const float SnapRadius = 0.12f;

        public string key;
        public string label;
        public Transform origin;     // where the cable leaves the device
        public CableSocket Plugged { get; private set; }
        public bool Held => grab != null && grab.isSelected;
        public Vector3 Tip => transform.position + transform.forward * 0.035f;
        public event Action<CablePlug, CableSocket> PluggedIn;
        public event Action<CablePlug> PickedUp;

        public static readonly List<CablePlug> All = new List<CablePlug>();

        XRGrabInteractable grab;
        LineRenderer line;
        Vector3 restPos;
        Quaternion restRot;
        float length = 2.2f;
        const int Segments = 20;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>A plug of the given colour resting at <paramref name="coil"/>, its cable starting at <paramref name="from"/>.</summary>
        public static CablePlug Create(string key, string label, Transform from, Vector3 coil, Color color, float cableLength, Transform parent)
        {
            var go = new GameObject("Plug_" + key);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(coil, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            // Body: a short cylinder with a lighter tip, sized for a fingertip grip.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            UnityEngine.Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.026f, 0.028f, 0.026f);
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tip.name = "Tip";
            UnityEngine.Object.Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = Vector3.forward * 0.032f;
            tip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tip.transform.localScale = new Vector3(0.012f, 0.008f, 0.012f);
            var mat = PlugMaterial(color);
            body.GetComponent<Renderer>().sharedMaterial = mat;
            tip.GetComponent<Renderer>().sharedMaterial = PlugMaterial(Color.Lerp(color, Color.white, 0.6f));

            // Generous grab volume: easy to pick up with a controller or a pinch.
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.045f;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var plug = go.AddComponent<CablePlug>();
            plug.key = key;
            plug.label = label;
            plug.origin = from;
            plug.length = cableLength;
            plug.grab = go.AddComponent<XRGrabInteractable>();
            plug.grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            plug.grab.throwOnDetach = false;
            plug.grab.useDynamicAttach = true;
            plug.grab.selectEntered.AddListener(plug.OnGrab);
            plug.grab.selectExited.AddListener(plug.OnRelease);
            plug.restPos = coil;
            plug.restRot = go.transform.rotation;

            plug.line = go.AddComponent<LineRenderer>();
            plug.line.positionCount = Segments + 1;
            plug.line.widthMultiplier = 0.006f;
            plug.line.numCapVertices = 2;
            plug.line.sharedMaterial = PlugMaterial(Color.Lerp(color, Color.black, 0.35f));
            plug.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return plug;
        }

        static Material PlugMaterial(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
            return m;
        }

        void OnGrab(SelectEnterEventArgs _)
        {
            if (Plugged != null) { Plugged.Occupant = null; Plugged = null; } // pulled out
            PickedUp?.Invoke(this);
        }

        void OnRelease(SelectExitEventArgs _)
        {
            // Snap into the nearest free matching socket in reach; otherwise back to the coil.
            CableSocket best = null;
            float bestD = SnapRadius;
            foreach (var s in CableSocket.All)
            {
                if (s.key != key || s.Occupant != null) continue;
                float d = Vector3.Distance(Tip, s.transform.position);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best != null) PlugInto(best);
            else transform.SetPositionAndRotation(restPos, restRot);
        }

        public void PlugInto(CableSocket s)
        {
            Plugged = s;
            s.Occupant = this;
            // Tip into the socket, body standing out along the socket's outward direction.
            transform.rotation = Quaternion.LookRotation(-s.transform.forward, Vector3.up);
            transform.position = s.transform.position - transform.forward * 0.035f + s.transform.forward * 0.002f;
            PluggedIn?.Invoke(this, s);
        }

        void LateUpdate()
        {
            if (origin == null || line == null) return;
            // A catenary-like sag: deeper when the ends are close (slack cable), shallower when pulled taut.
            Vector3 a = origin.position, b = transform.position - transform.forward * 0.015f;
            float span = Vector3.Distance(a, b);
            float slack = Mathf.Max(0f, length - span);
            float sag = Mathf.Clamp(slack * 0.45f, 0.01f, 0.6f);
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                var p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                p.y = Mathf.Max(p.y, 0.005f); // lies on the floor rather than through it
                line.SetPosition(i, p);
            }
        }
    }
}
