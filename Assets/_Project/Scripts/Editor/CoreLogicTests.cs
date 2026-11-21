using System.IO;
using System.Linq;
using System.Text;
using CityDriver.Career;
using CityDriver.Economy;
using CityDriver.Save;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Automated Edit Mode tests for save, wallet, XP, career and garage rules. Output: Bridge/core_test.txt.</summary>
    public static class CoreLogicTests
    {
        private static StringBuilder log;
        private static int failures, checks;

        [BridgeCommand]
        public static void Run()
        {
            log = new StringBuilder(); failures = 0; checks = 0;
            Fresh(); Wallet(); Catalog(); Missions(); Levels(); ShadowRules(); ShadowPenaltyAndDebt(); Saving(); Garage();
            Line($"\nRESULT: {checks - failures}/{checks} checks passed{(failures == 0 ? " - ALL PASSED" : $" - {failures} FAILED")}");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            File.WriteAllText(Path.Combine(dir, "core_test.txt"), log.ToString());
            Debug.Log("[CoreTests] " + (failures == 0 ? "all passed" : failures + " failed") + " (" + checks + " checks)");
        }

        private sealed class Ctx
        {
            public SaveData D = SaveData.CreateNew();
            public WalletService W; public XPService X; public CareerService C; public GarageService G;
            public Ctx() { W = new WalletService(D); X = new XPService(D); C = new CareerService(D, W, X); G = new GarageService(D, W); }
            public OwnedVehicleData V => D.FindVehicle(D.selectedVehicleId);
        }

        private static void CompleteLevel(Ctx c, int level) { foreach (var m in CareerCatalog.ForLevel(level)) c.C.CompleteMission(m); }
        private static void Equip(Ctx c, int level) { foreach (var r in CareerCatalog.RequirementsFor(level)) c.V.SetUpgradeLevel(r.Type, r.MinLevel); }

        private static void Fresh()
        {
            Section("New game");
            var c = new Ctx();
            Check(c.W.Balance == 5000 && c.X.Xp == 0 && c.C.Level == 1, "starts with ₹5,000, 0 XP, level 1");
            Check(c.D.vehicles.Count == 1 && c.D.selectedVehicleId == "sedan_compact", "owns exactly one starter sedan");
            Check(!c.D.hasShadowPass && !c.D.weaponShopUnlocked, "no Shadow Pass / weapon shop at start");
        }

        private static void Wallet()
        {
            Section("Wallet");
            var c = new Ctx();
            Check(c.W.Apply("t1", 300, "fare"), "first apply succeeds");
            Check(!c.W.Apply("t1", 300, "fare"), "same transaction id is rejected (no double payment)");
            Check(c.W.Balance == 5300, "balance changed exactly once");
            c.W.Apply("t2", -9000, "penalty");
            Check(c.W.Balance == -3700 && c.W.IsInDebt, "balance may go negative (debt)");
            Check(c.D.transactions.Count == 2 && c.D.transactions[1].balanceAfter == -3700, "ledger records history with running balance");
            Check(!c.W.TrySpend("buy", 1, "x"), "cannot spend while in debt");
            c.X.Add(100);
            Check(c.X.Xp == 100 && c.W.Balance == -3700, "XP and wallet are independent");
        }

        private static void Catalog()
        {
            Section("Career catalog");
            var all = CareerCatalog.Missions;
            Check(all.Count == 30, "30 missions");
            Check(Enumerable.Range(1, 10).All(l => CareerCatalog.ForLevel(l).Count == 3), "exactly 3 missions per level, 10 levels");
            Check(all.Select(m => m.Id).Distinct().Count() == 30, "mission ids are unique");
            Check(CareerCatalog.Find("L1_first_ride").Reward == 300 && CareerCatalog.Find("L1_city_trip").Reward == 600 && CareerCatalog.Find("L1_safe_driver").Reward == 500, "level 1 rewards 300 / 600 / 500");
            Check(all.Where(m => m.Level <= 3).All(m => !m.ShadowRun), "levels 1-3 are Normal Driver missions");
            Check(all.Where(m => m.Level >= 4).All(m => m.ShadowRun && m.FailPenalty > 0), "levels 4-10 are Shadow Run with a failure penalty");
            Check(all.All(m => !string.IsNullOrEmpty(m.Briefing) && !string.IsNullOrEmpty(m.CompletionCriteria) && !string.IsNullOrEmpty(m.FailureCriteria)), "every mission has briefing, completion and failure criteria");
        }

        private static void Missions()
        {
            Section("Mission rewards");
            var c = new Ctx();
            var m = CareerCatalog.Find("L1_first_ride");
            long paid = c.C.CompleteMission(m);
            Check(paid == 300 && c.W.Balance == 5300 && c.X.Xp == m.Xp, "first completion pays full reward and XP");
            Check(c.C.IsCompleted(m.Id), "mission recorded as completed");
            long replay = c.C.CompleteMission(m);
            Check(replay == 75 && c.X.Xp == m.Xp, "replay pays 25% cash and no extra XP");
            long bonus = c.C.CompleteMission(CareerCatalog.Find("L1_city_trip"), 150);
            Check(bonus == 750, "time/safe-driving bonus is added to the reward");
        }

        private static void Levels()
        {
            Section("Level progression");
            var c = new Ctx();
            string reason;
            Check(!c.C.CanAdvanceLevel(c.V, out reason), "cannot advance with no missions done: " + reason);
            c.C.CompleteMission(CareerCatalog.Find("L1_first_ride")); c.C.CompleteMission(CareerCatalog.Find("L1_city_trip"));
            Check(!c.C.CanAdvanceLevel(c.V, out reason), "cannot advance with 2 of 3 missions: " + reason);
            c.C.CompleteMission(CareerCatalog.Find("L1_safe_driver"));
            Check(c.C.TryAdvanceLevel(c.V, out _) && c.C.Level == 2, "level 1 -> 2 once all three are done (no upgrade needed)");

            CompleteLevel(c, 2);
            Check(!c.C.CanAdvanceLevel(c.V, out reason) && reason.Contains("Tires"), "level 2 blocked by missing mandatory upgrade: " + reason);
            Equip(c, 2);
            Check(c.C.TryAdvanceLevel(c.V, out _) && c.C.Level == 3, "level 2 -> 3 after the upgrade");

            Check(!c.D.hasShadowPass, "Shadow Pass not yet granted during level 3");
            CompleteLevel(c, 3); Equip(c, 3);
            Check(c.C.TryAdvanceLevel(c.V, out _) && c.C.Level == 4 && c.D.hasShadowPass, "Shadow Pass unlocks at the end of level 3");

            for (int l = 4; l <= 5; l++) { c.D.hasActiveShadowMission = false; CompleteLevel(c, l); Equip(c, l); c.C.TryAdvanceLevel(c.V, out _); }
            Check(c.C.Level == 6 && c.D.weaponShopUnlocked, "weapon shop unlocks after level 5");

            for (int l = 6; l <= 10; l++) { CompleteLevel(c, l); Equip(c, l); c.C.TryAdvanceLevel(c.V, out _); }
            Check(c.C.Level == 10 && !c.C.CanAdvanceLevel(c.V, out reason), "level is capped at 10");
        }

        private static void ShadowRules()
        {
            Section("Shadow Run eligibility (exact brief formula, full truth table)");
            bool allMatch = true;
            foreach (int level in new[] { 1, 2, 3, 4, 10 })
                foreach (bool pass in new[] { false, true })
                    foreach (long wallet in new long[] { -100, 0, 1, 5000 })
                        foreach (bool active in new[] { false, true })
                            foreach (bool captured in new[] { false, true })
                            {
                                bool expected = level >= 3 && pass && wallet > 0 && !active && !captured;
                                if (ShadowRunRules.CanReceiveShadowRunCall(level, pass, wallet, active, captured) != expected) allMatch = false;
                            }
            Check(allMatch, "all 160 combinations match: level>=3 && pass && wallet>0 && !active && !captured");
        }

        private static void ShadowPenaltyAndDebt()
        {
            Section("Shadow Run penalties and debt recovery");
            var c = new Ctx();
            c.D.level = 4; c.D.hasShadowPass = true;
            var m = CareerCatalog.Find("L4_first_rescue");
            Check(c.C.CanReceiveShadowRunCall() && c.C.IsAvailable(m), "eligible at level 4 with pass and positive wallet");
            c.C.StartMission(m);
            Check(c.D.hasActiveShadowMission && !c.C.CanReceiveShadowRunCall(), "an active Shadow mission blocks further calls");

            Check(c.C.FailMission(m, "a1") && c.W.Balance == 5000 - 800, "failure applies the penalty");
            Check(!c.C.FailMission(m, "a1") && c.W.Balance == 4200, "calling fail again for the same attempt does NOT charge twice");
            Check(!c.D.hasActiveShadowMission, "active Shadow flag cleared after failure");

            c.W.Apply("big", -10000, "debt");
            Check(c.W.IsInDebt && !c.C.CanReceiveShadowRunCall(), "no new Shadow calls at zero or below");
            Check(c.C.IsAvailable(CareerCatalog.Find("L1_first_ride")), "Normal Driver missions stay playable while in debt");
            c.C.CompleteMission(CareerCatalog.Find("L2_vip_transport"));    // normal earnings
            c.C.CompleteMission(CareerCatalog.Find("L3_long_journey"));
            c.C.CompleteMission(CareerCatalog.Find("L3_night_passenger"));
            c.C.CompleteMission(CareerCatalog.Find("L3_mysterious_passenger"));
            Check(c.W.Balance <= 0 && !c.C.CanReceiveShadowRunCall(), $"still in debt after some normal earnings (balance {c.W.Balance}): no Shadow calls yet");
            c.W.Apply("more_earnings", 1000 - c.W.Balance, "normal earnings bring the wallet to exactly ₹1,000");
            Check(c.W.Balance == 1000 && c.C.CanReceiveShadowRunCall(), "debt recovery: a positive balance re-enables Shadow Run calls");
            c.W.Apply("to_zero", -1000, "wallet exactly zero");
            Check(c.W.Balance == 0 && !c.C.CanReceiveShadowRunCall(), "a balance of exactly zero still blocks Shadow Run calls");
            c.W.Apply("back_up", 500, "recover");
            c.C.SetCaptured(true);
            Check(!c.C.CanReceiveShadowRunCall(), "captured blocks Shadow calls");
        }

        private static void Saving()
        {
            Section("Saving");
            var c = new Ctx();
            c.C.CompleteMission(CareerCatalog.Find("L1_first_ride"));
            c.V.SetUpgradeLevel(UpgradeType.Engine, 2);
            c.W.Apply("neg", -9000, "debt");

            var mem = new MemorySaveStorage();
            var mgr = new SaveManager(mem);
            mgr.Save(c.D);
            SaveData back = mgr.Load();
            Check(back.walletBalance == c.D.walletBalance && back.xp == c.D.xp && back.level == c.D.level, "wallet, XP and level round-trip");
            Check(back.completedMissions.Contains("L1_first_ride") && back.FindVehicle("sedan_compact").GetUpgradeLevel(UpgradeType.Engine) == 2, "missions and per-vehicle upgrades round-trip");
            Check(back.walletBalance < 0, "negative wallet round-trips");
            Check(new WalletService(back).HasApplied("neg"), "applied transaction ids survive a reload (no duplicate penalty after restart)");

            mgr.Save(c.D); mgr.Save(c.D);                       // second save creates the .bak
            mem.Files[SaveManager.SaveKey] = "{ not json";       // corrupt the main file
            SaveData fromBak = mgr.Load();
            Check(fromBak.walletBalance == c.D.walletBalance, "corrupt main save falls back to the backup");
            mem.Files.Clear(); mem.Files[SaveManager.SaveKey] = "garbage";
            SaveData fresh = mgr.Load();
            Check(fresh.walletBalance == 5000 && fresh.level == 1, "unreadable save with no backup starts a clean new game instead of crashing");

            string dir = Path.Combine(Path.GetTempPath(), "cdu_save_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var fm = new SaveManager(new FileSaveStorage(dir));
                fm.Save(c.D); fm.Save(c.D);
                Check(fm.HasSave && File.Exists(Path.Combine(dir, SaveManager.SaveKey + ".bak")) && !File.Exists(Path.Combine(dir, SaveManager.SaveKey + ".tmp")), "file storage writes atomically and keeps a .bak, no stray .tmp");
                Check(new SaveManager(new FileSaveStorage(dir)).Load().walletBalance == c.D.walletBalance, "file save survives a fresh load (simulated app restart)");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        private static void Garage()
        {
            Section("Garage");
            var c = new Ctx();
            c.D.vehicles.Add(new OwnedVehicleData { vehicleId = "sedan_premium" });
            Check(c.G.TryBuyUpgrade("sedan_compact", UpgradeType.Tires, out _) && c.V.GetUpgradeLevel(UpgradeType.Tires) == 1, "buying an upgrade raises its level");
            Check(c.W.Balance == 5000 - GarageService.UpgradeCost(UpgradeType.Tires, 1), "and charges the price");
            Check(c.D.FindVehicle("sedan_premium").GetUpgradeLevel(UpgradeType.Tires) == 0, "upgrades are independent per vehicle");
            long before = c.W.Balance;
            Check(c.G.TryBuyUpgrade("sedan_compact", UpgradeType.Tires, out _) && c.V.GetUpgradeLevel(UpgradeType.Tires) == 2 && c.W.Balance < before, "next level can be bought and costs more");
            c.W.Apply("drain", -c.W.Balance, "drain");
            Check(!c.G.TryBuyUpgrade("sedan_compact", UpgradeType.Brakes, out string why) && c.V.GetUpgradeLevel(UpgradeType.Brakes) == 0, "cannot buy without money: " + why);
            c.V.condition01 = 0.5f; c.W.Apply("fund", 2000, "fund");
            Check(c.G.TryRepair("sedan_compact", out _) && c.V.condition01 == 1f && c.W.Balance == 2000 - 500, "repair restores condition and charges proportionally");
        }

        private static void Section(string t) => Line("\n" + t);
        private static void Line(string s) => log.AppendLine(s);
        private static void Check(bool ok, string text) { checks++; if (!ok) failures++; Line($"  [{(ok ? "PASS" : "FAIL")}] {text}"); }
    }
}
