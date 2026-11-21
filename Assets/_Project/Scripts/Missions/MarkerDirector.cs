using CityDriver.Navigation;
using UnityEngine;

namespace CityDriver.Missions
{
    /// <summary>
    /// Decides which mission waypoints the player can see. Gameplay (the mission system, Phase 8) calls
    /// <see cref="ShowOnly"/> with the active objective's location id; nothing else is ever shown. A separate debug mode
    /// can reveal every waypoint for testing. One instance lives in the scene.
    /// </summary>
    public sealed class MarkerDirector : MonoBehaviour
    {
        [Tooltip("Optional: location id to show at start for testing (e.g. pickup_a). Leave empty to show nothing.")]
        [SerializeField] private string previewMarkerId = "";

        private Transform viewer;
        private string activeId;
        private bool debugShowAll;

        public static MarkerDirector Instance { get; private set; }

        public string ActiveId => activeId;
        public bool DebugShowAll => debugShowAll;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[MarkerDirector] A second MarkerDirector exists; disabling this one.", this);
                enabled = false;
                return;
            }

            Instance = this;
            activeId = string.IsNullOrEmpty(previewMarkerId) ? null : previewMarkerId;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start() => Apply();

        /// <summary>Sets who distances are measured from and pushes it to every waypoint.</summary>
        public void SetViewer(Transform newViewer)
        {
            viewer = newViewer;
            foreach (var marker in LocationMarker.All)
                if (marker.Indicator != null) marker.Indicator.SetViewer(viewer);
        }

        /// <summary>Shows only the waypoint with this location id (gameplay call). Null or empty hides all.</summary>
        public void ShowOnly(string locationId)
        {
            activeId = string.IsNullOrEmpty(locationId) ? null : locationId;
            Apply();

            // The active objective is also the GPS destination.
            if (NavigationService.Instance != null)
            {
                if (activeId == null) NavigationService.Instance.ClearDestination();
                else NavigationService.Instance.SetDestinationToLocation(activeId);
            }
        }

        public void HideAll() => ShowOnly(null);

        /// <summary>Debug aid: reveals every pickup/drop-off waypoint regardless of the active objective.</summary>
        public void SetDebugShowAll(bool value)
        {
            debugShowAll = value;
            Apply();
        }

        private void Apply()
        {
            foreach (var marker in LocationMarker.All)
            {
                bool isWaypoint = marker.Kind == LocationKind.Pickup || marker.Kind == LocationKind.Dropoff;
                if (!isWaypoint) continue;

                bool visible = debugShowAll || (activeId != null && marker.LocationId == activeId);
                marker.SetVisualsActive(visible);
                if (visible && marker.Indicator != null) marker.Indicator.SetViewer(viewer);
            }
        }
    }
}
