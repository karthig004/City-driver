using System;
using System.Collections.Generic;

namespace CityDriver.Save
{
    /// <summary>Upgrade slots a vehicle can have (Garage spec).</summary>
    public enum UpgradeType { Engine, Brakes, Tires, Suspension, Armor, FuelTank, Headlights }

    [Serializable]
    public sealed class TransactionRecord
    {
        public string id;
        public long amount;          // rupees, negative = money out
        public string reason;
        public long balanceAfter;
        public long utcTicks;
    }

    [Serializable]
    public sealed class OwnedVehicleData
    {
        public string vehicleId;
        public float condition01 = 1f;
        public string colorHex = "#F5BD0D";
        public bool metallic = true;
        // JsonUtility cannot serialise dictionaries, so upgrades are parallel lists.
        public List<int> upgradeTypes = new List<int>();
        public List<int> upgradeLevels = new List<int>();

        public int GetUpgradeLevel(UpgradeType type)
        {
            int i = upgradeTypes.IndexOf((int)type);
            return i < 0 ? 0 : upgradeLevels[i];
        }

        public void SetUpgradeLevel(UpgradeType type, int level)
        {
            int i = upgradeTypes.IndexOf((int)type);
            if (i < 0) { upgradeTypes.Add((int)type); upgradeLevels.Add(level); }
            else upgradeLevels[i] = level;
        }
    }

    /// <summary>
    /// Everything that is persisted. Versioned so older saves can be migrated; plain serialisable fields so it works with
    /// JsonUtility on every platform (Android, Web). Wallet and XP are deliberately separate fields.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;
        public const long StartingWallet = 5000;

        public int version = CurrentVersion;

        // Economy (kept apart on purpose)
        public long walletBalance = StartingWallet;
        public long xp;
        public int level = 1;

        // Progress / unlocks
        public bool tutorialSeen;                                     // the first-run How To Play has been shown or skipped
        public string driverLook = "";                                // JSON of the customised driver (CharacterLook); empty = default
        public bool hasShadowPass;
        public bool weaponShopUnlocked;
        public bool isCaptured;
        public float fuel01 = 1f;                                     // tank level 0..1
        public bool shadowCinematicSeen;                              // the Shadow Pass cutscene has been shown
        public List<string> ownedWeapons = new List<string>();        // weapon shop (unlocks after level 5)
        public string equippedWeapon = "";
        public bool hasActiveShadowMission;
        public string activeShadowMissionId = "";
        public List<string> completedMissions = new List<string>();

        // Transaction ledger (capped) + ids that were already applied (idempotency)
        public List<TransactionRecord> transactions = new List<TransactionRecord>();
        public List<string> appliedTransactionIds = new List<string>();

        // Garage
        public string selectedVehicleId = "sedan_compact";
        public List<string> ownedColors = new List<string> { "#F5BD0D" };   // paint colours bought in the garage
        public List<OwnedVehicleData> vehicles = new List<OwnedVehicleData>();

        public static SaveData CreateNew()
        {
            var d = new SaveData();
            d.vehicles.Add(new OwnedVehicleData { vehicleId = d.selectedVehicleId });
            return d;
        }

        public OwnedVehicleData FindVehicle(string vehicleId)
        {
            for (int i = 0; i < vehicles.Count; i++) if (vehicles[i].vehicleId == vehicleId) return vehicles[i];
            return null;
        }
    }
}
