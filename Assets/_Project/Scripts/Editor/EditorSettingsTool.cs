using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Editor helpers for the saved player settings (PlayerPrefs), e.g. when a test left the camera on the top-down view.</summary>
    internal static class EditorSettingsTool
    {
        [BridgeCommand, MenuItem("City Driver/Settings/Reset camera to chase view", priority = 400)]
        public static void ResetCamera()
        {
            PlayerPrefs.DeleteKey("cd_camera"); PlayerPrefs.DeleteKey("cd_steer_mode");
            PlayerPrefs.Save();
            Debug.Log("[Settings] Camera view reset to the default chase view.");
        }
    }
}
