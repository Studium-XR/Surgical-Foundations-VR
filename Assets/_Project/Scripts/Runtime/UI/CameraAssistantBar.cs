using System.Collections;
using SurgicalFoundations.Detection;
using UnityEngine;
using UnityEngine.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// The camera-assistant chips under the monitor. The six command chips ask the assistant to move the scope; the
    /// last chip switches between the assistant holding it and the learner holding it. Command chips go dim while the
    /// learner has the scope, because there is nobody to ask. The scene puts the bar under the monitor, behind the
    /// table and at hip height, out of reach; on start it moves in front of the learner, at chest height and arm's length,
    /// below the line of sight to the monitor.
    /// </summary>
    public class CameraAssistantBar : MonoBehaviour
    {
        // Order matches CameraCommand: left, right, up, down, zoom in, zoom out.
        [SerializeField] Button[] commands;
        [SerializeField] Button holdMyself;      // shown while the assistant holds the scope
        [SerializeField] Button holdingMyself;   // shown (selected) while the learner holds it
        [SerializeField] Vector3 offsetFromHead = new Vector3(0f, -0.45f, 0.6f); // right, down, forward (metres)
        [SerializeField] float scale = 0.6f; // the bar is 1.64 m wide as built; ~1 m reads well at arm's length

        void Awake()
        {
            for (int i = 0; i < commands.Length; i++)
            {
                var command = (CameraCommand)i;
                if (commands[i] != null) commands[i].onClick.AddListener(() => CameraAssistant.Instance?.Command(command));
            }
            if (holdMyself != null) holdMyself.onClick.AddListener(() => CameraAssistant.Instance?.SetSelfHold(true));
            if (holdingMyself != null) holdingMyself.onClick.AddListener(() => CameraAssistant.Instance?.SetSelfHold(false));
        }

        IEnumerator Start()
        {
            // Wait for the rig to be placed at the stage's spawn point (under the fade) before reading the head.
            yield return new WaitForSeconds(1f);
            var head = LearnerRig.Head;
            if (head == null) yield break;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var pos = head.position + right * offsetFromHead.x + Vector3.up * offsetFromHead.y + forward * offsetFromHead.z;
            // Canvas front is −Z: face the learner, tilted back a little like a lectern.
            var rot = Quaternion.LookRotation(pos - head.position, Vector3.up);
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale *= scale;
            // At chest height in front of the learner it can land in the table, the drape or an instrument: move it to
            // the nearest spot that is clear and in view.
            if (transform is RectTransform rt) PanelClearance.ClearCanvas(rt, head.position);
        }

        void Update()
        {
            var assistant = CameraAssistant.Instance;
            var ready = assistant != null && assistant.Ready;
            var self = ready && assistant.SelfHold;
            foreach (var chip in commands)
                if (chip != null && chip.interactable != (ready && !self)) chip.interactable = ready && !self;
            if (holdMyself != null && holdMyself.gameObject.activeSelf == self) holdMyself.gameObject.SetActive(!self);
            if (holdingMyself != null && holdingMyself.gameObject.activeSelf != self) holdingMyself.gameObject.SetActive(self);
        }
    }
}
