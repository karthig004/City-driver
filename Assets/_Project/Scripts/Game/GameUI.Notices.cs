using System;
using System.Collections.Generic;
using System.IO;
using CityDriver.Career;
using CityDriver.Controls;
using CityDriver.Navigation;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// In-game notifications, in three tiers so the player is never flooded:
    ///  1. IMPORTANT (gold modal, pauses): upgrade needed for the next level, level unlocked from the garage, "you cannot continue".
    ///  2. TRIP OFFER (card, game keeps running): while you drive with no job, the next fare pops up with ACCEPT / SKIP and a
    ///     15 s countdown. Skipping backs off (75 s, then 3 min after two skips in a row).
    ///  3. TIP (slim bar, 6 s, tap to close): one-time driving hints, at most one every 40 s.
    /// Only one notification shows at a time; nothing shows during menus, the intro, the Shadow Pass cinematic or a chase.
    /// Cards sit in the left column under the radar's right edge (the route / mission cards' column), so they never cover the car,
    /// the wheel or the pedals.
    /// </summary>
    public sealed partial class GameUI
    {
        private const float OfferSeconds = 15f, FirstOfferDelay = 10f, OfferGap = 75f, OfferGapAfterSkips = 180f;
        private const float TipSeconds = 6f, TipGap = 40f;
        private const float ColumnX = 330f, ColumnW = 620f;

        private RectTransform offerRoot; private Text offerKind, offerTitle, offerInfo; private Image offerStripe, offerTimer; private MissionDefinition offerMission;
        private float offerUntil, nextOfferAt = -1f, idleSince; private int skipStreak;
        private RectTransform tipRoot; private Text tipText; private float tipUntil, nextTipAt;
        private readonly Queue<Action> important = new Queue<Action>();
        private int pendingLevelUp; private string upgradeNoticeShownFor = "", blockedNoticeShownFor = "";
        private float stuckFor, reverseFor, nextUpgradeTipAt, sessionDriveTime, nextRuleCheck; private bool fuelTipShown;
        private bool? noticesAllowed;

        // Editor automation must never be blocked by a popup: notices only run in the Editor when Bridge/show_menu.txt exists.
        private bool NoticesAllowed
        {
            get
            {
                if (noticesAllowed.HasValue) return noticesAllowed.Value;
#if UNITY_EDITOR
                noticesAllowed = File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "show_menu.txt"));
#else
                noticesAllowed = true;
#endif
                return noticesAllowed.Value;
            }
        }

        // ============================================================================================ frame update
        private void UpdateNotices()
        {
            if (!NoticesAllowed || safe == null) return;
            float now = Time.unscaledTime;
            var s = GameSession.Instance;
            bool blocked = modal != null || Time.timeScale == 0f || (IntroSequence.Instance != null && IntroSequence.Instance.Playing)
                           || (ShadowPassSequence.Instance != null && ShadowPassSequence.Instance.Playing);
            bool chase = PursuitManager.Instance != null && PursuitManager.Instance.Running;
            bool onJob = CurrentRunnerBusy() || (runner != null && runner.State == MissionState.Finished);

            if (blocked || onJob) idleSince = now;
            if (blocked) { HideOffer(false); HideTip(); return; }

            // 1. important notices wait for a calm moment (no job running) and take priority over everything else
            if (!onJob && !chase && now >= nextRuleCheck) { nextRuleCheck = now + 1f; CheckImportant(s); }
            if (!onJob && important.Count > 0) { HideOffer(false); HideTip(); important.Dequeue()(); return; }

            // 2. trip offers
            if (offerRoot != null && offerRoot.gameObject.activeSelf)
            {
                if (onJob || chase) HideOffer(false);
                else
                {
                    float left = offerUntil - now;
                    offerTimer.fillAmount = Mathf.Clamp01(left / OfferSeconds);
                    if (left <= 0f) SkipOffer();
                }
            }
            else if (!onJob && !chase && GameSettings.JobOffers && car != null)
            {
                if (nextOfferAt < 0f) nextOfferAt = now + FirstOfferDelay;
                if (now >= nextOfferAt && now - idleSince >= FirstOfferDelay)
                {
                    var m = NextOffer(s);
                    if (m != null) { HideTip(); ShowOffer(m); }
                    else nextOfferAt = now + 20f;
                }
            }

            // 3. tips
            if (tipRoot != null && tipRoot.gameObject.activeSelf)
            {
                if (now >= tipUntil || chase) HideTip();
                else PlaceColumn(tipRoot);
            }
            else if (GameSettings.Tips && !chase && (offerRoot == null || !offerRoot.gameObject.activeSelf)) UpdateTips(s, now);
        }

        // the left notification column: right of the radar, under any route / mission card that is showing
        private void PlaceColumn(RectTransform rt)
        {
            bool navShown = NavigationService.Instance != null && NavigationService.Instance.HasDestination;
            bool bannerShown = bannerRoot != null && bannerRoot.activeSelf;
            float y = -30f - (navShown ? 124f : 0f) - (bannerShown ? 90f : 0f);
            if (!Mathf.Approximately(rt.anchoredPosition.y, y)) rt.anchoredPosition = new Vector2(ColumnX, y);
        }

        // ============================================================================================ trip offers
        // Next fare to suggest: the first unfinished assignment of the current level the player can start. When Shadow Run calls are
        // blocked (empty wallet / captured) a replay of an earlier taxi fare is offered instead, so there is always a way to earn cash.
        private MissionDefinition NextOffer(GameSession s)
        {
            var r = MissionRunner.Instance; if (r == null) return null;
            foreach (var m in CareerCatalog.ForLevel(s.Career.Level))
                if (!s.Career.IsCompleted(m.Id) && r.CanStart(m)) return m;
            bool levelHasOpenWork = false;
            foreach (var m in CareerCatalog.ForLevel(s.Career.Level)) if (!s.Career.IsCompleted(m.Id)) levelHasOpenWork = true;
            if (levelHasOpenWork && !s.Career.CanReceiveShadowRunCall())
                for (int lv = Mathf.Min(s.Career.Level, CareerCatalog.ShadowPassLevel); lv >= 1; lv--)
                    foreach (var m in CareerCatalog.ForLevel(lv)) if (!m.ShadowRun && r.CanStart(m)) return m;
            return null;
        }

        private void BuildOffer()
        {
            offerRoot = Box(safe, "TripOffer", new Vector2(0, 1), new Vector2(0, 1), new Vector2(ColumnX, -30), new Vector2(ColumnW, 196), PanelSolid); Rim(offerRoot);
            offerStripe = Box(offerRoot, "Stripe", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(8, 196), Accent, false).GetComponent<Image>();
            offerStripe.raycastTarget = false;
            offerKind = Label(offerRoot, "", 20, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -12), new Vector2(560, 26), FontStyle.Bold);
            offerKind.gameObject.AddComponent<LetterSpacing>().spacing = 3f;
            offerTitle = Label(offerRoot, "", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -40), new Vector2(570, 46), FontStyle.Bold);
            offerInfo = Label(offerRoot, "", 24, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -88), new Vector2(570, 32), FontStyle.Bold);
            offerInfo.supportRichText = true;
            Btn(offerRoot, "SKIP", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-232, 22), new Vector2(170, 60), SkipOffer, new Color(0.22f, 0.24f, 0.28f), 26);
            Btn(offerRoot, "ACCEPT", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-22, 22), new Vector2(200, 60), AcceptOffer, Accent, 28);
            var bar = Box(offerRoot, "TimerBg", new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 0), new Vector2(ColumnW - 8, 5), new Color(1, 1, 1, 0.10f), false);
            bar.GetComponent<Image>().raycastTarget = false;
            var fill = Box(bar, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(ColumnW - 8, 5), Accent, false);
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            offerTimer = fill.GetComponent<Image>(); offerTimer.raycastTarget = false; offerTimer.type = Image.Type.Filled; offerTimer.fillMethod = Image.FillMethod.Horizontal;
            offerRoot.gameObject.SetActive(false);
        }

        private void ShowOffer(MissionDefinition m)
        {
            if (offerRoot == null) BuildOffer();
            var s = GameSession.Instance;
            offerMission = m;
            bool replay = s.Career.IsCompleted(m.Id) || m.Level < s.Career.Level;
            Color tone = m.ShadowRun ? new Color(0.72f, 0.45f, 1f) : Accent;
            offerKind.text = m.ShadowRun ? "SHADOW RUN CALL" : replay ? "EARN CASH  -  REPLAY FARE" : "NEW FARE";
            offerKind.color = tone; offerStripe.color = tone; offerTimer.color = tone;
            offerTitle.text = m.Title;
            long cash = replay ? m.Reward / 4 : m.Reward;
            string extra = m.TimeLimitSeconds > 0 ? $"   <color=#F2C230>{m.TimeLimitSeconds / 60} min</color>" : "";
            string xp = replay ? "" : $"   XP {m.Xp}";
            offerInfo.text = $"<color=#5CD98C>{GameSession.Money(cash)}</color>{xp}{extra}   -   {m.Briefing}";
            offerInfo.horizontalOverflow = HorizontalWrapMode.Overflow;
            offerInfo.text = Trim(offerInfo.text, 96);
            offerUntil = Time.unscaledTime + OfferSeconds; offerTimer.fillAmount = 1f;
            PlaceColumn(offerRoot);
            offerRoot.gameObject.SetActive(true);
            Entrance.Add(offerRoot, 0f, new Vector2(-40f, 0f), 0.3f);
            GameAudio.PlayClick();
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";

        private void AcceptOffer()
        {
            var m = offerMission; HideOffer(false); skipStreak = 0;
            nextOfferAt = Time.unscaledTime + OfferGap;
            var r = MissionRunner.Instance;
            if (m == null || r == null || !r.StartMission(m)) ShowToast("This job is not available right now");
        }

        private void SkipOffer()
        {
            HideOffer(true);
        }

        private void HideOffer(bool skipped)
        {
            if (offerRoot == null || !offerRoot.gameObject.activeSelf) return;
            offerRoot.gameObject.SetActive(false); offerMission = null;
            if (skipped) { skipStreak++; nextOfferAt = Time.unscaledTime + (skipStreak >= 2 ? OfferGapAfterSkips : OfferGap); }
            else nextOfferAt = Mathf.Max(nextOfferAt, Time.unscaledTime + FirstOfferDelay);
        }

        // ============================================================================================ tips
        private void BuildTip()
        {
            tipRoot = Box(safe, "Tip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(ColumnX, -30), new Vector2(ColumnW, 76), PanelSolid); Rim(tipRoot);
            var chip = Box(tipRoot, "Chip", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(78, 40), HudCyan);
            chip.GetComponent<Image>().raycastTarget = false;
            Label(chip, "TIP", 22, new Color(0.03f, 0.08f, 0.13f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78, 40), FontStyle.Bold);
            tipText = Label(tipRoot, "", 25, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(ColumnW - 130, 70), FontStyle.Bold);
            var b = tipRoot.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None; b.targetGraphic = tipRoot.GetComponent<Image>();
            b.onClick.AddListener(HideTip);                                                         // tap to close
            tipRoot.gameObject.SetActive(false);
        }

        private void ShowTip(string text, string onceId = null)
        {
            var s = GameSession.Instance;
            if (onceId != null)
            {
                if (s.Data.seenHints == null) s.Data.seenHints = new List<string>();
                if (s.Data.seenHints.Contains(onceId)) return;
                s.Data.seenHints.Add(onceId); s.Persist();
            }
            if (tipRoot == null) BuildTip();
            tipText.text = text;
            tipUntil = Time.unscaledTime + TipSeconds; nextTipAt = Time.unscaledTime + TipGap;
            PlaceColumn(tipRoot);
            tipRoot.gameObject.SetActive(true);
            Entrance.Add(tipRoot, 0f, new Vector2(-30f, 0f), 0.26f);
        }

        private void HideTip() { if (tipRoot != null && tipRoot.gameObject.activeSelf) tipRoot.gameObject.SetActive(false); }

        private bool Seen(GameSession s, string id) => s.Data.seenHints != null && s.Data.seenHints.Contains(id);

        private void UpdateTips(GameSession s, float now)
        {
            if (car == null) return;
            float dt = Time.unscaledDeltaTime;
            var panel = TouchControlsPanel.Instance;
            bool touch = panel != null && panel.isActiveAndEnabled;
            float kmh = car.SpeedKmh;
            if (kmh > 3f) sessionDriveTime += dt;

            // conditions are tracked every frame, but a tip only shows when the gap since the last one has passed
            bool gasHeld = touch && panel.Current.Throttle > 0.5f;
            stuckFor = gasHeld && kmh < 3f && panel.Gear == TouchControlsPanel.GearDrive ? stuckFor + dt : 0f;
            reverseFor = touch && panel.Gear == TouchControlsPanel.GearReverse && kmh > 2f ? reverseFor + dt : 0f;
            if (now < nextTipAt) return;

            var r = MissionRunner.Instance;
            if (touch && stuckFor > 2.5f && !Seen(s, "stuck")) { ShowTip("Stuck? Tap R above BRAKE, then hold GAS to back out.", "stuck"); return; }
            if (touch && reverseFor > 3f && !Seen(s, "drive_again")) { ShowTip("Tap D to drive forward again.", "drive_again"); return; }
            if (r != null && r.State == MissionState.ToPickup && !Seen(s, "route")) { ShowTip("Follow the blue route on the radar to your passenger.", "route"); return; }
            if (r != null && r.State == MissionState.ToPickup && r.DistanceToTarget < 40f && !Seen(s, "stop")) { ShowTip("Stop inside the marker to pick up the passenger.", "stop"); return; }
            if (kmh > 75f && !Seen(s, "drift")) { ShowTip("At speed, brake while turning to drift.", "drift"); return; }
            if (sessionDriveTime > 90f && !Seen(s, "map")) { ShowTip("Tap the radar to open the city map.", "map"); return; }

            var fuel = FuelSystem.Instance;
            if (fuel != null)
            {
                if (fuel.Fuel01 > 0.35f) fuelTipShown = false;
                else if (fuel.Fuel01 < 0.2f && !fuelTipShown) { fuelTipShown = true; ShowTip("Fuel low: stop the car and tap REFUEL."); return; }
            }

            // gentle repeat (every 4 min, never saved) while the level is finished but the upgrade gate still blocks it
            if (now >= nextUpgradeTipAt && LevelDoneButLocked(s, out string need))
            {
                nextUpgradeTipAt = now + 240f;
                ShowTip($"Level {s.Career.Level + 1} is waiting: {need} in the GARAGE.");
            }
        }

        // ============================================================================================ important notices
        private bool AllAssignmentsDone(GameSession s)
        {
            foreach (var m in CareerCatalog.ForLevel(s.Career.Level)) if (!s.Career.IsCompleted(m.Id)) return false;
            return true;
        }

        // The current level's three jobs are done but the car still lacks a mandatory upgrade: returns what to buy.
        private bool LevelDoneButLocked(GameSession s, out string need)
        {
            need = "";
            if (s.Career.Level >= CareerCatalog.LevelCount || !AllAssignmentsDone(s)) return false;
            var v = s.SelectedVehicle; var parts = new List<string>();
            foreach (var req in CareerCatalog.RequirementsFor(s.Career.Level))
            {
                int have = v == null ? 0 : v.GetUpgradeLevel(req.Type);
                if (have < req.MinLevel) parts.Add($"{PartName(req.Type)} stage {req.MinLevel}");
            }
            need = parts.Count == 0 ? "" : "upgrade " + string.Join(" + ", parts);
            return parts.Count > 0;
        }

        private static string PartName(CityDriver.Save.UpgradeType t) => t == CityDriver.Save.UpgradeType.Tires ? "Tyres" : t == CityDriver.Save.UpgradeType.Armor ? "Armour" : t == CityDriver.Save.UpgradeType.FuelTank ? "Fuel tank" : t.ToString();

        /// <summary>Called by the garage after a purchase: advances the career if that upgrade was the last thing the level gate needed.</summary>
        private bool TryAdvanceAfterUpgrade()
        {
            var s = GameSession.Instance;
            if (!AllAssignmentsDone(s) || !s.Career.TryAdvanceLevel(s.SelectedVehicle, out _)) return false;
            pendingLevelUp = s.Career.Level;
            return true;
        }

        private void CheckImportant(GameSession s)
        {
            if (pendingLevelUp > 0) { int lv = pendingLevelUp; pendingLevelUp = 0; important.Enqueue(() => ShowLevelUnlocked(lv)); return; }

            string key = $"L{s.Career.Level}";
            if (upgradeNoticeShownFor != key && LevelDoneButLocked(s, out string need))
            {
                upgradeNoticeShownFor = key; nextUpgradeTipAt = Time.unscaledTime + 240f;
                important.Enqueue(() => ShowUpgradeNeeded(s.Career.Level, need));
                return;
            }

            // Shadow Run levels: the calls stop when the wallet is empty or the police hold you, so the level cannot continue
            if (s.Career.Level > CareerCatalog.ShadowPassLevel && !AllAssignmentsDone(s) && !s.Career.CanReceiveShadowRunCall() && !s.Data.hasActiveShadowMission)
            {
                string cause = s.Data.isCaptured ? "captured" : s.Wallet.Balance <= 0 ? "broke" : !s.Data.hasShadowPass ? "nopass" : "";
                string bkey = key + cause;
                if (cause.Length > 0 && blockedNoticeShownFor != bkey) { blockedNoticeShownFor = bkey; important.Enqueue(() => ShowCannotContinue(cause)); }
            }
            else blockedNoticeShownFor = "";
        }

        private RectTransform Notice(string title, Color tone, string headline, string body)
        {
            RectTransform p = OpenModal(title, 860, 430);
            var band = Box(p, "Band", new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -104), new Vector2(792, 210), new Color(1f, 1f, 1f, 0.05f));
            Box(band, "Edge", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 210), tone, false).GetComponent<Image>().raycastTarget = false;
            Label(band, headline, 36, tone, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(740, 48), FontStyle.Bold);
            Label(band, body, 26, Color.white, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -78), new Vector2(740, 120));
            return p;
        }

        private void ShowUpgradeNeeded(int level, string need)
        {
            var p = Notice("UPGRADE NEEDED", Accent, $"Level {level} complete!",
                $"To unlock Level {level + 1}, {need} for your car. Then the next level opens right away.");
            Btn(p, "LATER", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 26), new Vector2(240, 64), () => CloseModal(), new Color(0.22f, 0.24f, 0.28f), 28);
            Btn(p, "OPEN GARAGE", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 26), new Vector2(320, 64), OpenGarage, Accent, 30);
        }

        private void ShowLevelUnlocked(int level)
        {
            var p = Notice("LEVEL UP", Good, $"Level {level} unlocked",
                "New assignments are waiting. A fare offer will pop up while you drive, or pick one in MISSIONS.");
            Btn(p, "DRIVE", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 26), new Vector2(240, 64), () =>
            {
                CloseModal(); var ss = GameSession.Instance;
                if (ss.Data.hasShadowPass && !ss.Data.shadowCinematicSeen) PlayShadowPass();
            }, new Color(0.22f, 0.24f, 0.28f), 28);
            Btn(p, "VIEW MISSIONS", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 26), new Vector2(320, 64), OpenMissions, Accent, 30);
        }

        private void ShowCannotContinue(string cause)
        {
            string body = cause == "captured" ? "The police are holding you, so Shadow Run calls have stopped. Pay bail in MISSIONS to continue this level."
                        : cause == "broke" ? "Your wallet is empty, so Shadow Run calls have stopped. Replay earlier fares to earn cash, then the calls come back."
                        : "You need the Shadow Pass to take Shadow Run calls.";
            var p = Notice("CAN'T CONTINUE", Bad, cause == "captured" ? "You were captured" : cause == "broke" ? "Out of money" : "No Shadow Pass", body);
            Btn(p, "LATER", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 26), new Vector2(240, 64), () => CloseModal(), new Color(0.22f, 0.24f, 0.28f), 28);
            if (cause == "broke")
                Btn(p, "EARN CASH", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 26), new Vector2(320, 64), () =>
                {
                    CloseModal(); var m = NextOffer(GameSession.Instance);
                    if (m != null) ShowOffer(m); else OpenMissions();
                }, Accent, 30);
            else Btn(p, "MISSIONS", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 26), new Vector2(320, 64), OpenMissions, Accent, 30);
        }

        /// <summary>After the result screen closes: the next offer waits a little so the player can breathe.</summary>
        private void AfterResult(MissionResult r)
        {
            if (!NoticesAllowed) return;
            nextOfferAt = Time.unscaledTime + (r != null && r.Success ? 20f : 30f);
            nextRuleCheck = 0f;
        }
    }
}
