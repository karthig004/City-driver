using UnityEngine;

namespace CityDriver.Core
{
    /// <summary>
    /// Applies the <see cref="PerformanceProfile"/> once, before the first scene loads, so every scene
    /// (and the Editor's Play button on any scene) gets the same frame rate settings.
    /// No GameObject or manager is created, so this cannot produce duplicates.
    /// </summary>
    public static class PerformanceBootstrap
    {
        public const string ProfileResourceName = "PerformanceProfile";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            var profile = Resources.Load<PerformanceProfile>(ProfileResourceName);
            if (profile == null)
            {
                Debug.LogWarning($"[CityDriver] No '{ProfileResourceName}' asset found in a Resources folder. " +
                                 "Run the menu City Driver > Setup > Run Phase 2 Setup. Using Unity defaults.");
                return;
            }

            profile.Apply();
        }
    }
}
