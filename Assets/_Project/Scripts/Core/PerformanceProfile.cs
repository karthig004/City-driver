using UnityEngine;

namespace CityDriver.Core
{
    /// <summary>
    /// Designer-editable frame rate / vsync configuration. Loaded from Resources at startup by
    /// <see cref="PerformanceBootstrap"/>. Change the values in the asset (not in code) to move the
    /// Android target from 30 FPS to 60 FPS later.
    /// </summary>
    [CreateAssetMenu(fileName = "PerformanceProfile", menuName = "City Driver/Performance Profile")]
    public sealed class PerformanceProfile : ScriptableObject
    {
        [Header("Frame rate (FPS)")]
        [Tooltip("Target frame rate on Android / iOS / desktop builds. Use 30 now, 60 for a future high-end target.")]
        [SerializeField, Range(15, 120)] private int deviceTargetFrameRate = 30;

        [Tooltip("Target frame rate inside the Unity Editor. Kept low to reduce load on integrated GPUs.")]
        [SerializeField, Range(15, 120)] private int editorTargetFrameRate = 30;

        [Tooltip("Target frame rate for WebGL. -1 lets the browser drive the loop (recommended).")]
        [SerializeField, Range(-1, 120)] private int webTargetFrameRate = -1;

        [Header("Physics")]
        [Tooltip("Fixed physics step (s). 0.02 = 50 Hz. Raise to 0.0333 for cheaper physics on weak devices (less stable vehicles).")]
        [SerializeField, Range(0.01f, 0.05f)] private float fixedTimestep = 0.02f;

        [Header("Display")]
        [Tooltip("0 = vsync off (required for Application.targetFrameRate to take effect), 1 = every vblank.")]
        [SerializeField, Range(0, 2)] private int vSyncCount = 0;

        [Tooltip("Keep the screen awake while the game is running.")]
        [SerializeField] private bool keepScreenAwake = true;

        public int DeviceTargetFrameRate => deviceTargetFrameRate;
        public int EditorTargetFrameRate => editorTargetFrameRate;
        public int WebTargetFrameRate => webTargetFrameRate;

        /// <summary>Frame rate for the platform currently running.</summary>
        public int ActiveTargetFrameRate
        {
            get
            {
#if UNITY_EDITOR
                return EditorTargetFrameRate;
#elif UNITY_WEBGL
                return WebTargetFrameRate;
#else
                return DeviceTargetFrameRate;
#endif
            }
        }

        /// <summary>Applies the profile to the running application.</summary>
        public void Apply()
        {
            Time.fixedDeltaTime = fixedTimestep;
            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = ActiveTargetFrameRate;
            Screen.sleepTimeout = keepScreenAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }
    }
}
