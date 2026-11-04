using System;
using SurgicalFoundations.Contracts;

namespace SurgicalFoundations.Detection
{
    /// <summary>How detectors report: class, code (see <see cref="EventCodes"/>), learner-facing message.</summary>
    public delegate void ProtocolLog(EventClass eventClass, string code, string message);

    public enum StepState { Pending, Active, Done, Skipped }

    // ───────────────────────────── Surgical scrub ─────────────────────────────

    public enum ScrubStep { Inspect = 0, PreWash = 1, CleanNails = 2, Scrub = 3, Rinse = 4, Dry = 5 }

    /// <summary>What the hands are doing this frame, as measured by <c>ScrubSensor</c>.</summary>
    public struct ScrubSample
    {
        /// <summary>Both hands held up in front of the eyes (checking nails and jewellery).</summary>
        public bool inspecting;
        /// <summary>Both hands under the running tap.</summary>
        public bool inWater;
        public bool handsTogether;
        /// <summary>One hand on the other forearm (wrist to elbow), as when scrubbing up the arms.</summary>
        public bool onForearm;
        /// <summary>Hands moving against each other.</summary>
        public bool rubbing;
        /// <summary>Fingers pointing up, so water runs from fingertips to elbows.</summary>
        public bool fingertipsUp;
        /// <summary>Both hands at the sterile towels.</summary>
        public bool atTowel;
        /// <summary>A hand below the level of the elbows/table.</summary>
        public bool handsDropped;
    }

    /// <summary>
    /// The six scrub steps in order (screen 05). A step completes after its action is held for its required time.
    /// Doing a later step's action for <see cref="AheadSeconds"/> skips the steps in between, each logged as a deviation.
    /// Durations are compressed for a 15-minute session and, like the sequence itself, await SME sign-off.
    /// </summary>
    public class ScrubSequence
    {
        public const int StepCount = 6;
        public const float AheadSeconds = 2f;

        public static readonly string[] Labels =
        {
            "Remove jewellery, check nails", "Pre-wash hands and forearms", "Clean under nails",
            "Scrub fingers, hands, forearms", "Rinse, fingertips up", "Dry with sterile towel"
        };

        readonly float[] required;
        readonly float[] progress = new float[StepCount];
        readonly StepState[] states = new StepState[StepCount];
        readonly ProtocolLog log;
        int aheadStep = -1;
        float aheadTime;
        bool handsDroppedLogged;

        public ScrubSequence(float scrubSeconds, ProtocolLog log)
        {
            this.log = log;
            required = new[] { 2f, 5f, 5f, Math.Max(1f, scrubSeconds), 5f, 3f };
            states[0] = StepState.Active;
        }

        public event Action Changed;

        /// <summary>Index of the step the learner should be doing; <see cref="StepCount"/> once complete.</summary>
        public int Expected { get; private set; }
        public bool Complete => Expected >= StepCount;
        public StepState State(int step) => states[step];
        public float Progress(int step) => Math.Min(1f, progress[step] / required[step]);
        public float RemainingSeconds(int step) => Math.Max(0f, required[step] - progress[step]);

        public void Tick(ScrubSample s, float dt)
        {
            if (Complete || dt <= 0f) return;

            var action = Classify(s, Expected);
            if (action == Expected)
            {
                aheadStep = -1;
                progress[Expected] += dt;
                if (progress[Expected] >= required[Expected]) Finish(Expected);
                else Changed?.Invoke();
            }
            else if (action > Expected)
            {
                if (aheadStep != action) { aheadStep = action; aheadTime = 0f; }
                aheadTime += dt;
                if (aheadTime >= AheadSeconds)
                {
                    for (int i = Expected; i < action; i++) Skip(i);
                    Expected = action;
                    states[action] = StepState.Active;
                    progress[action] = aheadTime;
                    aheadStep = -1;
                    Changed?.Invoke();
                }
            }
            else
            {
                aheadStep = -1;
            }

            // After rinsing, hands stay up until dry: dropping them lets water run back from the elbows.
            if (!Complete && Expected > (int)ScrubStep.Rinse && s.handsDropped && !handsDroppedLogged)
            {
                handsDroppedLogged = true;
                log(EventClass.Delayed, EventCodes.ScrubHandsDropped, "Hands dropped below the elbows after rinsing");
            }
        }

        /// <summary>The learner moved on without finishing: every remaining step is a logged deviation.</summary>
        public void SkipRemaining()
        {
            if (Complete) return;
            for (int i = Expected; i < StepCount; i++) Skip(i);
            Expected = StepCount;
            Changed?.Invoke();
        }

