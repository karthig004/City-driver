using System.Collections.Generic;
using CityDriver.Navigation;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>
    /// Pooled street furniture around the player, placed along the real sidewalks of the road graph: litter bins, benches, planters,
    /// hawker stalls with striped awnings, parked motorbikes, shop-sign poles and bus shelters. Placement is deterministic per street
    /// (the same street always looks the same), everything beyond ~170 m is recycled, and solid props have colliders.
    /// </summary>
    public sealed class StreetDressing : MonoBehaviour
    {
        private const float ActiveRadius = 170f, SlotSpacing = 24f, NodeClearance = 16f;
        private const int MaxActive = 90;

        private struct Slot { public int edge, index, side, type; public Vector3 position; public Quaternion rotation; }

        private readonly Dictionary<long, GameObject> active = new Dictionary<long, GameObject>();
        private readonly Stack<GameObject> pool = new Stack<GameObject>();
        private readonly List<Slot> wanted = new List<Slot>();
        private TrafficNetwork net;
        private Transform viewer;
        private float next;
        private int groundMask;

        public static StreetDressing Instance { get; private set; }
        public int ActiveCount => active.Count;
        public IEnumerable<GameObject> ActiveObjects => active.Values;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<StreetDressing>() != null) return;
            new GameObject("StreetDressing").AddComponent<StreetDressing>();
        }

        private void Awake() { Instance = this; groundMask = LayerMask.GetMask("Road"); }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;

            if (net == null)
            {
                var nav = NavigationService.Instance;
                if (nav == null || nav.Data == null) return;
                net = new TrafficNetwork(nav.Data);
            }
            if (viewer == null) { var car = FindFirstObjectByType<VehicleController>(); if (car == null) return; viewer = car.transform; }

            Vector3 me = viewer.position;
            wanted.Clear();
            for (int e = 0; e < net.EdgeCount; e++)
            {
                RoadEdge ed = net.Edge(e);
                if (ed.roadClass == RoadClass.Highway) continue;
                Vector3 mid = net.edgeMid[e];
                float len = net.Length(e);
                if (Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(me.x, me.z)) > ActiveRadius + len * 0.5f) continue;
                CollectSlots(e, ed, len, me);
            }
            if (wanted.Count > MaxActive) wanted.Sort((a, b) => (a.position - me).sqrMagnitude.CompareTo((b.position - me).sqrMagnitude));

            var keep = new HashSet<long>();
            int limit = Mathf.Min(MaxActive, wanted.Count);
            for (int i = 0; i < limit; i++)
            {
                Slot s = wanted[i];
                long key = ((long)s.edge << 24) | ((long)s.index << 2) | (long)s.side;
                keep.Add(key);
                if (!active.ContainsKey(key)) Place(key, s);
            }
            var drop = new List<long>();
            foreach (var kv in active) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (long k in drop) { var go = active[k]; go.SetActive(false); pool.Push(go); active.Remove(k); }
        }

        private void CollectSlots(int e, RoadEdge ed, float len, Vector3 me)
        {
            Vector3 a = net.NodePos(ed.from), b = net.NodePos(ed.to);
            Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
            Vector3 left = TrafficNetwork.Left(dir);
            float sw = TrafficNetwork.SidewalkWidth(ed.roadClass);
            float offset = ed.width * 0.5f + sw * 0.62f;
            int count = Mathf.FloorToInt((len - NodeClearance * 2f) / SlotSpacing);
            for (int i = 0; i < count; i++)
            {
                for (int side = 0; side < 2; side++)
                {
                    uint h = Hash(e, i, side);
                    if (h % 100 > 62) continue;                                           // not every slot has something
                    float along = NodeClearance + (i + 0.5f) * SlotSpacing + ((h >> 8) % 9) - 4f;
                    Vector3 pos = a + dir * along + (side == 0 ? left : -left) * offset;
                    if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(me.x, me.z)) > ActiveRadius) continue;
                    int type = (int)((h >> 16) % 7);
                    if (type == 6 && (ed.roadClass == RoadClass.Local)) type = 1;            // bus shelters only on collectors / arterials
                    Vector3 toRoad = (side == 0 ? -left : left);
                    wanted.Add(new Slot { edge = e, index = i, side = side, type = type, position = pos, rotation = Quaternion.LookRotation(toRoad) });
                }
            }
        }

        private static uint Hash(int a, int b, int c) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 1274126177u; return h ^ (h >> 16); } }

        private void Place(long key, Slot s)
        {
            GameObject go;
            if (pool.Count > 0) { go = pool.Pop(); go.SetActive(true); }
            else
            {
                go = new GameObject("Prop");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = PopulationMeshes.Material;
                go.AddComponent<BoxCollider>();
            }
            go.name = "Prop_" + s.type;
            go.GetComponent<MeshFilter>().sharedMesh = PopulationMeshes.Prop(s.type);
            Vector3 p = s.position;
            if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 16f, groundMask)) p.y = hit.point.y;
            go.transform.SetPositionAndRotation(p, s.rotation);

            var box = go.GetComponent<BoxCollider>();
            Bounds bb = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            box.center = bb.center; box.size = s.type == 6 ? new Vector3(bb.size.x, bb.size.y, 0.3f) : bb.size;
            box.enabled = s.type != 5 || true;
            active[key] = go;
        }
    }
}
