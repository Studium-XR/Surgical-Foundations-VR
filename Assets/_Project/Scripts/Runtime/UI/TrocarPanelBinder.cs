using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Detection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>Screen 09: live angle, depth and force gauges for the trocar being inserted, with the technique flag (FR-08).</summary>
    public class TrocarPanelBinder : MonoBehaviour
    {
        [SerializeField] UITheme theme;
        [SerializeField] TMP_Text eyebrow;
        [SerializeField] RectTransform angleFill;
        [SerializeField] RectTransform depthFill;
        [SerializeField] RectTransform forceFill;
        [SerializeField] TMP_Text angleValue;
        [SerializeField] TMP_Text depthValue;
        [SerializeField] TMP_Text forceValue;
        [SerializeField] GameObject flagPanel;
        [SerializeField] TMP_Text flagTitle;

        void Update()
        {
            var access = StageController.Current as AccessController;
            if (access == null || access.Plan == null) return;
            var entry = access.Entry;
            var limits = access.Limits;
            if (eyebrow != null)
                eyebrow.text = access.PortIndex < access.PortCount
                    ? $"Port {access.PortIndex + 1} of {access.PortCount} · {access.Plan.Sites[access.PortIndex].name}"
                    : "All ports placed";

            float angle = entry != null ? entry.Angle : 0f, depth = entry != null ? entry.Depth : 0f, force = entry != null ? entry.Force : 0f;
            var entering = entry != null && entry.Phase == TrocarPhase.Entering;
            var angleOff = entering && angle > limits.angleToleranceDeg;
            var tooDeep = depth > limits.safeDepth;
            var tooHard = force > limits.maxForceN;

            Fill(angleFill, angle / 45f, angleOff ? Warn : Ok);
            Fill(depthFill, depth / limits.unsafeDepth, tooDeep ? Danger : Ok);
            Fill(forceFill, force / (limits.maxForceN * 1.3f), tooHard ? Danger : Ok);
            // The number is how far off the axis the trocar is ("23° on axis" read as if 23° were the target).
            Set(angleValue, $"<mspace=0.62em>{angle:0}°</mspace>  {(angleOff ? "off the axis" : "aligned")}", angleOff ? Warn : Ok);
            Set(depthValue, $"<mspace=0.62em>{depth * 1000f:0}</mspace> mm" + (tooDeep ? "  too deep" : depth >= limits.entryDepth ? "  through" : ""), tooDeep ? Danger : Ok);
            Set(forceValue, tooHard ? "Too hard" : "OK", tooHard ? Danger : Ok);

            var guided = SessionManager.Instance == null || SessionManager.Instance.Settings.mode == TrainingMode.Guided;
            var flag = entry != null && entry.AngleWarning && guided;
            if (flagPanel != null && flagPanel.activeSelf != flag) flagPanel.SetActive(flag);
            if (flag && flagTitle != null) flagTitle.text = "Angle off the entry axis";
        }

        Color Ok => theme != null ? theme.accent : Color.white;
        Color Warn => theme != null ? theme.warning : Color.yellow;
        Color Danger => theme != null ? theme.danger : Color.red;

        static void Fill(RectTransform fill, float amount, Color colour)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(amount), fill.anchorMax.y);
            var image = fill.GetComponent<Image>();
            if (image != null) image.color = colour;
        }

        static void Set(TMP_Text text, string value, Color colour)
        {
            if (text == null) return;
            text.text = value;
            text.color = colour;
        }
    }
}
