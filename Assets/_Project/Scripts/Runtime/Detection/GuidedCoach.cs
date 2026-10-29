using System.Collections.Generic;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Guided mode's coaching on top of <see cref="GuidedHighlight"/>'s marker:
    ///  • a hint card over the next target that says what to do with it ("Pick up the clip applier", "Point at the
    ///    swab and pull the trigger to count it", "Put both hands under the water"…);
    ///  • a guide line from the nearer hand to the target, so it can be found when it is out of view;
    ///  • right / wrong feedback on every grab: picking up the thing the step wants plays the on-protocol cue, picking
    ///    up something else plays the denied cue and the card says what was picked up and what is needed.
    /// Hidden in Assessment mode (FR-19). Lives in 10_OR_Base next to the marker.
    /// </summary>
    public class GuidedCoach : MonoBehaviour
    {
        const float CardLift = 0.13f;
        const float CardFontSize = 0.24f;      // TMP 3D: ≈ 2.4 cm line height
        const float MatchRadius = 0.15f;       // the object a target belongs to is the named one within this distance
        const float LineMinDistance = 0.3f;    // no guide line once the hand is this close
        const float WrongMessageSeconds = 3.5f;
        const float RescanSeconds = 1f;

        static readonly Color Mint = new Color(0.36f, 0.88f, 0.78f);
        static readonly Color Amber = new Color(0.96f, 0.71f, 0.29f);

        TextMeshPro card;
        LineRenderer line;
        readonly List<Transform> candidates = new List<Transform>();
        readonly HashSet<XRGrabInteractable> hooked = new HashSet<XRGrabInteractable>();
        float nextScan;
        Vector3 lastTarget = new Vector3(float.NaN, 0, 0);
        Transform targetObject;
        string targetHint;
        string wrongMessage;
        float wrongUntil;
        Vector3 cardPos, placedFor;
        string placedText;
        float nextPlace;

        void Start()
        {
            var go = new GameObject("GuidedCoach_Card");
            go.transform.SetParent(transform, false);
            card = go.AddComponent<TextMeshPro>();
            card.fontSize = CardFontSize;
            card.alignment = TextAlignmentOptions.Center;
            card.textWrappingMode = TextWrappingModes.Normal;
            card.rectTransform.sizeDelta = new Vector2(0.45f, 0.12f);
            card.outlineWidth = 0.22f;
            card.outlineColor = new Color32(5, 20, 24, 255);
            go.SetActive(false);

            var lineGo = new GameObject("GuidedCoach_Line");
            lineGo.transform.SetParent(transform, false);
            line = lineGo.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = 0.004f;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = Rendering.RuntimeMaterials.Lit(new Color(Mint.r, Mint.g, Mint.b, 1f));
            line.enabled = false;
        }

        void OnDestroy()
        {
            foreach (var g in hooked) if (g != null) g.selectEntered.RemoveListener(OnGrab);
        }

        static bool Active
        {
            get
            {
                var s = SessionManager.Instance;
                return s == null || (s.IsRunning && !s.IsPaused && s.Settings.mode == TrainingMode.Guided);
            }
        }

        void Update()
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + RescanSeconds; Rescan(); }

            var stage = StageController.Current;
            var target = Active && stage != null ? stage.GuideTarget : null;
            var head = LearnerRig.Head;
            if (target == null || head == null)
            {
                card.gameObject.SetActive(false);
                line.enabled = false;
                return;
            }

            if ((target.Value - lastTarget).sqrMagnitude > 0.0004f)
            {
                lastTarget = target.Value;
                Describe(stage, target.Value, out targetObject, out targetHint);
            }

            // Hint card above the target, facing the learner.
            var wrong = Time.unscaledTime < wrongUntil;
            card.text = wrong ? wrongMessage : targetHint;
            card.color = wrong ? Amber : Mint;
            card.fontSize = CardFontSize * (SessionManager.Instance != null ? SessionManager.Instance.Settings.textScale : 1f);
            card.gameObject.SetActive(!string.IsNullOrEmpty(card.text));
            // Above the target, but never inside a prop or hidden behind one: re-solved when the target or the text
            // changes, and twice a second as the learner moves.
            if (card.text != placedText || (target.Value - placedFor).sqrMagnitude > 0.0004f || Time.unscaledTime >= nextPlace)
            {
                placedText = card.text;
                placedFor = target.Value;
                nextPlace = Time.unscaledTime + 0.5f;
                cardPos = UI.PanelClearance.Clear(target.Value + Vector3.up * CardLift, card.rectTransform.sizeDelta, head.position);
            }
            card.transform.SetPositionAndRotation(cardPos, Quaternion.LookRotation(cardPos - head.position, Vector3.up));

            // Guide line from whichever hand is nearer.
            var hand = NearerHand(target.Value);
            var showLine = hand != null && (hand.position - target.Value).sqrMagnitude > LineMinDistance * LineMinDistance;
            line.enabled = showLine;
            if (showLine)
            {
                line.SetPosition(0, hand.position);
                line.SetPosition(1, target.Value);
            }
        }

        static Transform NearerHand(Vector3 p)
        {
            var l = LearnerRig.Hand(true);
            var r = LearnerRig.Hand(false);
            if (l == null) return r;
            if (r == null) return l;
            return (l.position - p).sqrMagnitude <= (r.position - p).sqrMagnitude ? l : r;
        }

        /// <summary>Finds the named object at the target and words the hint for it.</summary>
        void Describe(StageController stage, Vector3 target, out Transform obj, out string hint)
        {
            obj = null;
            var best = MatchRadius * MatchRadius;
            foreach (var c in candidates)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var d = (c.position - target).sqrMagnitude;
                if (d < best) { best = d; obj = c; }
            }

            var counting = stage is PrepController && stage.CurrentStep == PrepController.TrayStepIndex;
            var name = obj != null ? obj.name : "";
            var instrument = InstrumentLabels.DisplayName(name);

            if (counting && obj != null)
                hint = $"Point at the {(instrument ?? Pretty(name)).ToLowerInvariant()} and pull the trigger to count it";
            else if (instrument != null) hint = $"Pick up the {instrument.ToLowerInvariant()}";
            else if (name.StartsWith("PPE_GlovesPacket")) hint = "Pick up the glove packet to put your gloves on";
            else if (name.StartsWith("PROP_SterileTowels")) hint = "Dry your hands on the sterile towel";
            else if (name.StartsWith("WaterSocket_")) hint = "Put both hands under the running water";
            else if (obj != null) hint = $"Next: {Pretty(name)}";
            else hint = "Next: here";
        }

        static string Pretty(string objectName)
        {
            var n = objectName.Split(' ')[0];
            var i = n.IndexOf('_');
            if (i >= 0 && i < n.Length - 1) n = n.Substring(i + 1);
            // CamelCase → words
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < n.Length; k++)
            {
                if (k > 0 && char.IsUpper(n[k]) && !char.IsUpper(n[k - 1])) sb.Append(' ');
                sb.Append(n[k] == '_' ? ' ' : n[k]);
            }
            return sb.ToString().ToLowerInvariant();
        }

        void Rescan()
        {
            candidates.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>())
                {
                    var n = t.name;
                    if (n.StartsWith("INST_") || n.StartsWith("PPE_") || n.StartsWith("PROP_") || n.StartsWith("WaterSocket_"))
                        candidates.Add(t);
                }
            }

            foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
                if (hooked.Add(grab)) grab.selectEntered.AddListener(OnGrab);
            hooked.RemoveWhere(g => g == null);
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            if (!Active || args.interactableObject == null) return;
            var stage = StageController.Current;
            var target = stage != null ? stage.GuideTarget : null;
            if (target == null || targetObject == null) return; // the step isn't about picking something up
            if (stage is PrepController && stage.CurrentStep == PrepController.TrayStepIndex) return; // counting is by pointing

            var grabbed = args.interactableObject.transform;
            var right = grabbed == targetObject || targetObject.IsChildOf(grabbed) || grabbed.IsChildOf(targetObject);
            var audio = Audio.AudioManager.Instance;
            if (right)
            {
                audio?.Play(Audio.SoundId.FB_OnProtocol);
                return;
            }

            audio?.Play(Audio.SoundId.UI_Denied);
            var got = InstrumentLabels.DisplayName(grabbed.name) ?? Pretty(grabbed.name);
            var want = InstrumentLabels.DisplayName(targetObject.name) ?? Pretty(targetObject.name);
            wrongMessage = $"That's the {got.ToLowerInvariant()}. You need the {want.ToLowerInvariant()}.";
            wrongUntil = Time.unscaledTime + WrongMessageSeconds;
        }
    }
}
