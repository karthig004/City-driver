using System;
using CityDriver.Economy;
using CityDriver.Save;

namespace CityDriver.Career
{
    /// <summary>Upgrade purchases and repairs. Each vehicle keeps its own independent upgrade levels.</summary>
    public sealed class GarageService
    {
        public const int MaxUpgradeLevel = 3;

        private readonly SaveData data;
        private readonly WalletService wallet;

        public GarageService(SaveData data, WalletService wallet)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        /// <summary>First-pass price curve: grows with the next level.</summary>
        private static readonly double[] StagePercent = { 0.08, 0.18, 0.35 };

        /// <summary>Price of stage <paramref name="nextLevel"/> (1..3) of a part: a share of the car's price (taxi counts as Rs 10,000), weighted per part.</summary>
        public static long UpgradeCost(UpgradeType type, int nextLevel, long carPrice)
        {
            double weight = type == UpgradeType.Engine ? 1.0 : type == UpgradeType.Armor ? 0.8 : type == UpgradeType.Suspension ? 0.7 : type == UpgradeType.FuelTank ? 0.5 : type == UpgradeType.Headlights ? 0.3 : 0.6;
            long basis = Math.Max(carPrice, 10000);
            double raw = basis * StagePercent[Math.Max(0, Math.Min(2, nextLevel - 1))] * weight;
            return (long)(Math.Round(raw / 50.0) * 50.0);
        }

        public static long UpgradeCost(UpgradeType type, int nextLevel) => UpgradeCost(type, nextLevel, 10000);

        /// <summary>Player level needed for a stage: stage 1 = the car's unlock level, +2 per further stage, never above level 10.</summary>
        public static int GateLevel(int carUnlockLevel, int stage) => Math.Min(10, carUnlockLevel + 2 * (Math.Max(1, stage) - 1));

        public static long RepairCost(float condition01) => (long)Math.Ceiling((1f - Math.Max(0f, Math.Min(1f, condition01))) * 1000f);

        public bool TryBuyUpgrade(string vehicleId, UpgradeType type, out string reason)
        {
            OwnedVehicleData v = data.FindVehicle(vehicleId);
            if (v == null) { reason = "Vehicle not owned."; return false; }

            int next = v.GetUpgradeLevel(type) + 1;
            if (next > MaxUpgradeLevel) { reason = "Already at maximum level."; return false; }

            long cost = UpgradeCost(type, next, CityDriver.Game.VehicleCatalog.Find(vehicleId).price);
            if (!wallet.CanAfford(cost)) { reason = $"Need ₹{cost}."; return false; }

            // The id includes the target level, so a double tap on "buy" cannot charge twice.
            if (!wallet.TrySpend($"upgrade:{vehicleId}:{type}:{next}", cost, $"{type} level {next}")) { reason = "Purchase already processed."; return false; }
            v.SetUpgradeLevel(type, next);
            reason = null;
            return true;
        }

        public bool TryRepair(string vehicleId, out string reason)
        {
            OwnedVehicleData v = data.FindVehicle(vehicleId);
            if (v == null) { reason = "Vehicle not owned."; return false; }

            long cost = RepairCost(v.condition01);
            if (cost == 0) { reason = "Nothing to repair."; return false; }
            if (!wallet.CanAfford(cost)) { reason = $"Need ₹{cost}."; return false; }

            wallet.Apply($"repair:{vehicleId}:{data.transactions.Count}", -cost, "Repair");
            v.condition01 = 1f;
            reason = null;
            return true;
        }
    }
}
