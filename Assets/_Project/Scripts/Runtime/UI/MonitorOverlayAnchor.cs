using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Puts the stages' monitor overlays (UI_Monitor_*: drift arrow, task hints) on the laparoscopic tower's screen.
    /// The scenes place them at a fixed point that matched the old tower; the tower prefab marks its real screen with
    /// ScreenOverlayAnchor, so the overlays follow whatever tower model is in the theatre.
    /// </summary>
    public static class MonitorOverlayAnchor
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Transform anchor = null;
            for (int i = 0; i < SceneManager.sceneCount && anchor == null; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == "ScreenOverlayAnchor") { anchor = t; break; }
                    if (anchor != null) break;
                }
            }
            if (anchor == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("UI_Monitor_")) t.position = anchor.position;
        }
    }
}
