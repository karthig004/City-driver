using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Generates the small set of anti-aliased UI sprites the HUD needs (circle, rounded panel, triangle arrow).</summary>
    internal static class UiSprites
    {
        public const string Dir = "Assets/_Project/UI/Sprites";

        public static Sprite Circle, Round, Triangle;

        public static void EnsureAll()
        {
            CityAssetFactory.EnsureFolder(Dir);
            Circle = Make("ui_circle", 128, (x, y, s) => Smooth(s * 0.5f - Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f))), null);
            Round = Make("ui_round", 128, (x, y, s) => RoundedBox(x, y, s, 36f), new Vector4(40, 40, 40, 40));
            Triangle = Make("ui_triangle", 128, (x, y, s) => TriangleAlpha(x, y, s), null);
        }

        private static float Smooth(float signedDistance) => Mathf.Clamp01(signedDistance + 0.5f);

        private static float RoundedBox(float x, float y, int size, float radius)
        {
            Vector2 p = new Vector2(x, y) - new Vector2(size * 0.5f, size * 0.5f);
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(size * 0.5f - radius, size * 0.5f - radius);
            float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            return Smooth(-d);
        }

        // Upward-pointing arrow head, 4x4 supersampled for clean edges.
        private static float TriangleAlpha(int x, int y, int size)
        {
            float hit = 0f;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    float u = (x + (sx + 0.5f) / 4f) / size, v = (y + (sy + 0.5f) / 4f) / size;   // 0..1, v up
                    float top = 0.92f, bottom = 0.10f;
                    if (v < bottom || v > top) continue;
                    float halfWidth = 0.46f * (top - v) / (top - bottom);                         // wide at the bottom, point at the top
                    if (Mathf.Abs(u - 0.5f) <= halfWidth) hit += 1f;
                }

            return hit / 16f;
        }

        private static Sprite Make(string name, int size, Func<int, int, int, float> alpha, Vector4? border)
        {
            string assetPath = $"{Dir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = new Color(1f, 1f, 1f, alpha(x, y, size));
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (border.HasValue) importer.spriteBorder = border.Value;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
