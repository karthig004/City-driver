using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// First-impression settings that must always hold: no engine splash before the game's own studio card, a midnight background colour
    /// behind it (so there is no grey flash), and the custom WebGL loader page. Applied automatically when the editor loads.
    /// </summary>
    [InitializeOnLoad]
    public static class BrandingSettings
    {
        public const string WebTemplate = "PROJECT:CityDriver";

        static BrandingSettings() { EditorApplication.delayCall += Apply; }

        [BridgeCommand]
        public static void Apply()
        {
            bool changed = false;
            var midnight = new Color(0.02f, 0.03f, 0.06f, 1f);
            if (PlayerSettings.SplashScreen.show) { PlayerSettings.SplashScreen.show = false; changed = true; }
            if (PlayerSettings.SplashScreen.backgroundColor != midnight) { PlayerSettings.SplashScreen.backgroundColor = midnight; changed = true; }
            if (PlayerSettings.WebGL.template != WebTemplate && System.IO.Directory.Exists("Assets/WebGLTemplates/CityDriver")) { PlayerSettings.WebGL.template = WebTemplate; changed = true; }
            if (changed) { AssetDatabase.SaveAssets(); Debug.Log("[Branding] splash disabled, midnight background, WebGL template " + PlayerSettings.WebGL.template); }
        }
    }
}
