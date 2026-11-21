using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Missions
{
    public enum LocationKind
    {
        PlayerStart,
        GarageEntrance,
        Pickup,
        Dropoff,
    }

    /// <summary>
    /// Stable hook for gameplay systems: marks a named place in the city (spawn, garage, passenger pickup, drop-off).
    /// Missions refer to locations by <see cref="LocationId"/>, so the visual placeholder at this spot can be replaced freely.
    /// </summary>
    public sealed class LocationMarker : MonoBehaviour
    {
        [SerializeField] private LocationKind kind;
        [SerializeField] private string locationId;

        private static readonly List<LocationMarker> Registry = new List<LocationMarker>();

        /// <summary>Finds the first active marker of a kind (optionally with a specific id). No scene search.</summary>
        public static LocationMarker Find(LocationKind kind, string id = null)
        {
            for (int i = 0; i < Registry.Count; i++)
                if (Registry[i].kind == kind && (id == null || Registry[i].locationId == id)) return Registry[i];
            return null;
        }

        /// <summary>All currently enabled markers (read-only; no scene search).</summary>
        public static IReadOnlyList<LocationMarker> All => Registry;

        private WaypointIndicator indicator;

        /// <summary>The floating waypoint under this marker, if it has one (pickup / drop-off).</summary>
        public WaypointIndicator Indicator
        {
            get
            {
                if (indicator == null) indicator = GetComponentInChildren<WaypointIndicator>(true);
                return indicator;
            }
        }

        /// <summary>Shows or hides everything the player can see at this marker (waypoint, waiting passenger). The marker itself stays active.</summary>
        public void SetVisualsActive(bool active)
        {
            foreach (Transform child in transform) child.gameObject.SetActive(active);
        }

        private void OnEnable() => Registry.Add(this);
        private void OnDisable() => Registry.Remove(this);

        public LocationKind Kind => kind;
        public string LocationId => locationId;

        /// <summary>Used by the city builder; at runtime the values are read-only.</summary>
        public void Configure(LocationKind newKind, string newId)
        {
            kind = newKind;
            locationId = newId;
        }

#if UNITY_EDITOR
        // Editor-only scene aid (never part of a build, only visible with the Gizmos toggle on). Small and low-key on purpose.
        private void OnDrawGizmos()
        {
            switch (kind)
            {
                case LocationKind.Pickup: Gizmos.color = new Color(0.3f, 0.78f, 1f); break;
                case LocationKind.Dropoff: Gizmos.color = new Color(1f, 0.72f, 0.18f); break;
                case LocationKind.GarageEntrance: Gizmos.color = new Color(0.4f, 0.55f, 1f); break;
                default: Gizmos.color = Color.white; break;
            }

            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.35f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward * 1.2f);
        }
#endif
    }
}
