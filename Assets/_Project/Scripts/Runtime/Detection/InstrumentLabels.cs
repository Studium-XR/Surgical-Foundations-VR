using System.Collections.Generic;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using TMPro;
using UnityEngine;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Guided mode: a small name tag floating above every surgical instrument (INST_*) so the learner learns what each
    /// one is called. Tags face the learner, fade in only within reach, follow the instrument when it is picked up,
    /// and are hidden in Assessment mode. Installs itself; nothing has to be added to the scenes.
    /// </summary>
    public class InstrumentLabels : MonoBehaviour
    {
        const float ScanInterval = 0.5f;
        const float ShowDistance = 1.6f;
        const float Lift = 0.04f;
        const float FontSize = 0.014f;
        /// <summary>An instrument this close to a hand is labelled (the one being reached for or held).</summary>
        const float HandRadius = 0.2f;
        /// <summary>Otherwise only the one instrument nearest the centre of view, within this angle, is labelled.</summary>
        const float GazeAngle = 12f;

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "INST_AtraumaticGrasper", "Atraumatic grasper" },
            { "INST_ClipApplier", "Clip applier" },
            { "INST_LapScissors", "Laparoscopic scissors" },
            { "INST_Laparoscope30", "Laparoscope, 30°" },
            { "INST_MarylandDissector", "Maryland dissector" },
            { "INST_NeedleHolder", "Needle holder" },
            { "INST_Scalpel", "Scalpel" },
            { "INST_SurgicalClip", "Surgical clip" },
            { "INST_Swab", "Swab" },
            { "INST_Trocar12", "Trocar, 12 mm" },
            { "INST_Trocar5", "Trocar, 5 mm" },
            { "INST_TissueForceps", "Tissue forceps" },
            { "INST_MayoScissors", "Mayo scissors" },
            { "INST_Hemostat", "Haemostat" },
            { "INST_Retractor", "Retractor" },
            { "INST_Suction", "Suction tip" },
        };

        /// <summary>Readable name of an INST_* object (" (1)" suffixes ignored), or null if it isn't a known instrument.</summary>
        public static string DisplayName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            return Names.TryGetValue(objectName.Split(' ')[0], out var label) ? label : null;
        }

        class Tag
        {
            public Transform item;
            public Renderer[] renderers;
            public TextMeshPro text;
            public Vector3 top;
            public bool show;
            public Vector3 placedFor = new Vector3(float.NaN, 0f, 0f); // instrument top the clear spot was found for
            public Vector3 placed;
        }

        static bool NearHand(Vector3 p, Transform hand) => hand != null && (hand.position - p).sqrMagnitude <= HandRadius * HandRadius;

        readonly List<Tag> tags = new List<Tag>();
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<InstrumentLabels>() != null) return;
            var go = new GameObject("InstrumentLabels");
            DontDestroyOnLoad(go);
            go.AddComponent<InstrumentLabels>();
        }

        static bool Guided
        {
            get
            {
                var s = SessionManager.Instance;
                return s == null || s.Settings.mode == TrainingMode.Guided;
            }
        }

        void Update()
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + ScanInterval; Scan(); }

            var head = LearnerRig.Head;
            var show = Guided && head != null;
            var leftHand = LearnerRig.Hand(true);
            var rightHand = LearnerRig.Hand(false);

            // Pick what to label: whatever is at a hand, plus the single instrument nearest the centre of view.
            // Labelling everything in range piled a dozen tags on top of each other over the back table.
            Tag gazed = null;
            var bestAngle = GazeAngle;
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                var t = tags[i];
                if (t.item == null || t.text == null)
                {
                    if (t.text != null) Destroy(t.text.gameObject);
                    tags.RemoveAt(i);
                    continue;
                }
                t.top = TopOf(t);
                t.show = false;
                if (!show || !t.item.gameObject.activeInHierarchy) continue;
                var to = t.top - head.position;
                if (to.sqrMagnitude > ShowDistance * ShowDistance) continue;

                if (NearHand(t.top, leftHand) || NearHand(t.top, rightHand)) { t.show = true; continue; }
                var angle = Vector3.Angle(head.forward, to);
                if (angle < bestAngle) { bestAngle = angle; gazed = t; }
            }
            if (gazed != null) gazed.show = true;

            var size = FontSize * 10f * (SessionManager.Instance != null ? SessionManager.Instance.Settings.textScale : 1f);
            foreach (var t in tags)
            {
                if (t.text.gameObject.activeSelf != t.show) t.text.gameObject.SetActive(t.show);
                if (!t.show) continue;
                if (!Mathf.Approximately(t.text.fontSize, size)) t.text.fontSize = size; // follows the Text size setting
                var tr = t.text.transform;
                // Clear of the other instruments and props, solved again only once the instrument has moved (a held
                // one just floats at its lift, so it doesn't jitter while being carried).
                var held = NearHand(t.top, leftHand) || NearHand(t.top, rightHand);
                if (held) t.placed = t.top + Vector3.up * Lift;
                else if ((t.top - t.placedFor).sqrMagnitude > 0.0001f || float.IsNaN(t.placedFor.x))
                {
                    t.placedFor = t.top;
                    var width = Mathf.Min(t.text.preferredWidth, t.text.rectTransform.sizeDelta.x);
                    t.placed = UI.PanelClearance.Clear(t.top + Vector3.up * Lift, new Vector2(width, size * 0.12f), head.position, t.item);
                }
                tr.position = t.placed;
                tr.rotation = Quaternion.LookRotation(tr.position - head.position, Vector3.up);
            }
        }

        void Scan()
        {
            foreach (var r in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("INST_")) continue;
                var key = r.name.Split(' ')[0]; // instances keep the prefab name, sometimes with a " (1)" suffix
                if (!Names.TryGetValue(key, out var label)) continue;
                if (tags.Exists(t => t.item == r)) continue;
                // Only the instrument's own root: skip INST_ children nested inside another tagged instrument.
                if (HasTaggedAncestor(r)) continue;
                tags.Add(Create(r, label));
            }
        }

        static bool HasTaggedAncestor(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name.StartsWith("INST_")) return true;
            return false;
        }

        Tag Create(Transform item, string label)
        {
            var go = new GameObject("Label_" + item.name);
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = FontSize * 10f; // TMP's 3D text: font size 1 ≈ 0.1 m line height
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.36f, 0.88f, 0.78f);
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(5, 20, 24, 255);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.05f);
            go.SetActive(false);
            return new Tag { item = item, renderers = item.GetComponentsInChildren<Renderer>(), text = tmp };
        }

        static Vector3 TopOf(Tag t)
        {
            var found = false;
            var b = new Bounds(t.item.position, Vector3.zero);
            foreach (var r in t.renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found ? new Vector3(b.center.x, b.max.y, b.center.z) : t.item.position;
        }
    }
}
