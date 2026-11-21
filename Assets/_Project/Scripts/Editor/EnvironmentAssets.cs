using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>One-off asset fixes for the weather system and the tree canopies.</summary>
    public static class EnvironmentAssets
    {
        /// <summary>Creates Resources/RainParticle.mat so the URP particle shader is included in Android builds.</summary>
        [BridgeCommand]
        public static void CreateRainMaterial()
        {
            const string dir = "Assets/_Project/Resources";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            string path = dir + "/RainParticle.mat";
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { Debug.LogError("[Env] URP Particles/Unlit shader not found"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetColor("_BaseColor", new Color(0.85f, 0.9f, 1f, 0.6f));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "env_assets.txt"), "rain material ok: " + path + "\n");
        }

        /// <summary>Gives the leaf materials a little emission so two-sided canopy cards in shadow stay green instead of dark brown.</summary>
        [BridgeCommand]
        public static void FixTreeLeaves()
        {
            int n = 0;
            foreach (string name in new[] { "M_LeafNeem", "M_LeafGulmohar", "M_LeafAshoka", "M_LeafPalm" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Art/Materials/{name}.mat");
                if (m == null) { foreach (string g in AssetDatabase.FindAssets(name + " t:Material")) m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); }
                if (m == null) continue;
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.05f, 0.13f, 0.035f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_BaseColor", new Color(1.12f, 1.12f, 1.12f, 1f));
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "env_assets.txt"), $"leaf materials fixed: {n}\n");
        }
    }
}
