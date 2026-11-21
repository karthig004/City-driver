using UnityEngine;

namespace CityDriver.UI
{
    /// <summary>
    /// Fits this RectTransform to the device's safe area (notches, rounded corners, gesture bars) so HUD and touch controls
    /// never sit under them. Re-applies when the screen size, orientation or safe area changes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void OnEnable() { rect = GetComponent<RectTransform>(); Apply(true); }

        private void Update() => Apply(false);

        private void Apply(bool force)
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == lastSafeArea && screen == lastScreen) return;
            lastSafeArea = safe; lastScreen = screen;
            if (screen.x <= 0 || screen.y <= 0) return;

            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
