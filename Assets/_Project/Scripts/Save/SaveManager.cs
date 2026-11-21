using System;
using UnityEngine;

namespace CityDriver.Save
{
    /// <summary>
    /// Loads and saves <see cref="SaveData"/> as JSON through an <see cref="ISaveStorage"/>. A corrupt main file falls back
    /// to the .bak copy, then to a fresh game; the user is never left with an unreadable save and an exception.
    /// </summary>
    public sealed class SaveManager
    {
        public const string SaveKey = "cityDriverSave.json";

        private readonly ISaveStorage storage;

        public SaveManager(ISaveStorage storage) { this.storage = storage ?? throw new ArgumentNullException(nameof(storage)); }

        public bool HasSave => storage.TryRead(SaveKey, out _);

        public SaveData Load()
        {
            if (TryParse(SaveKey, out SaveData data)) return Migrate(data);
            if (TryParse(SaveKey + ".bak", out data))
            {
                Debug.LogWarning("[SaveManager] Main save unreadable; restored from backup.");
                return Migrate(data);
            }

            return SaveData.CreateNew();
        }

        public void Save(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            storage.Write(SaveKey, JsonUtility.ToJson(data, false));
        }

        public void DeleteSave() => storage.Delete(SaveKey);

        private bool TryParse(string key, out SaveData data)
        {
            data = null;
            if (!storage.TryRead(key, out string text) || string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                data = JsonUtility.FromJson<SaveData>(text);
                return data != null && data.version >= 1;
            }
            catch (ArgumentException) { return false; }
        }

        /// <summary>Upgrades older save versions step by step. Add a case per future version.</summary>
        private static SaveData Migrate(SaveData data)
        {
            if (data.version > SaveData.CurrentVersion)
                Debug.LogWarning($"[SaveManager] Save version {data.version} is newer than this build ({SaveData.CurrentVersion}); loading best-effort.");

            // if (data.version == 1) { ...transform to v2...; data.version = 2; }
            if (data.vehicles == null || data.vehicles.Count == 0) data.vehicles = SaveData.CreateNew().vehicles;
            if (data.completedMissions == null) data.completedMissions = new System.Collections.Generic.List<string>();
            if (data.transactions == null) data.transactions = new System.Collections.Generic.List<TransactionRecord>();
            if (data.appliedTransactionIds == null) data.appliedTransactionIds = new System.Collections.Generic.List<string>();
            return data;
        }
    }
}
