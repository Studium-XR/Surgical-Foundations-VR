using SurgicalFoundations.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Opens and closes an instrument's jaws or blades. Jaws are open at rest and follow the holding hand's trigger
    /// (or pinch) continuously: half a squeeze is half closed, so tissue can be held gently or crushed.
    /// </summary>
    public class InstrumentJaws : MonoBehaviour
    {
        [SerializeField] Transform upperJaw;
        [SerializeField] Transform lowerJaw;
        [SerializeField] float openAngle = 28f;
        [SerializeField] float speed = 10f;
        [SerializeField] SoundId closeSound = SoundId.INST_JawClose;
        [SerializeField] SoundId openSound = SoundId.INST_JawOpen;

        XRGrabInteractable grab;
        Quaternion upperRest, lowerRest;
        float openness = 1f;
        float target = 1f;
        bool closedCue;
        bool wasHeld;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            if (upperJaw == null) upperJaw = FindDeep(transform, "Jaw_Upper");
            if (lowerJaw == null) lowerJaw = FindDeep(transform, "Jaw_Lower");
            if (upperJaw != null) upperRest = upperJaw.localRotation;
            if (lowerJaw != null) lowerRest = lowerJaw.localRotation;
        }

        void OnEnable()
        {
            if (grab == null) return;
            grab.activated.AddListener(OnActivated);
            grab.deactivated.AddListener(OnDeactivated);
            grab.selectEntered.AddListener(OnGrabbed);
        }

        void OnDisable()
        {
            if (grab == null) return;
            grab.activated.RemoveListener(OnActivated);
            grab.deactivated.RemoveListener(OnDeactivated);
            grab.selectEntered.RemoveListener(OnGrabbed);
        }

        void OnGrabbed(SelectEnterEventArgs _) => AudioManager.Instance?.PlayAt(SoundId.INST_PickupMetal, transform.position);

        // Fallback for interactors with no analogue value (e.g. a poke or gaze interactor): fully closed or fully open.
        void OnActivated(ActivateEventArgs _) => target = 0f;
        void OnDeactivated(DeactivateEventArgs _) => target = 1f;

        public void SetOpenness(float value) => target = Mathf.Clamp01(value);

        /// <summary>0 = closed, 1 = open. Recorded in session replays.</summary>
        public float Openness => openness;
        /// <summary>How hard the jaws are being squeezed, 0–1 (the trigger value).</summary>
        public float Squeeze => 1f - target;
        public bool IsClosed => openness <= ClosedBelow;

        /// <summary>
        /// Held shut whatever the trigger says (a haemostat's ratchet). <see cref="RatchetLock"/> sets it; the jaws spring
        /// open again once it is cleared.
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>The trigger value while held (0 when not held), for mechanisms that react to a firm squeeze.</summary>
        public float Trigger { get; private set; }

        const float ClosedBelow = 0.25f;
        const float OpenAbove = 0.6f;

        void Update()
        {
            var hand = grab != null && grab.isSelected ? grab.firstInteractorSelecting as XRBaseInputInteractor : null;
            Trigger = hand != null ? Mathf.Clamp01(hand.activateInput.ReadValue()) : 0f;
            if (hand != null)
            {
                target = 1f - Trigger;
                wasHeld = true;
            }
            else if (wasHeld)
            {
                wasHeld = false;
                target = 1f; // let go: the handle springs open
            }

            if (Locked) target = 0f;
            openness = Mathf.MoveTowards(openness, target, speed * Time.deltaTime);
            // One click as the jaws shut and one as they open, not a sound per frame of trigger travel.
            if (!closedCue && openness <= ClosedBelow)
            {
                closedCue = true;
                AudioManager.Instance?.PlayAt(closeSound, transform.position);
            }
            else if (closedCue && openness >= OpenAbove)
            {
                closedCue = false;
                AudioManager.Instance?.PlayAt(openSound, transform.position);
            }

            float a = openAngle * 0.5f * openness;
            if (upperJaw != null) upperJaw.localRotation = upperRest * Quaternion.Euler(-a, 0f, 0f);
            if (lowerJaw != null) lowerJaw.localRotation = lowerRest * Quaternion.Euler(a, 0f, 0f);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
