using SurgicalFoundations.Audio;
using UnityEngine;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// A haemostat's ratchet. Squeezing the trigger all the way shuts the jaws and locks them: they stay shut after the
    /// trigger is let go, and stay shut when the clamp is put down. A second firm squeeze, after letting go, releases
    /// the ratchet and the jaws spring open with the trigger.
    /// </summary>
    [RequireComponent(typeof(InstrumentJaws))]
    public class RatchetLock : MonoBehaviour
    {
        const float FirmSqueeze = 0.92f;
        const float LetGo = 0.25f;

        InstrumentJaws jaws;
        bool armed = true; // the trigger has been let go since the last lock or unlock

        public bool IsLocked => jaws != null && jaws.Locked;

        void Awake() => jaws = GetComponent<InstrumentJaws>();

        void Update()
        {
            float t = jaws.Trigger;
            if (t < LetGo) { armed = true; return; }
            if (!armed || t < FirmSqueeze) return;

            armed = false;
            if (!jaws.Locked)
            {
                if (jaws.Openness > 0.15f) { armed = true; return; } // not shut yet: keep squeezing
                jaws.Locked = true;
                AudioManager.Instance?.PlayAt(SoundId.INST_JawClose, transform.position);
            }
            else
            {
                jaws.Locked = false;
                AudioManager.Instance?.PlayAt(SoundId.INST_JawOpen, transform.position);
            }
        }
    }
}
