using CityDriver.Characters;
using System;
using System.Collections.Generic;
using System.IO;
using CityDriver.Career;
using CityDriver.Controls;
using CityDriver.Navigation;
using CityDriver.Population;
using CityDriver.Save;
using CityDriver.UI;
using CityDriver.Vehicle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// The product UI layer, built in code: status bar (wallet / level / XP), mission banner, subtitles, toast, plus the
    /// main menu, mission list, garage, wallet history and the mission result screen. Menus pause the game (timeScale 0).
    /// English only: Tamil text is NOT implemented (the built-in font has no Tamil glyphs).
    /// </summary>
    public sealed partial class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        private static readonly Color Panel = new Color(0.07f, 0.09f, 0.12f, 0.88f);
        private static readonly Color PanelSolid = new Color(0.09f, 0.11f, 0.15f, 0.98f);
        private static readonly Color Accent = new Color(0.98f, 0.76f, 0.12f);
        private static readonly Color HudCyan = new Color(0.22f, 0.74f, 1f);
        private static readonly Color Good = new Color(0.35f, 0.85f, 0.5f);
        private static readonly Color Bad = new Color(0.95f, 0.38f, 0.33f);
        private static readonly Color Muted = new Color(0.68f, 0.72f, 0.78f);

        private Font font;
        private Sprite rounded;
        private Canvas canvas;
        private RectTransform safe;
        private Text walletText, levelText, bannerTitle, bannerSub, subtitleText, toastText;
        private Image xpFill;
        private GameObject bannerRoot, subtitleRoot, toastRoot, modal;
        private float subtitleUntil, toastUntil;
        private string shownWallet, shownLevel, shownSub;
        private float shownXp = -1f;
        private MissionRunner runner;
        private VehicleController car;
        private bool paintApplied;
        private Text radioButtonText, heatText, weaponButtonText, speedValueText, gearValueText, fuelLabel;
        private Image fuelFill, healthFill; private RectTransform needle; private GameObject fuelButtonRoot; private Text fuelButtonText;
        public CityDriver.UI.ThreatHud ThreatUi { get; private set; }
        private GameObject driftRoot; private CanvasGroup driftGroup; private Text driftBig, driftSmall; private float driftShowUntil, shownBank = -100f;
        private Image shadowGlow;
        private Text avatarLevel, timeLeftText, timeSubText; private Image timeFill; private GameObject timeBarRoot; private float stackY = -30f;
        private Text skyChipText; private int shownSky = -1, shownWx = -1; private VehicleHUD legacyHud; private Image handbrakeImage;
        private GameObject weaponButtonRoot, shadowButtonRoot, heatRoot;
        private RectTransform wheel;
        private int shownCamera = -2;

        public bool ModalOpen => modal != null;
        public string ModalTitle { get; private set; } = "";

        private static readonly (string name, string hex, long price)[] Paints =
        {
            ("Taxi Yellow", "#F5BD0D", 0), ("Pearl White", "#E9E9E6", 400), ("Midnight Black", "#15161A", 400),
            ("Racing Red", "#B3141A", 400), ("Ocean Blue", "#1F4FA3", 400), ("Graphite", "#4C4F57", 400),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "disable_game_layer.txt"))) return;   // bisecting aid
#endif
            if (FindFirstObjectByType<GameUI>() != null) return;
            new GameObject("GameUI").AddComponent<GameUI>();
        }

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameSettings.Changed -= OnSettingsChanged;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
            if (runner != null) { runner.Finished -= OnFinished; runner.Subtitle -= ShowSubtitle; runner.Toast -= ShowToast; }
        }

        private void Start()
        {
            font = UiTheme.Semi;
            rounded = MakeRoundedSprite();
            BuildCanvas();
            BuildHud();
            EnsureEventSystem();
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsInactive.Include)) UiTheme.ApplyFonts(cv.transform);      // game typeface everywhere, including the scene-built HUD
            if (ShouldShowIntro()) IntroSequence.Ensure().Play(font, AfterIntro);
            else AfterIntro();
        }

        // After the logo + loading intro: first launch shows How To Play (skippable), later launches go straight to the menu.
        private void AfterIntro()
        {
            if (!ShouldShowMenuAtStart()) return;
            var s = GameSession.Instance;
            if (s != null && !s.Data.tutorialSeen) OpenHowToPlay(0, true); else OpenMainMenu();
        }

        private static bool ShouldShowIntro()
        {
#if UNITY_EDITOR
            return File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "show_intro.txt"));
#else
            return true;
#endif
        }

        private static bool ShouldShowMenuAtStart()
        {
#if UNITY_EDITOR
            // In the Editor automation runs must not be blocked by a menu: it only shows when Bridge/show_menu.txt exists.
            return File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "show_menu.txt"));
#else
            return true;
