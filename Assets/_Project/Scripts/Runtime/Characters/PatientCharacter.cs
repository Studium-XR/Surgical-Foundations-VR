using System.Collections;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Detection;
using SurgicalFoundations.Procedures;
using UnityEngine;

namespace SurgicalFoundations.Characters
{
    /// <summary>
    /// An awake patient who moves and talks. Lying on the table it breathes and turns its head; sitting it rests, nods
    /// while listening, gestures while talking, waves hello and doubles over in pain. Whatever it is doing, its head
    /// follows the learner (Animator IK look-at), so it keeps eye contact through a conversation. Speech is subtitled and
    /// spoken by the headset's text-to-speech; the body plays the talking clip for as long as the line takes to say.
    /// Motion: HY-Motion clips retargeted onto the rigged patient (Build → 1j · Patient Character).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PatientCharacter : MonoBehaviour
    {
        public enum Pose { Lying, Sitting }

        [SerializeField] Pose pose = Pose.Lying;
        [Range(0f, 1f)] [SerializeField] float lookWeight = 0.85f;
        [SerializeField] float voicePitch = 0.92f;

        Animator animator;
        float lookBlend;
        Coroutine speaking;

        public Pose CurrentPose => pose;
        public bool Speaking { get; private set; }

        const string LieIdle = "patient_lie_breathe", LieTalk = "patient_lie_talk";
        const string SitIdle = "patient_sit_idle", SitTalk = "patient_sit_talk", SitListen = "patient_sit_listen",
            SitPain = "patient_sit_pain", SitWave = "patient_sit_wave";

        string Idle => pose == Pose.Lying ? LieIdle : SitIdle;
        string Talk => pose == Pose.Lying ? LieTalk : SitTalk;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
        }

        void OnEnable() => Play(Idle, 0f);

        public void SetPose(Pose p)
        {
            pose = p;
            Play(Idle, 0.3f);
        }

        // ─────────────────────────────── behaviour ───────────────────────────────

        /// <summary>Says a line: subtitle, voice, and the talking motion for as long as it takes.</summary>
        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            AudioManager.Instance?.Say(line);
            Speech.Say(line, voicePitch);
            if (speaking != null) StopCoroutine(speaking);
            speaking = StartCoroutine(SpeakFor(Mathf.Clamp(line.Split(' ').Length * 0.38f, 1.6f, 7f)));
        }

        IEnumerator SpeakFor(float seconds)
        {
            Speaking = true;
            Play(Talk, 0.25f);
            yield return new WaitForSeconds(seconds);
            Speaking = false;
            Play(Idle, 0.4f);
            speaking = null;
        }

        /// <summary>Nods along while the learner chooses what to say (sitting); lying it just keeps looking.</summary>
        public void Listen()
        {
            if (Speaking || pose != Pose.Sitting) return;
            PlayOnce(SitListen);
        }

        public void Pain()
        {
            if (pose == Pose.Sitting) PlayOnce(SitPain);
        }

        public void Wave()
        {
            if (pose == Pose.Sitting) PlayOnce(SitWave);
        }

        public void Gesture(string name)
        {
            switch (name)
            {
                case "pain": Pain(); break;
                case "wave": Wave(); break;
                case "listen": Listen(); break;
            }
        }

        void PlayOnce(string state)
        {
            if (!Has(state)) return;
            Play(state, 0.25f);
            StartCoroutine(BackToIdleAfter(state));
        }

        IEnumerator BackToIdleAfter(string state)
        {
            yield return null;
            var info = animator.GetCurrentAnimatorStateInfo(0);
            float wait = Mathf.Max(0.5f, (animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).length : info.length) - 0.3f);
            yield return new WaitForSeconds(wait);
            if (!Speaking) Play(Idle, 0.4f);
        }

        void Play(string state, float fade)
        {
            if (animator == null || animator.runtimeAnimatorController == null || !Has(state)) return;
            if (fade <= 0f) animator.Play(state, 0, Random.value);
            else animator.CrossFadeInFixedTime(state, fade, 0);
        }

        bool Has(string state) => animator.HasState(0, Animator.StringToHash(state));

        // ─────────────────────────────── eye contact ───────────────────────────────

        void Update() => lookBlend = Mathf.MoveTowards(lookBlend, lookWeight, Time.deltaTime);

        void OnAnimatorIK(int layer)
        {
            var head = LearnerRig.Head;
            if (head == null) { animator.SetLookAtWeight(0f); return; }
            // Lying, the neck can turn only so far: less body, a tighter clamp.
            if (pose == Pose.Lying) animator.SetLookAtWeight(lookBlend, 0f, 0.7f, 1f, 0.7f);
            else animator.SetLookAtWeight(lookBlend, 0.15f, 0.85f, 1f, 0.55f);
            animator.SetLookAtPosition(head.position);
        }

        // ─────────────────────────────── placement ───────────────────────────────

        /// <summary>
        /// Lays the patient on the table: once the lying pose has been evaluated, turns the body so head points along
        /// <paramref name="cranial"/> and moves it so the pelvis sits under the given umbilicus.
        /// </summary>
        public IEnumerator LieOn(Vector3 umbilicus, Vector3 cranial)
        {
            SetPose(Pose.Lying);
            Play(LieIdle, 0f);
            yield return null;
            yield return null;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (hips == null || head == null) yield break;
            var along = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up);
            var want = Vector3.ProjectOnPlane(cranial, Vector3.up);
            if (along.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.FromToRotation(along.normalized, want.normalized) * transform.rotation;
            yield return null;
            // The pelvis joint lies about 9 cm caudal to the navel and 13 cm below the skin of the belly.
            var target = umbilicus - want.normalized * 0.09f + Vector3.down * 0.13f;
            transform.position += target - hips.position;
        }

        /// <summary>Seats the patient at <paramref name="seat"/> (the floor point under the hips), facing <paramref name="face"/>.</summary>
        public IEnumerator SitAt(Vector3 seat, Vector3 face)
        {
            SetPose(Pose.Sitting);
            Play(SitIdle, 0f);
            yield return null;
            yield return null;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var kneeL = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var kneeR = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (hips == null || kneeL == null || kneeR == null) yield break;
            var forward = Vector3.ProjectOnPlane((kneeL.position + kneeR.position) * 0.5f - hips.position, Vector3.up);
            var want = Vector3.ProjectOnPlane(face - seat, Vector3.up);
            if (forward.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.FromToRotation(forward.normalized, want.normalized) * transform.rotation;
            yield return null;
            var flat = new Vector3(hips.position.x, transform.position.y, hips.position.z);
            transform.position += new Vector3(seat.x, transform.position.y, seat.z) - flat;
        }
    }
}
