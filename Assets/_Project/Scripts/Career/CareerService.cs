using System;
using CityDriver.Economy;
using CityDriver.Save;

namespace CityDriver.Career
{
    /// <summary>The exact Shadow Run call rule from the design brief, kept in one place.</summary>
    public static class ShadowRunRules
    {
        public static bool CanReceiveShadowRunCall(int playerLevel, bool hasShadowPass, long walletBalance, bool hasActiveShadowMission, bool isCaptured)
        {
            return playerLevel >= CareerCatalog.ShadowPassLevel
                && hasShadowPass
                && walletBalance > 0
                && !hasActiveShadowMission
                && !isCaptured;
        }
    }

    /// <summary>
    /// Career rules: which missions can be played, paying rewards once, applying Shadow Run failure penalties exactly once,
    /// and advancing the level only when all three assignments are done AND the mandatory vehicle upgrades are installed.
    /// </summary>
    public sealed class CareerService
    {
        private const double ReplayRewardFraction = 0.25;

        private readonly SaveData data;
        private readonly WalletService wallet;
        private readonly XPService xp;

        public CareerService(SaveData data, WalletService wallet, XPService xp)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.xp = xp ?? throw new ArgumentNullException(nameof(xp));
        }

        public event Action<int> LevelAdvanced;
        public event Action<int, int> LevelLost;

        /// <summary>
        /// The price of a failed Shadow Run: the career drops one level. The XP bar goes back to the start of the lower level and its three
        /// assignments must be played again to climb back. Level 1 cannot drop. Returns false when nothing changed.
        /// </summary>
        public bool DemoteAfterShadowFailure(out int from, out int to, out long xpLost)
        {
            from = data.level; to = data.level; xpLost = 0;
            if (data.level <= 1) return false;
            data.level--; to = data.level;
            foreach (var m in CareerCatalog.ForLevel(to)) data.completedMissions.Remove(m.Id);
            long start = XPService.BandStartXp(to);
            if (xp.Xp > start) { xpLost = xp.Xp - start; xp.SetXp(start); }
            if (to < CareerCatalog.ShadowPassLevel) data.hasShadowPass = false;
            LevelLost?.Invoke(from, to);
            return true;
        }

        public int Level => data.level;

        public bool IsCompleted(string missionId) => data.completedMissions.Contains(missionId);

        public bool CanReceiveShadowRunCall() =>
            ShadowRunRules.CanReceiveShadowRunCall(data.level, data.hasShadowPass, wallet.Balance, data.hasActiveShadowMission, data.isCaptured);

        /// <summary>Normal missions: current level or earlier (replay). Shadow Run missions additionally need the call rule to pass.</summary>
        public bool IsAvailable(MissionDefinition mission)
        {
            if (mission == null || mission.Level > data.level) return false;
            if (mission.ShadowRun) return CanReceiveShadowRunCall() || (data.hasActiveShadowMission && data.activeShadowMissionId == mission.Id);
            return true;
        }

        public void StartMission(MissionDefinition mission)
        {
            if (!IsAvailable(mission)) throw new InvalidOperationException($"Mission '{mission?.Id}' is not available.");
            if (mission.ShadowRun) { data.hasActiveShadowMission = true; data.activeShadowMissionId = mission.Id; }
        }

        /// <summary>Pays the reward + XP. First completion pays in full; replays pay a fraction of the cash and no XP. Returns the cash paid.</summary>
        public long CompleteMission(MissionDefinition mission, long bonus = 0)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            bool first = !IsCompleted(mission.Id);
            long cash = first ? mission.Reward : (long)(mission.Reward * ReplayRewardFraction);
            long total = cash + Math.Max(0, bonus);

            string txId = first ? $"mission:{mission.Id}:complete" : $"mission:{mission.Id}:replay:{data.completedMissions.Count}:{wallet.Balance}";
            if (first && wallet.HasApplied(txId)) return 0;     // already paid (e.g. save reloaded mid-result-screen)

            wallet.Apply(txId, total, $"Mission: {mission.Title}");
            if (first) { xp.Add(mission.Xp); data.completedMissions.Add(mission.Id); }
            if (mission.ShadowRun) ClearActiveShadow(mission);
            return total;
        }

        /// <summary>Fails a mission. Shadow Run penalties are applied once per attempt id, even if called repeatedly.</summary>
        public bool FailMission(MissionDefinition mission, string attemptId)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            bool applied = false;
            if (mission.ShadowRun && mission.FailPenalty > 0)
                applied = wallet.Apply($"mission:{mission.Id}:{attemptId}:penalty", -mission.FailPenalty, $"Failed: {mission.Title}");
            if (mission.ShadowRun) ClearActiveShadow(mission);
            return applied;
        }

        /// <summary>Marks the player as captured by police (blocks Shadow Run calls until released).</summary>
        public void SetCaptured(bool captured) => data.isCaptured = captured;

        /// <summary>True when all three assignments of the current level are done and the vehicle has the mandatory upgrades.</summary>
        public bool CanAdvanceLevel(OwnedVehicleData vehicle, out string reason)
        {
            if (data.level >= CareerCatalog.LevelCount) { reason = "Maximum level reached."; return false; }

            foreach (var m in CareerCatalog.ForLevel(data.level))
                if (!IsCompleted(m.Id)) { reason = $"Complete '{m.Title}' first."; return false; }

            foreach (var req in CareerCatalog.RequirementsFor(data.level))
            {
                int have = vehicle == null ? 0 : vehicle.GetUpgradeLevel(req.Type);
                if (have < req.MinLevel) { reason = $"Upgrade {req.Type} to level {req.MinLevel} (currently {have})."; return false; }
            }

            reason = null;
            return true;
        }

        public bool TryAdvanceLevel(OwnedVehicleData vehicle, out string reason)
        {
            if (!CanAdvanceLevel(vehicle, out reason)) return false;

            int completed = data.level;
            data.level++;
            if (completed == CareerCatalog.ShadowPassLevel) data.hasShadowPass = true;
            if (completed == CareerCatalog.WeaponShopLevel) data.weaponShopUnlocked = true;
            LevelAdvanced?.Invoke(data.level);
            return true;
        }

        private void ClearActiveShadow(MissionDefinition mission)
        {
            if (data.activeShadowMissionId == mission.Id) { data.hasActiveShadowMission = false; data.activeShadowMissionId = ""; }
        }
    }
}
