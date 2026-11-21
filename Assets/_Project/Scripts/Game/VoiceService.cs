using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Spoken dialogue through the phone's built-in text-to-speech (Android). There are no recorded voice files in the project;
    /// the quality and the available languages (including Tamil, where installed) depend on the device's TTS engine.
    /// In the Editor and on other platforms it does nothing. NOT verified on a physical device. No lip-sync (no talking character models).
    /// </summary>
    public static class VoiceService
    {
        public static int SpokenCount { get; private set; }
        public static string LastLine { get; private set; } = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject tts;
        private static bool ready, failed;
        private static AndroidJavaObject activity;
#endif

        public static bool Available
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return !failed;
#else
                return false;
#endif
            }
        }

        public static void Speak(string speaker, string line)
        {
            if (!GameSettings.Voice || string.IsNullOrEmpty(line)) return;
            SpokenCount++; LastLine = $"{speaker}: {line}";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (tts == null && !failed)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new TtsInit());
                }
                if (tts == null || !ready) return;
                float pitch = 0.85f + (Mathf.Abs(speaker.GetHashCode()) % 40) / 100f;          // a different voice colour per speaker
                tts.Call<int>("setPitch", pitch);
                var locale = new AndroidJavaObject("java.util.Locale", "en", "IN");
                tts.Call<int>("setLanguage", locale);
                tts.Call<int>("speak", line, 0, null, "cd_" + SpokenCount);
            }
            catch (System.Exception e) { failed = true; Debug.LogWarning("[Voice] text-to-speech unavailable: " + e.Message); }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class TtsInit : AndroidJavaProxy
        {
            public TtsInit() : base("android.speech.tts.TextToSpeech$OnInitListener") { }
            public void onInit(int status) { ready = status == 0; if (!ready) failed = true; }
        }
#endif
    }
}
