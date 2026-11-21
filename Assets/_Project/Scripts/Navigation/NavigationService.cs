using System;
using CityDriver.Missions;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Navigation
{
    /// <summary>
    /// The GPS. Owns the city's <see cref="RoadGraph"/> (loaded from the generated road-graph asset), the current destination,
    /// the computed route and the turn-by-turn guidance. Re-routes automatically when the player leaves the route.
    /// One instance lives in the scene; minimap, full map and the navigation banner all read from it.
    /// </summary>
    public sealed class NavigationService : MonoBehaviour
    {
        [SerializeField] private RoadGraphAsset graphAsset;

        [Tooltip("How often guidance is recalculated (seconds).")]
        [SerializeField, Min(0.05f)] private float updateInterval = 0.2f;
        [Tooltip("Minimum seconds between automatic re-routes.")]
        [SerializeField, Min(0.5f)] private float rerouteCooldown = 2.5f;
        [Tooltip("Assumed average speed used for the ETA (km/h).")]
        [SerializeField, Min(5f)] private float etaSpeedKmh = 38f;

        private readonly Route route = new Route();
        private readonly RouteGuidance guidance = new RouteGuidance();
        private RoadGraph graph;
        private Transform viewer;
        private Vector3 destination;
        private float nextUpdate, lastReroute = -100f;

        public static NavigationService Instance { get; private set; }

        /// <summary>Raised whenever the route (or destination) changes, including when it is cleared.</summary>
        public event Action RouteChanged;
        /// <summary>Raised once when the player reaches the destination.</summary>
        public event Action DestinationReached;

        public RoadGraph Graph => graph;
        public RoadGraphData Data => graphAsset != null ? graphAsset.data : null;
        public Route Route => route;
        /// <summary>While true the route stays on the map after "arrival" (set by the mission runner, which clears it when the mission ends).</summary>
        public bool KeepRouteOnArrival { get; set; }
        private bool arrivedFired;
        public bool HasDestination { get; private set; }
        public string DestinationLabel { get; private set; } = "";
        public Vector3 Destination => destination;
        public float DistanceRemaining => guidance.DistanceRemaining;
        public float EtaSeconds => guidance.DistanceRemaining / (etaSpeedKmh / 3.6f);
        public TurnType NextTurn => guidance.NextTurn;
        public float DistanceToNextTurn => guidance.DistanceToNextTurn;
        public string Instruction => HasDestination ? guidance.Instruction() : "";
        public int RouteVersion { get; private set; }

        /// <summary>Transparent texture holding only the route + destination ring; shared by the minimap and the full map.</summary>
        public Texture2D RouteOverlay { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Debug.LogWarning("[Navigation] Duplicate NavigationService ignored.", this); enabled = false; return; }
            Instance = this;
            if (graphAsset == null) { Debug.LogError("[Navigation] Road graph asset is not assigned.", this); enabled = false; return; }
            graph = new RoadGraph(graphAsset.data);
            RouteOverlay = new Texture2D(1536, 1536, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RouteOverlay" };
            RoadMapRenderer.RenderRouteOverlay(RouteOverlay, null, null);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (RouteOverlay != null) Destroy(RouteOverlay);
        }

        private void RedrawOverlay() => RoadMapRenderer.RenderRouteOverlay(RouteOverlay, HasDestination ? route : null, HasDestination ? destination : (Vector3?)null);

        public void SetViewer(Transform newViewer) => viewer = newViewer;

        /// <summary>Plans a route from the player to a world position. Returns false if no route exists.</summary>
        public bool SetDestination(Vector3 worldPosition, string label)
        {
            if (graph == null || viewer == null) return false;
            if (!graph.TryBuildRoute(viewer.position, worldPosition, route)) return false;

            destination = worldPosition; arrivedFired = false;
            DestinationLabel = label ?? "";
            HasDestination = true;
            guidance.SetRoute(route);
            RouteVersion++;
            RedrawOverlay();
            RouteChanged?.Invoke();
            return true;
        }

        /// <summary>Destination by id: a mission marker (pickup_a, dropoff_c, garage...) or a road-graph point of interest.</summary>
        public bool SetDestinationToLocation(string locationId)
        {
            foreach (var marker in LocationMarker.All)
                if (marker.LocationId == locationId) return SetDestination(marker.transform.position, FriendlyLabel(locationId));

            if (graphAsset != null)
                foreach (var poi in graphAsset.data.pois)
                    if (poi.id == locationId) return SetDestination(poi.position, poi.displayName);
            return false;
        }

        // mission trips use generated marker ids; show a readable name instead
        private static string FriendlyLabel(string id)
        {
            if (id == "trip_pickup") return "Passenger pickup";
            if (id == "trip_dropoff") { var r = CityDriver.Game.MissionRunner.Instance; return r != null && !string.IsNullOrEmpty(r.DestinationLabel) ? r.DestinationLabel : "Drop-off"; }
            return id;
        }

        public void ClearDestination()
        {
            if (!HasDestination) return;
            HasDestination = false;
            route.Clear();
            guidance.SetRoute(route);
            RouteVersion++;
            RedrawOverlay();
            RouteChanged?.Invoke();
        }

        private void Update()
        {
            if (!HasDestination || viewer == null || Time.time < nextUpdate) return;
            nextUpdate = Time.time + updateInterval;

            guidance.Update(viewer.position);

            if (guidance.Arrived)
            {
                if (!arrivedFired) { arrivedFired = true; DestinationReached?.Invoke(); }
                if (!KeepRouteOnArrival) { ClearDestination(); return; }      // missions keep the route and marker until the trip is actually finished
                return;
            }
            arrivedFired = false;

            if (guidance.OffRoute && Time.time - lastReroute > rerouteCooldown)
            {
                lastReroute = Time.time;
                SetDestination(destination, DestinationLabel);   // recalculated from the player's current position
            }
        }
    }
}
