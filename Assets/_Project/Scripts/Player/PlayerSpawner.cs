using CityDriver.CameraSystem;
using CityDriver.Controls;
using CityDriver.Missions;
using CityDriver.Navigation;
using CityDriver.UI;
using CityDriver.World;
using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.Player
{
    /// <summary>
    /// Spawns the player's vehicle at the scene's PlayerStart <see cref="LocationMarker"/>, facing the marker's forward
    /// direction, and hands it to the camera and HUD. Swap <see cref="vehiclePrefab"/> (e.g. from the Garage selection)
    /// to change the car without touching any gameplay code.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField] private VehicleCameraController cameraController;
        [SerializeField] private VehicleHUD hud;
        [SerializeField] private MinimapView minimap;
        [SerializeField] private FullMapView fullMap;
        [SerializeField] private ChunkStreamer chunkStreamer;

        [Tooltip("Layers the spawn point is dropped onto.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Height above the ground the car is dropped from (m).")]
        [SerializeField, Min(0f)] private float dropHeight = 0.35f;

        public VehicleController Vehicle { get; private set; }

        private System.Collections.IEnumerator Start()
        {
            // The ground around the spawn is streamed in; wait for it so the car never falls through unloaded roads.
            LocationMarker start = null;
            for (float waited = 0f; start == null && waited < 5f; waited += Time.unscaledDeltaTime) { start = LocationMarker.Find(LocationKind.PlayerStart); if (start == null) yield return null; }
            if (chunkStreamer != null && start != null)
            {
                float waited = 0f;
                while (!chunkStreamer.IsReadyAround(start.transform.position) && waited < 60f) { waited += Time.unscaledDeltaTime; yield return null; }
            }

            Spawn();
        }

        public void Spawn()
        {
            if (vehiclePrefab == null) { Debug.LogError("[PlayerSpawner] No vehicle prefab assigned.", this); return; }

            LocationMarker start = LocationMarker.Find(LocationKind.PlayerStart);
            if (start == null) { Debug.LogError("[PlayerSpawner] No PlayerStart LocationMarker found in the scene.", this); return; }

            Vector3 position = start.transform.position;
            if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, groundMask, QueryTriggerInteraction.Ignore))
                position = hit.point;
            position += Vector3.up * dropHeight;

            Quaternion rotation = Quaternion.Euler(0f, start.transform.eulerAngles.y, 0f);

            // Start on a real street in the left-hand lane, facing the longer stretch of road (so the first view is a street with traffic,
            // not an empty plaza). Falls back to the PlayerStart marker if there is no road graph.
            var nav = NavigationService.Instance;
            if (nav != null && nav.Graph != null && nav.Data != null && nav.Graph.TrySnap(start.transform.position, out RoadSnap snap))
            {
                RoadEdge edge = nav.Data.edges[snap.edge];
                Vector3 a = nav.Data.nodes[edge.from].position, b = nav.Data.nodes[edge.to].position;
                Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
                if (Vector3.Dot(dir, (Vector3.zero - snap.point).normalized) < 0f) dir = -dir;                 // head towards the city centre
                Vector3 laneCentre = snap.point + Vector3.Cross(dir, Vector3.up) * Mathf.Max(1.6f, edge.width * 0.5f * 0.45f);
                position = laneCentre;
                if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit2, 20f, groundMask, QueryTriggerInteraction.Ignore)) position = hit2.point;
                position += Vector3.up * dropHeight;
                rotation = Quaternion.LookRotation(dir);
            }
            GameObject instance = Instantiate(vehiclePrefab, position, rotation);
            instance.name = vehiclePrefab.name;

            Vehicle = instance.GetComponent<VehicleController>();
            if (Vehicle == null) { Debug.LogError($"[PlayerSpawner] '{vehiclePrefab.name}' has no VehicleController.", this); return; }

            if (cameraController != null) cameraController.SetTarget(instance.transform, Vehicle.GetComponent<Rigidbody>(), instance.GetComponent<VehicleInputHandler>());
            if (hud != null) hud.SetVehicle(Vehicle);
            if (MarkerDirector.Instance != null) MarkerDirector.Instance.SetViewer(instance.transform);
            if (NavigationService.Instance != null) NavigationService.Instance.SetViewer(instance.transform);
            if (minimap != null) minimap.SetViewer(instance.transform);
            if (fullMap != null) fullMap.SetViewer(instance.transform);
            if (chunkStreamer != null) chunkStreamer.SetViewer(instance.transform);
        }
    }
}
