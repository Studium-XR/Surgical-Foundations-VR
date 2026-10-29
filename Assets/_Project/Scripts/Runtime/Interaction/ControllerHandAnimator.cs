using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Animates the hand shown on a controller (Resources/Hands, the XRI hand visual without hand tracking) from the
    /// controller's own inputs: the grip curls the middle, ring and little fingers, the trigger curls the index finger,
    /// and resting the thumb on the thumbstick or buttons bends the thumb down. With nothing pressed the hand rests
    /// slightly open round the controller.
    /// </summary>
    public class ControllerHandAnimator : MonoBehaviour
    {
        public bool leftHand;

        [SerializeField] float restCurl = 12f;      // degrees per joint with nothing pressed
        [SerializeField] float fullCurl = 70f;      // degrees per joint at full grip / trigger
        [SerializeField] float thumbCurl = 35f;
        [SerializeField] float followSpeed = 18f;

        class Joint
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 localAxis; // curl axis in the bone's parent space
            public int group;         // 0 index, 1 middle/ring/little, 2 thumb
        }

        readonly List<Joint> joints = new List<Joint>();
        readonly float[] current = new float[3];
        InputAction grip, trigger, thumbTouch;

        void Start()
        {
            var p = leftHand ? "L_" : "R_";
            Transform Bone(string n)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == p + n) return t;
                return null;
            }

            var wrist = Bone("Wrist");
            var indexProx = Bone("IndexProximal");
            var littleProx = Bone("LittleProximal");
            var middleProx = Bone("MiddleProximal");
            var thumbTip = Bone("ThumbTip");
            if (wrist == null || indexProx == null || littleProx == null || middleProx == null) { enabled = false; return; }

            // Palm normal, pointed to the thumb's side (the palm side), as in the builder.
            var fingers = (middleProx.position - wrist.position).normalized;
            var across = (littleProx.position - indexProx.position).normalized;
            var palm = Vector3.Cross(fingers, across).normalized;
            if (thumbTip != null && Vector3.Dot(thumbTip.position - indexProx.position, palm) < 0f) palm = -palm;

            foreach (var finger in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
            foreach (var j in finger == "Thumb" ? new[] { "Proximal", "Distal" } : new[] { "Proximal", "Intermediate", "Distal" })
            {
                var bone = Bone(finger + j);
                if (bone == null || bone.childCount == 0 || bone.parent == null) continue;
                var dir = (bone.GetChild(0).position - bone.position).normalized;
                var worldAxis = Vector3.Cross(dir, palm).normalized; // rotating about dir×palm turns the bone towards the palm
                joints.Add(new Joint
                {
                    bone = bone,
                    rest = bone.localRotation,
                    localAxis = bone.parent.InverseTransformDirection(worldAxis),
                    group = finger == "Index" ? 0 : finger == "Thumb" ? 2 : 1
                });
            }

            var hand = leftHand ? "{LeftHand}" : "{RightHand}";
            grip = new InputAction("grip", InputActionType.Value, $"<XRController>{hand}/grip");
            trigger = new InputAction("trigger", InputActionType.Value, $"<XRController>{hand}/trigger");
            thumbTouch = new InputAction("thumbTouch", InputActionType.Button);
            thumbTouch.AddBinding($"<XRController>{hand}/thumbstickTouched");
            thumbTouch.AddBinding($"<XRController>{hand}/primaryTouched");
            thumbTouch.AddBinding($"<XRController>{hand}/secondaryTouched");
            grip.Enable();
            trigger.Enable();
            thumbTouch.Enable();
        }

        void OnDestroy()
        {
            grip?.Dispose();
            trigger?.Dispose();
            thumbTouch?.Dispose();
        }

        void LateUpdate()
        {
            if (grip == null) return;
            var target = new[]
            {
                Mathf.Lerp(restCurl, fullCurl, trigger.ReadValue<float>()),
                Mathf.Lerp(restCurl, fullCurl, grip.ReadValue<float>()),
                thumbTouch.IsPressed() ? thumbCurl : restCurl * 0.5f
            };
            var k = Mathf.Clamp01(Time.deltaTime * followSpeed);
            for (int g = 0; g < 3; g++) current[g] = Mathf.Lerp(current[g], target[g], k);

            foreach (var j in joints)
                j.bone.localRotation = Quaternion.AngleAxis(current[j.group], j.localAxis) * j.rest;
        }
    }
}
