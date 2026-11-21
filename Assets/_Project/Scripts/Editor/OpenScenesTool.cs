using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Lists every scene open in the Editor (Bridge/open_scenes.txt). Streamed city chunks must NOT be open in the Editor:
    /// an open chunk cannot be streamed in at Play, which blocks the player spawn.
    /// </summary>
    public static class OpenScenesTool
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        [BridgeCommand]
        public static void ListOpenScenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                sb.AppendLine($"{i}: {s.path} loaded={s.isLoaded} dirty={s.isDirty}");
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "open_scenes.txt"), sb.ToString());
        }

        /// <summary>Closes every open scene except CityDriving (never saves them).</summary>
        [BridgeCommand]
        public static void CloseStrayChunkScenes()
        {
            int closed = 0;
            for (int i = EditorSceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.path == CityScenePath || EditorSceneManager.sceneCount <= 1) continue;
                if (EditorSceneManager.CloseScene(s, true)) closed++;
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "open_scenes.txt"), $"closed {closed}; remaining {EditorSceneManager.sceneCount}\n");
        }
    }
}
