using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using UnityEngine;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Guided mode's "look here": one pulsing marker that sits on whatever the current step needs next (the tap, the
    /// glove packet, the next port site, the ring to pick up, the peg to put it on…) and moves on the moment detection
    /// sees that step done. Hidden in Assessment mode, where the learner works unprompted (FR-19). It is an ordinary
    /// object in the world, so it also shows on the laparoscope monitor for targets inside the abdomen.
    /// </summary>
    public class GuidedHighlight : MonoBehaviour
    {
        const float Size = 0.05f;
        const float FollowSpeed = 10f;

        Transform marker;
        Renderer markerRenderer;

        void Start()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "GuidedHighlight_Marker";
            Destroy(go.GetComponent<Collider>());
            marker = go.transform;
            marker.SetParent(transform, false);
            marker.localScale = Vector3.one * Size;
            markerRenderer = go.GetComponent<Renderer>();
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;

            // The same pulse the port-site landmarks use; a plain mint disc if the shader isn't in the build.
            var pulse = Shader.Find("SF/FX/Target Pulse");
            markerRenderer.material = pulse != null ? new Material(pulse) : Rendering.RuntimeMaterials.Lit(new Color(0.36f, 0.88f, 0.78f, 0.9f));
            markerRenderer.material.color = new Color(0.36f, 0.88f, 0.78f, 0.9f);
            if (markerRenderer.material.HasProperty("_Color")) markerRenderer.material.SetColor("_Color", new Color(0.36f, 0.88f, 0.78f, 1f));
            go.SetActive(false);
        }

        void LateUpdate()
        {
            if (marker == null) return;
            var target = Target();
            if (target == null)
            {
                if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }

            if (!marker.gameObject.activeSelf)
            {
                marker.position = target.Value; // appears on the target; only later moves glide
                marker.gameObject.SetActive(true);
            }
            marker.position = Vector3.Lerp(marker.position, target.Value, Mathf.Clamp01(Time.deltaTime * FollowSpeed));

            // Faces whoever is looking: the learner in the room, which is also roughly the scope's side for cavity targets.
            var head = LearnerRig.Head;
            if (head != null && (head.position - marker.position).sqrMagnitude > 1e-4f)
                marker.rotation = Quaternion.LookRotation(marker.position - head.position, Vector3.up);
        }

        static Vector3? Target()
        {
            var session = SessionManager.Instance;
            if (session != null && (!session.IsRunning || session.IsPaused || session.Settings.mode != TrainingMode.Guided)) return null;
            var stage = StageController.Current;
            return stage != null ? stage.GuideTarget : null;
        }
    }
}
