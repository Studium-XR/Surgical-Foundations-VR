using System.Collections.Generic;
using SurgicalFoundations.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.Core
{
    /// <summary>
    /// Makes the lobby settings do something. SessionManager only stores them; this applies them:
    ///  • Input (Hands / Controllers) is a recorded preference only. The rig already switches between them by itself:
    ///    put the controllers down and the tracked hands take over, pick them up and they come back. Switching the
    ///    controllers off from here took away the pointer the learner was clicking with, so the lobby locked up.
    ///  • Text size: every TextMeshPro label on a world-space canvas is scaled from its authored size.
    /// Installs itself; nothing has to be added to the scenes.
    /// </summary>
    public class SettingsApplier : MonoBehaviour
    {
        const float RescanInterval = 1f;

        readonly Dictionary<TMP_Text, float> baseSizes = new Dictionary<TMP_Text, float>();
        float nextScan;
        SessionManager subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SettingsApplier>() != null) return;
            var go = new GameObject("SettingsApplier");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsApplier>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (subscribed != null) subscribed.SettingsChanged -= Apply;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => nextScan = 0f;

        void Update()
        {
            // SessionManager lives in 00_Bootstrap, which may load after this installs.
            var session = SessionManager.Instance;
            if (session != subscribed)
            {
                if (subscribed != null) subscribed.SettingsChanged -= Apply;
                subscribed = session;
                if (session != null) { session.SettingsChanged += Apply; Apply(); }
            }

            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + RescanInterval;
            ApplyTextScale(); // panels instantiated since the last pass
        }

        void Apply()
        {
            ApplyTextScale();
        }

        void ApplyTextScale()
        {
            var session = SessionManager.Instance;
            var scale = session != null ? session.Settings.textScale : 1f;

            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
                foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!baseSizes.TryGetValue(t, out var size))
                    {
                        // First sight of this label: nothing but this pass resizes it, so this is its authored size.
                        size = t.fontSize;
                        baseSizes[t] = size;
                    }
                    var target = size * scale;
                    if (!Mathf.Approximately(t.fontSize, target)) t.fontSize = target;
                    if (t.enableAutoSizing) t.fontSizeMax = Mathf.Max(t.fontSizeMax, target);
                }
            }

            // Forget labels whose panels were unloaded.
            var dead = new List<TMP_Text>();
            foreach (var kv in baseSizes) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) baseSizes.Remove(d);
        }
    }
}
