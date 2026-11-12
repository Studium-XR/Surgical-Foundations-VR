using System;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using SurgicalFoundations.Rendering;
using SurgicalFoundations.Scenario;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Everything about the session and its settings, on the learner's left palm. Turn the palm towards your face and a
    /// card appears just above it: the clock, stage and mode, the course and lesson, and six buttons (settings, pause,
    /// graphics quality, subtitles, recenter, lesson objectives) to press with the other hand. Lower the hand and it is
    /// gone. Nothing here is attached to the head, so nothing moves when the learner looks around.
    /// Installs itself, so no scene has to change.
    /// </summary>
    public class PalmHub : MonoBehaviour
    {
        // Authored at hand size: 1 px = 0.35 mm, so the 520 × 400 px card is 18 × 14 cm.
        const float PxToMetres = 0.00035f;
        const float W = 520f, H = 400f;
        const float ShowDot = 0.65f, HideDot = 0.4f, HoldSeconds = 0.15f;

        static readonly Color Panel = Hex("17181C", 0.96f), TileBg = Hex("25272C"), TileOn = Hex("30443F"),
            Text = Hex("D6D9DE"), Muted = Hex("8A9097"), Mint = Hex("5CE0C8"), Amber = Hex("F5B54B");

        public static PalmHub Instance { get; private set; }
        public bool Showing => card != null && card.gameObject.activeSelf;

        RectTransform card;
        TextMeshProUGUI clock, stage, mode, course, objectives;
        TextMeshProUGUI pauseLabel, qualityLabel, subtitlesLabel;
        GameObject objectivesPanel;
        bool showing;
        float pendingSince = -1f;
        float nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<PalmHub>() != null) return;
            var go = new GameObject("PalmHub");
            DontDestroyOnLoad(go);
            go.AddComponent<PalmHub>();
        }

        void Awake()
        {
            Instance = this;
            Build();
            card.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────── show / follow ───────────────────────────────

        void LateUpdate()
        {
            // Hidden while a bigger palm panel (settings dial, pause card) is up: that panel is on the palm instead.
            bool blocked = (QuickSettingsDial.Instance != null && QuickSettingsDial.Instance.IsOpen)
                           || (SessionManager.Instance != null && SessionManager.Instance.IsPaused);
            float dot = PalmPose.FacingHead(true, out var pose);
            bool facing = !blocked && dot > (showing ? HideDot : ShowDot);

            if (facing != showing)
            {
                if (pendingSince < 0f) pendingSince = Time.unscaledTime;
                if (Time.unscaledTime - pendingSince >= HoldSeconds || blocked) { showing = facing; pendingSince = -1f; Shown(showing); }
            }
            else pendingSince = -1f;

            if (!showing) return;
            var (pos, rot) = PalmDock.OnPalm(pose, 0.075f, 0.03f);
            float k = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            card.SetPositionAndRotation(Vector3.Lerp(card.position, pos, k), Quaternion.Slerp(card.rotation, rot, k));

            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        void Shown(bool on)
        {
            if (on)
            {
                // Appear exactly on the palm, not sliding in from where it was last.
                if (PalmPose.FacingHead(true, out var pose) > -1f)
                {
                    var (pos, rot) = PalmDock.OnPalm(pose, 0.075f, 0.03f);
                    card.SetPositionAndRotation(pos, rot);
                }
                var c = card.GetComponent<Canvas>();
                if (c.worldCamera == null) c.worldCamera = Camera.main;
                Refresh();
                AudioManager.Instance?.Play(SoundId.UI_Hover);
            }
            else objectivesPanel.SetActive(false);
            card.gameObject.SetActive(on);
        }

        void Refresh()
        {
            var s = SessionManager.Instance;
            clock.text = s != null ? $"<mspace=0.6em>{SessionManager.FormatClock(s.ElapsedSeconds)}</mspace>" : "--:--";
            mode.text = s == null ? "" : s.Settings.mode == TrainingMode.Guided ? "GUIDED" : "ASSESSMENT";
            mode.color = s != null && s.Settings.mode == TrainingMode.Assessment ? Amber : Mint;
            var d = ScenarioDirector.Instance;
            stage.text = d != null ? d.Stage.ToString().ToUpperInvariant() : SceneLabel();
            course.text = CourseSession.Course != null
                ? $"{CourseSession.Course.code} · {(CourseSession.Lesson != null ? CourseSession.Lesson.title : CourseSession.Course.title)}"
                : "Free practice";
            pauseLabel.text = s != null && s.IsPaused ? "Resume" : "Pause";
            var q = DeviceQuality.Instance;
            qualityLabel.text = q != null ? DeviceQuality.Label(q.Preference) : "Auto";
            subtitlesLabel.text = s == null || s.Settings.subtitles ? "Subtitles on" : "Subtitles off";
        }

        static string SceneLabel()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var n = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name;
                if (n == SceneIds.Lobby) return "LOBBY";
                if (n == SceneIds.SkillsLab) return "SKILLS LAB";
            }
            return "";
        }

        // ─────────────────────────────── actions ───────────────────────────────

        void OpenSettings()
        {
            Shown(false); showing = false;
            QuickSettingsDial.Instance?.Open();
        }

        void TogglePause()
        {
            var s = SessionManager.Instance;
            if (s == null || !s.IsRunning) { AudioManager.Instance?.Play(SoundId.UI_Denied); return; }
            s.TogglePause();
        }

        void CycleQuality()
        {
            var q = DeviceQuality.Instance;
            if (q == null) return;
            var next = (QualityPreference)(((int)q.Preference + 1) % Enum.GetValues(typeof(QualityPreference)).Length);
            q.SetPreference(next);
            AudioManager.Instance?.Play(SoundId.UI_Click);
            Refresh();
        }

        void ToggleSubtitles()
        {
            var s = SessionManager.Instance;
            if (s == null) return;
            s.SetSubtitles(!s.Settings.subtitles);
            AudioManager.Instance?.Play(SoundId.UI_Click);
            Refresh();
        }

        void Recenter()
        {
            PlayerSpawnPoint best = null;
            foreach (var sp in FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None))
                if (sp.isActiveAndEnabled && (best == null || sp.Priority > best.Priority)) best = sp;
            if (best != null) SceneLoader.Instance?.PlaceRigAt(best.transform);
            HeightCalibrator.Instance?.MarkDirty();
            foreach (var w in FindObjectsByType<WorldLockedPanel>(FindObjectsSortMode.None)) w.MarkForPlacement();
            AudioManager.Instance?.Play(SoundId.UI_Confirm);
        }

        void ToggleObjectives()
        {
            bool on = !objectivesPanel.activeSelf;
            if (on)
            {
                var c = CourseSession.Course;
                var l = CourseSession.Lesson;
                if (c == null) objectives.text = "<b>Free practice</b>\nChoose a course in the lobby to see its objectives here.";
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<b>").Append(l != null ? l.title : c.title).Append("</b>\n");
                    if (l != null && !string.IsNullOrEmpty(l.summary)) sb.Append(l.summary).Append('\n');
                    int n = 0;
                    foreach (var o in c.outcomes) { if (n++ >= 4) break; sb.Append("• ").Append(o).Append('\n'); }
                    if (l != null && (l.kind == LessonKind.VrLab || l.kind == LessonKind.VrAssessment))
                        sb.Append($"<color=#8A9097>Pass mark {l.passScore:0}%</color>");
                    objectives.text = sb.ToString();
                }
            }
            objectivesPanel.SetActive(on);
            AudioManager.Instance?.Play(SoundId.UI_Click);
        }

        // ─────────────────────────────── building ───────────────────────────────

        void Build()
        {
            var go = new GameObject("PalmHub_Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            card = (RectTransform)go.transform;
            card.sizeDelta = new Vector2(W, H);
            card.localScale = Vector3.one * PxToMetres;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = 45;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var bg = Img(card, "Panel", UISprites.Rounded, Panel, Vector2.zero, new Vector2(W, H));
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.3f;

            // Header: clock · stage · mode
            clock = Label(card, "00:00", 40, FontStyles.Bold, Text, new Vector2(-W / 2 + 28, H / 2 - 44), new Vector2(170, 50), TextAlignmentOptions.Left);
            stage = Label(card, "", 22, FontStyles.Bold, Muted, new Vector2(-W / 2 + 200, H / 2 - 44), new Vector2(170, 40), TextAlignmentOptions.Left);
            mode = Label(card, "GUIDED", 20, FontStyles.Bold, Mint, new Vector2(W / 2 - 28, H / 2 - 44), new Vector2(160, 40), TextAlignmentOptions.Right);
            course = Label(card, "", 20, FontStyles.Normal, Muted, new Vector2(-W / 2 + 28, H / 2 - 90), new Vector2(W - 56, 30), TextAlignmentOptions.Left);
            course.textWrappingMode = TextWrappingModes.NoWrap;
            course.overflowMode = TextOverflowModes.Ellipsis;

            // 3 × 2 buttons
            const float tw = 148f, th = 112f, gap = 14f;
            float x0 = -(tw + gap), y0 = -10f;
            Tile("Settings", "Settings", new Vector2(x0, y0), OpenSettings, out _);
            Tile("Pause", "Pause", new Vector2(0, y0), TogglePause, out pauseLabel);
            Tile("Quality", "Auto", new Vector2(-x0, y0), CycleQuality, out qualityLabel, "Graphics");
            Tile("Subtitles", "Subtitles on", new Vector2(x0, y0 - th - gap), ToggleSubtitles, out subtitlesLabel);
            Tile("Recenter", "Recenter", new Vector2(0, y0 - th - gap), Recenter, out _);
            Tile("Objectives", "Objectives", new Vector2(-x0, y0 - th - gap), ToggleObjectives, out _);

            // Objectives overlay (covers the buttons while shown; tap to close).
            var ov = Img(card, "ObjectivesPanel", UISprites.Rounded, Hex("1E2024", 0.98f), new Vector2(0, -70), new Vector2(W - 32, H - 130));
            ov.type = Image.Type.Sliced;
            ov.pixelsPerUnitMultiplier = 0.3f;
            objectivesPanel = ov.gameObject;
            objectives = Label(ov.rectTransform, "", 19, FontStyles.Normal, Text, Vector2.zero, new Vector2(W - 72, H - 160), TextAlignmentOptions.TopLeft);
            objectives.textWrappingMode = TextWrappingModes.Normal;
            ov.gameObject.AddComponent<Button>().onClick.AddListener(ToggleObjectives);
            objectivesPanel.SetActive(false);
        }

        void Tile(string name, string label, Vector2 pos, Action onClick, out TextMeshProUGUI valueLabel, string caption = null)
        {
            var t = Img(card, "Btn_" + name, UISprites.Rounded, TileBg, pos, new Vector2(148, 112));
            t.type = Image.Type.Sliced;
            t.pixelsPerUnitMultiplier = 0.4f;
            var b = t.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = TileOn; colors.pressedColor = TileOn * 0.9f; colors.selectedColor = Color.white; colors.fadeDuration = 0.06f;
            b.colors = colors;
            b.onClick.AddListener(() => { AudioManager.Instance?.Play(SoundId.UI_Click); onClick(); });
            if (caption != null)
            {
                Label(t.rectTransform, caption, 16, FontStyles.Normal, Muted, new Vector2(0, 22), new Vector2(140, 26), TextAlignmentOptions.Center);
                valueLabel = Label(t.rectTransform, label, 22, FontStyles.Bold, Text, new Vector2(0, -12), new Vector2(140, 34), TextAlignmentOptions.Center);
            }
            else valueLabel = Label(t.rectTransform, label, 22, FontStyles.Bold, Text, Vector2.zero, new Vector2(140, 60), TextAlignmentOptions.Center);
            valueLabel.textWrappingMode = TextWrappingModes.Normal;
        }

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
            rt.sizeDelta = box;
            if (align == TextAlignmentOptions.Left || align == TextAlignmentOptions.TopLeft) rt.pivot = new Vector2(0, 0.5f);
            else if (align == TextAlignmentOptions.Right) rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = align == TextAlignmentOptions.TopLeft ? pos - new Vector2(box.x / 2, 0) : pos;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            c.a = alpha;
            return c;
        }
    }
}
