using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// One-off visual upgrade of the city's shared materials (the baked chunks keep referencing the same assets, so nothing has to be rebuilt):
    /// a window-only emission map on the four building materials (lit by <see cref="CityDriver.Game.CityMaterialFx"/> at night), a richer asphalt
    /// texture (cracks, patches, wear), livelier foliage and brighter road paint.
    /// </summary>
    public static class CityLook
    {
        private const string Tex = "Assets/_Project/Art/Textures", Mats = "Assets/_Project/Art/Materials";

        private static float Hash(int x, int y, int seed)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
        }

        private static Texture2D Write(string name, int size, System.Func<int, int, Color> f, bool srgb)
        {
            string path = $"{Tex}/{name}.png";
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, false); var px = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x, y);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Trilinear; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.sRGBTexture = srgb; imp.maxTextureSize = 512;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [BridgeCommand]
        public static void Apply()
        {
            // 1. window emission map: same layout as T_Window (one 4 m x 3.5 m floor); only the glass glows
            var emit = Write("T_WindowEmit", 256, (x, y) =>
            {
                float fx = x / 256f, fy = y / 256f;
                bool glass = fx > 0.23f && fx < 0.77f && fy > 0.33f && fy < 0.80f && Mathf.Abs(fx - 0.5f) > 0.012f;
                if (!glass) return Color.black;
                float g = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(0.33f, 0.80f, fy));             // a little brighter near the ceiling, like a lamp
                float flicker = 0.85f + 0.15f * Hash(x / 8, y / 8, 21);
                return new Color(g * flicker, g * flicker * 0.92f, g * flicker * 0.8f, 1f);
            }, true);
            int n = 0;
            for (int i = 1; i <= 4; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/M_Building{i}.mat"); if (m == null) continue;
                m.SetTexture("_EmissionMap", emit); m.SetColor("_EmissionColor", Color.black); m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(m); n++;
            }
            // 2. asphalt: darker, with hairline cracks, lighter patches and speckle (replaces the plain noise; same file so every road keeps its material)
            Write("T_Asphalt", 256, (x, y) =>
            {
                float v = 0.17f + (Hash(x, y, 1) - 0.5f) * 0.06f;
                if (Hash(x, y, 2) > 0.988f) v += 0.12f;                                              // aggregate speckle
                float patch = Hash(x / 48, y / 48, 7); if (patch > 0.82f) v += 0.025f; else if (patch < 0.12f) v -= 0.02f;   // repaired patches
                float cr = Mathf.Abs(Mathf.Sin((x * 0.045f + y * 0.031f) * 3.1f) * 0.5f + Mathf.Sin(y * 0.11f + x * 0.02f) * 0.5f);
                if (cr < 0.012f && Hash(x / 24, y / 24, 9) > 0.55f) v *= 0.55f;                         // thin cracks
                float tracks = Mathf.Exp(-Mathf.Pow((x / 256f - 0.3f) * 14f, 2f)) + Mathf.Exp(-Mathf.Pow((x / 256f - 0.7f) * 14f, 2f)); v += tracks * 0.012f;   // polished wheel tracks
                return new Color(v, v, v * 1.04f, 1f);
            }, true);
            // 3. foliage and road paint
            Tweak("M_Leaves", new Color(0.15f, 0.46f, 0.15f), 0.2f); Tweak("M_Leaves2", new Color(0.34f, 0.55f, 0.14f), 0.2f);
            Tweak("M_PaintWhite", new Color(0.97f, 0.97f, 0.94f), 0.5f); Tweak("M_PaintYellow", new Color(1f, 0.8f, 0.08f), 0.5f);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[CityLook] window emission on {n} building materials, richer asphalt, foliage and paint updated");
        }

        private static void Tweak(string name, Color c, float smooth)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/{name}.mat"); if (m == null) return;
            m.SetColor("_BaseColor", c); m.color = c; if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(m);
        }
    }
}
