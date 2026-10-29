using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// After a scene loads (and the rig has been placed under the fade), nudges every step panel that sits inside a
    /// prop, or behind one from where the learner stands, to the nearest clear spot (see <see cref="PanelClearance"/>).
    /// Panels that are already clear keep their authored place. Screens drawn on the monitors, the HUD and subtitles
    /// are left alone. Installs itself.
    /// </summary>
    public class PanelClearanceInstaller : MonoBehaviour
    {
        const float SettleSeconds = 1.2f;

        static PanelClearanceInstaller runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (runner == null)
            {
                var go = new GameObject("PanelClearance");
                DontDestroyOnLoad(go);
                runner = go.AddComponent<PanelClearanceInstaller>();
            }
            runner.StartCoroutine(runner.ClearScene(scene));
        }

        IEnumerator ClearScene(Scene scene)
        {
            yield return new WaitForSecondsRealtime(SettleSeconds);
            if (!scene.isLoaded) yield break;
            var viewer = PanelClearance.Viewer();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.WorldSpace) continue;
                if (canvas.GetComponentInParent<XROrigin>() != null) continue;
                if (Skip(canvas.transform)) continue;
                if (PanelClearance.ClearCanvas((RectTransform)canvas.transform, viewer))
                    Debug.Log($"[PanelClearance] Moved {canvas.name} clear of the props");
            }
        }

        /// <summary>Monitor screens are meant to sit on the monitors; the HUD and subtitles follow the head.</summary>
        static bool Skip(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                var n = p.name;
                if (n.StartsWith("UI_Monitor") || n.StartsWith("UI_HUD") || n.StartsWith("UI_Subtitles") || n.StartsWith("Bar_CameraAssistant")) return true;
            }
            return false;
        }
    }
}
