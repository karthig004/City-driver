using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.World
{
    public enum RoadClass { Local = 0, Collector = 1, Arterial = 2, Highway = 3 }

    public enum DistrictType { Residential, Commercial, Downtown, Industrial, Entertainment, Highway, Special }

    public enum PoiType { Hospital, PoliceStation, SafeHouse, PetrolStation, Garage, RepairWorkshop, LuxuryBuilding, Nightclub, Cinema, Hotel, Warehouse }

    [Serializable]
    public struct RoadNode
    {
        public int id;                    // equals the node's index in the list
        public Vector3 position;
        public bool signalised;
        public DistrictType district;
    }

    [Serializable]
    public struct RoadEdge
    {
        public int id;                    // equals the edge's index in the list
        public int from, to;
        public RoadClass roadClass;
        public int lanesPerDirection;
        public float width;               // total carriageway width in metres
        public float speedLimitKmh;
        public bool oneWay;               // from -> to only
        public float length;
    }

    [Serializable]
    public struct DistrictRect
    {
        public string name;
        public DistrictType type;
        public Vector2 min, max;          // world X/Z
        public bool Contains(float x, float z) => x >= min.x && x <= max.x && z >= min.y && z <= max.y;
    }

    [Serializable]
    public struct PointOfInterest
    {
        public string id;
        public string displayName;
        public PoiType type;
        public Vector3 position;
        public DistrictType district;
    }

    /// <summary>The city's road network as plain serialisable data. Shared by routing, traffic, minimap, full map and the generator.</summary>
    [Serializable]
    public sealed class RoadGraphData
    {
        public List<RoadNode> nodes = new List<RoadNode>();
        public List<RoadEdge> edges = new List<RoadEdge>();
        public List<DistrictRect> districts = new List<DistrictRect>();
        public List<PointOfInterest> pois = new List<PointOfInterest>();
    }
}
