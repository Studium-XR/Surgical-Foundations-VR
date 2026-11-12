using System.Collections;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Core;
using TMPro;
using UnityEngine;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Speaker-icon subtitle bar while a voice prompt plays (US-ACS-04). The bar is world-locked: each new line is placed
    /// in front of the learner only if the last spot is out of view, and it does not follow the head while it shows.
    /// </summary>
    public class SubtitlePresenter : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text label;
        [SerializeField] float fade = 0.2f;

        Coroutine running;

        void Awake() => group.alpha = 0f;

        IEnumerator Start()
        {
            while (AudioManager.Instance == null) yield return null;
            AudioManager.Instance.SubtitleRequested += Show;
        }

        void OnDestroy()
        {
            if (AudioManager.Instance != null) AudioManager.Instance.SubtitleRequested -= Show;
        }

        public void Show(string text, float seconds)
        {
            var s = SessionManager.Instance;
            if (s != null && !s.Settings.subtitles) return;
            label.text = $"“{text}”";
            var anchor = GetComponentInParent<WorldLockedPanel>();
            if (anchor != null) anchor.PlaceIfOutOfView();
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(seconds));
        }

        IEnumerator Run(float seconds)
        {
            for (float t = 0; t < fade; t += Time.unscaledDeltaTime) { group.alpha = t / fade; yield return null; }
            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0; t < fade; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / fade; yield return null; }
            group.alpha = 0f;
        }
    }
}
