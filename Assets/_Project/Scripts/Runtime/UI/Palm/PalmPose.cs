using System.Collections.Generic;
using SurgicalFoundations.Detection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Where a palm is and which way it faces, from tracked hands (XR Hands palm joint) or, without hand tracking, from
    /// the controller grip. Shared by everything that lives on the hand: the palm hub, the settings dial, the pause card.
    /// </summary>
    public static class PalmPose
    {
        static readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        static XROrigin origin;

        public struct Pose
        {
            public Vector3 centre;
            public Vector3 normal;   // out of the palm
            public Vector3 fingers;  // from the wrist towards the fingertips
            public Vector3 thumb;    // across the palm towards the thumb
        }

        public static bool TryGet(bool left, out Pose pose)
        {
            pose = default;
            if (origin == null) origin = Object.FindAnyObjectByType<XROrigin>();
            var space = origin != null && origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : null;

            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);
            foreach (var hs in handSubsystems)
            {
                var hand = left ? hs.leftHand : hs.rightHand;
                if (!hs.running || !hand.isTracked || space == null) continue;
                if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var p)) continue;
                // XR Hands palm joint: +Y out of the back of the hand, +Z towards the fingers, +X towards the thumb on the
                // left hand (away from it on the right).
                pose.centre = space.TransformPoint(p.position);
                pose.normal = -space.TransformDirection(p.rotation * Vector3.up);
                pose.fingers = space.TransformDirection(p.rotation * Vector3.forward);
                pose.thumb = space.TransformDirection(p.rotation * (left ? Vector3.right : Vector3.left));
                return true;
            }

            var controller = LearnerRig.Hand(left);
            if (controller == null || !LearnerRig.Tracked(left)) return false;
            // Controllers: the palm wraps round the grip's inner side (+X on the left controller, −X on the right).
            pose.centre = controller.position;
            pose.normal = left ? controller.right : -controller.right;
            pose.fingers = controller.forward;
            pose.thumb = controller.up;
            return true;
        }

        /// <summary>
        /// How directly the palm faces the head (1 = straight at it), or −1 when the palm can't be found or is out of reach.
        /// </summary>
        public static float FacingHead(bool left, out Pose pose, float maxReach = 0.75f)
        {
            var head = LearnerRig.Head;
            if (head == null || !TryGet(left, out pose)) { pose = default; return -1f; }
            var toHead = head.position - pose.centre;
            if (toHead.magnitude > maxReach || toHead.sqrMagnitude < 1e-4f) return -1f;
            return Vector3.Dot(pose.normal, toHead.normalized);
        }
    }
}
