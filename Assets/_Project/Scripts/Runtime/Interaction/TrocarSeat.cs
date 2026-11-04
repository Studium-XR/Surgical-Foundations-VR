using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Holds a trocar's tip on its entry point once it is in the skin: the hand still sets the angle and how far it
    /// goes in, but the tip can't slide across the abdomen or wobble off the site with hand tremor. Runs after the
    /// default grab transformer and moves its target pose along the trocar's own axis so the axis passes through the
    /// entry point. Drawing the tip back out of the skin lets go of the seat.
    /// </summary>
    public class TrocarSeat : XRBaseGrabTransformer
    {
        /// <summary>Drawn back this far above the entry point, the trocar is free again.</summary>
        const float LiftOff = 0.012f;

        Transform tip;

        public bool Seated { get; private set; }
        public Vector3 EntryPoint { get; private set; }

        // Registered by Install, after the default transformer, never automatically (the order matters).
        protected override RegistrationMode registrationMode => RegistrationMode.None;

        /// <summary>Adds the seat to a trocar, behind its default grab transformer (added first if it has none yet).</summary>
        public static TrocarSeat Install(XRGrabInteractable grab, Transform tip)
        {
            var seat = grab.GetComponent<TrocarSeat>();
            if (seat != null) return seat;
            if (grab.singleGrabTransformersCount == 0 && grab.startingSingleGrabTransformers.Count == 0)
            {
                var general = grab.GetComponent<XRGeneralGrabTransformer>() ?? grab.gameObject.AddComponent<XRGeneralGrabTransformer>();
                grab.AddSingleGrabTransformer(general);
                grab.AddMultipleGrabTransformer(general);
            }
            seat = grab.gameObject.AddComponent<TrocarSeat>();
            seat.tip = tip;
            grab.AddSingleGrabTransformer(seat);
            grab.AddMultipleGrabTransformer(seat);
            return seat;
        }

        public void Seat(Vector3 entryPoint)
        {
            EntryPoint = entryPoint;
            Seated = true;
        }

        public void Release() => Seated = false;

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (!Seated || tip == null) return;
            var axis = targetPose.rotation * Vector3.forward; // instruments: long axis +Z, tip at +Z
            var tipOffset = targetPose.rotation * (Quaternion.Inverse(transform.rotation) * (tip.position - transform.position));
            var depth = Vector3.Dot(targetPose.position + tipOffset - EntryPoint, axis);
            if (depth < -LiftOff) { Seated = false; return; }
            targetPose.position = EntryPoint + axis * Mathf.Max(depth, 0f) - tipOffset;
        }
    }
}
