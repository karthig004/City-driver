using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Keeps the CityDriving scene free of leftovers from automated tests.</summary>
    public static class SceneHygiene
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        /// <summary>
        /// Removes any Sedan_Placeholder instance saved in the scene. The player's car is created at Play by PlayerSpawner,
        /// so a saved instance is always a leftover (it would also answer to the keyboard).
        /// </summary>
        [BridgeCommand]
        public static void RemoveStrayVehicles()
        {
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            var scene = EditorSceneManager.GetActiveScene();
            var strays = scene.GetRootGameObjects().Where(g => g.name == "Sedan_Placeholder").ToList();
            foreach (var g in strays)
            {
                Debug.Log($"[Hygiene] Removing stray vehicle '{g.name}' at {g.transform.position}.");
                Object.DestroyImmediate(g);
            }

            if (strays.Count > 0) EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Hygiene] Stray vehicles removed: {strays.Count}.");
        }
    }
}