        /// <summary>Which step (0–5) the current hand activity belongs to; −1 for none. Depends on where the learner is in the sequence.</summary>
        public static int Classify(ScrubSample s, int expected)
        {
            if (s.atTowel) return (int)ScrubStep.Dry;
            // Scrubbing hands or forearms counts with the tap still running, as it is usually done.
            var scrubbing = s.rubbing && (s.handsTogether || s.onForearm);
            if (scrubbing && expected == (int)ScrubStep.Scrub) return (int)ScrubStep.Scrub;
            if (s.inWater)
            {
                // Before scrubbing, water means washing; after it, fingertips-up under the tap is the rinse.
                if (expected <= (int)ScrubStep.PreWash) return (int)ScrubStep.PreWash;
                if (expected == (int)ScrubStep.CleanNails) return s.handsTogether ? (int)ScrubStep.CleanNails : (int)ScrubStep.PreWash;
                if (s.fingertipsUp) return (int)ScrubStep.Rinse;
                return s.handsTogether ? (int)ScrubStep.CleanNails : (int)ScrubStep.PreWash;
            }
            if (scrubbing) return (int)ScrubStep.Scrub;
            if (s.inspecting) return (int)ScrubStep.Inspect;
            return -1;
        }

        void Finish(int step)
        {
            states[step] = StepState.Done;
            log(EventClass.OnProtocol, EventCodes.ScrubStepDone, Labels[step]);
            Expected = step + 1;
            if (Complete) log(EventClass.OnProtocol, EventCodes.ScrubComplete, "Surgical scrub complete");
            else states[Expected] = StepState.Active;
            Changed?.Invoke();
        }

        void Skip(int step)
        {
            if (states[step] == StepState.Done) return;
            states[step] = StepState.Skipped;
            log(EventClass.Deviation, EventCodes.ScrubStepSkipped, "Scrub step skipped: " + Labels[step].ToLowerInvariant());
        }
    }

    // ───────────────────────────── Sterility (FR-07) ─────────────────────────────

    public enum HandSterility { Bare, Scrubbed, Gloved, Contaminated }

    /// <summary>
    /// Each hand's sterile state. A gloved hand that touches a non-sterile surface is contaminated (one deviation per
    /// break); re-gloving restores it and is logged too, so the score records both the break and the recovery.
    /// </summary>
    public class SterilityState
    {
        readonly ProtocolLog log;

        public SterilityState(ProtocolLog log) => this.log = log;

        public event Action Changed;

        public HandSterility Left { get; private set; }
        public HandSterility Right { get; private set; }
        public bool AnyContaminated => Left == HandSterility.Contaminated || Right == HandSterility.Contaminated;
        public bool Sterile => Left == HandSterility.Gloved && Right == HandSterility.Gloved;
        /// <summary>The surface of the most recent break, for the recovery card.</summary>
        public string LastBreak { get; private set; }

        public void MarkScrubbed()
        {
            if (Left == HandSterility.Bare) Left = HandSterility.Scrubbed;
            if (Right == HandSterility.Bare) Right = HandSterility.Scrubbed;
            Changed?.Invoke();
        }

        /// <summary>Already gowned and gloved, with no event: for stages entered directly (retry, editor).</summary>
        public void StartGloved()
        {
            Left = Right = HandSterility.Gloved;
            Changed?.Invoke();
        }

        /// <summary>Gloves on (or changed after a break).</summary>
        public void Glove()
        {
            var recovering = AnyContaminated;
            if (Sterile) return;
            Left = Right = HandSterility.Gloved;
            if (recovering) log(EventClass.OnProtocol, EventCodes.Regloved, "Re-gloved · sterility restored");
            else log(EventClass.OnProtocol, EventCodes.Gloved, "Gowned and gloved");
            Changed?.Invoke();
        }

        /// <summary>A hand touched something non-sterile. Only a sterile glove can be contaminated.</summary>
        public void Contact(bool leftHand, string surface)
        {
            if ((leftHand ? Left : Right) != HandSterility.Gloved) return;
            if (leftHand) Left = HandSterility.Contaminated; else Right = HandSterility.Contaminated;
            LastBreak = $"{(leftHand ? "Left" : "Right")} glove touched {surface}";
            log(EventClass.Deviation, EventCodes.Contamination, LastBreak);
            Changed?.Invoke();
        }
    }
}
