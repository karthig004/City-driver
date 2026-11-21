using System.IO;
using CityDriver.World;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Bakes the city map image from the generated road graph (so the game does not spend startup time drawing it).</summary>
    internal static class MapBaker
    {
        public const string RoadGraphPath = "Assets/_Project/Data/World/CityRoadGraph.asset";
        public const string MapPath = "Assets/_Project/Art/UI/CityMap.png";

        public static Texture2D Bake(int size = 2048)
        {
            var graph = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(RoadGraphPath);
            if (graph == null) throw new System.InvalidOperationException("Road graph asset missing. Run WorldTests.GenerateAndTest first.");

            CityAssetFactory.EnsureFolder("Assets/_Project/Art/UI");
            Texture2D tex = RoadMapRenderer.Render(graph.data, size, null, true);
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, MapPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MapPath);
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
        }
    }
}
