using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Compares the in-memory Build Settings scene list with the chunk scenes on disk (Bridge/build_settings_audit.txt).</summary>
    public static class BuildSettingsAudit
    {
        [BridgeCommand]
        public static void Run()
        {
            var sb = new StringBuilder();
            var inSettings = EditorBuildSettings.scenes;
            sb.AppendLine($"Build Settings entries: {inSettings.Length}, enabled: {inSettings.Count(s => s.enabled)}");
            var onDisk = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/CityChunks" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            sb.AppendLine($"Chunk scenes on disk: {onDisk.Count}");
            foreach (string path in onDisk)
            {
                var entry = inSettings.FirstOrDefault(s => s.path == path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (entry == null) sb.AppendLine($"NOT IN SETTINGS: {path}");
                else if (!entry.enabled) sb.AppendLine($"DISABLED: {path}");
                else if (entry.guid.ToString() != guid) sb.AppendLine($"GUID MISMATCH: {path} settings={entry.guid} asset={guid}");
            }
            foreach (string name in new[] { "Chunk_5_4", "Chunk_5_5", "Chunk_4_4" })
            {
                string p = $"Assets/_Project/Scenes/CityChunks/{name}.unity";
                var e = inSettings.FirstOrDefault(s => s.path == p);
                sb.AppendLine($"{name}: in settings={(e != null)} enabled={(e != null && e.enabled)} index={System.Array.FindIndex(inSettings, s => s.path == p)} asset exists={File.Exists(p)}");
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "build_settings_audit.txt"), sb.ToString());
        }
    }
}
