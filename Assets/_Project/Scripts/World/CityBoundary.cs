using CityDriver.Population;
using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.World
{
    /// <summary>
    /// Keeps the player inside the city: a solid, visible barrier (concrete base with a red-white band) runs round the whole edge, a little outside
    /// the last road, so the green land beyond can no longer be driven into. If the car ever ends up outside (a teleport, a physics glitch) it is
    /// put back inside. <see cref="EdgeDistance"/> lets the HUD warn the player when the barrier is near.
    /// </summary>
    public sealed class CityBoundary : MonoBehaviour
    {
        /// <summary>Distance from the city centre (per axis) at which the barrier stands.</summary>
        public const float Limit = CityLayout.HalfSize + 28f;

        public static CityBoundary Instance { get; private set; }
        private VehicleController car;
        private float nextSearch;

        /// <summary>Metres between a point and the barrier (negative = outside).</summary>
        public static float EdgeDistance(Vector3 p) => Limit - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<CityBoundary>() != null) return;
            new GameObject("CityBoundary").AddComponent<CityBoundary>();
        }

        private void Awake()
        {
            Instance = this;
            BuildWall("North", new Vector3(0f, 0f, Limit), new Vector3(2f * Limit + 8f, 1f, 1f));
            BuildWall("South", new Vector3(0f, 0f, -Limit), new Vector3(2f * Limit + 8f, 1f, 1f));
            BuildWall("East", new Vector3(Limit, 0f, 0f), new Vector3(1f, 1f, 2f * Limit + 8f));
            BuildWall("West", new Vector3(-Limit, 0f, 0f), new Vector3(1f, 1f, 2f * Limit + 8f));
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void BuildWall(string name, Vector3 centre, Vector3 sizeXz)
        {
            var wall = new GameObject("Wall_" + name); wall.transform.SetParent(transform, false); wall.transform.position = centre;
            // the collider: tall and thick so nothing can hop or tunnel through it
            var col = wall.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 4f, 0f); col.size = new Vector3(sizeXz.x > sizeXz.z ? sizeXz.x : 3f, 9f, sizeXz.x > sizeXz.z ? 3f : sizeXz.z);
            // the look: concrete base + a red and white warning band
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(mesh.GetComponent<Collider>()); mesh.name = "Visual"; mesh.transform.SetParent(wall.transform, false);
            bool alongX = sizeXz.x > sizeXz.z; float len = alongX ? sizeXz.x : sizeXz.z;
            mesh.transform.localPosition = new Vector3(0f, 0.55f, 0f); mesh.transform.localScale = alongX ? new Vector3(len, 1.1f, 1.1f) : new Vector3(1.1f, 1.1f, len);
            mesh.GetComponent<Renderer>().sharedMaterial = CityDriver.Game.StageMaterial.Make(new Color(0.72f, 0.73f, 0.76f));
            var band = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(band.GetComponent<Collider>()); band.name = "Band"; band.transform.SetParent(wall.transform, false);
            band.transform.localPosition = new Vector3(0f, 1.3f, 0f); band.transform.localScale = alongX ? new Vector3(len, 0.35f, 1.15f) : new Vector3(1.15f, 0.35f, len);
            band.GetComponent<Renderer>().sharedMaterial = CityDriver.Game.StageMaterial.Make(new Color(0.86f, 0.1f, 0.1f));
            // a thin tall fence above so it reads from far away
            var fence = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(fence.GetComponent<Collider>()); fence.name = "Fence"; fence.transform.SetParent(wall.transform, false);
            fence.transform.localPosition = new Vector3(0f, 3.1f, 0f); fence.transform.localScale = alongX ? new Vector3(len, 3.2f, 0.12f) : new Vector3(0.12f, 3.2f, len);
            var fm = CityDriver.Game.StageMaterial.Make(new Color(0.16f, 0.17f, 0.2f)); fence.GetComponent<Renderer>().sharedMaterial = fm;
        }

        private void Update()
        {
            if (car == null && Time.unscaledTime >= nextSearch) { car = FindFirstObjectByType<VehicleController>(); nextSearch = Time.unscaledTime + 1f; }
            if (car == null) return;
            Vector3 p = car.transform.position;
            if (EdgeDistance(p) < -6f)                                     // outside: put the car back just inside, facing the city
            {
                float k = (Limit - 40f) / Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                var inside = new Vector3(p.x * k, p.y + 1f, p.z * k);
                Vector3 toCentre = new Vector3(-inside.x, 0f, -inside.z).normalized;
                car.Teleport(inside, Quaternion.LookRotation(toCentre));
                CityDriver.Game.GameUI.Instance?.ShowToast("City limit: you were brought back inside");
            }
        }
    }
}
