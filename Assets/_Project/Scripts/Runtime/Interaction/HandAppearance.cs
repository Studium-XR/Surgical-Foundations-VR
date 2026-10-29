using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace SurgicalFoundations.Interaction
{
    public enum HandLook { Bare, Gloved, Contaminated }

    /// <summary>
    /// Dresses the learner's tracked XR Hands meshes (from the XRI "XR Origin Hands" rig) for the current moment in
    /// the scenario: bare skin while scrubbing, latex gloves after gowning, a contaminated glove after a sterility
    /// break (FR-07). Lives on the XR Origin in 00_Bootstrap; <see cref="HandLookCue"/> components on step panels
    /// request looks as the scenario moves on. With controllers the controller model is replaced by a hand gripping it
    /// (Resources/Hands, built from the same XR Hands meshes), dressed the same way, so the learner always sees hands.
    /// </summary>
    public class HandAppearance : MonoBehaviour
    {
        public static HandAppearance Instance { get; private set; }

        [SerializeField] Material bare;
        [SerializeField] Material gloved;
        [SerializeField] Material contaminated;
        [SerializeField] HandLook left = HandLook.Bare;
        [SerializeField] HandLook right = HandLook.Bare;

        readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        readonly GameObject[] controllerHands = new GameObject[2];
        readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        readonly List<Renderer>[] hiddenController = { new List<Renderer>(), new List<Renderer>() };
        float nextScan;
        int lastCount = -1;

        public HandLook Left => left;
        public HandLook Right => right;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Set(HandLook leftLook, HandLook rightLook)
        {
            left = leftLook;
            right = rightLook;
            Apply();
        }

        void Update()
        {
            // Hand meshes can appear late (tracking acquired, modality switch), so re-check a couple of times a second.
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            var root = GetComponentInParent<XROrigin>() != null ? GetComponentInParent<XROrigin>().transform : transform;
            root.GetComponentsInChildren(true, renderers);
            if (renderers.Count != lastCount) Apply();
            else ApplyControllerHands();
        }

        void Apply()
        {
            var root = GetComponentInParent<XROrigin>() != null ? GetComponentInParent<XROrigin>().transform : transform;
            root.GetComponentsInChildren(true, renderers);
            lastCount = renderers.Count;
            foreach (var r in renderers)
            {
                // Only the hand meshes (LeftHand / RightHand), not the pinch and poke highlight visuals next to them.
                if (r.name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var side = SideOf(r.transform);
                if (side == 0) continue;
                var mat = For(side < 0 ? left : right);
                if (mat == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            ApplyControllerHands();
        }

        void ApplyControllerHands()
        {
            var origin = GetComponentInParent<XROrigin>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
            if (origin == null) return;
            var space = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            for (int i = 0; i < 2; i++)
            {
                var look = i == 0 ? left : right;
                var controller = space.Find(i == 0 ? "Left Controller" : "Right Controller");
                if (controller != null && controller.gameObject.activeInHierarchy) ShowHand(i, controller, look == HandLook.Bare ? null : For(look));
                else RemoveHand(i);
            }
        }

        void ShowHand(int i, Transform controller, Material mat)
        {
            if (controllerHands[i] == null)
            {
                var prefab = Resources.Load<GameObject>(i == 0 ? "Hands/ControllerHand_Left" : "Hands/ControllerHand_Right");
                if (prefab == null) return; // not built yet: the controller model stays
                controllerHands[i] = Instantiate(prefab, controller, false);
                controllerHands[i].transform.localPosition = Vector3.zero;
                controllerHands[i].transform.localRotation = Quaternion.identity;
            }

            // Hide the controller model (it can load after the hand appears, so check every time). Rays and other
            // line or sprite visuals are not MeshRenderers and stay.
            foreach (var r in controller.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.IsChildOf(controllerHands[i].transform) || !r.enabled) continue;
                r.enabled = false;
                hiddenController[i].Add(r);
            }
            // Bare: the XRI hand visual's own materials, exactly as the tracked hands look while scrubbing.
            // Gloved / contaminated: the glove material on every slot.
            foreach (var r in controllerHands[i].GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!originalMaterials.TryGetValue(r, out var original)) originalMaterials[r] = original = r.sharedMaterials;
                if (mat == null) { if (r.sharedMaterial != original[0]) r.sharedMaterials = original; }
                else if (r.sharedMaterial != mat)
                {
                    var mats = new Material[original.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = mat;
                    r.sharedMaterials = mats;
                }
            }
        }

        void RemoveHand(int i)
        {
            if (controllerHands[i] != null) Destroy(controllerHands[i]);
            controllerHands[i] = null;
            foreach (var r in hiddenController[i]) if (r != null) r.enabled = true;
            hiddenController[i].Clear();
        }

        Material For(HandLook look) => look switch
        {
            HandLook.Gloved => gloved,
            HandLook.Contaminated => contaminated,
            _ => bare
        };

        /// <summary>−1 left hand, +1 right hand, 0 not a hand (controller models etc.).</summary>
        static int SideOf(Transform t)
        {
            bool isHand = false;
            for (var p = t; p != null; p = p.parent)
            {
                var n = p.name;
                if (n.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (n.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0) isHand = true;
                if (!isHand) continue;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            }
            return 0;
        }
    }
}
