using SurgicalFoundations.Core;
using SurgicalFoundations.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Guided-mode aid for trocar entry, drawn at the port instead of on the far stage panel: a line from the entry
    /// point along the axis the trocar should follow (green when the trocar lines up, amber when close, red when not),
    /// and a short readout beside it saying which way to move the handle and how deep the tip is.
    /// </summary>
    public class TrocarGuide
    {
        const float LineLength = 0.26f;
        static readonly Color Good = new Color(0.36f, 0.88f, 0.6f), Close = new Color(0.96f, 0.71f, 0.29f), Bad = new Color(0.93f, 0.33f, 0.3f);
        static readonly Color Idle = new Color(0.36f, 0.88f, 0.78f);

        readonly GameObject root;
        readonly LineRenderer line;
        readonly Material lineMaterial;
        readonly TextMeshPro text;

        public TrocarGuide(Scene scene)
        {
            root = new GameObject("TrocarGuide");
            SceneManager.MoveGameObjectToScene(root, scene);
            line = new GameObject("Axis").AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.positionCount = 2;
            line.widthMultiplier = 0.006f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = lineMaterial = RuntimeMaterials.Unlit(Idle);

            var label = new GameObject("Readout");
            label.transform.SetParent(root.transform, false);
            text = label.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(5, 20, 24, 255);
            text.rectTransform.sizeDelta = new Vector2(0.5f, 0.1f);
            root.SetActive(false);
        }

        public void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        /// <param name="entry">Entry point on the skin.</param>
        /// <param name="ideal">Direction the trocar should point (into the body).</param>
        /// <param name="trocarAxis">The trocar's axis, or null when no trocar is at the site.</param>
        public void Show(Vector3 entry, Vector3 ideal, Vector3? trocarAxis, TrocarEntry state, TrocarLimits limits, string portName)
        {
            if (root == null) return;
            if (!root.activeSelf) root.SetActive(true);
            line.SetPosition(0, entry);
            line.SetPosition(1, entry - ideal * LineLength); // outside the body, where the shaft should be

            var head = LearnerRig.Head;
            string message;
            Color colour;
            if (trocarAxis == null || state == null)
            {
                colour = Idle;
                message = $"{portName} port\n<size=75%>Put the trocar tip on the ring, then follow the line</size>";
            }
            else
            {
                var angle = Vector3.Angle(trocarAxis.Value, ideal);
                colour = angle <= limits.angleToleranceDeg ? Good : angle <= limits.angleToleranceDeg * 2f ? Close : Bad;
                var aim = angle <= limits.angleToleranceDeg ? "Angle good" : $"Move the handle {Direction(trocarAxis.Value - ideal, head)}  ·  {angle:0}° off";
                var mm = state.Depth * 1000f;
                string depth;
                if (state.Depth > limits.safeDepth) depth = "Too deep: draw back";
                else if (state.Depth >= limits.entryDepth) depth = angle <= limits.angleToleranceDeg ? $"Through: hold still {state.HoldProgress * 100f:0}%" : "Through: fix the angle";
                else depth = $"Push in steadily  ·  {mm:0} / {limits.entryDepth * 1000f:0} mm";
                message = $"{aim}\n<size=80%>{depth}</size>";
            }
            lineMaterial.color = colour;
            lineMaterial.SetColor("_BaseColor", colour);
            text.text = message;
            text.color = colour;
            var scale = SessionManager.Instance != null ? SessionManager.Instance.Settings.textScale : 1f;
            text.fontSize = 0.3f * scale;

            // Beside the top of the guide line, turned to the learner.
            var anchor = entry - ideal * (LineLength + 0.05f);
            if (head != null)
            {
                var toHead = Vector3.ProjectOnPlane(head.position - anchor, Vector3.up).normalized;
                text.transform.position = anchor + toHead * 0.06f + Vector3.up * 0.03f;
                text.transform.rotation = Quaternion.LookRotation(text.transform.position - head.position, Vector3.up);
            }
            else text.transform.position = anchor;
        }

        /// <summary>Which way (as the learner sees it) to move the handle so the axis swings onto the ideal.</summary>
        static string Direction(Vector3 handleMove, Transform head)
        {
            var v = Vector3.ProjectOnPlane(handleMove, Vector3.up);
            if (head == null) return "towards the line";
            var right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            float x = Vector3.Dot(v, right), z = Vector3.Dot(v, forward);
            if (Mathf.Abs(x) >= Mathf.Abs(z)) return x > 0f ? "to your right" : "to your left";
            return z > 0f ? "away from you" : "towards you";
        }

        public void Destroy()
        {
            if (root != null) RuntimeMaterials.Remove(root);
            if (lineMaterial != null) RuntimeMaterials.Remove(lineMaterial);
        }
    }
}