#endif
        }

        // =============================================================================================== building blocks
        private void BuildCanvas()
        {
            var go = new GameObject("GameCanvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var s = new GameObject("SafeArea", typeof(RectTransform));
            s.transform.SetParent(go.transform, false);
            safe = (RectTransform)s.transform; Stretch(safe);
            s.AddComponent<SafeAreaFitter>();
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>();
        }

        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        private static Sprite MakeRoundedSprite()
        {
            const int S = 64, R = 18;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Max(R - x - 0.5f, x + 0.5f - (S - R), 0f), dy = Mathf.Max(R - y - 0.5f, y + 0.5f - (S - R), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)Mathf.Clamp(Mathf.RoundToInt((R - d + 0.5f) * 255f), 0, 255);
                    px[y * S + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        }

        private RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool round = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = color;
            if (round) { img.sprite = rounded; img.type = Image.Type.Sliced; }
            return r;
        }

        private Text Label(Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = box;
            var t = go.GetComponent<Text>();
            bool bold = (style & FontStyle.Bold) != 0;
            t.font = bold ? UiTheme.Bold : UiTheme.Semi; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style & ~FontStyle.Bold; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            return t;
        }

        private Button Btn(Transform parent, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Action onClick, Color? fill = null, int fontSize = 30)
        {
            RectTransform r = Box(parent, "Button_" + label, anchor, pivot, pos, size, fill ?? new Color(0.17f, 0.21f, 0.29f, 1f));
            var b = r.gameObject.AddComponent<Button>();
            var cb = b.colors; cb.highlightedColor = new Color(1f, 1f, 1f, 0.9f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); b.colors = cb;
            b.targetGraphic = r.GetComponent<Image>();
            b.onClick.AddListener(() => { GameAudio.PlayClick(); onClick(); });
            r.gameObject.AddComponent<PressFx>();
            var gloss = Box(r, "Gloss", new Vector2(0, 0.5f), new Vector2(0, 0), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.09f));
            gloss.anchorMin = new Vector2(0, 0.5f); gloss.anchorMax = new Vector2(1, 1); gloss.offsetMin = new Vector2(4, 0); gloss.offsetMax = new Vector2(-4, -4); gloss.GetComponent<Image>().raycastTarget = false;
            Label(r, label, fontSize, fill.HasValue && fill.Value.r > 0.6f ? new Color(0.1f, 0.1f, 0.1f) : Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, FontStyle.Bold);
            return b;
        }

        // =============================================================================================== HUD
        private void BuildHud()
        {
            // status (compact): coins + wallet, a level badge whose ring is the XP progress, and a fuel / health row. Nothing else.
            RectTransform status = Box(safe, "Status", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(420, 142), Panel); Rim(status);
            for (int ci = 0; ci < 3; ci++) { var coin = CityDriver.UI.ThreatMarkers.NewImage(status, "Coin" + ci, CityDriver.UI.ThreatMarkers.Dot, new Vector2(34, 34)); coin.rectTransform.anchorMin = coin.rectTransform.anchorMax = new Vector2(0, 1); coin.rectTransform.anchoredPosition = new Vector2(40 + ci * 3, -50 + ci * 8); coin.color = Color.Lerp(new Color(1f, 0.78f, 0.15f), new Color(1f, 0.93f, 0.5f), ci * 0.4f); }
            walletText = Label(status, "", 48, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(76, -8), new Vector2(236, 66), FontStyle.Bold);
            walletText.resizeTextForBestFit = true; walletText.resizeTextMinSize = 26; walletText.resizeTextMaxSize = 48;
            var badge = CityDriver.UI.ThreatMarkers.NewImage(status, "LevelBadge", UiTheme.Disc, new Vector2(74, 74)); badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1, 1); badge.rectTransform.anchoredPosition = new Vector2(-48, -44); badge.color = new Color(0.16f, 0.22f, 0.32f);
            var ringFill = CityDriver.UI.ThreatMarkers.NewImage(badge.rectTransform, "XpRing", UiTheme.Disc, new Vector2(74, 74)); ringFill.color = HudCyan;
            ringFill.type = Image.Type.Filled; ringFill.fillMethod = Image.FillMethod.Radial360; ringFill.fillOrigin = 2; ringFill.fillClockwise = true; ringFill.fillAmount = 0f; xpFill = ringFill;
            var badgeIn = CityDriver.UI.ThreatMarkers.NewImage(badge.rectTransform, "In", UiTheme.Disc, new Vector2(58, 58)); badgeIn.color = new Color(0.06f, 0.09f, 0.15f);
            avatarLevel = Label(badge.rectTransform, "1", 40, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 56), FontStyle.Normal); avatarLevel.font = UiTheme.Display;

            AddBtnIcon(Btn(safe, "MISSIONS", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -318), new Vector2(250, 74), OpenMissions, null, 30), UiIcons.Clipboard);
            AddBtnIcon(Btn(safe, "MENU", new Vector2(0, 1), new Vector2(0, 1), new Vector2(298, -318), new Vector2(210, 74), OpenMainMenu, null, 30), UiIcons.Grid);

            // mission banner (top centre, below the GPS instruction banner)
            // mission strip: sits directly under the route card (same x, same width): PICKUP / DROP-OFF + passenger on the left, time left on the right
            bannerRoot = Box(safe, "MissionBanner", new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, -154), new Vector2(620, 78), Panel).gameObject; Rim((RectTransform)bannerRoot.transform);
            bannerTitle = Label(bannerRoot.transform, "", 20, HudCyan, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -8), new Vector2(330, 26), FontStyle.Bold); bannerTitle.gameObject.AddComponent<LetterSpacing>().spacing = 3f;
            bannerSub = Label(bannerRoot.transform, "", 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 12), new Vector2(340, 40), FontStyle.Bold);
            timeLeftText = Label(bannerRoot.transform, "", 40, Color.white, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -4), new Vector2(240, 48), FontStyle.Bold);
            timeSubText = Label(bannerRoot.transform, "", 20, Muted, TextAnchor.MiddleRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-26, 12), new Vector2(240, 26), FontStyle.Bold);
            var tbar = Box(bannerRoot.transform, "TimeBar", new Vector2(0, 0), new Vector2(0, 0), new Vector2(22, 5), new Vector2(576, 5), new Color(1, 1, 1, 0.12f), false); tbar.GetComponent<Image>().raycastTarget = false;
            var tfill = Box(tbar, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(576, 5), HudCyan, false); timeFill = tfill.GetComponent<Image>(); timeFill.raycastTarget = false;
            timeFill.type = Image.Type.Filled; timeFill.fillMethod = Image.FillMethod.Horizontal; timeFill.fillAmount = 1f; tfill.anchorMin = Vector2.zero; tfill.anchorMax = Vector2.one; tfill.offsetMin = tfill.offsetMax = Vector2.zero; timeBarRoot = tbar.gameObject;
            bannerRoot.SetActive(false);

            // subtitles (bottom centre, above the speedometer)
            subtitleRoot = Box(safe, "Subtitles", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-470, -30), new Vector2(470, 112), Panel).gameObject; Rim((RectTransform)subtitleRoot.transform);
            subtitleText = Label(subtitleRoot.transform, "", 25, Color.white, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 100));
            subtitleRoot.SetActive(false);

            toastRoot = Box(safe, "Toast", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(820, 70), Panel).gameObject;
            toastText = Label(toastRoot.transform, "", 30, Accent, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780, 60), FontStyle.Bold);
            toastRoot.SetActive(false);


            // second row: radio + settings
            // (radio and settings live in the menu and the gear button: the HUD keeps only Missions and Menu, like the reference)

            // steering wheel (shown in Wheel steering mode; the touch panel reads it directly) and handbrake / horn
            BuildWheel();
            IconButton("SettingsGear", HudIcons.Kind.Gear, new Vector2(-100, -230), 66, OpenSettings);
            GameSettings.Changed += OnSettingsChanged;
            ThreatUi = CityDriver.UI.ThreatHud.Create(safe);


            // heat meter, weapon and Shadow Pass buttons
            heatRoot = new GameObject("HeatUnused"); heatRoot.transform.SetParent(safe, false); heatRoot.SetActive(false);        // heat now lives in the chase banner
            heatText = Label(heatRoot.transform, "", 20, Bad, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(200, 30), FontStyle.Bold);
            var wb = Btn(safe, "WEAPON", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -404), new Vector2(478, 66), UseWeapon, new Color(0.5f, 0.2f, 0.18f, 1f), 26);
            weaponButtonRoot = wb.gameObject; weaponButtonText = wb.GetComponentInChildren<Text>(); weaponButtonRoot.SetActive(false);
            // Shadow Pass: a small glowing card icon in the icon row (left of the gear), only while the pass can be used
            var sbGo = new GameObject("ShadowPassIcon", typeof(RectTransform)); sbGo.transform.SetParent(safe, false);
            var sbRt = (RectTransform)sbGo.transform; sbRt.anchorMin = sbRt.anchorMax = new Vector2(1, 1); sbRt.pivot = new Vector2(1, 1); sbRt.anchoredPosition = new Vector2(-176, -230); sbRt.sizeDelta = new Vector2(66, 66);
            shadowGlow = CityDriver.UI.ThreatMarkers.NewImage(sbRt, "Glow", UiTheme.Glow, new Vector2(150, 150)); shadowGlow.color = new Color(0.7f, 0.3f, 1f, 0.6f);
            var sbDisc = CityDriver.UI.ThreatMarkers.NewImage(sbRt, "Disc", UiTheme.Disc, new Vector2(66, 66)); sbDisc.color = new Color(0.3f, 0.12f, 0.5f); sbDisc.raycastTarget = true;
            var sbIn = CityDriver.UI.ThreatMarkers.NewImage(sbRt, "In", UiTheme.Disc, new Vector2(58, 58)); sbIn.color = new Color(0.08f, 0.04f, 0.16f);
            var sbEye = CityDriver.UI.ThreatMarkers.NewImage(sbRt, "Eye", MagicCardArt.Sigil, new Vector2(52, 52)); sbEye.color = new Color(1f, 0.85f, 0.45f);
            var sbBtn = sbGo.AddComponent<Button>(); sbBtn.targetGraphic = sbDisc; sbBtn.transition = Selectable.Transition.None; sbBtn.onClick.AddListener(() => { GameAudio.PlayClick(); PlayShadowPass(); });
            sbGo.AddComponent<PressFx>();
            shadowButtonRoot = sbGo; shadowButtonRoot.SetActive(false);


            BuildSkin(status);
            var pinButton = FindScene("MapButton", transform); if (pinButton != null) pinButton.gameObject.SetActive(false);        // the radar itself opens the city map now
            // the old placeholder title and key-help text give way to the new status bar
            foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Include))
                if (t.transform.root != transform && (t.text.StartsWith("CITY DRIVER") || t.text.StartsWith("W accelerate"))) t.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (runner == null && MissionRunner.Instance != null)
            {
                runner = MissionRunner.Instance;
                runner.Finished += OnFinished; runner.Subtitle += ShowSubtitle; runner.Toast += ShowToast;
            }
            if (car == null) car = FindFirstObjectByType<VehicleController>();
            if (car != null && !paintApplied) { VehicleTuning.Apply(car, GameSession.Instance.SelectedVehicle); ApplySavedPaint(); paintApplied = true; }

            var session = GameSession.Instance;
            string w = GameSession.Money(session.Wallet.Balance);
            if (w != shownWallet) { shownWallet = w; walletText.text = w; walletText.color = session.Wallet.IsInDebt ? Bad : Accent; }
            int level = session.Career.Level;
            float xp = session.Xp.BandProgress01(level);
            long nextBand = level < CareerCatalog.LevelCount ? CityDriver.Economy.XPService.BandStartXp(level + 1) : session.Xp.Xp;
            string lv = $"LEVEL {level}  {session.Xp.Xp:N0}/{nextBand:N0}";
            if (lv != shownLevel) { shownLevel = lv; if (avatarLevel != null) avatarLevel.text = level.ToString(); }
            if (!Mathf.Approximately(xp, shownXp)) { shownXp = xp; xpFill.fillAmount = xp; }

            UpdateBanner();
            UpdatePursuitHud();
            UpdateSkin();
            UpdateBoundaryWarning();
            UpdateNotices();
            float now = Time.unscaledTime;
            if (subtitleRoot.activeSelf && now > subtitleUntil) subtitleRoot.SetActive(false);
            if (toastRoot.activeSelf && now > toastUntil) toastRoot.SetActive(false);

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.jKey.wasPressedThisFrame) { if (modal != null) CloseModal(); else OpenMissions(); }
                if (kb.escapeKey.wasPressedThisFrame) { if (modal != null) CloseModal(); else OpenMainMenu(); }
            }
        }

        private float nextBoundaryWarn;
        // near the edge of the city: tell the player once in a while that the road ends there
        private void UpdateBoundaryWarning()
        {
            if (car == null || Time.unscaledTime < nextBoundaryWarn || Time.timeScale == 0f) return;
            float d = CityDriver.World.CityBoundary.EdgeDistance(car.transform.position);
            if (d < 90f && d > -10f && car.SpeedKmh > 8f) { ShowToast(d < 35f ? "CITY LIMIT - turn back" : "City limit ahead - turn back"); nextBoundaryWarn = Time.unscaledTime + 7f; }
        }

        private void UpdateBanner()
        {
            bool show = runner != null && runner.Active != null && runner.State != MissionState.Idle && runner.State != MissionState.Finished;
            if (bannerRoot.activeSelf != show) bannerRoot.SetActive(show);
            // the chase banner stacks under whatever route / mission cards are showing, in the same column
            bool navShown = NavigationService.Instance != null && NavigationService.Instance.HasDestination;
            float top = -30f - (navShown ? 124f : 0f) - (show ? 90f : 0f);
            if (!Mathf.Approximately(top, stackY) && ThreatUi != null) { stackY = top; ThreatUi.SetStackTop(top); }
            if (!show) return;

            string caption = runner.State == MissionState.ToPickup ? "PICKUP" : runner.State == MissionState.Boarding ? "BOARDING" : runner.State == MissionState.ToDropoff ? "DROP-OFF" : "ARRIVED";
            string who = runner.State == MissionState.Boarding ? runner.PassengerName + " is getting in" : runner.PassengerName;
            if (bannerTitle.text != caption) bannerTitle.text = caption;
            if (bannerSub.text != who) bannerSub.text = who;
            int el = Mathf.FloorToInt(runner.Elapsed);
            if (runner.TimeRemaining >= 0f)
            {
                float left = runner.TimeRemaining; int t = Mathf.CeilToInt(left);
                string lt = $"{t / 60:00}:{t % 60:00}"; if (timeLeftText.text != lt) timeLeftText.text = lt;
                bool low = left < 60f; timeLeftText.color = low ? Color.Lerp(Color.white, Bad, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f)) : Color.white;
                string ss = $"LEFT   -   TIME {el / 60:00}:{el % 60:00}"; if (timeSubText.text != ss) timeSubText.text = ss;
                timeBarRoot.SetActive(true); timeFill.fillAmount = Mathf.Clamp01(left / Mathf.Max(1f, left + runner.Elapsed)); timeFill.color = low ? Bad : HudCyan;
            }
            else
            {
                string lt = $"{el / 60:00}:{el % 60:00}"; if (timeLeftText.text != lt) timeLeftText.text = lt; timeLeftText.color = Color.white;
                if (timeSubText.text != "TIME") timeSubText.text = "TIME"; timeBarRoot.SetActive(false);
            }
        }

        public void ShowSubtitle(string speaker, string line)
        {
            if (!GameSettings.Subtitles) return;
            subtitleText.text = $"<b>{speaker}:</b> {line}";
            subtitleText.supportRichText = true;
            subtitleUntil = Time.unscaledTime + 4.5f; subtitleRoot.SetActive(true);
        }

        public void ShowToast(string text) { toastText.text = text; toastUntil = Time.unscaledTime + 3f; toastRoot.SetActive(true); }

        // =============================================================================================== modals
        private bool lastModalReopen;

        private RectTransform OpenModal(string title, float w, float h)
        {
            bool reopen = modal != null && ModalTitle == title;           // rebuilding the same screen (a tab or a setting changed): no entrance animation
            lastModalReopen = reopen;
            CloseModal(false);
            ModalTitle = title;
            var dim = new GameObject("Modal", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(safe, false); Stretch((RectTransform)dim.transform);
            dim.GetComponent<Image>().color = new Color(0.01f, 0.015f, 0.03f, 0.78f);
            modal = dim;
            var glow = new GameObject("Glow", typeof(RectTransform), typeof(Image)); glow.transform.SetParent(dim.transform, false);
            var gr = (RectTransform)glow.transform; gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f); gr.sizeDelta = new Vector2(w + 360, h + 360);
            var gi = glow.GetComponent<Image>(); gi.sprite = UiTheme.Glow; gi.color = new Color(0.98f, 0.62f, 0.1f, 0.13f); gi.raycastTarget = false;
            RectTransform holder = Box(dim.transform, "Holder", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), new Color(0.98f, 0.76f, 0.12f, 1f));     // gold frame
            RectTransform panel = Box(holder, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w - 5, h - 5), new Color(PanelSolid.r, PanelSolid.g, PanelSolid.b, 1f));     // fully opaque: the project blends UI in linear space, so even 2% of the gold frame showing through turns the dark navy grey-brown
            panel.sizeDelta = new Vector2(w, h); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = new Vector2(2.5f, 2.5f); panel.offsetMax = new Vector2(-2.5f, -2.5f);
            panel.sizeDelta = new Vector2(-5f, -5f);                        // stretched to the frame; children are laid out against the frame size w x h
            // header band with a gold edge and accent tick
            var header = Box(panel, "Header", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(w - 5, 80), new Color(0.04f, 0.05f, 0.085f, 1f)); header.GetComponent<Image>().raycastTarget = false;
            Box(panel, "HeaderLine", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -79), new Vector2(w - 5, 3), new Color(0.98f, 0.76f, 0.12f, 0.85f), false).GetComponent<Image>().raycastTarget = false;
            Box(panel, "HeaderTick", new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -22), new Vector2(8, 38), Accent, false).GetComponent<Image>().raycastTarget = false;
            var tt = Label(panel, title, 56, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(46, -10), new Vector2(w - 180, 60), FontStyle.Normal);
            tt.font = UiTheme.Display; tt.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            Btn(panel, "X", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -12), new Vector2(64, 56), () => CloseModal(), new Color(0.42f, 0.17f, 0.18f), 30);
            Time.timeScale = 0f;
            if (!reopen) { var pop = ModalPop.Add(dim, holder, panel); pop.stagger = true; }
            return panel;
        }

        public void CloseModal(bool resume = true)
        {
            if (modal != null) Destroy(modal);
            modal = null; ModalTitle = "";
            if (resume) { Time.timeScale = 1f; pendingPaintHex = ""; editLook = null; }
        }

        public void OpenMainMenu()
        {
            RectTransform p = OpenModal("CITY DRIVER: UNDERCOVER", 760, 1070);
            Label(p, "Drive Your Career. Risk Your Money. Become a Legend.", 24, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -78), new Vector2(690, 34));
            var s = GameSession.Instance;
            float y = -140;
            Btn(p, s.HasSave ? "CONTINUE" : "PLAY", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 74), () => CloseModal(), Accent, 36); y -= 90;
            Btn(p, "MISSIONS", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenMissions); y -= 78;
            Btn(p, "GARAGE", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenGarage); y -= 78;
            Btn(p, "CHARACTER", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenCharacter); y -= 78;
            Btn(p, "WALLET", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenWallet); y -= 78;
            Btn(p, "VEHICLES", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenVehicles); y -= 78;
            Btn(p, "WEAPON SHOP", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenWeaponShop); y -= 78;
            Btn(p, "SETTINGS", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), OpenSettings); y -= 78;
            Btn(p, "HOW TO PLAY", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 66), () => OpenHowToPlay(0, false)); y -= 78;
            Btn(p, "NEW GAME (erases save)", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 56), () => { GameSession.Instance.NewGame(); paintApplied = false; CloseModal(); ShowToast("New game started"); }, new Color(0.3f, 0.16f, 0.16f), 24);
        }

        public void OpenMissions()
        {
            var s = GameSession.Instance;
            int level = s.Career.Level;
            RectTransform p = OpenModal($"MISSIONS - LEVEL {level}", 1100, 640);
            var list = CareerCatalog.ForLevel(level);
            float y = -100;
            foreach (var m in list)
            {
                bool done = s.Career.IsCompleted(m.Id);
                RectTransform row = Box(p, "Row_" + m.Id, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1030, 118), m.ShadowRun ? new Color(0.38f, 0.14f, 0.58f, 0.34f) : new Color(1, 1, 1, 0.06f));
                if (m.ShadowRun)
                {
                    Rim(row); var rimO = row.GetComponent<Outline>(); if (rimO != null) rimO.effectColor = new Color(1f, 0.8f, 0.4f, 0.7f);
                    var eye = CityDriver.UI.ThreatMarkers.NewImage(row, "Sigil", MagicCardArt.Sigil, new Vector2(70, 70)); eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(1, 0.5f); eye.rectTransform.anchoredPosition = new Vector2(-300, 8); eye.color = new Color(1f, 0.82f, 0.45f, 0.85f);
                    var tag = Label(row, "SHADOW RUN", 18, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(560, -14), new Vector2(200, 26), FontStyle.Bold); tag.gameObject.AddComponent<LetterSpacing>().spacing = 3f;
                }
                Label(row, $"{m.Order}. {m.Title}", 32, Color.white, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -10), new Vector2(560, 40), FontStyle.Bold);
                Label(row, m.Briefing, 22, Muted, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -54), new Vector2(640, 56));
                string tl = m.TimeLimitSeconds > 0 ? "   Timed" : "";
                Label(row, $"Reward {GameSession.Money(m.Reward)}   XP {m.Xp}{tl}", 22, Accent, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-230, -14), new Vector2(330, 34));
                if (done) Label(row, "COMPLETED", 24, Good, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-230, -52), new Vector2(330, 34), FontStyle.Bold);
                MissionDefinition mm = m;
                var runnerNow = MissionRunner.Instance;
                bool can = runnerNow != null && runnerNow.CanStart(mm);
                Btn(row, done ? "REPLAY" : "START", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(190, 70), () =>
                {
                    CloseModal();
                    if (runnerNow != null && !runnerNow.StartMission(mm)) ShowToast("Cannot start this mission right now");
                }, can ? Accent : new Color(0.25f, 0.27f, 0.3f), 28);
                y -= 134;
            }

            if (s.Data.isCaptured)
                Btn(p, "PAY BAIL " + GameSession.Money(1000), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-90, -24), new Vector2(330, 52), () => { s.Wallet.Apply("bail:" + s.Data.transactions.Count, -1000, "Police bail"); s.Career.SetCaptured(false); s.Persist(); ShowToast("Released. Shadow Run calls resume."); OpenMissions(); }, new Color(0.45f, 0.2f, 0.18f), 22);
            s.Career.CanAdvanceLevel(s.SelectedVehicle, out string reason);
            Label(p, reason == null ? "Level complete: all assignments done." : "To reach the next level: " + reason, 24, Muted, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 22), new Vector2(1000, 40));
            if (MissionRunner.Instance != null && MissionRunner.Instance.Active != null && MissionRunner.Instance.State != MissionState.Finished)
                Btn(p, "ABANDON CURRENT", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 22), new Vector2(300, 52), () => { CloseModal(); MissionRunner.Instance.Abandon(); }, new Color(0.35f, 0.16f, 0.16f), 22);
        }



        // =============================================================================================== pursuit HUD, weapon, Shadow Pass
        private void UpdatePursuitHud()
        {
            var pm = PursuitManager.Instance;
            var session = GameSession.Instance;
            bool pursuit = pm != null && pm.Running;
            string weapon = session.Data.equippedWeapon;
            bool showWeapon = pursuit && !string.IsNullOrEmpty(weapon) && session.Data.ownedWeapons.Contains(weapon);
            if (weaponButtonRoot.activeSelf != showWeapon) weaponButtonRoot.SetActive(showWeapon);
            if (showWeapon)
            {
                float cd = pm.WeaponCooldown01;
                string w = cd > 0.01f ? $"{WeaponName(weapon)}: {Mathf.CeilToInt(cd * PursuitManager.WeaponCooldownSeconds)} s" : $"USE {WeaponName(weapon).ToUpperInvariant()}";
                if (weaponButtonText.text != w) weaponButtonText.text = w;
            }

            bool showShadow = session.Data.hasShadowPass && !pursuit && (ShadowPassSequence.Instance == null || !ShadowPassSequence.Instance.Playing)
                              && (MissionRunner.Instance == null || MissionRunner.Instance.State == MissionState.Idle);
            if (shadowButtonRoot.activeSelf != showShadow) shadowButtonRoot.SetActive(showShadow);
        }

        private static string WeaponName(string id) { foreach (var w in PursuitManager.Weapons) if (w.id == id) return w.name; return id; }

        private void UseWeapon()
        {
            var s = GameSession.Instance;
            if (PursuitManager.Instance != null && !string.IsNullOrEmpty(s.Data.equippedWeapon)) { if (!PursuitManager.Instance.UseWeapon(s.Data.equippedWeapon)) ShowToast("Nothing in range / still recharging"); }
        }

        public void PlayShadowPass()
        {
            if (CurrentRunnerBusy()) { ShowToast("Finish the current mission first"); return; }
            if (!GameSession.Instance.Data.hasShadowPass) { ShowToast("You have not earned the Shadow Pass yet"); return; }
            ShadowPassSequence.Ensure().Play(safe, font, rounded);
        }

        private static bool CurrentRunnerBusy() { var r = MissionRunner.Instance; return r != null && r.State != MissionState.Idle && r.State != MissionState.Finished; }

        // =============================================================================================== vehicles + weapon shop
        // =============================================================================================== showroom (vehicles) with a live 3D preview
        public int showroomIndex = -1;

        private RectTransform MakePreview(Transform parent, Vector2 pos, Vector2 size, VehicleSpec spec, string hex, int kitStage, int suspensionLevel)
        {
            var frame = Box(parent, "PreviewFrame", new Vector2(0, 1), new Vector2(0, 1), pos, size + new Vector2(8, 8), new Color(1, 1, 1, 0.10f));
            var go = new GameObject("CarPreviewImage", typeof(RectTransform), typeof(RawImage), typeof(PreviewImage));
            go.transform.SetParent(frame, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
            var prev = CarPreview.Ensure(); prev.Show(spec, hex, kitStage, suspensionLevel);
            go.GetComponent<RawImage>().texture = prev.Texture;
            return frame;
        }

        private void StatBar(Transform p, string name, float value01, Vector2 pos, float width)
        {
            Label(p, name, 20, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(110, 26), FontStyle.Bold);
            Box(p, "BarBg", new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(112, -6), new Vector2(width, 14), new Color(1, 1, 1, 0.14f));
            Box(p, "BarFill", new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(112, -6), new Vector2(Mathf.Max(6f, width * Mathf.Clamp01(value01)), 14), Accent);
        }

        public void OpenVehicles()
        {
            var s = GameSession.Instance;
            var all = VehicleCatalog.All;
            if (showroomIndex < 0) { showroomIndex = 0; for (int i = 0; i < all.Count; i++) if (all[i].id == s.Data.selectedVehicleId) showroomIndex = i; }
            showroomIndex = Mathf.Clamp(showroomIndex, 0, all.Count - 1);
            var spec = all[showroomIndex];
            var ov = s.Data.FindVehicle(spec.id);
            bool owned = ov != null, selected = s.Data.selectedVehicleId == spec.id, unlocked = s.Career.Level >= spec.unlockLevel;
            RectTransform p = OpenModal("VEHICLE SHOWROOM", 1320, 740);
            Label(p, $"Wallet {GameSession.Money(s.Wallet.Balance)}    Level {s.Career.Level}", 26, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -76), new Vector2(700, 36));
            MakePreview(p, new Vector2(30, -118), new Vector2(640, 360), spec, owned ? ov.colorHex : spec.defaultPaint, owned ? VehicleTuning.VisualStage(ov) : 0, owned ? ov.GetUpgradeLevel(UpgradeType.Suspension) : 0);
            Btn(p, "<", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -270), new Vector2(64, 80), () => { showroomIndex = (showroomIndex + all.Count - 1) % all.Count; OpenVehicles(); }, new Color(0.08f, 0.10f, 0.15f, 0.88f), 40);
            Btn(p, ">", new Vector2(0, 1), new Vector2(0, 1), new Vector2(602, -270), new Vector2(64, 80), () => { showroomIndex = (showroomIndex + 1) % all.Count; OpenVehicles(); }, new Color(0.08f, 0.10f, 0.15f, 0.88f), 40);
            Label(p, spec.name, 40, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -500), new Vector2(640, 50), FontStyle.Bold);
            Label(p, $"{spec.className}   -   {(spec.drive == DriveLayout.Rear ? "RWD" : spec.drive == DriveLayout.Front ? "FWD" : "AWD")}   -   TIER {spec.tier}", 22, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -548), new Vector2(640, 30));
            string cost = owned ? "OWNED" : unlocked ? GameSession.Money(spec.price) : $"Unlocks at level {spec.unlockLevel}";
            Label(p, cost, 30, owned ? Good : unlocked ? Accent : Bad, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -582), new Vector2(640, 40), FontStyle.Bold);
            float[] r = VehicleTuning.Ratings(spec);
            StatBar(p, "SPEED", r[0], new Vector2(34, -640), 160); StatBar(p, "POWER", r[1], new Vector2(34, -672), 160);
            StatBar(p, "BRAKES", r[2], new Vector2(350, -640), 160); StatBar(p, "GRIP", r[3], new Vector2(350, -672), 160);
            Label(p, "DRIVE FEEL: " + spec.feel, 19, new Color(1f, 0.88f, 0.55f), TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -700), new Vector2(640, 44));

            float y = -118;
            for (int i = 0; i < all.Count; i++)
            {
                int idx = i; var sp = all[i];
                bool o2 = s.Data.FindVehicle(sp.id) != null, u2 = s.Career.Level >= sp.unlockLevel;
                RectTransform row = Box(p, "Veh_" + sp.id, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, y), new Vector2(590, 58), new Color(1, 1, 1, idx == showroomIndex ? 0.16f : 0.05f));
                var b = row.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => { showroomIndex = idx; OpenVehicles(); });
                Label(row, sp.name, 26, idx == showroomIndex ? Accent : Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(300, 40), FontStyle.Bold);
                Label(row, $"T{sp.tier}   " + (s.Data.selectedVehicleId == sp.id ? "SELECTED" : o2 ? "OWNED" : u2 ? GameSession.Money(sp.price) : $"LEVEL {sp.unlockLevel}"), 20, o2 ? Good : u2 ? Muted : Bad,
                      TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(250, 36));
                y -= 66;
            }

            string label = selected ? "SELECTED" : owned ? "SELECT THIS CAR" : !unlocked ? $"LOCKED - LEVEL {spec.unlockLevel}" : $"BUY  {GameSession.Money(spec.price)}";
            Btn(p, label, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 28), new Vector2(590, 72), () =>
            {
                if (!owned)
                {
                    if (!unlocked) { ShowToast($"Reach Level {spec.unlockLevel} to unlock the {spec.name}"); return; }
                    if (!s.TryBuyVehicle(spec.id, out string why)) { ShowToast(why); return; }
                }
                s.Data.selectedVehicleId = spec.id; s.Persist();
                VehicleTuning.ApplyToCurrentCar(); paintApplied = false; ShowToast(spec.name + " selected");
                OpenVehicles();
            }, selected ? new Color(0.2f, 0.4f, 0.25f) : owned || unlocked ? Accent : new Color(0.22f, 0.24f, 0.27f), 30);
        }

        public void OpenWeaponShop()
        {
            var s = GameSession.Instance;
            RectTransform p = OpenModal("WEAPON SHOP", 980, 520);
            if (!s.Data.weaponShopUnlocked)
            {
                Label(p, "The weapon shop opens after you complete Level 5.", 30, Muted, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 80));
                return;
            }
            Label(p, $"Wallet {GameSession.Money(s.Wallet.Balance)}   Weapons are optional and only work during a chase.", 24, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -76), new Vector2(900, 36));
            float y = -130;
            foreach (var w in PursuitManager.Weapons)
            {
                var weapon = w;
                bool owned = s.Data.ownedWeapons.Contains(weapon.id), equipped = s.Data.equippedWeapon == weapon.id;
                RectTransform row = Box(p, "Wpn_" + weapon.id, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(910, 100), new Color(1, 1, 1, 0.06f));
                Label(row, weapon.name, 30, equipped ? Accent : Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -10), new Vector2(500, 40), FontStyle.Bold);
                Label(row, weapon.blurb, 22, Muted, TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 8), new Vector2(560, 44));
                Btn(row, equipped ? "EQUIPPED" : owned ? "EQUIP" : GameSession.Money(weapon.price), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(230, 60), () =>
                {
                    if (!owned && !s.TryBuyWeapon(weapon.id, out string why)) { ShowToast(why); return; }
                    s.Data.equippedWeapon = weapon.id; s.Persist(); OpenWeaponShop();
                }, equipped ? new Color(0.2f, 0.4f, 0.25f) : Accent, 24);
                y -= 116;
            }
        }


        // =============================================================================================== HUD skin (reference-style layout)
        private static Sprite circleSprite;
        private static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int S = 256; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S]; float c = S * 0.5f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)); px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt((c - d) * 3f), 0, 255)); }
            tex.SetPixels32(px); tex.Apply();
            return circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D arcTexture;
        private static Texture2D ArcTexture()
        {
            if (arcTexture != null) return arcTexture;
            const int W = 560, H = 300; var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H]; Vector2 centre = new Vector2(W * 0.5f, 40f);
            const float a0 = 200f, a1 = -20f;                                     // arc from the lower left over the top to the lower right
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - centre; float r = d.magnitude;
                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; if (ang < -90f) ang += 360f;
                    float a = 0f; Color col = Color.white;
                    bool inArc = ang <= a0 && ang >= a1;
                    if (inArc && r > 238f && r < 252f) { a = 0.85f; col = new Color(0.25f, 0.78f, 1f); float t = Mathf.InverseLerp(a0, a1, ang); if (t > 0.72f) col = new Color(0.95f, 0.25f, 0.2f); }
                    if (inArc && r > 214f && r <= 238f)
                    {
                        float t = Mathf.InverseLerp(a0, a1, ang) * 7f; float frac = Mathf.Abs(t - Mathf.Round(t));
                        if (frac * (a0 - a1) / 7f < 0.9f) { a = 0.9f; col = Mathf.Round(t) >= 6 ? new Color(0.95f, 0.25f, 0.2f) : Color.white; }                // major tick marks
                    }
                    px[y * W + x] = new Color(col.r, col.g, col.b, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return arcTexture = tex;
        }


        // =============================================================================================== button styling (reference-style)
        private Image IconButton(string name, HudIcons.Kind kind, Vector2 pos, float size, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(safe, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(1, 1); r.anchoredPosition = pos; r.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>(); img.sprite = HudIcons.Disc(kind); img.color = Color.white;
            if (onClick != null) { var b = go.AddComponent<Button>(); b.targetGraphic = img; PressTint(b); b.onClick.AddListener(() => { GameAudio.PlayClick(); onClick(); }); }
            else img.raycastTarget = false;
            return img;
        }

        // every round icon visibly reacts to a press (blue tint + slight shrink) so it is obvious that it was hit
        private static void PressTint(Button b)
        {
            b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = Color.white; cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.42f, 0.74f, 1f, 1f); cb.colorMultiplier = 1f; cb.fadeDuration = 0.05f; b.colors = cb;
        }

        private static void SetTint(TouchControlsPanel panel, string field, Color c)
        {
            var f = typeof(TouchControlsPanel).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(panel, c);
        }

        private void Skin(string name, Sprite sprite, bool hideChildren, Vector2? pos = null, float? size = null)
        {
            var rt = FindScene(name, transform); if (rt == null) return;
            var img = rt.GetComponent<Image>();
            if (img != null) { img.sprite = sprite; img.type = Image.Type.Simple; img.preserveAspect = false; img.color = Color.white; }
            if (pos.HasValue) rt.anchoredPosition = pos.Value;
            if (size.HasValue) rt.sizeDelta = new Vector2(size.Value, size.Value);
            foreach (Transform c in rt)
            {
                if (hideChildren) { c.gameObject.SetActive(false); continue; }
                var t = c.GetComponent<Text>();
                if (t != null) { t.fontSize = 24; t.color = new Color(0.78f, 0.8f, 0.85f, 0.9f); t.alignment = TextAnchor.LowerCenter; t.fontStyle = FontStyle.Bold; }
            }
        }

        // Chevron steering arrows, ridged grip-pad pedals and round icon buttons, like the reference HUD.
        private void RestyleButtons()
        {
            var panel = TouchControlsPanel.Instance;
            if (panel != null) { SetTint(panel, "idleTint", new Color(1f, 1f, 1f, 0.96f)); SetTint(panel, "pressedTint", new Color(0.70f, 0.78f, 0.92f, 1f)); }
            Skin("SteerLeft", HudIcons.ChevronLeft, true);
            Skin("SteerRight", HudIcons.ChevronRight, true);
            Skin("Accelerator", HudIcons.PedalMetalTall, true); Skin("Brake", HudIcons.PedalMetalShort, true);
            Place(FindScene("Accelerator", transform), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 36), new Vector2(190, 320));
            Place(FindScene("Brake", transform), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-250, 36), new Vector2(190, 226));
            var oldRev = FindScene("Reverse", transform); if (oldRev != null) oldRev.gameObject.SetActive(false);        // reverse is now the R position of the gear selector
            Skin("CameraReset", HudIcons.Disc(HudIcons.Kind.Camera), true, new Vector2(-24, -230), 66);
            Skin("MapButton", HudIcons.Disc(HudIcons.Kind.Pin), true, new Vector2(-24, -332), 72);
            var map = FindScene("MapButton", transform); if (map != null) { var mi = map.GetComponent<Image>(); if (mi != null) mi.color = Color.white; }
            foreach (string n in new[] { "MapButton", "CameraReset" }) { var rt = FindScene(n, transform); var btn = rt != null ? rt.GetComponent<Button>() : null; if (btn != null) PressTint(btn); }
        }

        private static RectTransform FindScene(string name, Transform notUnder)
        {
            foreach (var rt in FindObjectsByType<RectTransform>(FindObjectsInactive.Include)) if (rt.name == name && rt.root != notUnder.root) return rt;
            return null;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2? size = null)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; if (size.HasValue) rt.sizeDelta = size.Value;
        }

        // Moves the scene-built HUD pieces to the reference layout: minimap top-left, map/camera buttons stacked on the right,
        // GPS banner shifted right of the mission banner, and the old condition read-out removed (the new Health bar replaces it).
        private void RelayoutSceneHud()
        {
            Place(FindScene("Minimap", transform), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(270, 270));
            Place(FindScene("MapButton", transform), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -332));
            Place(FindScene("CameraReset", transform), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -230));
            var nav = FindScene("NavigationBanner", transform);
            if (nav != null)
            {
                var panel = nav.Find("Panel") as RectTransform;
                Place(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, -30), new Vector2(620, 112));     // right of the radar, top edge level with the radar
                var pimg = panel != null ? panel.GetComponent<Image>() : null;
                if (pimg != null) { pimg.sprite = rounded; pimg.type = Image.Type.Sliced; pimg.color = Panel; Rim(panel); }
                var arrowRt = panel != null ? panel.Find("TurnArrow") as RectTransform : null;
                if (arrowRt != null) { arrowRt.pivot = new Vector2(0.5f, 0.5f); arrowRt.anchoredPosition = new Vector2(66, 0); arrowRt.sizeDelta = new Vector2(66, 66); var ai = arrowRt.GetComponent<Image>(); if (ai != null) ai.color = HudCyan; }
                if (panel != null) foreach (string t in new[] { "Instruction", "Detail" }) {
                    var tr = panel.Find(t) as RectTransform; if (tr == null) continue;
                    tr.sizeDelta = new Vector2(480, tr.sizeDelta.y);
                    var tx = tr.GetComponent<Text>(); if (tx == null) continue;
                    tr.anchoredPosition = new Vector2(120, t == "Instruction" ? 17f : -25f); tr.sizeDelta = new Vector2(480, 46);
                    if (t == "Instruction") { tx.fontSize = 36; tx.fontStyle = FontStyle.Bold; tx.color = Color.white; }
                    else { tx.fontSize = 28; tx.fontStyle = FontStyle.Bold; tx.color = new Color(0.22f, 0.74f, 1f); }
                    UiTheme.Apply(tx);
                }
            }
            foreach (string n in new[] { "ConditionText", "ConditionBackground", "Title", "Hints" }) { var rt = FindScene(n, transform); if (rt != null) rt.gameObject.SetActive(false); }
        }

        private void BuildSkin(RectTransform status)
        {
            RelayoutSceneHud();
            RestyleButtons();
            // fuel + health bars inside the status panel
            fuelLabel = Label(status, "FUEL", 20, Color.white, TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 36), new Vector2(180, 30), FontStyle.Bold);
            Label(status, "HEALTH", 20, Color.white, TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(226, 36), new Vector2(170, 30), FontStyle.Bold);
            foreach (var (x, name) in new[] { (24f, "Fuel"), (226f, "Health") })
            {
                RectTransform bar = Box(status, name + "Bar", new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 16), new Vector2(170, 14), new Color(1, 1, 1, 0.15f));
                RectTransform fill = Box(bar, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(170, 14), name == "Fuel" ? HudCyan : Good);
                var img = fill.GetComponent<Image>(); img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillAmount = 1f;
                fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
                if (name == "Fuel") fuelFill = img; else healthFill = img;
            }

            foreach (var (pedal, text) in new[] { ("Accelerator", "ACCEL"), ("Brake", "BRAKE") })
            {
                var prt = FindScene(pedal, transform); if (prt == null) continue;
                var tag = Box(prt, "PedalTag", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(136, 46), new Color(0.05f, 0.07f, 0.11f, 0.92f)); tag.GetComponent<Image>().raycastTarget = false;
                Label(tag, text, 26, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(136, 46), FontStyle.Bold);
            }

            // refuel / tow button
            var fb = Btn(safe, "REFUEL", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -482), new Vector2(478, 66), RefuelOrTow, new Color(0.2f, 0.45f, 0.65f, 1f), 26);
            fuelButtonRoot = fb.gameObject; fuelButtonText = fb.GetComponentInChildren<Text>(); fuelButtonRoot.SetActive(false);

            // sky chip: read-only "DAY - CLEAR" (the world changes by itself as jobs start and end)
            var chip = Box(safe, "SkyChip", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -178), new Vector2(236, 42), Panel); Rim(chip);
            skyChipText = Label(chip, "", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(232, 40), FontStyle.Bold);

            // speedometer arc with rpm needle, gear and big speed (replaces the old panel)
            var mini = FindFirstObjectByType<MinimapView>();
            legacyHud = FindFirstObjectByType<VehicleHUD>();
            if (legacyHud != null)
            {
                var f = typeof(VehicleHUD).GetField("speedText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var st = f != null ? f.GetValue(legacyHud) as Text : null;
                if (st != null && st.transform.parent != null && st.transform.parent.GetComponent<Canvas>() == null) st.transform.parent.gameObject.SetActive(false);
            }
            var arcGo = new GameObject("Speedo", typeof(RectTransform), typeof(RawImage)); arcGo.transform.SetParent(safe, false);
            var arcRt = (RectTransform)arcGo.transform; arcRt.anchorMin = arcRt.anchorMax = new Vector2(0.5f, 0); arcRt.pivot = new Vector2(0.5f, 0); arcRt.anchoredPosition = new Vector2(0, 6); arcRt.sizeDelta = new Vector2(560, 300);
            var arcImg = arcGo.GetComponent<RawImage>(); arcImg.texture = ArcTexture(); arcImg.raycastTarget = false;
            arcRt.localScale = Vector3.one * 0.8f;                                   // a lower, slimmer gauge so the car stays clear of it
            for (int i = 0; i <= 7; i++)                                                                                   // rpm x1000 numbers
            {
                float ang = Mathf.Lerp(200f, -20f, i / 7f) * Mathf.Deg2Rad;
                Label(arcRt, i.ToString(), 28, i >= 6 ? Bad : Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Cos(ang) * 190f, 40f + Mathf.Sin(ang) * 190f), new Vector2(40, 40), FontStyle.Bold);
            }
            var needleGo = new GameObject("Needle", typeof(RectTransform), typeof(Image)); needleGo.transform.SetParent(arcRt, false);
            needle = (RectTransform)needleGo.transform; needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0); needle.pivot = new Vector2(0.5f, -3.9f); needle.anchoredPosition = new Vector2(0, 40); needle.sizeDelta = new Vector2(9, 54);
            needleGo.GetComponent<Image>().color = new Color(1f, 0.97f, 0.85f); needleGo.GetComponent<Image>().raycastTarget = false;
            speedValueText = Label(arcRt, "0", 104, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(300, 124), FontStyle.Normal); speedValueText.font = UiTheme.Italic; speedValueText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            Label(arcRt, "km/h", 30, new Color(0.8f, 0.86f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(200, 40), FontStyle.Bold);
            gearValueText = Label(arcRt, "N", 56, HudCyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 46), new Vector2(100, 76), FontStyle.Bold);

            // drift readout: appears while the car slides, banks "+score" when the slide ends
            driftRoot = new GameObject("DriftReadout", typeof(RectTransform), typeof(CanvasGroup)); driftRoot.transform.SetParent(safe, false);
            var drt = (RectTransform)driftRoot.transform; drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.74f); drt.sizeDelta = new Vector2(620, 150); drt.anchoredPosition = Vector2.zero;
            driftSmall = Label(drt, "DRIFT", 30, new Color(0.45f, 0.82f, 1f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(620, 40), FontStyle.Bold);
            driftBig = Label(drt, "0", 96, Accent, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(620, 110), FontStyle.Normal); driftBig.font = UiTheme.Display;
            driftBig.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.7f);
            driftGroup = driftRoot.GetComponent<CanvasGroup>(); driftGroup.alpha = 0f; driftGroup.blocksRaycasts = false; driftGroup.interactable = false;

            // minimap: round, ringed, with a compass N that rotates with the map
            if (mini != null)
            {
                var rt = (RectTransform)mini.transform;
                var bg = mini.GetComponent<Image>();
                if (bg != null) { bg.sprite = CircleSprite(); bg.type = Image.Type.Simple; bg.color = new Color(0.1f, 0.12f, 0.16f, 1f); }
                if (mini.GetComponent<Mask>() == null) { var mk = mini.gameObject.AddComponent<Mask>(); mk.showMaskGraphic = true; }
                var ring = new GameObject("MinimapRing", typeof(RectTransform), typeof(Image)); ring.transform.SetParent(rt.parent, false);
                var rr = (RectTransform)ring.transform; rr.anchorMin = rt.anchorMin; rr.anchorMax = rt.anchorMax; rr.pivot = rt.pivot; rr.anchoredPosition = rt.anchoredPosition; rr.sizeDelta = rt.sizeDelta + new Vector2(10, 10);
                var ri = ring.GetComponent<Image>(); ri.sprite = CircleSprite(); ri.color = new Color(0.22f, 0.74f, 1f, 1f); ri.raycastTarget = false;
                ring.transform.SetSiblingIndex(rt.GetSiblingIndex());                       // behind the map so only a thin ring shows
                var radarBtn = mini.GetComponent<Button>(); if (radarBtn == null) radarBtn = mini.gameObject.AddComponent<Button>();
                radarBtn.transition = Selectable.Transition.None; radarBtn.onClick.AddListener(() => { GameAudio.PlayClick(); var fm = FindFirstObjectByType<FullMapView>(); if (fm != null) fm.Toggle(); });
                var cf = typeof(MinimapView).GetField("mapContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var container = cf != null ? cf.GetValue(mini) as RectTransform : null;
                if (container != null) Label(container, "N", 26, new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, rt.rect.height * 0.5f - 18f), new Vector2(34, 34), FontStyle.Bold);
            }
        }

        private void RefuelOrTow()
        {
            var fs = FuelSystem.Instance; if (fs == null) return;
            if (fs.Empty && !fs.CanRefuel) { fs.Tow(); ShowToast("Towed to the pump: Rs " + (long)FuelSystem.TowCost + ", tank 25%"); return; }
            long paid = fs.Refuel();
            ShowToast(paid > 0 ? "Refuelled: " + GameSession.Money(paid) : "Not enough money to refuel");
        }

        private void UpdateSkin()
        {
            if (shadowGlow != null && shadowButtonRoot != null && shadowButtonRoot.activeSelf) { float pz = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f); shadowGlow.color = new Color(0.7f, 0.3f, 1f, 0.35f + 0.4f * pz); shadowGlow.rectTransform.localScale = Vector3.one * (0.9f + 0.18f * pz); }
            UpdateGearLetters();
            if (handbrakeImage != null && TouchControlsPanel.Instance != null) handbrakeImage.color = TouchControlsPanel.Instance.IsPressed((RectTransform)handbrakeImage.transform) ? new Color(0.42f, 0.74f, 1f, 1f) : Color.white;
            if (car != null && needle != null)
            {
                float rpm = GameAudio.Instance != null ? GameAudio.Instance.EngineRpm01 : Mathf.Clamp01(car.SpeedKmh / 140f);
                float ang = Mathf.Lerp(200f, -20f, Mathf.Clamp01(rpm));
                needle.localRotation = Quaternion.Euler(0f, 0f, ang - 90f);
                int speed = Mathf.RoundToInt(Mathf.Abs(car.SpeedKmh));
                string sv = speed.ToString(); if (speedValueText.text != sv) speedValueText.text = sv;
                string gv = car.GearLabel; if (gearValueText.text != gv) gearValueText.text = gv;
                if (driftGroup != null)
                {
                    int live = car.DriftScore;
                    if (live >= 40)
                    {
                        driftShowUntil = Time.unscaledTime + 0.5f; driftSmall.text = "DRIFT"; driftSmall.color = new Color(0.45f, 0.82f, 1f);
                        driftBig.text = live.ToString("N0"); driftBig.color = Accent; driftBig.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp01(car.DriftAmount) * 0.12f);
                        driftGroup.alpha = 1f;
                    }
                    else if (car.LastDriftTime > shownBank && Time.time - car.LastDriftTime < 2.4f)
                    {
                        shownBank = car.LastDriftTime; driftShowUntil = Time.unscaledTime + 2.2f;
                        driftSmall.text = "DRIFT BANKED"; driftSmall.color = Good; driftBig.text = "+" + car.LastDriftScore.ToString("N0"); driftBig.color = Good; driftBig.rectTransform.localScale = Vector3.one; driftGroup.alpha = 1f;
                    }
                    else driftGroup.alpha = Mathf.MoveTowards(driftGroup.alpha, Time.unscaledTime < driftShowUntil ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
                }
                healthFill.fillAmount = car.Condition01; healthFill.color = car.Condition01 > 0.6f ? Good : car.Condition01 > 0.3f ? Accent : Bad;
            }
            var fs = FuelSystem.Instance;
            if (fs != null && fuelFill != null)
            {
                fuelFill.fillAmount = fs.Fuel01; fuelFill.color = fs.Fuel01 > 0.25f ? new Color(0.35f, 0.7f, 0.95f) : Bad;
                string fl = $"FUEL {Mathf.RoundToInt(fs.Fuel01 * 100f)}%"; if (fuelLabel.text != fl) fuelLabel.text = fl;
                bool show = fs.Empty || (fs.CanRefuel && fs.Fuel01 < 0.97f);
                if (fuelButtonRoot.activeSelf != show) fuelButtonRoot.SetActive(show);
                if (show) { string t = fs.Empty && !fs.CanRefuel ? $"TOW  Rs {(long)FuelSystem.TowCost}" : $"REFUEL  Rs {fs.RefuelCost}"; if (fuelButtonText.text != t) fuelButtonText.text = t; }
            }
            var env = WorldEnvironment.Instance;
            if (env != null && skyChipText != null)
            {
                int sky = (int)env.Time01Preset, wx = (int)env.CurrentWeather;
                if (sky != shownSky || wx != shownWx)
                {
                    string[] tn = { "MORNING", "DAY", "SUNSET", "NIGHT" }, wn = { "CLEAR", "RAIN", "HEAVY RAIN", "FOG" };
                    skyChipText.text = tn[sky] + "  -  " + wn[wx];
                    if (shownSky != -1)
                    {
                        if (wx != shownWx && wx != 0) ShowToast(wx == 3 ? "Fog is rolling in. Slow down." : wx == 2 ? "Heavy rain. The road is slippery." : "It is starting to rain.");
                        else if (wx != shownWx) ShowToast("The weather is clearing up.");
                        else if (sky != shownSky) ShowToast(sky == 3 ? "Night falls over the city." : sky == 2 ? "The sun is going down." : sky == 0 ? "A new morning." : "Daytime.");
                    }
                    shownSky = sky; shownWx = wx;
                }
            }
        }

        // =============================================================================================== steering wheel
        private void BuildWheel()
        {
            var go = new GameObject("SteeringWheel", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(safe, false);
            wheel = (RectTransform)go.transform;
            wheel.anchorMin = wheel.anchorMax = new Vector2(0, 0); wheel.pivot = new Vector2(0.5f, 0.5f); wheel.anchoredPosition = new Vector2(230, 230); wheel.sizeDelta = new Vector2(380, 380);   // pivot at the centre so it spins in place
            var img = go.GetComponent<RawImage>(); img.texture = HudIcons.SteeringWheel.texture; img.raycastTarget = false;
            if (TouchControlsPanel.Instance != null) TouchControlsPanel.Instance.SetWheel(wheel);
            go.SetActive(GameSettings.Steering == SteeringMode.Wheel);
            BuildGearSelector();
        }

        // R | D switch above the BRAKE pedal (not above GAS, so a thumb on the accelerator never touches it). Tap R to reverse with
        // GAS, tap D to drive. The sliding knob sits under the chosen letter.
        private Text[] gearLetters;
        private static readonly int[] GearSlots = { TouchControlsPanel.GearReverse, TouchControlsPanel.GearDrive };
        private void BuildGearSelector()
        {
            var p = Box(safe, "GearSelector", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-250, 290), new Vector2(190, 84), new Color(0.04f, 0.06f, 0.1f, 0.88f)); Rim(p);
            var knob = Box(p, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(47.5f, 0), new Vector2(84, 68), HudCyan);
            Box(knob, "KnobInner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72, 56), new Color(0.06f, 0.2f, 0.32f, 1f));
            string[] g = { "R", "D" };
            gearLetters = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                gearLetters[i] = Label(p, g[i], 44, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -47.5f : 47.5f, 0), new Vector2(84, 68), FontStyle.Bold);
                gearLetters[i].raycastTarget = false;
            }
            if (TouchControlsPanel.Instance != null) TouchControlsPanel.Instance.SetGear(p, knob);
        }

        private void AddBtnIcon(Button b, Sprite icon)
        {
            var im = CityDriver.UI.ThreatMarkers.NewImage(b.transform, "Icon", icon, new Vector2(40, 40));
            im.rectTransform.anchorMin = im.rectTransform.anchorMax = new Vector2(0, 0.5f); im.rectTransform.anchoredPosition = new Vector2(34, 0); im.color = new Color(0.78f, 0.9f, 1f);
            var t = b.GetComponentInChildren<Text>(); if (t != null) t.rectTransform.anchoredPosition = new Vector2(22, 0);
        }

        // thin cyan edge that makes a panel read as glass (an Outline effect on the rounded panel image)
        private static void Rim(RectTransform panel)
        {
            var img = panel != null ? panel.GetComponent<Image>() : null; if (img == null) return;
            var o = img.gameObject.GetComponent<Outline>(); if (o == null) o = img.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.22f, 0.74f, 1f, 0.38f); o.effectDistance = new Vector2(1.6f, -1.6f);
        }

        private void UpdateGearLetters()
        {
            var panel = TouchControlsPanel.Instance; if (gearLetters == null || panel == null) return;
            for (int i = 0; i < gearLetters.Length; i++)
            {
                bool on = panel.Gear == GearSlots[i]; Color c = !on ? new Color(0.62f, 0.66f, 0.74f, 0.9f) : GearSlots[i] == TouchControlsPanel.GearDrive ? Color.white : new Color(1f, 0.74f, 0.22f);
                if (gearLetters[i].color != c) gearLetters[i].color = c;
            }
        }

        // =============================================================================================== radio + settings
        private void CycleRadio()
        {
            GameSettings.Radio = (GameSettings.Radio + 1) % 4;
            ShowToast("Radio: " + GameSettings.RadioNames[GameSettings.Radio]);
        }

        private void RefreshRadioLabel()
        {
            if (radioButtonText != null) radioButtonText.text = "RADIO: " + (GameSettings.Radio == 0 ? "OFF" : GameSettings.RadioNames[GameSettings.Radio].ToUpperInvariant());
        }

        private void OnSettingsChanged()
        {
            RefreshRadioLabel();
            if (shownCamera != -2 && shownCamera != GameSettings.CameraView) ShowToast("Camera: " + GameSettings.CameraNames[GameSettings.CameraView]);
            shownCamera = GameSettings.CameraView;
        }

        private static int Wrap(int v, int count) => (v % count + count) % count;
        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

        // =============================================================================================== settings (tabs, sliders, switches)
        private static readonly string[] SettingsTabs = { "CONTROLS", "AUDIO", "GAMEPLAY", "GRAPHICS" };
        private int settingsTab;
        private static readonly Color SegOff = new Color(0.13f, 0.16f, 0.24f, 1f);

        public void OpenSettings() { OpenSettingsTab(-1); }

        private static string SteerHint() => GameSettings.Steering == SteeringMode.Tilt ? "Tilt: roll the phone like a steering wheel." : GameSettings.Steering == SteeringMode.Wheel ? "Wheel: drag around the on-screen wheel; it springs back when you let go." : "Buttons: hold the left and right arrows.";

        private static void SegStyle(Image img, Text t, bool on) { img.color = on ? Accent : SegOff; t.color = on ? new Color(0.1f, 0.1f, 0.1f) : Color.white; }

        private void Segmented(RectTransform card, string[] names, int selected, Action<int> set, float totalW = 520f)
        {
            int n = names.Length; const float gapS = 6f; float sw = (totalW - gapS * (n - 1)) / n;
            var imgs = new Image[n]; var txts = new Text[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var b = Btn(card, names[i], new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24 - (n - 1 - i) * (sw + gapS), 0), new Vector2(sw, 58), () => { set(idx); for (int j = 0; j < n; j++) SegStyle(imgs[j], txts[j], j == idx); }, SegOff, n > 4 ? 19 : 22);
                imgs[i] = b.GetComponent<Image>(); txts[i] = b.GetComponentInChildren<Text>(); SegStyle(imgs[i], txts[i], i == selected);
            }
        }

        private void SliderRow(RectTransform card, float raw, float min, float max, Func<float, string> fmt, Action<float> set)
        {
            const float W = 380f;
            var val = Label(card, fmt(raw), 30, Accent, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(104, 44), FontStyle.Bold);
            var hit = Box(card, "SliderHit", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-142, 0), new Vector2(W, 64), new Color(0, 0, 0, 0.004f), false);
            Box(hit, "Groove", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(W, 16), new Color(0.03f, 0.04f, 0.07f, 1f)).GetComponent<Image>().raycastTarget = false;
            var fill = Box(hit, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(20, 16), Accent); fill.GetComponent<Image>().raycastTarget = false;
            var knob = CityDriver.UI.ThreatMarkers.NewImage(hit, "Knob", CityDriver.UI.ThreatMarkers.Dot, new Vector2(50, 50)); knob.color = Color.white;
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0, 0.5f);
            float last = float.NaN;
            void Apply(float k)
            {
                float x = k * W; fill.sizeDelta = new Vector2(Mathf.Max(20f, x), 16f); knob.rectTransform.anchoredPosition = new Vector2(x, 0f); val.text = fmt(Mathf.Lerp(min, max, k));
            }
            Apply(Mathf.InverseLerp(min, max, raw));
            var sl = hit.gameObject.AddComponent<UiSlider>();
            sl.onValue = k =>
            {
                float step = (max - min) / 20f, v = min + Mathf.Round(Mathf.Lerp(min, max, k) / step - min / step) * step; v = Mathf.Clamp(v, min, max);
                if (!Mathf.Approximately(v, last)) { if (!float.IsNaN(last)) GameAudio.PlayClick(); last = v; set(v); }
                Apply(Mathf.InverseLerp(min, max, v));
            };
        }

        private void SwitchRow(RectTransform card, bool state, Action<bool> set)
        {
            var track = Box(card, "Switch", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(108, 54), new Color(0.22f, 0.25f, 0.33f));
            var knob = CityDriver.UI.ThreatMarkers.NewImage(track, "Knob", CityDriver.UI.ThreatMarkers.Dot, new Vector2(46, 46)); knob.color = Color.white;
            var anim = track.gameObject.AddComponent<SwitchAnim>(); anim.knob = knob.rectTransform; anim.track = track.GetComponent<Image>(); anim.travel = 25f; anim.SnapTo(state);
            var stateLabel = Label(card, state ? "ON" : "OFF", 26, state ? Accent : Muted, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-146, 0), new Vector2(90, 40), FontStyle.Bold);
            track.gameObject.AddComponent<PressFx>();
            var btn = track.gameObject.AddComponent<Button>(); btn.targetGraphic = track.GetComponent<Image>(); btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                state = !state; set(state); anim.on = state; stateLabel.text = state ? "ON" : "OFF"; stateLabel.color = state ? Accent : Muted; GameAudio.PlayClick();
            });
        }

        public void OpenSettingsTab(int tab)
        {
            if (tab >= 0) settingsTab = tab;
            RectTransform p = OpenModal("SETTINGS", 1240, 820);
            bool reopen = lastModalReopen;

            Box(p, "Side", new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -98), new Vector2(284, 612), new Color(0f, 0f, 0f, 0.24f));
            for (int i = 0; i < SettingsTabs.Length; i++)
            {
                int idx = i;
                Btn(p, SettingsTabs[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -116 - i * 88), new Vector2(256, 74), () => OpenSettingsTab(idx), i == settingsTab ? Accent : SegOff, 30);
            }
            Label(p, "CITY DRIVER: UNDERCOVER  v" + Application.version, 18, Muted, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 28), new Vector2(380, 30));

            float y = -108f; int rowIndex = 0;
            const float rowH = 92f, gap = 12f, cx = 318f, cw = 906f;
            RectTransform Row(string name, string desc)
            {
                var card = Box(p, "Row", new Vector2(0, 1), new Vector2(0, 1), new Vector2(cx, y), new Vector2(cw, rowH), new Color(1f, 1f, 1f, 0.055f));
                Label(card, name, 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, desc.Length > 0 ? 15 : 0), new Vector2(340, 40), FontStyle.Bold);
                if (desc.Length > 0) Label(card, desc, 19, Muted, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, -21), new Vector2(340, 34));
                y -= rowH + gap;
                Entrance.Add(card, (reopen ? 0.02f : 0.16f) + rowIndex++ * 0.05f, new Vector2(26f, 0f), 0.26f);
                return card;
            }

            Text hint = null;
            switch (settingsTab)
            {
                case 0:
                {
                    var modes = new[] { "Buttons", "Wheel", "Tilt" };
                    Segmented(Row("STEERING", "How you turn the car"), modes, (int)GameSettings.Steering, i => { GameSettings.Steering = (SteeringMode)i; if (hint != null) hint.text = SteerHint(); }, 440f);
                    SliderRow(Row("SENSITIVITY", "Steering response"), GameSettings.SteerSensitivity, 0.5f, 1.5f, v => Mathf.RoundToInt(v * 100f) + "%", v => GameSettings.SteerSensitivity = v);
                    Segmented(Row("CAMERA", "Default driving view"), GameSettings.CameraNames, GameSettings.CameraView, i => GameSettings.CameraView = i);
                    hint = Label(p, SteerHint(), 22, Muted, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(cx + 8, y + 2), new Vector2(880, 40));
                    break;
                }
                case 1:
                {
                    SliderRow(Row("MASTER VOLUME", "Everything"), GameSettings.Master, 0f, 1f, Pct, v => GameSettings.Master = v);
                    SliderRow(Row("MUSIC", "Radio and menu music"), GameSettings.Music, 0f, 1f, Pct, v => GameSettings.Music = v);
                    SliderRow(Row("EFFECTS", "Engine, horn, crashes"), GameSettings.Sfx, 0f, 1f, Pct, v => GameSettings.Sfx = v);
                    Segmented(Row("RADIO", "Station while driving"), GameSettings.RadioNames, GameSettings.Radio, i => GameSettings.Radio = i);
                    SwitchRow(Row("SUBTITLES", "Show dialogue text"), GameSettings.Subtitles, v => GameSettings.Subtitles = v);
                    SwitchRow(Row("VOICE", "Device speech for dialogue"), GameSettings.Voice, v => GameSettings.Voice = v);
                    break;
                }
                case 2:
                {
                    Segmented(Row("TRIP LENGTH", "Distance of fares"), GameSettings.TripNames, GameSettings.TripLength, i => GameSettings.TripLength = i, 440f);
                    SwitchRow(Row("JOB OFFERS", "New fares pop up while you drive"), GameSettings.JobOffers, v => GameSettings.JobOffers = v);
                    SwitchRow(Row("DRIVING TIPS", "Short hints, each shown once"), GameSettings.Tips, v => GameSettings.Tips = v);
                    var sky = Row("TIME AND WEATHER", "Automatic: they change as jobs start and end");
                    Label(sky, "Automatic", 28, Accent, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(300, 44), FontStyle.Bold);
                    var lang = Row("LANGUAGE", "More coming soon");
                    Label(lang, "English", 28, Accent, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(300, 44), FontStyle.Bold);
                    break;
                }
                default:
                {
                    int q = GameSettings.Quality < 0 ? QualitySettings.GetQualityLevel() : GameSettings.Quality;
                    string[] qn = QualitySettings.names;
                    if (qn.Length == 0) qn = new[] { "Default" };
                    Segmented(Row("GRAPHICS", "Lower it if the game feels slow"), qn, Mathf.Clamp(q, 0, qn.Length - 1), i => GameSettings.Quality = i);
                    var info = Row("PERFORMANCE", "Tips for smooth driving");
                    Label(info, "Close other apps  -  use Low on older phones", 22, Muted, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(520, 40));
                    break;
                }
            }

            Btn(p, "RESET DEFAULTS", new Vector2(0, 0), new Vector2(0, 0), new Vector2(cx, 22), new Vector2(300, 58), () => { GameSettings.ResetToDefaults(); OpenSettingsTab(-1); }, new Color(0.3f, 0.16f, 0.16f), 22);
            Btn(p, "DONE", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 22), new Vector2(260, 62), () => CloseModal(), Accent, 30);
        }

        public void OpenWallet()
        {
            var s = GameSession.Instance;
            RectTransform p = OpenModal("WALLET", 900, 640);
            Label(p, GameSession.Money(s.Wallet.Balance), 56, s.Wallet.IsInDebt ? Bad : Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -86), new Vector2(600, 70), FontStyle.Bold);
            Label(p, s.Wallet.IsInDebt ? "In debt: keep driving normal fares to recover." : "Recent transactions", 24, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -160), new Vector2(800, 34));
            var tx = s.Data.transactions;
            float y = -204;
            for (int i = tx.Count - 1, n = 0; i >= 0 && n < 9; i--, n++)
            {
                Label(p, tx[i].reason, 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(520, 36));
                Label(p, GameSession.Money(tx[i].amount), 24, tx[i].amount >= 0 ? Good : Bad, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, y), new Vector2(260, 36));
                y -= 40;
            }
            if (tx.Count == 0) Label(p, "No transactions yet.", 24, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -204), new Vector2(500, 36));
        }

        private string pendingPaintHex = "";

        public void OpenGarage()
        {
            var s = GameSession.Instance;
            OwnedVehicleData v = s.SelectedVehicle;
            var spec = VehicleCatalog.Find(v.vehicleId);
            RectTransform p = OpenModal("GARAGE  -  " + spec.name, 1320, 740);
            Label(p, $"Wallet {GameSession.Money(s.Wallet.Balance)}    Condition {Mathf.RoundToInt(v.condition01 * 100f)}%", 26, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -76), new Vector2(700, 36));

            int stage = VehicleTuning.VisualStage(v);
            MakePreview(p, new Vector2(30, -118), new Vector2(560, 315), spec, string.IsNullOrEmpty(pendingPaintHex) ? v.colorHex : pendingPaintHex, stage, v.GetUpgradeLevel(UpgradeType.Suspension));
            Label(p, stage == 0 ? "Stock look. Upgrades add a body kit and a lower stance." : $"Body kit stage {stage} / 3  -  drag the car to turn it", 20, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -446), new Vector2(560, 28));

            // paint swatches (tap to preview, then equip / buy)
            Label(p, s.Career.Level >= 2 ? "PAINT" : "PAINT  (new colours unlock at Level 2)", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -486), new Vector2(560, 32), FontStyle.Bold);
            string shownHex = string.IsNullOrEmpty(pendingPaintHex) ? v.colorHex : pendingPaintHex;
            for (int i = 0; i < Paints.Length; i++)
            {
                var pt = Paints[i];
                ColorUtility.TryParseHtmlString(pt.hex, out Color c);
                bool isShown = string.Equals(shownHex, pt.hex, StringComparison.OrdinalIgnoreCase);
                if (isShown) Box(p, "SwatchRing", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30 + i * 88, -520), new Vector2(76, 76), Accent);
                var local = pt;
                Btn(p, "", new Vector2(0, 1), new Vector2(0, 1), new Vector2(36 + i * 88, -526), new Vector2(64, 64), () => { pendingPaintHex = local.hex; OpenGarage(); }, c, 20);
            }
            var shown = Array.Find(Paints, x => string.Equals(x.hex, shownHex, StringComparison.OrdinalIgnoreCase));
            bool owns = s.Data.ownedColors.Contains(shownHex), canBuy = s.Career.Level >= 2 || shown.price == 0, equipped = string.Equals(v.colorHex, shownHex, StringComparison.OrdinalIgnoreCase);
            Label(p, shown.name ?? "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -610), new Vector2(300, 36), FontStyle.Bold);
            Btn(p, equipped ? "EQUIPPED" : owns ? "EQUIP" : canBuy ? "BUY  " + GameSession.Money(shown.price) : "LOCKED", new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, -606), new Vector2(250, 48), () =>
            {
                if (equipped) return;
                if (!s.Data.ownedColors.Contains(shownHex))
                {
                    if (!canBuy) { ShowToast("Reach Level 2 to unlock new colours"); return; }
                    if (!s.Wallet.TrySpend($"paint:{s.Data.selectedVehicleId}:{shownHex}", shown.price, "Paint: " + shown.name)) { ShowToast("Not enough money"); return; }
                    s.Data.ownedColors.Add(shownHex);
                }
                v.colorHex = shownHex; pendingPaintHex = ""; s.Persist(); ApplyPaint(shownHex); OpenGarage();
            }, equipped ? new Color(0.2f, 0.4f, 0.25f) : Accent, 22);
            Btn(p, $"REPAIR  {GameSession.Money(GarageService.RepairCost(v.condition01))}", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 24), new Vector2(330, 56), () =>
            {
                if (s.Garage.TryRepair(s.Data.selectedVehicleId, out string why)) { s.Persist(); ShowToast("Repaired"); OpenGarage(); } else ShowToast(why ?? "Cannot repair");
            }, null, 24);

            // upgrades: 3 stages per part
            float y = -118;
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                int lvl = Mathf.Clamp(v.GetUpgradeLevel(type), 0, GarageService.MaxUpgradeLevel);
                bool max = lvl >= GarageService.MaxUpgradeLevel;
                long cost = max ? 0 : GarageService.UpgradeCost(type, lvl + 1, spec.price);
                int gate = max ? 0 : GarageService.GateLevel(spec.unlockLevel, lvl + 1);
                bool gated = !max && s.Career.Level < gate;
                Box(p, "UpRow", new Vector2(0, 1), new Vector2(0, 1), new Vector2(620, y + 4), new Vector2(670, 76), new Color(1, 1, 1, 0.05f));
                Label(p, type == UpgradeType.Tires ? "Tyres" : type == UpgradeType.FuelTank ? "Fuel tank" : type == UpgradeType.Armor ? "Armour" : type.ToString(), 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(636, y - 4), new Vector2(190, 34), FontStyle.Bold);
                for (int k = 0; k < 3; k++) Box(p, "Pip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(640 + k * 40, y - 42), new Vector2(32, 14), k < lvl ? Accent : new Color(1, 1, 1, 0.18f));
                Label(p, max ? "Fully upgraded" : VehicleTuning.UpgradeEffect(type, lvl + 1), 18, max ? Good : Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(790, y - 6), new Vector2(330, 62));
                UpgradeType tt = type;
                Btn(p, max ? "MAX" : gated ? $"LEVEL {gate}" : GameSession.Money(cost), new Vector2(0, 1), new Vector2(0, 1), new Vector2(1128, y - 8), new Vector2(150, 56), () =>
                {
                    if (gated) { ShowToast($"Reach Level {gate} for this upgrade stage"); return; }
                    if (s.Garage.TryBuyUpgrade(s.Data.selectedVehicleId, tt, out string why))
                    {
                        // the level gate used to be checked only when a mission ended, so buying the last required upgrade left the player stuck
                        bool advanced = TryAdvanceAfterUpgrade();
                        s.Persist(); VehicleTuning.ApplyToCurrentCar(); paintApplied = false;
                        ShowToast(advanced ? $"{tt} upgraded - LEVEL {s.Career.Level} UNLOCKED" : $"{tt} upgraded"); OpenGarage();
                    }
                    else ShowToast(why ?? "Cannot buy");
                }, max ? new Color(0.22f, 0.24f, 0.27f) : gated ? new Color(0.30f, 0.20f, 0.20f) : Accent, 22);
                y -= 84;
            }
        }

        // =============================================================================================== character customisation (driver)
        private CharacterLook editLook;

        private void LookSwatches(Transform p, string label, float y, string[] hexes, string current, Action<string> set)
        {
            Label(p, label, 22, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(540, y), new Vector2(170, 40), FontStyle.Bold);
            for (int i = 0; i < hexes.Length; i++)
            {
                string hex = hexes[i];
                ColorUtility.TryParseHtmlString(hex, out Color c);
                bool on = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                if (on) Box(p, "Ring", new Vector2(0, 1), new Vector2(0, 1), new Vector2(712 + i * 56 - 3, y + 3), new Vector2(50, 50), Accent);
                Btn(p, "", new Vector2(0, 1), new Vector2(0, 1), new Vector2(715 + i * 56, y), new Vector2(44, 44), () => { set(hex); RefreshCharacter(); }, c, 20);
            }
        }

        private void LookChooser(Transform p, string label, float y, string[] names, int index, Action<int> set)
        {
            Label(p, label, 22, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(540, y), new Vector2(170, 40), FontStyle.Bold);
            Btn(p, "<", new Vector2(0, 1), new Vector2(0, 1), new Vector2(712, y + 2), new Vector2(54, 44), () => { set((index + names.Length - 1) % names.Length); RefreshCharacter(); }, new Color(0.17f, 0.21f, 0.29f), 26);
            Label(p, names[Mathf.Clamp(index, 0, names.Length - 1)], 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(0, 1), new Vector2(770, y), new Vector2(200, 40), FontStyle.Bold);
            Btn(p, ">", new Vector2(0, 1), new Vector2(0, 1), new Vector2(974, y + 2), new Vector2(54, 44), () => { set((index + 1) % names.Length); RefreshCharacter(); }, new Color(0.17f, 0.21f, 0.29f), 26);
        }

        private void RefreshCharacter() { OpenCharacter(); }

        public void OpenCharacter()
        {
            var s = GameSession.Instance;
            if (editLook == null) editLook = CharacterLook.FromJson(s.Data.driverLook, null) ?? new CharacterLook();
            var look = editLook;
            RectTransform p = OpenModal("CHARACTER  -  THE DRIVER", 1260, 790);
            var frame = Box(p, "PreviewFrame", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -92), new Vector2(488, 608), new Color(1, 1, 1, 0.10f));
            var go = new GameObject("CharPreviewImage", typeof(RectTransform), typeof(RawImage), typeof(CharPreviewImage));
            go.transform.SetParent(frame, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(480, 600); rt.anchoredPosition = Vector2.zero;
            var prev = CharacterPreview.Ensure(); prev.Show(look.female ? "driver_f" : "driver", look, CharPose.Idle); go.GetComponent<RawImage>().texture = prev.Texture;
            Label(p, "Drag the character to turn it", 20, Muted, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -704), new Vector2(488, 26));
            Btn(p, look.female ? "BODY: FEMALE  (tap to change)" : "BODY: MALE  (tap to change)", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -734), new Vector2(488, 44), () => { look.female = !look.female; if (look.female && look.hairStyle < 6) look.hairStyle = 7; if (!look.female && look.hairStyle >= 6) look.hairStyle = 1; RefreshCharacter(); }, new Color(0.17f, 0.21f, 0.29f), 22);

            float y = -96;
            LookSwatches(p, "SKIN", y, CharacterLook.SkinTones, look.skin, h => look.skin = h); y -= 62;
            LookChooser(p, "HAIR STYLE", y, CharacterLook.HairNames, look.hairStyle, i => look.hairStyle = i); y -= 62;
            LookSwatches(p, "HAIR COLOUR", y, CharacterLook.HairColors, look.hair, h => look.hair = h); y -= 62;
            LookChooser(p, "FACIAL HAIR", y, CharacterLook.FacialNames, look.facial, i => look.facial = i); y -= 62;
            LookSwatches(p, "SHIRT", y, CharacterLook.OutfitColors, look.top, h => look.top = h); y -= 62;
            LookSwatches(p, "TROUSERS", y, CharacterLook.OutfitColors, look.bottom, h => look.bottom = h); y -= 62;
            LookSwatches(p, "JACKET / CAP", y, CharacterLook.OutfitColors, look.accent, h => look.accent = h); y -= 62;
            LookChooser(p, "SLEEVES", y, new[] { "Short", "Long" }, look.sleeves, i => look.sleeves = i); y -= 66;

            Label(p, "EXTRAS", 22, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(540, y), new Vector2(170, 36), FontStyle.Bold);
            for (int i = 0; i < CharacterLook.ExtraNames.Length; i++)
            {
                string ex = CharacterLook.ExtraNames[i]; bool on = look.Has(ex);
                int col = i % 4, row = i / 4;
                Btn(p, ex, new Vector2(0, 1), new Vector2(0, 1), new Vector2(712 + col * 138, y + 2 - row * 54), new Vector2(130, 46), () => { look.Toggle(ex); RefreshCharacter(); }, on ? Accent : new Color(0.17f, 0.21f, 0.29f), 20);
            }

            Btn(p, "SAVE LOOK", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 24), new Vector2(330, 62), () =>
            {
                s.Data.driverLook = look.ToJson(); s.Persist(); ShowToast("Driver look saved");
            }, Accent, 28);
            Btn(p, "RANDOM", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-380, 24), new Vector2(200, 62), () =>
            {
                var r = new System.Random();
                look.skin = CharacterLook.SkinTones[r.Next(CharacterLook.SkinTones.Length)]; look.hair = CharacterLook.HairColors[r.Next(CharacterLook.HairColors.Length)];
                look.hairStyle = r.Next(CharacterLook.HairNames.Length); look.facial = r.Next(CharacterLook.FacialNames.Length);
                look.top = CharacterLook.OutfitColors[r.Next(CharacterLook.OutfitColors.Length)]; look.bottom = CharacterLook.OutfitColors[r.Next(CharacterLook.OutfitColors.Length)]; look.accent = CharacterLook.OutfitColors[r.Next(CharacterLook.OutfitColors.Length)];
                RefreshCharacter();
            }, new Color(0.17f, 0.21f, 0.29f), 24);
        }

        // =============================================================================================== how to play (first run + menu)
        private static readonly (string title, string sub, string[] lines)[] HowToPages =
        {
            ("DRIVE", "Wheel, pedals and gear lever", new[] { "Drag the STEERING WHEEL left and right to turn.", "Hold GAS to speed up and BRAKE to slow down or stop.", "Gear switch above BRAKE:  tap D to drive,  tap R then hold GAS to reverse.", "Brake while turning at speed to DRIFT. Tap the radar for the city map." }),
            ("MISSIONS", "Pick up, drive, drop off, get paid", new[] { "Open MISSIONS from the menu and tap START.", "Drive to the glowing marker and STOP inside it to pick up your passenger.", "Follow the GPS arrows to the drop-off and stop again.", "You earn money and XP. Level up to unlock new cars and upgrades." }),
            ("MAP AND GPS", "Never get lost", new[] { "The round radar shows your car, the road and your route.", "Tap the pin button for the full city map. Switch between 3D and 2D.", "In 3D: drag to move, + and - to zoom, tap any place to plan a route.", "Turn instructions appear at the top of the screen." }),
            ("SHADOW RUN", "Night jobs where the chase begins", new[] { "After you earn the Shadow Pass, secret night jobs unlock.", "Police and gangs chase you. Keep moving and get away from them.", "If police box you in and you stop, the job fails.", "Smoke and EMP weapons (weapon shop, Level 5) help you escape." }),
            ("GARAGE AND STYLE", "Make the car and the driver yours", new[] { "Buy up to 8 cars and upgrade engine, brakes, tyres and more in 3 stages.", "Upgrades add a body kit and change how the car looks.", "Change paint colours in the GARAGE and customise your driver in CHARACTER.", "Watch the fuel gauge and refuel at petrol stations." }),
        };

        private GameObject Pic(Transform parent, Sprite sprite, Vector2 pos, Vector2 size, float rotation = 0f)
        {
            var g = new GameObject("Pic", typeof(RectTransform), typeof(Image)); g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; rt.localRotation = Quaternion.Euler(0, 0, rotation);
            var im = g.GetComponent<Image>(); im.sprite = sprite; im.preserveAspect = true; im.raycastTarget = false; return g;
        }

        public void OpenHowToPlay(int page = 0, bool firstRun = false)
        {
            page = Mathf.Clamp(page, 0, HowToPages.Length - 1);
            var pg = HowToPages[page];
            RectTransform p = OpenModal("HOW TO PLAY", 1180, 700);
            Label(p, $"{page + 1} / {HowToPages.Length}", 26, Muted, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -30), new Vector2(200, 36));

            // illustration panel (left)
            var art = Box(p, "Art", new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -104), new Vector2(420, 430), new Color(1, 1, 1, 0.06f));
            switch (page)
            {
                case 0:
                    Pic(art, HudIcons.SteeringWheel, new Vector2(150, -150), new Vector2(240, 240));
                    Pic(art, HudIcons.PedalMetalShort, new Vector2(272, -330), new Vector2(86, 120));
                    Pic(art, HudIcons.PedalMetalTall, new Vector2(366, -300), new Vector2(86, 190));
                    for (int i = 0; i < 2; i++) Label(art, "RD"[i].ToString(), 34, i == 1 ? Good : new Color(1f, 0.74f, 0.22f), TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(304 + i * 52, -168), new Vector2(48, 44), FontStyle.Bold);
                    break;
                case 1: Pic(art, HudIcons.Disc(HudIcons.Kind.Pin), new Vector2(210, -215), new Vector2(260, 260)); break;
                case 2: Pic(art, HudIcons.Disc(HudIcons.Kind.Pin), new Vector2(130, -215), new Vector2(190, 190)); Pic(art, HudIcons.Disc(HudIcons.Kind.Camera), new Vector2(300, -215), new Vector2(150, 150)); break;
                case 3: Pic(art, HudIcons.Disc(HudIcons.Kind.Handbrake), new Vector2(210, -215), new Vector2(260, 260)); break;
                default: Pic(art, HudIcons.Disc(HudIcons.Kind.Gear), new Vector2(210, -215), new Vector2(260, 260)); break;
            }

            Label(p, pg.title, 54, Accent, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(490, -92), new Vector2(660, 66), FontStyle.Bold);
            Label(p, pg.sub, 28, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(490, -156), new Vector2(660, 40));
            float y = -222;
            foreach (string line in pg.lines)
            {
                Box(p, "Bullet", new Vector2(0, 1), new Vector2(0, 1), new Vector2(490, y - 16), new Vector2(14, 14), Accent);
                Label(p, line, 28, Color.white, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(520, y), new Vector2(630, 84));
                y -= 96;
            }

            // page dots
            for (int i = 0; i < HowToPages.Length; i++)
                Box(p, "Dot", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2((i - (HowToPages.Length - 1) * 0.5f) * 34f, 34), new Vector2(i == page ? 26 : 14, 14), i == page ? Accent : new Color(1, 1, 1, 0.28f));

            Action finish = () => { var ss = GameSession.Instance; if (firstRun && ss != null) { ss.Data.tutorialSeen = true; ss.Persist(); } OpenMainMenu(); };
            Btn(p, firstRun ? "SKIP" : "CLOSE", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 22), new Vector2(240, 62), finish, new Color(0.22f, 0.24f, 0.28f), 28);
            if (page > 0) Btn(p, "BACK", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-300, 22), new Vector2(200, 62), () => OpenHowToPlay(page - 1, firstRun), new Color(0.17f, 0.21f, 0.29f), 28);
            bool last = page == HowToPages.Length - 1;
            Btn(p, last ? (firstRun ? "LET'S DRIVE" : "DONE") : "NEXT", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 22), new Vector2(250, 62), () => { if (last) finish(); else OpenHowToPlay(page + 1, firstRun); }, Accent, 30);
        }

        public void OpenResult(MissionResult r)
        {
            RectTransform p = OpenModal(r.Success ? "MISSION COMPLETE" : "MISSION FAILED", 920, r.LevelDown ? 830 : 720);
            Label(p, r.Mission.Title + (r.Replay ? "  (replay: 25% reward, no XP)" : ""), 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -84), new Vector2(840, 40), FontStyle.Bold);
            float y = -140;
            Action<string, string, Color> row = (a, b, col) =>
            {
                Label(p, a, 28, Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, y), new Vector2(520, 40));
                Label(p, b, 28, col, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, y), new Vector2(300, 40), FontStyle.Bold);
                y -= 46;
            };
            if (r.Success)
            {
                row("Base reward", GameSession.Money(r.BaseReward), Color.white);
                row($"Distance bonus ({r.TripLength / 1000f:F1} km trip)", "+" + GameSession.Money(r.DistanceBonus), r.DistanceBonus > 0 ? Good : Muted);
                row("Time bonus", "+" + GameSession.Money(r.TimeBonus), r.TimeBonus > 0 ? Good : Muted);
                row("Safe driving bonus", "+" + GameSession.Money(r.SafeBonus), r.SafeBonus > 0 ? Good : Muted);
                if (r.Mission.ShadowRun) row("Escape bonus", "+" + GameSession.Money(r.EscapeBonus), r.EscapeBonus > 0 ? Good : Muted);
                row("Damage deduction", r.DamageDeduction > 0 ? GameSession.Money(-r.DamageDeduction) : "none", r.DamageDeduction > 0 ? Bad : Muted);
            }
            else
            {
                row("Reason", r.FailReason, Bad);
                row("Failure penalty", r.PenaltyApplied > 0 ? GameSession.Money(-r.PenaltyApplied) : "none", r.PenaltyApplied > 0 ? Bad : Muted);
            }
            y -= 10;
            row("TOTAL WALLET CHANGE", (r.WalletChange >= 0 ? "+" : "") + GameSession.Money(r.WalletChange), r.WalletChange >= 0 ? Good : Bad);
            row("XP gained", "+" + r.XpGained, r.XpGained > 0 ? Accent : Muted);
            int t = Mathf.RoundToInt(r.ElapsedSeconds);
            row("Trip time", $"{t / 60:00}:{t % 60:00}", Color.white);
            var s = GameSession.Instance;
            row("Level progress", $"{Mathf.RoundToInt(s.Xp.BandProgress01(s.Career.Level) * 100f)}%  (Level {s.Career.Level})", Color.white);
            if (r.LevelDown)
            {
                var banner = Box(p, "LevelDownBanner", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(840, 130), new Color(0.42f, 0.07f, 0.09f, 1f));
                Box(banner, "Edge", new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(10, 130), Bad, false);
                var lvl = Label(banner, $"LEVEL DOWN   {r.LevelFrom}  >  {r.LevelTo}", 50, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -8), new Vector2(780, 62), FontStyle.Normal); lvl.font = UiTheme.Display;
                Label(banner, $"The Shadow Run failed. You lose {r.XpLost} XP and drop back to Level {r.LevelTo}. Replay its assignments to climb back up.", 22, new Color(1f, 0.86f, 0.84f), TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 10), new Vector2(790, 60));
            }
            else if (!string.IsNullOrEmpty(r.LevelNote)) Label(p, r.LevelNote, 24, r.LeveledUp ? Good : Muted, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 100), new Vector2(840, 60));
            Action next = () =>
            {
                CloseModal(); if (MissionRunner.Instance != null) MissionRunner.Instance.Dismiss();
                var ss = GameSession.Instance;
                if (ss.Data.hasShadowPass && !ss.Data.shadowCinematicSeen) PlayShadowPass();       // the Shadow Pass is awarded at the end of Level 3
                else AfterResult(r);
            };
            // a failed run gets a one-tap way back in: RETRY the same job, or (after a level drop) jump to the lower level's assignments
            bool canRetry = !r.Success && !r.LevelDown && r.Mission != null && r.FailReason != "Mission abandoned." && MissionRunner.Instance != null;
            if (r.LevelDown || canRetry)
            {
                Btn(p, "LATER", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 26), new Vector2(260, 66), next, new Color(0.22f, 0.24f, 0.28f), 28);
                MissionDefinition again = r.Mission;
                Btn(p, r.LevelDown ? $"REPLAY LEVEL {r.LevelTo}" : "RETRY", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(150, 26), new Vector2(320, 66), () =>
                {
                    CloseModal(); var mr = MissionRunner.Instance; if (mr != null) mr.Dismiss();
                    if (r.LevelDown) { OpenMissions(); return; }
                    if (mr == null || !mr.StartMission(again)) ShowToast("Cannot restart this job right now");
                }, Accent, 30);
            }
            else Btn(p, "CONTINUE", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(420, 66), next, Accent, 32);
        }

        private void OnFinished(MissionResult r) => OpenResult(r);

        // =============================================================================================== paint
        private void ApplySavedPaint()
        {
            var v = GameSession.Instance.SelectedVehicle;
            if (v != null) ApplyPaint(v.colorHex);
        }

        public void ApplyPaint(string hex)
        {
            if (car == null || !ColorUtility.TryParseHtmlString(hex, out Color c)) return;
            var block = new MaterialPropertyBlock();
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith("M_CarPaint")) continue;
                    r.GetPropertyBlock(block, i); block.SetColor("_BaseColor", c); r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
