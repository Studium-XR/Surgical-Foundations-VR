using UnityEngine;

namespace SurgicalFoundations.Procedures
{
    /// <summary>
    /// Spoken lines for characters with no recorded voice yet: the headset's own text-to-speech engine on Android
    /// (Quest, Android XR), nothing elsewhere. Subtitles are shown by the caller either way.
    /// </summary>
    public static class Speech
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject tts;
        static bool ready;

        class InitListener : AndroidJavaProxy
        {
            public InitListener() : base("android.speech.tts.TextToSpeech$OnInitListener") { }
            public void onInit(int status) => ready = status == 0; // TextToSpeech.SUCCESS
        }

        static void Ensure()
        {
            if (tts != null) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener());
            }
            catch (System.Exception e) { Debug.LogWarning("[Speech] Text-to-speech unavailable: " + e.Message); }
        }
#endif

        /// <summary>Starts loading the engine so the first line is not delayed.</summary>
        public static void Warm()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Ensure();
#endif
        }

        /// <param name="pitch">1 = normal; slightly lower or higher gives characters different voices.</param>
        public static void Say(string text, float pitch = 1f, float rate = 0.95f)
        {
            if (string.IsNullOrEmpty(text)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Ensure();
            if (tts == null || !ready) return;
            try
            {
                tts.Call<int>("setPitch", pitch);
                tts.Call<int>("setSpeechRate", rate);
                tts.Call<int>("speak", text, 0, null, "sf-" + Time.frameCount); // QUEUE_FLUSH
            }
            catch (System.Exception e) { Debug.LogWarning("[Speech] " + e.Message); }
#endif
        }

        public static void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { tts?.Call<int>("stop"); } catch { }
#endif
        }
    }
}
