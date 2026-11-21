using CityDriver.Career;
using CityDriver.Missions;
using CityDriver.Navigation;
using CityDriver.Population;
using CityDriver.Save;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Fuel makes the long trips matter: 1% of the tank per 150 m (a full tank is about 15 km; each Fuel Tank upgrade level adds 20%).
    /// Refuel near a petrol station or the garage (Rs 3 per %); if the tank runs dry the engine cuts out and a tow (Rs 400, may leave
    /// the wallet negative) restores 25%. The level is saved.
    /// </summary>
    public sealed class FuelSystem : MonoBehaviour
    {
        public const float MetresPerPercent = 150f, RefuelCostPerPercent = 3f, TowCost = 400f;

        public static FuelSystem Instance { get; private set; }
        public float Fuel01 { get; private set; } = 1f;
        public bool Empty => Fuel01 <= 0.001f;
        public float RangeKm => Fuel01 * 100f * MetresPerPercent * RangeMultiplier / 1000f;
        public float RangeMultiplier { get; private set; } = 1f;

        private VehicleController car;
        private Vector3 last; private bool haveLast;
        private float nextSave;
        private bool cut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<FuelSystem>() != null) return;
            new GameObject("FuelSystem").AddComponent<FuelSystem>();
        }

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; VehicleTuning.FuelFactor = 1f; }

        private void Update()
        {
            var session = GameSession.Instance;
            if (car == null) { car = FindFirstObjectByType<VehicleController>(); if (car != null) { Fuel01 = Mathf.Clamp01(session.Data.fuel01); haveLast = false; } return; }

            var owned = session.SelectedVehicle;
            RangeMultiplier = new[] { 1f, 1.15f, 1.3f, 1.5f }[Mathf.Clamp(owned != null ? owned.GetUpgradeLevel(UpgradeType.FuelTank) : 0, 0, 3)];

            Vector3 p = car.transform.position;
            if (haveLast)
            {
                Vector3 d = p - last; d.y = 0f;
                float m = d.magnitude;
                if (m < 40f && Time.timeScale > 0f) Fuel01 = Mathf.Max(0f, Fuel01 - m / (100f * MetresPerPercent * RangeMultiplier));     // jumps (teleports) are ignored
            }
            last = p; haveLast = true;

            if (Empty && !cut) { cut = true; VehicleTuning.FuelFactor = 0f; VehicleTuning.ApplyToCurrentCar(); GameUI.Instance?.ShowToast("Out of fuel! Use TOW or push to a petrol station."); }
            else if (!Empty && cut) { cut = false; VehicleTuning.FuelFactor = 1f; VehicleTuning.ApplyToCurrentCar(); }

            if (Time.unscaledTime > nextSave) { nextSave = Time.unscaledTime + 5f; session.Data.fuel01 = Fuel01; }
        }

        /// <summary>True when the car is stopped next to a petrol station, repair shop or the garage.</summary>
        public bool CanRefuel
        {
            get
            {
                if (car == null || car.SpeedKmh > 6f) return false;
                Vector3 p = car.transform.position;
                var nav = NavigationService.Instance;
                if (nav != null && nav.Data != null)
                    foreach (var poi in nav.Data.pois)
                        if ((poi.type == PoiType.PetrolStation || poi.type == PoiType.Garage || poi.type == PoiType.RepairWorkshop) && Vector2.Distance(new Vector2(poi.position.x, poi.position.z), new Vector2(p.x, p.z)) < 30f) return true;
                foreach (var mk in LocationMarker.All)
                    if (mk.Kind == LocationKind.GarageEntrance && Vector2.Distance(new Vector2(mk.transform.position.x, mk.transform.position.z), new Vector2(p.x, p.z)) < 25f) return true;
                return false;
            }
        }

        public long RefuelCost => (long)Mathf.Ceil((1f - Fuel01) * 100f * RefuelCostPerPercent);

        /// <summary>Fills the tank as far as the wallet allows (never into debt). Returns the amount paid.</summary>
        public long Refuel()
        {
            var session = GameSession.Instance;
            float missing = (1f - Fuel01) * 100f;
            if (missing < 0.5f) return 0;
            long afford = (long)Mathf.Floor(Mathf.Max(0f, session.Wallet.Balance) / RefuelCostPerPercent);
            float percent = Mathf.Min(missing, afford);
            if (percent < 1f) return 0;
            long cost = (long)Mathf.Ceil(percent * RefuelCostPerPercent);
            if (!session.Wallet.Apply("fuel:" + session.Data.transactions.Count, -cost, "Fuel")) return 0;
            Fuel01 = Mathf.Min(1f, Fuel01 + percent / 100f); session.Data.fuel01 = Fuel01; session.Persist();
            return cost;
        }

        /// <summary>Emergency tow: costs Rs 400 (the wallet may go negative) and restores 25% fuel.</summary>
        public void Tow()
        {
            var session = GameSession.Instance;
            session.Wallet.Apply("tow:" + session.Data.transactions.Count, -(long)TowCost, "Fuel tow");
            Fuel01 = Mathf.Max(Fuel01, 0.25f); session.Data.fuel01 = Fuel01; session.Persist();
        }

        public void SetFuel(float value01) { Fuel01 = Mathf.Clamp01(value01); GameSession.Instance.Data.fuel01 = Fuel01; }
    }
}
