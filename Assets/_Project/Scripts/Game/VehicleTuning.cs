using System.Collections.Generic;
using CityDriver.Career;
using CityDriver.Save;
using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>One purchasable vehicle: a class with its own handling multipliers, price, level gate and body proportions.</summary>
    public sealed class VehicleSpec
    {
        public string id, name, className, defaultPaint;
        public int tier = 1;
        public long price;
        public int unlockLevel;
        public float mass = 1f, torque = 1f, topSpeed = 1f, brake = 1f, grip = 1f, steer = 1f;
        public Vector3 bodyScale = Vector3.one;
        // driving character: every car drives differently (see the showroom text and VehicleStats "Handling character")
        public DriveLayout drive = DriveLayout.Rear;
        public float maxDrift = 40f, rearGrip = 1f, driftDrop = 0.6f, counter = 0.5f, steerResp = 1f, steerCurve = 1f, oversteer = 0f, sustain = 0.3f, yaw = 1f, downforce = 1f;
        public string feel = "";
    }

    public static class VehicleCatalog
    {
        public static readonly IReadOnlyList<VehicleSpec> All = new List<VehicleSpec>
        {
            // Tier 1 (Street)
            new VehicleSpec { id = "sedan_compact", name = "City Taxi", className = "Compact sedan", tier = 1, price = 0, unlockLevel = 1, drive = DriveLayout.Rear, rearGrip = 1.15f, driftDrop = 0.7f, counter = 0.8f, steerResp = 0.95f, steerCurve = 1.15f, oversteer = 0.1f, sustain = 0.2f, yaw = 1.3f, downforce = 1f, feel = "Easy and stable. Forgiving understeer, the tail stays planted.", maxDrift = 30f, defaultPaint = "#F5BD0D" },
            new VehicleSpec { id = "hatch_city", name = "Metro Hatch", className = "Hatchback", tier = 1, price = 7000, unlockLevel = 2, mass = 0.88f, torque = 1.0f, topSpeed = 1.06f, brake = 1.0f, grip = 1.0f, steer = 1.1f, drive = DriveLayout.Front, rearGrip = 0.95f, driftDrop = 0.6f, counter = 0.7f, steerResp = 1.15f, steerCurve = 1.2f, oversteer = 0.15f, sustain = 0.25f, yaw = 1.1f, downforce = 1f, feel = "Light and quick to turn. Lift off the throttle and the tail swings.", maxDrift = 32f, defaultPaint = "#2E8B57" },
            new VehicleSpec { id = "sedan_premium", name = "Metro Premium", className = "Premium sedan", tier = 1, price = 15000, unlockLevel = 3, mass = 1.1f, torque = 1.18f, topSpeed = 1.12f, brake = 1.1f, grip = 1.05f, drive = DriveLayout.All, rearGrip = 1.1f, driftDrop = 0.65f, counter = 0.75f, steerResp = 1.0f, steerCurve = 1.1f, oversteer = 0.1f, sustain = 0.2f, yaw = 1.3f, downforce = 1.2f, feel = "Smooth all-wheel drive. Calm, grippy and hard to unsettle.", maxDrift = 28f, defaultPaint = "#15161A" },
            // Tier 2 (Pro)
            new VehicleSpec { id = "sedan_sports", name = "Apex GT", className = "Sports sedan", tier = 2, price = 30000, unlockLevel = 5, mass = 1.0f, torque = 1.4f, topSpeed = 1.3f, brake = 1.2f, grip = 1.15f, steer = 1.05f, drive = DriveLayout.Rear, rearGrip = 0.88f, driftDrop = 0.5f, counter = 0.65f, steerResp = 1.25f, steerCurve = 1.3f, oversteer = 0.6f, sustain = 0.45f, yaw = 0.8f, downforce = 1.5f, feel = "Rear-drive sports car. Sharp steering and a tail that steps out under power: made for drifting.", maxDrift = 45f, defaultPaint = "#B3141A" },
            new VehicleSpec { id = "muscle_coupe", name = "Bulldog", className = "Muscle coupe", tier = 2, price = 38000, unlockLevel = 6, mass = 1.2f, torque = 1.6f, topSpeed = 1.32f, brake = 1.1f, grip = 1.0f, steer = 0.95f, drive = DriveLayout.Rear, rearGrip = 0.78f, driftDrop = 0.42f, counter = 0.5f, steerResp = 0.9f, steerCurve = 1.2f, oversteer = 1.0f, sustain = 0.6f, yaw = 0.6f, downforce = 0.8f, feel = "Muscle car. Huge torque breaks the rear loose on demand: smoky, wild drifts.", maxDrift = 52f, defaultPaint = "#E07A1F" },
            new VehicleSpec { id = "suv_performance", name = "Ridge Runner", className = "Performance SUV", tier = 2, price = 45000, unlockLevel = 7, mass = 1.5f, torque = 1.5f, topSpeed = 1.2f, brake = 1.15f, grip = 1.05f, steer = 0.92f, drive = DriveLayout.All, rearGrip = 1.0f, driftDrop = 0.6f, counter = 0.7f, steerResp = 0.95f, steerCurve = 1.15f, oversteer = 0.3f, sustain = 0.35f, yaw = 1.1f, downforce = 1.1f, feel = "Planted all-wheel-drive SUV. Fast and stable; drifts only when you push it.", maxDrift = 30f, defaultPaint = "#4C4F57" },
            // Tier 3 (Elite)
            new VehicleSpec { id = "suv_luxury", name = "Sovereign LX", className = "Luxury SUV", tier = 3, price = 70000, unlockLevel = 8, mass = 1.65f, torque = 1.45f, topSpeed = 1.18f, brake = 1.2f, grip = 1.08f, steer = 0.9f, drive = DriveLayout.All, rearGrip = 1.1f, driftDrop = 0.7f, counter = 0.8f, steerResp = 0.85f, steerCurve = 1.05f, oversteer = 0.15f, sustain = 0.25f, yaw = 1.3f, downforce = 1.2f, feel = "Heavy luxury cruiser. Floaty and smooth: very stable, slow to rotate.", maxDrift = 26f, defaultPaint = "#E9E9E6" },
            new VehicleSpec { id = "supercar", name = "Vantage S", className = "Supercar", tier = 3, price = 140000, unlockLevel = 9, mass = 1.05f, torque = 1.9f, topSpeed = 1.6f, brake = 1.5f, grip = 1.3f, steer = 1.1f, drive = DriveLayout.Rear, rearGrip = 0.9f, driftDrop = 0.5f, counter = 0.8f, steerResp = 1.4f, steerCurve = 1.35f, oversteer = 0.55f, sustain = 0.5f, yaw = 0.9f, downforce = 2.2f, feel = "Supercar. Razor steering, huge grip and downforce; controllable high-speed drifts.", maxDrift = 40f, defaultPaint = "#1F4FA3" },
        };

        public static VehicleSpec Find(string id) { foreach (var v in All) if (v.id == id) return v; return All[0]; }
    }

    /// <summary>
    /// Turns the selected vehicle class + garage upgrades + current weather into real physics: it clones the base
    /// <see cref="VehicleStats"/>, scales engine / brakes / grip / suspension, sets the armour damage multiplier, headlight range
    /// and the body proportions, and pushes the result into the <see cref="VehicleController"/>.
    /// </summary>
    public static class VehicleTuning
    {
        private static VehicleStats baseStats;
        private static VehicleController baseOwner;

        // Upgrade stages 0 (stock = the minimum) .. 3 (the hard maximum) for each part.
        public const float MaxTopSpeedKmh = 330f;
        private static readonly float[] EngTorque = { 1f, 1.08f, 1.16f, 1.25f };
        private static readonly float[] EngTop = { 1f, 1.04f, 1.08f, 1.12f };
        private static readonly float[] BrkMul = { 1f, 1.12f, 1.24f, 1.40f };
        private static readonly float[] TyreMul = { 1f, 1.05f, 1.10f, 1.18f };
        private static readonly float[] SusMul = { 1f, 1.08f, 1.16f, 1.25f };
        private static readonly float[] SusRoll = { 1f, 1.10f, 1.20f, 1.30f };
        private static readonly float[] ArmDamage = { 1f, 0.85f, 0.70f, 0.55f };
        private static readonly float[] BeamRange = { 24f, 28.8f, 33.6f, 38.4f };

        /// <summary>One-line effect of reaching <paramref name="stage"/> (1..3) of a part, for the garage.</summary>
        public static string UpgradeEffect(UpgradeType type, int stage)
        {
            stage = Mathf.Clamp(stage, 1, 3);
            switch (type)
            {
                case UpgradeType.Engine: return $"+{Mathf.RoundToInt((EngTorque[stage] - 1f) * 100f)}% power, +{Mathf.RoundToInt((EngTop[stage] - 1f) * 100f)}% top speed";
                case UpgradeType.Brakes: return $"+{Mathf.RoundToInt((BrkMul[stage] - 1f) * 100f)}% stopping power";
                case UpgradeType.Tires: return $"+{Mathf.RoundToInt((TyreMul[stage] - 1f) * 100f)}% grip";
                case UpgradeType.Suspension: return $"+{Mathf.RoundToInt((SusMul[stage] - 1f) * 100f)}% stiffness, lower stance";
                case UpgradeType.Armor: return $"-{Mathf.RoundToInt((1f - ArmDamage[stage]) * 100f)}% damage taken";
                case UpgradeType.FuelTank: return $"+{new[] { 0, 15, 30, 50 }[stage]}% range";
                default: return $"+{Mathf.RoundToInt((BeamRange[stage] / 24f - 1f) * 100f)}% beam range";
            }
        }

        /// <summary>Body-kit stage shown on the car (0..3), from the total of the engine, brake, tyre and suspension stages (0..12).</summary>
        public static int VisualStage(OwnedVehicleData v)
        {
            if (v == null) return 0;
            int total = Mathf.Clamp(v.GetUpgradeLevel(UpgradeType.Engine), 0, 3) + Mathf.Clamp(v.GetUpgradeLevel(UpgradeType.Brakes), 0, 3) + Mathf.Clamp(v.GetUpgradeLevel(UpgradeType.Tires), 0, 3) + Mathf.Clamp(v.GetUpgradeLevel(UpgradeType.Suspension), 0, 3);
            return total >= 10 ? 3 : total >= 6 ? 2 : total >= 2 ? 1 : 0;
        }

        /// <summary>Shows body kit parts Kit1..Kit3 up to <paramref name="stage"/> and lowers the body a little per suspension stage.</summary>
        public static void ApplyKit(Transform body, int stage, int suspensionLevel)
        {
            if (body == null) return;
            for (int k = 1; k <= 3; k++) { var t = body.Find("Kit" + k); if (t != null) t.gameObject.SetActive(stage >= k); }
            body.localPosition = new Vector3(0f, -0.012f * Mathf.Clamp(suspensionLevel, 0, 3), 0f);
        }

        /// <summary>Speed, acceleration, braking and handling of a class as 0..1 bars (relative to the best car in the catalogue).</summary>
        public static float[] Ratings(VehicleSpec s)
        {
            float maxAcc = 0f; foreach (var v in VehicleCatalog.All) maxAcc = Mathf.Max(maxAcc, v.torque / v.mass);
            return new[] { Mathf.Clamp01(s.topSpeed / 1.6f), Mathf.Clamp01((s.torque / s.mass) / maxAcc), Mathf.Clamp01(s.brake / 1.5f), Mathf.Clamp01(s.grip * s.steer / 1.43f) };
        }

        public static float FuelFactor { get; set; } = 1f;         // 0 when the tank is empty
        public static float WeatherGrip { get; set; } = 1f;       // < 1 in rain (set by WorldEnvironment)

        /// <summary>Result of the last <see cref="Apply"/> (for tests and the garage): the multipliers that were used.</summary>
        public static string LastSummary { get; private set; } = "";

        public static void Apply(VehicleController car, OwnedVehicleData owned)
        {
            if (car == null || owned == null) return;
            if (baseStats == null || baseOwner != car)
            {
                // The prefab's stats asset is never modified: keep a pristine copy as the baseline.
                baseStats = Object.Instantiate(car.Stats == null ? ScriptableObject.CreateInstance<VehicleStats>() : car.Stats);
                if (car.Stats != null && car.Stats.name.EndsWith("(Clone)")) baseStats = Object.Instantiate(car.Stats);
                baseOwner = car;
            }

            VehicleSpec spec = VehicleCatalog.Find(owned.vehicleId);
            VehicleStats s = Object.Instantiate(baseStats);
            int eng = owned.GetUpgradeLevel(UpgradeType.Engine), brk = owned.GetUpgradeLevel(UpgradeType.Brakes), tyr = owned.GetUpgradeLevel(UpgradeType.Tires),
                sus = owned.GetUpgradeLevel(UpgradeType.Suspension), arm = owned.GetUpgradeLevel(UpgradeType.Armor), hl = owned.GetUpgradeLevel(UpgradeType.Headlights);
            eng = Mathf.Clamp(eng, 0, 3); brk = Mathf.Clamp(brk, 0, 3); tyr = Mathf.Clamp(tyr, 0, 3); sus = Mathf.Clamp(sus, 0, 3); arm = Mathf.Clamp(arm, 0, 3); hl = Mathf.Clamp(hl, 0, 3);

            s.mass = baseStats.mass * spec.mass;
            s.maxMotorTorque = baseStats.maxMotorTorque * spec.torque * EngTorque[eng] * FuelFactor;
            s.maxSpeedKmh = Mathf.Min(MaxTopSpeedKmh, baseStats.maxSpeedKmh * spec.topSpeed * EngTop[eng]);
            s.maxBrakeTorque = baseStats.maxBrakeTorque * spec.brake * BrkMul[brk] * Mathf.Lerp(1f, spec.mass, 0.6f);
            float tyre = spec.grip * TyreMul[tyr] * Mathf.Clamp(WeatherGrip, 0.4f, 1f);
            s.forwardStiffness = baseStats.forwardStiffness * tyre;
            s.sidewaysStiffness = baseStats.sidewaysStiffness * tyre;
            s.spring = baseStats.spring * SusMul[sus] * spec.mass;
            s.damper = baseStats.damper * SusMul[sus] * spec.mass;
            s.antiRollFront = baseStats.antiRollFront * SusRoll[sus] * spec.mass;
            s.antiRollRear = baseStats.antiRollRear * SusRoll[sus] * spec.mass;
            s.maxSteerAngleLow = Mathf.Clamp(baseStats.maxSteerAngleLow * spec.steer, 5f, 45f);
            s.drivenWheels = spec.drive;
            s.rearGripRatio = spec.rearGrip; s.driftGripDrop = spec.driftDrop; s.counterSteer = spec.counter; s.steerResponse = spec.steerResp; s.steerCurve = spec.steerCurve;
            s.powerOversteer = spec.oversteer; s.driftSustain = spec.sustain; s.maxDriftAngle = spec.maxDrift;
            s.yawAssist = Mathf.Clamp01(baseStats.yawAssist * spec.yaw); s.downforce = baseStats.downforce * spec.downforce;
            for (int i = 0; i < s.upshiftSpeedsKmh.Length && i < baseStats.upshiftSpeedsKmh.Length; i++) s.upshiftSpeedsKmh[i] = baseStats.upshiftSpeedsKmh[i] * spec.topSpeed * EngTop[eng];

            car.SetStats(s);

            var damage = car.GetComponent<VehicleDamage>();
            if (damage != null) damage.DamageMultiplier = ArmDamage[arm];

            var lights = car.GetComponent<VehicleLights>();
            if (lights != null) lights.SetBeamRange(BeamRange[hl]);

            SwapBody(car, spec);
            ApplyKit(car.transform.Find("Body"), VisualStage(owned), sus);

            LastSummary = $"{spec.name} ({spec.className}): mass {s.mass:F0} kg, torque {s.maxMotorTorque:F0}, top {s.maxSpeedKmh:F0} km/h, brake {s.maxBrakeTorque:F0}, tyre grip x{tyre:F2}, armour x{(damage != null ? damage.DamageMultiplier : 1f):F2}, beam {BeamRange[hl]:F0} m";
        }

        /// <summary>Replaces the car's visual body with the model of the selected vehicle (Resources/CarBodies/Body_{id}); physics are untouched.</summary>
        private static void SwapBody(VehicleController car, VehicleSpec spec)
        {
            Transform body = car.transform.Find("Body");
            if (body == null) return;
            if (currentBodyId.TryGetValue(car, out string cur) && cur == spec.id) return;
            var prefab = Resources.Load<GameObject>("CarBodies/Body_" + spec.id);
            if (prefab == null) { body.localScale = spec.bodyScale; return; }                 // fallback: no model for this id, keep the old look
            int layer = body.gameObject.layer;
            var fresh = Object.Instantiate(prefab, car.transform);
            fresh.name = "Body"; fresh.transform.localPosition = Vector3.zero; fresh.transform.localRotation = Quaternion.identity; fresh.transform.localScale = Vector3.one;
            foreach (var t in fresh.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Object.DestroyImmediate(body.gameObject);
            currentBodyId[car] = spec.id;
            var lights = car.GetComponent<VehicleLights>(); if (lights != null) lights.Rescan();
        }

        private static readonly Dictionary<VehicleController, string> currentBodyId = new Dictionary<VehicleController, string>();

        public static void ApplyToCurrentCar()
        {
            var car = Object.FindFirstObjectByType<VehicleController>();
            if (car != null) Apply(car, GameSession.Instance.SelectedVehicle);
        }
    }
}
