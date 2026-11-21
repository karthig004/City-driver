using CityDriver.Career;
using CityDriver.Economy;
using CityDriver.Save;

namespace CityDriver.Game
{
    /// <summary>
    /// The player's persistent state in one place: save data, wallet, XP and career. Loaded from disk on first use.
    /// Tests can swap the storage so they never touch the real save file.
    /// </summary>
    public sealed class GameSession
    {
        private static GameSession instance;

        public static GameSession Instance => instance ?? (instance = new GameSession(new FileSaveStorage()));

        /// <summary>Replaces the session with one backed by <paramref name="storage"/> (tests, or a "new game").</summary>
        public static GameSession UseStorage(ISaveStorage storage) => instance = new GameSession(storage);

        public readonly ISaveStorage Storage;
        public readonly SaveManager Saves;
        public SaveData Data { get; private set; }
        public WalletService Wallet { get; private set; }
        public XPService Xp { get; private set; }
        public CareerService Career { get; private set; }
        public GarageService Garage { get; private set; }

        private GameSession(ISaveStorage storage)
        {
            Storage = storage;
            Saves = new SaveManager(storage);
            Rebuild(Saves.Load());
        }

        private void Rebuild(SaveData data)
        {
            Data = data;
            Wallet = new WalletService(data);
            Xp = new XPService(data);
            Career = new CareerService(data, Wallet, Xp);
            Garage = new GarageService(data, Wallet);
        }

        public bool HasSave => Saves.HasSave;
        public OwnedVehicleData SelectedVehicle => Data.FindVehicle(Data.selectedVehicleId);

        public void Persist() => Saves.Save(Data);

        /// <summary>Buys a vehicle class (level-gated). The new vehicle starts with its own paint and no upgrades.</summary>
        public bool TryBuyVehicle(string vehicleId, out string reason)
        {
            VehicleSpec spec = VehicleCatalog.Find(vehicleId);
            if (Data.FindVehicle(spec.id) != null) { reason = "Already owned."; return true; }
            if (Career.Level < spec.unlockLevel) { reason = $"Reach Level {spec.unlockLevel} first."; return false; }
            if (!Wallet.CanAfford(spec.price)) { reason = $"Need {Money(spec.price)}."; return false; }
            if (!Wallet.TrySpend("vehicle:" + spec.id, spec.price, "Vehicle: " + spec.name)) { reason = "Purchase already processed."; return false; }
            Data.vehicles.Add(new OwnedVehicleData { vehicleId = spec.id, colorHex = spec.defaultPaint });
            Persist(); reason = null; return true;
        }

        /// <summary>Weapon shop (unlocked by finishing Level 5). Weapons are optional and only act during a chase.</summary>
        public bool TryBuyWeapon(string weaponId, out string reason)
        {
            if (!Data.weaponShopUnlocked) { reason = "The weapon shop opens after Level 5."; return false; }
            if (Data.ownedWeapons.Contains(weaponId)) { reason = null; return true; }
            WeaponInfo info = null; foreach (var w in PursuitManager.Weapons) if (w.id == weaponId) info = w;
            if (info == null) { reason = "Unknown weapon."; return false; }
            if (!Wallet.CanAfford(info.price)) { reason = $"Need {Money(info.price)}."; return false; }
            if (!Wallet.TrySpend("weapon:" + weaponId, info.price, "Weapon: " + info.name)) { reason = "Purchase already processed."; return false; }
            Data.ownedWeapons.Add(weaponId); if (string.IsNullOrEmpty(Data.equippedWeapon)) Data.equippedWeapon = weaponId;
            Persist(); reason = null; return true;
        }

        /// <summary>Reloads everything from storage (what "Continue" does after an app restart).</summary>
        public void Reload() => Rebuild(Saves.Load());

        public void NewGame() { Saves.DeleteSave(); Rebuild(SaveData.CreateNew()); Persist(); }

        public static string Money(long rupees)
        {
            string sign = rupees < 0 ? "-" : "";
            return sign + "Rs " + System.Math.Abs(rupees).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
