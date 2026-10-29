using System;
using System.Collections.Generic;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Detection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// In-headset quick settings, laid out as a dial: six settings round a ring (eye height, posture, text size, mode,
    /// subtitles, recenter), the chosen one's value on the centre knob, and its options in a fading list beside the dial
    /// joined to the segment by a leader line, with up/down steppers. Changes apply at once so the learner can feel them
    /// (eye height especially); Save keeps them for next time, Cancel puts back what was there when the dial opened.
    /// Opens with Y on the left controller, the menu button that appears over the left palm when it is turned towards
    /// the learner, or F2 at the desk. Built in code and installed by itself, so no scene has to change.
    /// </summary>
    public class QuickSettingsDial : MonoBehaviour
    {
        // Canvas: 1 px = 0.5 mm.
        const float PxToMetres = 0.0005f;
        const float Width = 980f, Height = 640f;
        const float RingOuter = 200f, RingInner = 112f, KnobD = 196f;
        const float SegmentGapDeg = 4f;
        const float RowH = 62f, RowGap = 12f, RowW = 300f;
        const int VisibleRows = 5;
        static readonly Vector2 DialCentre = new Vector2(-215f, -5f);
        static readonly Vector2 ListCentre = new Vector2(205f, -5f);

        // Palette: graphite glass from the reference, mint from the project theme.
        static readonly Color Panel = Hex("17181C", 0.97f), RingBack = Hex("1F2125"), Segment = Hex("2A2D33"),
            SegmentHover = Hex("343840"), SegmentSelected = Hex("40454E"), Knob = Hex("E7E8EB"), KnobInner = Hex("F3F4F6"),
            Row = Hex("25272C"), RowSelected = Hex("30333A"), Text = Hex("D6D9DE"), TextMuted = Hex("8A9097"),
            TextOnKnob = Hex("1B1C20"), Mint = Hex("5CE0C8"), Line = Hex("6E737B");

        class Setting
        {
            public string name;
            public string hint;
            public Func<int> current;
            public Func<int, string> label;
            public int count;
            public Action<int> choose;
            public Func<bool> locked;
            public bool isAction; // the row runs something instead of selecting a value
        }

        public static QuickSettingsDial Instance { get; private set; }
        public bool IsOpen => root != null && root.gameObject.activeSelf;

        readonly List<Setting> settings = new List<Setting>();
        readonly List<Image> segments = new List<Image>();
        readonly List<Image> rows = new List<Image>();
        readonly List<TextMeshProUGUI> rowTexts = new List<TextMeshProUGUI>();
        readonly List<int> rowValues = new List<int>();
        RectTransform root;
        Canvas canvas;
        TextMeshProUGUI knobName, knobValue, hintText, stepText;
        RectTransform leader;
        int selected;
        int hovered = -1;
        InputAction toggle;
        Snapshot snapshot;
        bool prefsApplied;

        // Wrist opener for hand tracking.
        RectTransform wrist;

        struct Snapshot
        {
            public float offset, textScale;
            public Posture posture;
            public TrainingMode mode;
            public bool subtitles;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<QuickSettingsDial>() != null) return;
            var go = new GameObject("QuickSettingsDial");
            DontDestroyOnLoad(go);
            go.AddComponent<QuickSettingsDial>();
        }

        void Awake()
        {
            Instance = this;
            DefineSettings();
            Build();
            BuildWristButton();
            root.gameObject.SetActive(false);

            toggle = new InputAction("QuickSettings", InputActionType.Button);
            toggle.AddBinding("<XRController>{LeftHand}/secondaryButton"); // Y
            toggle.AddBinding("<Keyboard>/f2");                            // desk testing
            toggle.performed += _ => Toggle();
            toggle.Enable();
        }

        void OnDestroy()
        {
            toggle?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────── settings ───────────────────────────────

        static readonly float[] TextScales = { 0.9f, 1f, 1.15f };

        void DefineSettings()
        {
            int heightSteps = Mathf.RoundToInt((HeightCalibrator.MaxUserOffset - HeightCalibrator.MinUserOffset) / 0.05f) + 1;
            settings.Add(new Setting
            {
                name = "Eye height",
                hint = "Raise or lower your view. 0 is your measured height.",
                count = heightSteps,
                current = () => Mathf.RoundToInt((HeightCalibrator.UserOffset - HeightCalibrator.MinUserOffset) / 0.05f),
                label = i =>
                {
                    int cm = Mathf.RoundToInt((HeightCalibrator.MinUserOffset + i * 0.05f) * 100f);
                    return cm == 0 ? "0 cm (auto)" : (cm > 0 ? "+" : "") + cm + " cm";
                },
                choose = i => HeightCalibrator.SetUserOffset(HeightCalibrator.MinUserOffset + i * 0.05f),
            });
            settings.Add(new Setting
            {
                name = "Posture",
                hint = "Seated lifts your view to a standing surgeon's eye height.",
                count = 2,
                current = () => Session != null && Session.Settings.posture == Posture.Seated ? 1 : 0,
                label = i => i == 0 ? "Standing" : "Seated",
                choose = i => Session?.SetPosture(i == 0 ? Posture.Standing : Posture.Seated),
            });
            settings.Add(new Setting
            {
                name = "Text size",
                hint = "Size of the writing on every panel and label.",
                count = TextScales.Length,
                current = () => Nearest(TextScales, Session != null ? Session.Settings.textScale : 1f),
                label = i => i == 0 ? "Small" : i == 1 ? "Medium" : "Large",
                choose = i => Session?.SetTextScale(TextScales[i]),
            });
            settings.Add(new Setting
            {
                name = "Mode",
                hint = "Guided shows hints, highlights and sounds. Assessment is scored with no help.",
                count = 2,
                current = () => Session != null && Session.Settings.mode == TrainingMode.Assessment ? 1 : 0,
                label = i => i == 0 ? "Guided" : "Assessment",
                choose = i => Session?.SetMode(i == 0 ? TrainingMode.Guided : TrainingMode.Assessment),
                locked = () => Session != null && Session.IsRunning, // switching mid-session would mix two scorings
            });
            settings.Add(new Setting
            {
                name = "Subtitles",
                hint = "Show what the team and the coach say as text.",
                count = 2,
                current = () => Session != null && !Session.Settings.subtitles ? 1 : 0,
                label = i => i == 0 ? "On" : "Off",
                choose = i => Session?.SetSubtitles(i == 0),
            });
            settings.Add(new Setting
            {
                name = "Recenter",
                hint = "Put yourself back at this step's standing spot, facing the work.",
                count = 1,
                current = () => 0,
                label = _ => "Recenter now",
                choose = _ => Recenter(),
                isAction = true,
            });
        }

        static SessionManager Session => SessionManager.Instance;

        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        static void Recenter()
        {
            PlayerSpawnPoint best = null;
            foreach (var sp in FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None))
                if (sp.isActiveAndEnabled && (best == null || sp.Priority > best.Priority)) best = sp;
            if (best != null) SceneLoader.Instance?.PlaceRigAt(best.transform);
            HeightCalibrator.Instance?.MarkDirty();
        }

        // ─────────────────────────────── open / close ───────────────────────────────

        public void Toggle()
        {
            if (IsOpen) Close(false);
            else Open();
        }

        public void Open()
        {
            var head = LearnerRig.Head;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;

            snapshot = Take();
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var pos = head.position + fwd * 0.55f + Vector3.down * 0.14f;
            root.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            canvas.worldCamera = Camera.main;
            root.gameObject.SetActive(true);
            PanelClearance.ClearCanvas(root, head.position);
            Select(selected);
            AudioManager.Instance?.Play(SoundId.UI_PanelOpen);
        }

        /// <summary>Save keeps what is showing (and remembers it); otherwise everything goes back to how it was.</summary>
        public void Close(bool save)
        {
            if (!IsOpen) return;
            if (save) Save();
            else Restore(snapshot);
            root.gameObject.SetActive(false);
            AudioManager.Instance?.Play(save ? SoundId.UI_Confirm : SoundId.UI_PanelClose);
        }

        Snapshot Take() => new Snapshot
        {
            offset = HeightCalibrator.UserOffset,
            textScale = Session != null ? Session.Settings.textScale : 1f,
            posture = Session != null ? Session.Settings.posture : Posture.Standing,
            mode = Session != null ? Session.Settings.mode : TrainingMode.Guided,
            subtitles = Session == null || Session.Settings.subtitles,
        };

        static void Restore(Snapshot s)
        {
            HeightCalibrator.SetUserOffset(s.offset);
            var session = Session;
            if (session == null) return;
            if (session.Settings.posture != s.posture) session.SetPosture(s.posture);
            if (!Mathf.Approximately(session.Settings.textScale, s.textScale)) session.SetTextScale(s.textScale);
            if (session.Settings.mode != s.mode && !session.IsRunning) session.SetMode(s.mode);
            if (session.Settings.subtitles != s.subtitles) session.SetSubtitles(s.subtitles);
        }

        // Kept between sessions: eye height, posture, text size, subtitles. Mode is chosen per session in the lobby.
        const string PosturePref = "sf.posture", TextPref = "sf.textScale", SubtitlesPref = "sf.subtitles";

        static void Save()
        {
            HeightCalibrator.SaveUserOffset();
            var s = Session;
            if (s == null) { PlayerPrefs.Save(); return; }
            PlayerPrefs.SetInt(PosturePref, (int)s.Settings.posture);
            PlayerPrefs.SetFloat(TextPref, s.Settings.textScale);
            PlayerPrefs.SetInt(SubtitlesPref, s.Settings.subtitles ? 1 : 0);
            PlayerPrefs.Save();
        }

        static void ApplySaved(SessionManager s)
        {
            if (PlayerPrefs.HasKey(PosturePref)) s.SetPosture((Posture)PlayerPrefs.GetInt(PosturePref));
            if (PlayerPrefs.HasKey(TextPref)) s.SetTextScale(PlayerPrefs.GetFloat(TextPref));
            if (PlayerPrefs.HasKey(SubtitlesPref)) s.SetSubtitles(PlayerPrefs.GetInt(SubtitlesPref) == 1);
        }

        void Update()
        {
            if (!prefsApplied && Session != null) { ApplySaved(Session); prefsApplied = true; }
            UpdateWristButton();
        }

        // ─────────────────────────────── selection ───────────────────────────────

        void Select(int index)
        {
            selected = Mathf.Clamp(index, 0, settings.Count - 1);
            Refresh();
        }

        void Step(int delta)
        {
            var s = settings[selected];
            if (s.isAction || (s.locked != null && s.locked())) return;
            int next = Mathf.Clamp(s.current() + delta, 0, s.count - 1);
            if (next == s.current()) return;
            s.choose(next);
            AudioManager.Instance?.Play(SoundId.UI_Click);
            Refresh();
        }

        void ChooseRow(int row)
        {
            if (row < 0 || row >= rowValues.Count || rowValues[row] < 0) return;
            var s = settings[selected];
            if (s.locked != null && s.locked()) { AudioManager.Instance?.Play(SoundId.UI_Denied); return; }
            s.choose(rowValues[row]);
            AudioManager.Instance?.Play(s.isAction ? SoundId.UI_Confirm : SoundId.UI_Click);
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < segments.Count; i++)
                segments[i].color = i == selected ? SegmentSelected : i == hovered ? SegmentHover : Segment;

            var s = settings[selected];
            int cur = s.current();
            bool locked = s.locked != null && s.locked();
            knobName.text = s.name;
            knobValue.text = s.label(cur);
            hintText.text = locked ? "Locked while a session is running. Change it in the lobby." : s.hint;
            hintText.color = locked ? Hex("F5B54B") : TextMuted;
            stepText.text = s.isAction ? "·" : $"{cur + 1}/{s.count}";

            // Rows: the current value in the middle, neighbours fading out above and below (as on the reference).
            // Higher values sit above, so the up arrow moves up the list.
            int mid = VisibleRows / 2;
            for (int r = 0; r < rows.Count; r++)
            {
                int value = cur - (r - mid);
                bool exists = value >= 0 && value < s.count;
                rowValues[r] = exists ? value : -1;
                rows[r].gameObject.SetActive(exists);
                if (!exists) continue;
                int dist = Mathf.Abs(r - mid);
                float alpha = dist == 0 ? 1f : dist == 1 ? 0.55f : 0.2f;
                rows[r].color = (r == mid ? RowSelected : Row) * new Color(1, 1, 1, alpha);
                rowTexts[r].text = s.label(value);
                rowTexts[r].color = new Color(Text.r, Text.g, Text.b, alpha * (locked && r != mid ? 0.5f : 1f));
                rows[r].transform.Find("Bar").gameObject.SetActive(r == mid);
            }

            // Leader line from the selected segment's outer edge to the current row.
            float a = (selected + 0.5f) * 360f / settings.Count * Mathf.Deg2Rad;
            var from = DialCentre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (RingOuter + 6f);
            var to = ListCentre + new Vector2(-RowW / 2f - 4f, 0f);
            var d = to - from;
            leader.anchoredPosition = from + d * 0.5f;
            leader.sizeDelta = new Vector2(d.magnitude, 2f);
            leader.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        // Ring hit-testing, from the pointer position on the dial.
        int SegmentAt(Vector3 worldPoint)
        {
            var local = (Vector2)root.InverseTransformPoint(worldPoint) - DialCentre;
            float r = local.magnitude;
            if (r < RingInner || r > RingOuter) return -1;
            float deg = (Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg + 360f) % 360f; // 0 at the top, clockwise
            return Mathf.Clamp(Mathf.FloorToInt(deg / (360f / settings.Count)), 0, settings.Count - 1);
        }

        void OnRingMove(Vector3 world)
        {
            int seg = SegmentAt(world);
            if (seg == hovered) return;
            hovered = seg;
            if (seg >= 0) AudioManager.Instance?.Play(SoundId.UI_Hover);
            Refresh();
        }

        void OnRingClick(Vector3 world)
        {
            int seg = SegmentAt(world);
            if (seg < 0) return;
            AudioManager.Instance?.Play(SoundId.UI_Click);
            Select(seg);
        }

        void OnRingExit()
        {
            hovered = -1;
            Refresh();
        }

        class RingPointer : MonoBehaviour, IPointerMoveHandler, IPointerClickHandler, IPointerExitHandler
        {
            public QuickSettingsDial dial;
            public void OnPointerMove(PointerEventData e) => dial.OnRingMove(e.pointerCurrentRaycast.worldPosition);
            public void OnPointerClick(PointerEventData e) => dial.OnRingClick(e.pointerCurrentRaycast.worldPosition);
            public void OnPointerExit(PointerEventData e) => dial.OnRingExit();
        }

        // ─────────────────────────────── building ───────────────────────────────

        void Build()
        {
            var go = new GameObject("QuickSettings_Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            root = (RectTransform)go.transform;
            root.sizeDelta = new Vector2(Width, Height);
            root.localScale = Vector3.one * PxToMetres;
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var bg = Img(root, "Panel", UISprites.Rounded, Panel, Vector2.zero, new Vector2(Width, Height));
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true; // catches rays between controls so they don't fall through to the scene
            bg.pixelsPerUnitMultiplier = 0.25f; // bigger corner radius

            Label(root, "Quick settings", 34, FontStyles.Bold, Text, new Vector2(-Width / 2 + 40, Height / 2 - 48), new Vector2(600, 44), TextAlignmentOptions.Left);
            Label(root, "Adjust your view and training aids", 20, FontStyles.Normal, TextMuted, new Vector2(-Width / 2 + 40, Height / 2 - 84), new Vector2(600, 30), TextAlignmentOptions.Left);

            // Dial: back disc, segments, labels, knob.
            Img(root, "RingBack", UISprites.Circle, RingBack, DialCentre, Vector2.one * (RingOuter * 2 + 24)).raycastTarget = false;
            float step = 360f / settings.Count;
            for (int i = 0; i < settings.Count; i++)
            {
                var seg = Img(root, $"Segment_{settings[i].name}", UISprites.Ring(RingInner / RingOuter), Segment, DialCentre, Vector2.one * RingOuter * 2);
                seg.type = Image.Type.Filled;
                seg.fillMethod = Image.FillMethod.Radial360;
                seg.fillOrigin = (int)Image.Origin360.Top;
                seg.fillClockwise = true;
                seg.fillAmount = (step - SegmentGapDeg) / 360f;
                seg.rectTransform.localRotation = Quaternion.Euler(0, 0, -(i * step + SegmentGapDeg / 2f));
                seg.raycastTarget = false;
                segments.Add(seg);

                float a = (i + 0.5f) * step * Mathf.Deg2Rad;
                var at = DialCentre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * ((RingOuter + RingInner) / 2f);
                Label(root, settings[i].name, 17, FontStyles.Normal, Text, at, new Vector2(110, 44), TextAlignmentOptions.Center);
            }
            // One invisible hit area over the whole ring; the angle under the pointer picks the segment.
            var hit = Img(root, "RingHit", UISprites.Circle, new Color(0, 0, 0, 0), DialCentre, Vector2.one * RingOuter * 2);
            hit.gameObject.AddComponent<RingPointer>().dial = this;

            Img(root, "KnobShadow", UISprites.Circle, new Color(0, 0, 0, 0.35f), DialCentre + new Vector2(0, -6), Vector2.one * (KnobD + 10)).raycastTarget = false;
            Img(root, "Knob", UISprites.Circle, Knob, DialCentre, Vector2.one * KnobD).raycastTarget = false;
            Img(root, "KnobFace", UISprites.Circle, KnobInner, DialCentre, Vector2.one * (KnobD - 26)).raycastTarget = false;
            knobName = Label(root, "", 18, FontStyles.Normal, Hex("555960"), DialCentre + new Vector2(0, 26), new Vector2(KnobD - 30, 28), TextAlignmentOptions.Center);
            knobValue = Label(root, "", 25, FontStyles.Bold, TextOnKnob, DialCentre + new Vector2(0, -12), new Vector2(KnobD - 24, 64), TextAlignmentOptions.Center);
            knobValue.textWrappingMode = TextWrappingModes.Normal;

            // Leader line (under the rows).
            var line = Img(root, "Leader", null, Line, Vector2.zero, new Vector2(10, 2));
            line.raycastTarget = false;
            leader = line.rectTransform;

            // Option rows, centre one current.
            for (int r = 0; r < VisibleRows; r++)
            {
                int index = r;
                var pos = ListCentre + new Vector2(0, (VisibleRows / 2 - r) * (RowH + RowGap));
                var row = Img(root, $"Row_{r}", UISprites.Rounded, Row, pos, new Vector2(RowW, RowH));
                row.type = Image.Type.Sliced;
                row.pixelsPerUnitMultiplier = 1.4f;
                var bar = Img(row.rectTransform, "Bar", UISprites.Rounded, Mint, new Vector2(-RowW / 2 + 6, 0), new Vector2(5, RowH - 22));
                bar.type = Image.Type.Sliced;
                bar.pixelsPerUnitMultiplier = 8f;
                bar.raycastTarget = false;
                var text = Label(row.rectTransform, "", 23, FontStyles.Normal, Text, new Vector2(-RowW / 2 + 26, 0), new Vector2(RowW - 40, RowH), TextAlignmentOptions.Left);
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => ChooseRow(index));
                rows.Add(row);
                rowTexts.Add(text);
                rowValues.Add(-1);
            }
            hintText = Label(root, "", 18, FontStyles.Normal, TextMuted, ListCentre + new Vector2(0, -(VisibleRows / 2 + 1) * (RowH + RowGap) + 6), new Vector2(RowW + 40, 52), TextAlignmentOptions.Center);
            hintText.textWrappingMode = TextWrappingModes.Normal;

            // Stepper pill: up, position, down.
            var pillX = ListCentre.x + RowW / 2 + 70;
            var pill = Img(root, "Stepper", UISprites.Rounded, Hex("222428"), new Vector2(pillX, -5), new Vector2(72, 220));
            pill.type = Image.Type.Sliced;
            pill.pixelsPerUnitMultiplier = 0.7f;
            IconButton(root, "Up", UISprites.Triangle, new Vector2(pillX, 70), 0f, () => Step(+1));
            IconButton(root, "Down", UISprites.Triangle, new Vector2(pillX, -80), 180f, () => Step(-1));
            stepText = Label(root, "", 16, FontStyles.Normal, TextMuted, new Vector2(pillX, -5), new Vector2(70, 30), TextAlignmentOptions.Center);

            // Close, cancel, save.
            var close = Label(root, "×", 44, FontStyles.Normal, TextMuted, new Vector2(pillX, Height / 2 - 52), new Vector2(60, 60), TextAlignmentOptions.Center);
            close.raycastTarget = true;
            close.gameObject.AddComponent<Button>().onClick.AddListener(() => Close(false));

            var cancel = Label(root, "Cancel", 24, FontStyles.Normal, Text, new Vector2(-Width / 2 + 100, -Height / 2 + 56), new Vector2(140, 60), TextAlignmentOptions.Center);
            cancel.raycastTarget = true;
            cancel.gameObject.AddComponent<Button>().onClick.AddListener(() => Close(false));

            var save = Img(root, "Save", UISprites.Rounded, Hex("2C2F35"), new Vector2(-Width / 2 + 330, -Height / 2 + 56), new Vector2(260, 64));
            save.type = Image.Type.Sliced;
            save.pixelsPerUnitMultiplier = 1.2f;
            Label(save.rectTransform, "Save changes", 24, FontStyles.Normal, Text, Vector2.zero, new Vector2(240, 60), TextAlignmentOptions.Center);
            save.gameObject.AddComponent<Button>().onClick.AddListener(() => Close(true));

            // Button tints multiply the graphic's colour: white leaves it as drawn, the mint wash marks hover.
            foreach (var b in root.GetComponentsInChildren<Button>(true)) Tint(b, Color.white, Hex("C8F2EA"));
        }

        void BuildWristButton()
        {
            var go = new GameObject("QuickSettings_WristButton", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            wrist = (RectTransform)go.transform;
            wrist.sizeDelta = new Vector2(100, 100);
            wrist.localScale = Vector3.one * PxToMetres;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = 40;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var disc = Img(wrist, "Disc", UISprites.Circle, Hex("1E2024", 0.95f), Vector2.zero, new Vector2(96, 96));
            Img(wrist, "Rim", UISprites.Ring(0.9f), Hex("4A4F57"), Vector2.zero, new Vector2(96, 96)).raycastTarget = false;
            for (int i = -1; i <= 1; i++)
                Img(wrist, "Line", null, Text, new Vector2(0, i * 13), new Vector2(38, 4)).raycastTarget = false; // ≡ as on the reference knob
            disc.gameObject.AddComponent<Button>().onClick.AddListener(Toggle);
        }

        // Palm-facing test: shown above this (cosine of ~49°), hidden again below the lower one, and only after the
        // state has held for a moment, so it doesn't flicker as the wrist turns through the boundary.
        const float PalmShowDot = 0.65f, PalmHideDot = 0.4f, PalmHoldSeconds = 0.15f, PalmMaxReach = 0.75f;
        bool palmShowing;
        float palmPendingSince = -1f;

        /// <summary>
        /// The wrist opener appears only while the left palm is turned towards the learner (the "look at your hand"
        /// gesture), floating just off the palm; it hides while the dial is open.
        /// </summary>
        void UpdateWristButton()
        {
            var head = LearnerRig.Head;
            bool facing = false;
            Vector3 centre = default, normal = default;
            if (!IsOpen && head != null && TryLeftPalm(out centre, out normal))
            {
                var toHead = head.position - centre;
                float dot = toHead.sqrMagnitude > 1e-4f ? Vector3.Dot(normal, toHead.normalized) : 0f;
                facing = toHead.magnitude < PalmMaxReach && dot > (palmShowing ? PalmHideDot : PalmShowDot);
            }

            if (facing != palmShowing)
            {
                if (palmPendingSince < 0f) palmPendingSince = Time.unscaledTime;
                if (Time.unscaledTime - palmPendingSince >= PalmHoldSeconds || IsOpen) { palmShowing = facing; palmPendingSince = -1f; }
            }
            else palmPendingSince = -1f;

            if (wrist.gameObject.activeSelf != palmShowing) wrist.gameObject.SetActive(palmShowing);
            if (!palmShowing || head == null) return;
            var pos = centre + normal * 0.06f + Vector3.up * 0.02f;
            wrist.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            var c = wrist.GetComponent<Canvas>();
            if (c.worldCamera == null) c.worldCamera = Camera.main;
        }

        static readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        XROrigin origin;

        /// <summary>
        /// Centre of the left palm and the direction it faces, in world space. Tracked hands: the XR Hands palm joint,
        /// whose +Y points out of the back of the hand, so the palm faces −Y. Controllers: the side of the grip the
        /// palm wraps round, which for the left hand is the controller's +X.
        /// </summary>
        bool TryLeftPalm(out Vector3 centre, out Vector3 normal)
        {
            centre = normal = default;
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
            var space = origin != null && origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : null;

            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);
            foreach (var hs in handSubsystems)
            {
                if (!hs.running || !hs.leftHand.isTracked || space == null) continue;
                if (!hs.leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose)) continue;
                centre = space.TransformPoint(pose.position);
                normal = -space.TransformDirection(pose.rotation * Vector3.up);
                return true;
            }

            var controller = LearnerRig.Hand(true);
            if (controller == null || !LearnerRig.Tracked(true)) return false;
            centre = controller.position;
            normal = controller.right;
            return true;
        }

        // ─────────────────────────────── helpers ───────────────────────────────

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Label(RectTransform parent, string text, float size, FontStyles style, Color color, Vector2 pos, Vector2 box, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            if (align == TextAlignmentOptions.Left) { rt.pivot = new Vector2(0, 0.5f); }
            return t;
        }

        void IconButton(RectTransform parent, string name, Sprite sprite, Vector2 pos, float rotation, Action onClick)
        {
            var hitArea = Img(parent, name, UISprites.Circle, new Color(1, 1, 1, 0.001f), pos, new Vector2(64, 64));
            var icon = Img(hitArea.rectTransform, "Icon", sprite, Text, Vector2.zero, new Vector2(26, 24));
            icon.raycastTarget = false;
            icon.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
            var b = hitArea.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
        }

        static void Tint(Button b, Color normal, Color highlighted)
        {
            var c = b.colors;
            c.normalColor = normal;
            c.highlightedColor = highlighted;
            c.selectedColor = normal;
            c.pressedColor = highlighted * 0.9f;
            c.fadeDuration = 0.08f;
            b.colors = c;
        }

        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            c.a = alpha;
            return c;
        }
    }
}
