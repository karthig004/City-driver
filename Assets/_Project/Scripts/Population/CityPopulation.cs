using System.Collections.Generic;
using CityDriver.Navigation;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityDriver.Population
{
    /// <summary>
    /// Keeps a small, pooled crowd around the player: moving traffic, parked cars and sidewalk pedestrians. Everything is
    /// recycled near the player (no allocation during play), and nothing is simulated far away, so the cost is bounded.
    /// It is created automatically when the CityDriving scene loads.
    /// </summary>
    public sealed class CityPopulation : MonoBehaviour
    {
        public const string SceneName = "CityDriving";

        [SerializeField, Range(0, 24)] private int movingCars = 10;
        [SerializeField, Range(0, 24)] private int parkedCars = 8;
        [SerializeField, Range(0, 40)] private int pedestrians = 16;
        [SerializeField] private float carRecycleDistance = 270f, parkedRecycleDistance = 230f, pedRecycleDistance = 170f;

        private readonly List<TrafficCar> cars = new List<TrafficCar>();
        private readonly List<TrafficCar> parked = new List<TrafficCar>();
        private readonly List<Pedestrian> people = new List<Pedestrian>();
        private readonly List<TrafficCar> allCars = new List<TrafficCar>();
        private TrafficNetwork net;
        private Transform viewer;
        private Camera cam;
        private int groundCursor, mask;

        public static CityPopulation Instance { get; private set; }
        public bool Ready => net != null && viewer != null;
        public int ActiveMovingCars { get { int n = 0; foreach (var c in cars) if (c.Active) n++; return n; } }
        public int ActiveParkedCars { get { int n = 0; foreach (var c in parked) if (c.Active) n++; return n; } }
        public int ActivePedestrians { get { int n = 0; foreach (var p in people) if (p.Active) n++; return n; } }
        public int CarsWaitingAtSignals { get { int n = 0; foreach (var c in cars) if (c.Active && c.StoppedForSignalSeconds > 0.5f) n++; return n; } }
        public int MovingCarsInMotion { get { int n = 0; foreach (var c in cars) if (c.Active && c.Speed > 1f) n++; return n; } }
        public int PedestriansInMotion { get { int n = 0; foreach (var p in people) if (p.Active && !p.Idle) n++; return n; } }

        public int VisibleMovingCars(Camera camera) { int n = 0; foreach (var c in cars) if (c.Active && InView(camera, c.transform.position)) n++; return n; }
        public int VisibleParkedCars(Camera camera) { int n = 0; foreach (var c in parked) if (c.Active && InView(camera, c.transform.position)) n++; return n; }
        public int VisiblePedestrians(Camera camera) { int n = 0; foreach (var p in people) if (p.Active && InView(camera, p.transform.position)) n++; return n; }

        /// <summary>Test/diagnostic audit: pedestrians must be off the carriageway, moving cars must be on the left of the road centre mid-block.</summary>
        public void Audit(out int pedChecked, out int pedOnRoad, out int carChecked, out int carWrongSide)
        {
            pedChecked = pedOnRoad = carChecked = carWrongSide = 0;
            var nav = NavigationService.Instance;
            if (nav == null || nav.Graph == null) return;
            foreach (var p in people)
            {
                if (!p.Active) continue;
                if (!nav.Graph.TrySnap(p.transform.position, out RoadSnap snap)) continue;
                pedChecked++;
                if (snap.distance < nav.Graph.Edge(snap.edge).width * 0.5f + 0.3f) pedOnRoad++;
            }
            foreach (var c in cars)
            {
                if (!c.Active || c.Speed < 0.5f) continue;
                if (!nav.Graph.TrySnap(c.transform.position, out RoadSnap snap) || snap.t < 0.25f || snap.t > 0.75f) continue;
                carChecked++;
                Vector3 left = Vector3.Cross(c.transform.forward, Vector3.up);
                if (Vector3.Dot(c.transform.position - snap.point, left) < 0f) carWrongSide++;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;
#if UNITY_EDITOR
            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Bridge", "disable_game_layer.txt"))) return;   // bisecting aid
#endif
            if (FindFirstObjectByType<CityPopulation>() != null) return;
            new GameObject("CityPopulation").AddComponent<CityPopulation>();
        }

        private void Awake()
        {
            Instance = this;
            mask = LayerMask.GetMask("Road");
            if (QualitySettings.GetQualityLevel() == 0) { movingCars = Mathf.Min(movingCars, 8); parkedCars = Mathf.Min(parkedCars, 6); pedestrians = Mathf.Min(pedestrians, 12); }
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private bool TryInit()
        {
            var nav = NavigationService.Instance;
            if (nav == null || nav.Data == null) return false;
            if (viewer == null)
            {
                var car = FindFirstObjectByType<VehicleController>();
                if (car == null) return false;
                viewer = car.transform;
            }
            if (net == null)
            {
                net = new TrafficNetwork(nav.Data);
                for (int i = 0; i < movingCars; i++) { var c = MakeCar("TrafficCar_" + i, true); cars.Add(c); allCars.Add(c); }
                for (int i = 0; i < parkedCars; i++) { var c = MakeCar("ParkedCar_" + i, false); parked.Add(c); allCars.Add(c); }
                for (int i = 0; i < pedestrians; i++) people.Add(MakePedestrian("Pedestrian_" + i));
                foreach (var c in cars) c.transform.gameObject.SetActive(false);
                foreach (var c in parked) c.transform.gameObject.SetActive(false);
                foreach (var p in people) p.transform.gameObject.SetActive(false);
            }
            cam = Camera.main;
            return true;
        }

        /// <summary>Gives a traffic car a random baked model and paint (falls back to the old box meshes when the baked set is missing).</summary>
        private static void ApplyCarLook(MeshFilter mf, MeshRenderer mr)
        {
            var set = TrafficCarSet.Get();
            if (set != null && set.ShapeCount > 0) { mf.sharedMesh = set.traffic[Random.Range(0, set.ShapeCount)]; mr.sharedMaterials = set.Materials(Random.Range(0, set.PaintCount)); }
            else { mf.sharedMesh = PopulationMeshes.Car(Random.Range(0, PopulationMeshes.CarPaintCount)); mr.sharedMaterial = PopulationMeshes.Material; }
        }

        private TrafficCar MakeCar(string name, bool moving)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mfCar = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            ApplyCarLook(mfCar, mr);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var box = go.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0.68f, 0); box.size = new Vector3(1.8f, 1.3f, 4.2f);
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            return new TrafficCar(go.transform);
        }

        private Pedestrian MakePedestrian(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { PopulationMeshes.Material, PopulationMeshes.FaceMaterial };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new Pedestrian(go.transform, mf);
        }

        private void Update()
        {
            if (net == null || viewer == null) { if (!TryInit()) return; }
            if (cam == null) cam = Camera.main;

            float dt = Time.deltaTime, time = Time.time;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            Vector3 me = viewer.position;

            // simulate
            foreach (var c in cars)
            {
                if (!c.Active) continue;
                c.Tick(dt, time, allCars, viewer);
            }
            foreach (var p in people) if (p.Active) p.Tick(dt);

            SampleGround();

            // recycle at most a few agents per frame so there are no spikes
            int budget = 3;
            foreach (var c in cars)
            {
                if (budget <= 0) break;
                if (!c.Active || c.NeedsRecycle || Far(c.transform.position, me, carRecycleDistance)) { if (PlaceMoving(c, me, !c.Active)) budget--; }
            }
            foreach (var c in parked)
            {
                if (budget <= 0) break;
                if (!c.Active || Far(c.transform.position, me, parkedRecycleDistance)) { if (PlaceParked(c, me, !c.Active)) budget--; }
            }
            foreach (var p in people)
            {
                if (budget <= 0) break;
                if (!p.Active || Far(p.transform.position, me, pedRecycleDistance)) { if (PlacePedestrian(p, me, !p.Active)) budget--; }
            }
        }

        private static bool Far(Vector3 a, Vector3 b, float d) { a.y = 0; b.y = 0; return (a - b).sqrMagnitude > d * d; }

        private static bool InView(Camera camera, Vector3 world)
        {
            if (camera == null) return false;
            Vector3 vp = camera.WorldToViewportPoint(world + Vector3.up);
            return vp.z > 0f && vp.z < 150f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        }

        // A few ground raycasts per frame keep cars and pedestrians on the road / sidewalk height.
        private void SampleGround()
        {
            int total = allCars.Count + people.Count;
            if (total == 0) return;
            for (int k = 0; k < 6; k++)
            {
                groundCursor = (groundCursor + 1) % total;
                Transform t = groundCursor < allCars.Count ? allCars[groundCursor].transform : people[groundCursor - allCars.Count].transform;
                if (!t.gameObject.activeSelf) continue;
                Vector3 p = t.position;
                if (Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore))
                {
                    if (groundCursor < allCars.Count) allCars[groundCursor].SetGround(hit.point.y); else people[groundCursor - allCars.Count].SetGround(hit.point.y);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------- placement
        private bool PickEdge(Vector3 me, float min, float max, bool avoidView, bool noHighway, float minLength, out int edge, out Vector3 point)
        {
            edge = -1; point = Vector3.zero;
            int count = net.EdgeCount;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int e = Random.Range(0, count);
                RoadEdge ed = net.Edge(e);
                if (noHighway && ed.roadClass == RoadClass.Highway) continue;
                if (net.Length(e) < minLength) continue;
                Vector3 mid = net.edgeMid[e];
                float d = Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(me.x, me.z));
                if (d < min || d > max) continue;
                if (avoidView && attempt < 28 && InView(cam, mid)) continue;
                // prefer the half of the city the player is heading into, so the crowd is actually seen
                if (attempt < 24 && viewer != null)
                {
                    Vector3 f = viewer.forward; f.y = 0f; Vector3 to = mid - me; to.y = 0f;
                    if (Vector3.Dot(f.normalized, to.normalized) < (avoidView ? 0.55f : 0.35f)) continue;
                }
                edge = e; point = mid; return true;
            }
            return false;
        }

        private bool PlaceMoving(TrafficCar car, Vector3 me, bool initial)
        {
            if (!PickEdge(me, initial ? 45f : 110f, 230f, !initial, false, 40f, out int e, out _)) return false;
            RoadEdge ed = net.Edge(e);
            int from = Random.value < 0.5f ? ed.from : ed.to;
            if (!net.CanEnter(e, from)) from = net.Other(e, from);
            if (!net.CanEnter(e, from)) return false;
            float len = net.Length(e);
            car.transform.gameObject.SetActive(true);
            ApplyCarLook(car.transform.GetComponent<MeshFilter>(), car.transform.GetComponent<MeshRenderer>());
            car.PlaceMoving(net, e, from, Random.Range(14f, Mathf.Max(15f, len - 16f)), Random.Range(0, Mathf.Max(1, ed.lanesPerDirection)), Mathf.Clamp(ed.speedLimitKmh * 0.75f / 3.6f, 6f, 13f));
            return true;
        }

        private bool PlaceParked(TrafficCar car, Vector3 me, bool initial)
        {
            if (!PickEdge(me, initial ? 38f : 90f, 200f, !initial, true, 50f, out int e, out _)) return false;
            RoadEdge ed = net.Edge(e);
            int from = Random.value < 0.5f ? ed.from : ed.to;
            float len = net.Length(e);
            car.transform.gameObject.SetActive(true);
            ApplyCarLook(car.transform.GetComponent<MeshFilter>(), car.transform.GetComponent<MeshRenderer>());
            car.PlaceParked(net, e, from, Random.Range(14f, Mathf.Max(15f, len - 14f)));
            return true;
        }

        private bool PlacePedestrian(Pedestrian p, Vector3 me, bool initial)
        {
            if (!PickEdge(me, initial ? 22f : 70f, 140f, !initial, true, 40f, out int e, out _)) return false;
            RoadEdge ed = net.Edge(e);
            int from = Random.value < 0.5f ? ed.from : ed.to;
            float len = net.Length(e);
            p.transform.gameObject.SetActive(true);
            p.Place(net, e, from, Random.Range(12f, Mathf.Max(13f, len - 14f)), Random.value < 0.5f ? 1 : -1, Random.Range(0, PopulationMeshes.PedVariants));
            return true;
        }
    }
}
