using SurgicalFoundations.Detection;
using UnityEngine;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Keeps a panel on the learner's left palm. While the palm is turned up the panel rides just above it (following
    /// the hand, never the head); when the hand is lowered the panel stays where it was in the room, so the other hand
    /// can keep using it, and turning the palm back up picks it up again. Nothing docked here ever moves with the camera.
    /// Without tracked hands or controllers (desk testing) the panel is placed once in front of the view and left there.
    /// </summary>
    public class PalmDock : MonoBehaviour
    {
        [Tooltip("Uniform scale of the panel while it is on the hand (panels are authored at room size).")]
        public float handScale = 0.55f;
        [Tooltip("Metres above the palm, along the palm normal.")]
        public float lift = 0.07f;
        [Tooltip("Metres towards the fingertips, so the panel clears the fingers of the other hand poking it.")]
        public float forward = 0.04f;
        [Tooltip("Room-size scale the panel returns to if it is ever placed in front of the view (no hands).")]
        public float roomScale = 1f;

        const float FollowShowDot = 0.55f, FollowHideDot = 0.3f, ReleaseSeconds = 0.35f, PositionSmoothing = 18f;

        Vector3 baseScale;
        bool following;
        float notFacingSince = -1f;

        public bool OnHand => following;

        void Awake() => baseScale = transform.localScale;

        /// <summary>Puts the panel on the palm now (or in front of the view when there is no hand).</summary>
        public void Dock()
        {
            float dot = PalmPose.FacingHead(true, out var pose);
            if (dot > -1f)
            {
                following = true;
                notFacingSince = -1f;
                Snap(pose, immediate: true);
                return;
            }
            following = false;
            PlaceInFrontOnce();
        }

        void LateUpdate()
        {
            float dot = PalmPose.FacingHead(true, out var pose);
            bool facing = dot > (following ? FollowHideDot : FollowShowDot);

            if (following)
            {
                if (facing) { notFacingSince = -1f; Snap(pose, immediate: false); }
                else
                {
                    // Hand lowered: after a moment the panel lets go of the hand and stays put in the room.
                    if (notFacingSince < 0f) notFacingSince = Time.unscaledTime;
                    if (Time.unscaledTime - notFacingSince > ReleaseSeconds) following = false;
                }
            }
            else if (facing)
            {
                following = true; // palm back up: pick the panel up again
                Snap(pose, immediate: false);
            }
        }

        /// <summary>Where a card sits on the palm: lifted off it, nudged towards the fingers, upright and facing the learner.</summary>
        public static (Vector3 position, Quaternion rotation) OnPalm(PalmPose.Pose pose, float lift, float forward)
        {
            var head = LearnerRig.Head;
            var pos = pose.centre + pose.normal * lift + pose.fingers * forward;
            var look = head != null ? pos - head.position : -pose.normal;
            return (pos, Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look : Vector3.forward, Vector3.up));
        }

        void Snap(PalmPose.Pose pose, bool immediate)
        {
            var (pos, rot) = OnPalm(pose, lift, forward);
            float k = immediate ? 1f : 1f - Mathf.Exp(-PositionSmoothing * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, k), Quaternion.Slerp(transform.rotation, rot, k));
            transform.localScale = baseScale * handScale;
        }

        void PlaceInFrontOnce()
        {
            var head = LearnerRig.Head != null ? LearnerRig.Head : (Camera.main != null ? Camera.main.transform : null);
            if (head == null) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var pos = head.position + fwd * 0.6f + Vector3.down * 0.15f;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            transform.localScale = baseScale * roomScale;
        }
    }
}
