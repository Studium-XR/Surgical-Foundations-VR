using SurgicalFoundations.Audio;
using SurgicalFoundations.Lighting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// A laparoscopic instrument. Outside the patient it is held freely; introduced into a port it becomes
    /// fulcrum-constrained (FR-10): it pivots about the port, slides in and out and rolls with the wrist. Pulled right
    /// out, it is free again, which is how instruments are exchanged. Let go outside a port, it goes back to the Mayo
    /// stand rather than hanging in the air. Added at runtime to every instrument with a shaft (<see cref="InstrumentInstaller"/>).
    /// </summary>
    public class LapInstrument : MonoBehaviour
    {
        /// <summary>Tip this close to a port's valve, roughly lined up with it, goes in.</summary>
        const float EngageRadius = 0.035f;
        const float EngageAngle = 40f;
        /// <summary>How far the hand must pull past "tip at the valve" before the instrument comes out.</summary>
        const float WithdrawSlack = 0.05f; // more than EngageRadius, so going in and coming out can never overlap
        /// <summary>The handle stops this far above the port.</summary>
        const float HandleClearance = 0.05f;
        /// <summary>The scope's starting depth past its port: the instruments' tips and the task area lie ahead of the lens.</summary>
        const float ScopeWorkingInsertion = 0.09f;

        XRGrabInteractable grab;
        Transform tip;
        Vector3 gripLocal;
        float tipZ, minPortZ, maxPortZ;
        Vector3 restPosition;
        Quaternion restRotation;
        bool returnToRest;

        public InstrumentPort Port { get; private set; }
        public bool InPort => Port != null;
        public Transform Tip => tip;
        public InstrumentJaws Jaws { get; private set; }
        public bool Held => grab != null && grab.isSelected;
        /// <summary>Length of shaft beyond the port, in metres (0 when not in a port).</summary>
        public float Insertion => InPort && tip != null ? Mathf.Max(0f, Vector3.Dot(tip.position - Port.Fulcrum, transform.forward)) : 0f;
        /// <summary>The scope stays in its port: taking the camera out blinds the whole team.</summary>
        public bool CanWithdraw { get; set; } = true;
        /// <summary>The furthest the tip can go beyond the port before the handle meets it.</summary>
        public float MaxInsertion => tipZ - minPortZ;
        /// <summary>Whether a hand can take hold of it (the camera assistant keeps the scope otherwise).</summary>
        public bool Grabbable { set { if (grab != null) grab.enabled = value; } }

        /// <summary>Points the instrument along a direction through its port, with the tip this far inside.</summary>
        public void Aim(Vector3 direction, float insertion)
        {
            if (!InPort || direction.sqrMagnitude < 1e-8f) return;
            direction.Normalize();
            insertion = Mathf.Clamp(insertion, 0f, MaxInsertion);
            // Keep the roll it has: only the aim changes.
            var up = transform.up - direction * Vector3.Dot(transform.up, direction);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(direction, Vector3.right);
            transform.SetPositionAndRotation(Port.Fulcrum + direction * (insertion - tipZ), Quaternion.LookRotation(direction, up.normalized));
        }

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            Jaws = GetComponent<InstrumentJaws>();
            tip = Find("Pivot_Tip");
            var attach = grab != null && grab.attachTransform != null ? grab.attachTransform : transform;
            gripLocal = transform.InverseTransformPoint(attach.position);
            tipZ = tip != null ? transform.InverseTransformPoint(tip.position).z : 0.35f;
            minPortZ = HandleClearance;
            maxPortZ = tipZ;
            restPosition = transform.position;
            restRotation = transform.rotation;
        }

        void OnEnable()
        {
            if (grab != null) grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            if (grab != null) grab.selectExited.RemoveListener(OnReleased);
            if (Port != null && Port.Occupant == this) Port.Occupant = null;
        }

        void Start()
        {
            // Scenes are built with some instruments already through their ports (Pivot_Port on the skin mark).
            var portPivot = Find("Pivot_Port");
            if (portPivot == null) return;
            foreach (var port in InstrumentPort.All)
            {
                if (port.Occupant != null || (portPivot.position - port.Fulcrum).magnitude > 0.03f) continue;
                Engage(port, silent: true);
                // Its place on the Mayo stand, for when it is taken out and handed back.
                InstrumentInstaller.NextRestSlot(gameObject.scene, out restPosition, out restRotation);
                break;
            }

            // The picture comes from the scope: once it is in a port, the camera rides on it, at its tip. The scope is
            // drawn back to a working distance first: built in to the full depth, its tip sat past the instrument
            // tips, so they were behind the camera and could never be brought into view.
            if (InPort && name.Contains("Laparoscope"))
            {
                CanWithdraw = false;
                Aim(transform.forward, Mathf.Min(Insertion, ScopeWorkingInsertion));
                foreach (var feed in FindObjectsByType<LaparoscopeFeed>(FindObjectsInactive.Include))
                {
                    if (feed.gameObject.scene != gameObject.scene) continue;
                    // At the lens; the rig keeps the direction it was built with, ~30° off the shaft like a 30° scope.
                    if (tip != null) feed.transform.position = tip.position + transform.forward * 0.002f;
                    feed.transform.SetParent(transform, true);
                }
            }
        }

        void LateUpdate()
        {
            if (returnToRest)
            {
                returnToRest = false;
                if (!Held && !InPort) transform.SetPositionAndRotation(restPosition, restRotation);
            }
            if (!Held) return;

            var hand = grab.firstInteractorSelecting.GetAttachTransform(grab);
            if (InPort) DriveHeld(hand.position, hand.up);
            else TryEngage();
        }

        /// <summary>Moves the instrument as the hand at <paramref name="grip"/> would, about the port it is in.</summary>
        public void DriveHeld(Vector3 grip, Vector3 handUp)
        {
            if (!InPort) return;
            var pose = FulcrumSolver.Solve(grip, handUp, Port.Fulcrum, gripLocal, minPortZ, maxPortZ);
            if (CanWithdraw && pose.rawPortZ > maxPortZ + WithdrawSlack)
            {
                Disengage();
                return;
            }
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void TryEngage()
        {
            if (tip == null) return;
            foreach (var port in InstrumentPort.All)
            {
                if (port.Occupant != null) continue;
                if ((tip.position - port.Entry).magnitude > EngageRadius) continue;
                if (Vector3.Angle(transform.forward, port.Axis) > EngageAngle) continue;
                Engage(port, silent: false);
                return;
            }
        }

        void Engage(InstrumentPort port, bool silent)
        {
            Port = port;
            port.Occupant = this;
            // Constrained from the moment the tip enters the valve, all the way down the cannula and into the abdomen.
            maxPortZ = tipZ + (port.Entry - port.Fulcrum).magnitude;
            // From here the port decides where the instrument is; the hand only steers it.
            if (grab != null) { grab.trackPosition = false; grab.trackRotation = false; }
            if (!silent) AudioManager.Instance?.PlayAt(SoundId.INST_TrocarValve, port.Entry);
        }

        void Disengage()
        {
            if (Port != null && Port.Occupant == this) Port.Occupant = null;
            if (Port != null) AudioManager.Instance?.PlayAt(SoundId.INST_TrocarValve, Port.Entry);
            Port = null;
            if (grab != null) { grab.trackPosition = true; grab.trackRotation = true; }
        }

        void OnReleased(SelectExitEventArgs _)
        {
            // In a port the instrument stays where it was left, held by the abdominal wall. Outside one, it is handed back.
            if (!InPort) returnToRest = true;
        }

        Transform Find(string childName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }
    }
}
