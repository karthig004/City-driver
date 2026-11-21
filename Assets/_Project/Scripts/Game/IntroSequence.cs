using System;
using System.Collections;
using System.Collections.Generic;
using CityDriver.CameraSystem;
using CityDriver.Missions;
using CityDriver.Player;
using CityDriver.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// App-open cinematic, built like a console title sequence: studio card, then three continuous camera shots over the live city
    /// at golden hour (high aerial, a swoop down the road, a hero orbit around the player's car), with bloom / colour grading /
    /// vignette on the 3D picture, light rays, anamorphic streak, drifting dust bokeh, twinkling stars and a flash on the title
    /// reveal. An original gold title with shield emblem sits top-right, and a glowing pill loading bar with a percentage, rotating
    /// tips and "TAP TO START" runs along the bottom. The bar shows real progress (streamed chunks around the spawn + the car).
    /// The city keeps running behind it (traffic, pedestrians); the HUD is hidden until the intro ends.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class IntroSequence : MonoBehaviour
    {
        public static IntroSequence Instance { get; private set; }
        public bool Playing { get; private set; }
        public float Progress { get; private set; }
        public string Stage { get; private set; } = "";
        public bool CameraControlled { get; private set; }
        public bool LogoShown { get; private set; }
        public bool PostFxActive { get; private set; }
        public int TipsShown { get; private set; }
        public int ShotIndex { get; private set; }
        public float Elapsed { get; private set; }

        private static readonly string[] Tips =
        {
            "Stop inside the glowing marker to pick up and drop off your passengers.",
            "Long trips pay a distance bonus. Keep an eye on your fuel gauge.",
            "Rain and fog reduce grip. Slow down before the corners.",
            "Upgrade your engine, brakes and armour in the Garage.",
            "Police only take an interest when a Shadow Run goes wrong. Stay out of sight.",
            "A smoke launcher can shake off a chasing gang car.",
            "Fuel runs low on long trips. Refuel at a petrol station or the garage.",
        };

        // camera timeline (seconds after the studio card): key times, field of view, and the four key poses are built per frame from the car
        private static readonly float[] KeyTime = { 0f, 5.5f, 10.5f, 14f };
        private static readonly float[] KeyFov = { 56f, 50f, 64f, 40f };
        private const float StudioSeconds = 2.2f;
        private const float LogoAt = 6.2f;

        private Font font;
        private Vector3 anchor, refPos, refFwd;
        private Camera cam;
        private PlayerSpawner spawner;
        private float nextSpawnerSearch;
        private Vector3 camPos; private Quaternion camRot; private float camFov = 55f;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public static IntroSequence Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("IntroSequence").AddComponent<IntroSequence>();
        }

        public void Play(Font uiFont, Action onDone, float minSeconds = 16f)
        {
            if (Playing) return;
            font = uiFont;
            StartCoroutine(Run(onDone, minSeconds));
        }

        // the camera pose is written every frame after everything else (execution order 1000)
        private void LateUpdate()
        {
            if (!CameraControlled || cam == null) return;
            cam.transform.SetPositionAndRotation(camPos, camRot);
            cam.fieldOfView = camFov;
        }

        // ----------------------------------------------------------------------------------------------- the sequence
        private IEnumerator Run(Action onDone, float minSeconds)
        {
            Playing = true; Stage = "start"; Progress = 0f; LogoShown = false; TipsShown = 0; ShotIndex = 0; Elapsed = 0f;

            cam = Camera.main;
            var camCtl = UnityEngine.Object.FindFirstObjectByType<VehicleCameraController>();
            if (camCtl != null) camCtl.enabled = false;
            float oldFov = cam != null ? cam.fieldOfView : 60f;
            var start = LocationMarker.Find(LocationKind.PlayerStart);
            anchor = start != null ? start.transform.position : new Vector3(40f, 0f, 22f);
            refPos = anchor;
            Vector3 toCentre = new Vector3(-anchor.x, 0f, -anchor.z); refFwd = toCentre.sqrMagnitude > 1f ? toCentre.normalized : Vector3.forward;
            var env = WorldEnvironment.Instance;
            if (env != null) { env.AutoCycle = false; env.SetTimeOfDay(TimeOfDay.Sunset, 0.01f); }

            // the HUD and every other canvas stay hidden until the intro is over
            var hidden = new List<Canvas>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (c.enabled && c.isRootCanvas) { c.enabled = false; hidden.Add(c); }

            // cinematic picture: bloom, warm grade, vignette (removed again afterwards; the game itself keeps its light mobile pipeline)
            GameObject volGo = null; UniversalAdditionalCameraData camData = null; bool oldPost = false;
            try
            {
                if (cam != null)
                {
                    camData = cam.GetUniversalAdditionalCameraData(); oldPost = camData.renderPostProcessing; camData.renderPostProcessing = true;
                    volGo = new GameObject("IntroVolume"); volGo.transform.SetParent(transform, false);
                    var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f; vol.weight = 1f;
                    var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                    var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(0.85f); bloom.threshold.Override(0.85f); bloom.scatter.Override(0.72f); bloom.tint.Override(new Color(1f, 0.9f, 0.75f));
                    var grade = prof.Add<ColorAdjustments>(true); grade.postExposure.Override(0.75f); grade.contrast.Override(14f); grade.saturation.Override(18f); grade.colorFilter.Override(new Color(1f, 0.93f, 0.86f));
                    var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.26f); vig.smoothness.Override(0.55f);
                    vol.sharedProfile = prof; PostFxActive = true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[IntroSequence] post-processing unavailable: " + e.Message); PostFxActive = false; }

            // ------------------------------------------------------------------------------------------ canvas
            var canvasGo = new GameObject("IntroCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var scaler = canvasGo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            var vignette = FullImage(root, "Vignette", Color.white); vignette.sprite = VignetteSprite(); vignette.color = new Color(0.02f, 0.01f, 0.03f, 0.5f);
            var bottomShade = Img(root, "BottomShade", new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(2400, 520), GradientSprite(), new Color(0.02f, 0.01f, 0.03f, 0.6f));
            var topShade = Img(root, "TopShade", new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(2400, 300), GradientSprite(), new Color(0.02f, 0.01f, 0.03f, 0.55f)); topShade.rectTransform.localScale = new Vector3(1, -1, 1);
            var streak = Img(root, "Streak", new Vector2(0.80f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2000, 70), StreakSprite(), new Color(1f, 0.82f, 0.5f, 0.0f));
            var sun = Img(root, "SunGlow", new Vector2(0.80f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300, 1000), RadialGlow(), new Color(1f, 0.78f, 0.35f, 0.3f));

            // dust / bokeh drifting through the light
            var dustRoot = new GameObject("Dust", typeof(RectTransform)); dustRoot.transform.SetParent(root, false);
            var dr = (RectTransform)dustRoot.transform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
            const int DustN = 34; var dust = new Image[DustN]; var dustPos = new Vector2[DustN]; var dustVel = new Vector2[DustN]; var dustPhase = new float[DustN]; var dustBase = new float[DustN];
            var rng = new System.Random(7);
            for (int i = 0; i < DustN; i++)
            {
                float sz = 14f + (float)rng.NextDouble() * 80f;
                dust[i] = Img(dr, "d" + i, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(sz, sz), RadialGlow(), Color.white);
                dustPos[i] = new Vector2((float)rng.NextDouble() * 1920f, (float)rng.NextDouble() * 1080f);
                dustVel[i] = new Vector2(-6f + (float)rng.NextDouble() * 12f, 6f + (float)rng.NextDouble() * 22f);
                dustPhase[i] = (float)rng.NextDouble() * 6.28f; dustBase[i] = 0.05f + (float)rng.NextDouble() * 0.17f;
            }

            // ---- logo lockup (centred): hex monogram badge that draws itself, per-letter gold title, widening UNDERCOVER line, tagline
            var logo = new GameObject("Logo", typeof(RectTransform)); logo.transform.SetParent(root, false);
            var logoRt = (RectTransform)logo.transform; logoRt.anchorMin = logoRt.anchorMax = new Vector2(0.5f, 0.5f); logoRt.pivot = new Vector2(0.5f, 0.5f); logoRt.anchoredPosition = new Vector2(0, 28); logoRt.sizeDelta = new Vector2(1200, 700); logoRt.localScale = Vector3.one * 0.88f;
            var rays = Img(logoRt, "Rays", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 190), new Vector2(1500, 1500), RaysSprite(), new Color(1f, 0.82f, 0.5f, 0f));
            var badgeGlow = Img(logoRt, "BadgeGlow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 190), new Vector2(720, 720), RadialGlow(), new Color(1f, 0.6f, 0.16f, 0f));
            var emblemRt = new GameObject("Emblem", typeof(RectTransform)); emblemRt.transform.SetParent(logoRt, false);
            var emRt = (RectTransform)emblemRt.transform; emRt.anchorMin = emRt.anchorMax = new Vector2(0.5f, 0.5f); emRt.pivot = new Vector2(0.5f, 0.5f); emRt.anchoredPosition = new Vector2(0, 190); emRt.sizeDelta = new Vector2(230, 274);
            var faceImg = Img(emRt, "Face", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 274), EmblemSprite(2), new Color(1, 1, 1, 0));
            var ringImg = Img(emRt, "Rim", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 274), EmblemSprite(1), Color.white);
            ringImg.type = Image.Type.Filled; ringImg.fillMethod = Image.FillMethod.Radial360; ringImg.fillOrigin = 2; ringImg.fillClockwise = true; ringImg.fillAmount = 0f;
            faceImg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var emblemShine = Img(faceImg.rectTransform, "Shine", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 0), new Vector2(90, 440), MagicCardArt.Beam, new Color(1f, 1f, 1f, 0f)); emblemShine.rectTransform.localRotation = Quaternion.Euler(0, 0, 22f);

            // title: CITY DRIVER as individual letters so each can fly in; positions come from the font's own advance widths
            const string TitleStr = "CITY DRIVER"; const int TitleSize = 150; const float LetterGap = 7f;
            var letters = new Text[TitleStr.Length]; var letterX = new float[TitleStr.Length]; var letterW = new float[TitleStr.Length]; var letterGrad = new TextGradient[TitleStr.Length];
            float titleW = 0f;
            for (int i = 0; i < TitleStr.Length; i++)
            {
                var lt0 = Txt(logoRt, TitleStr[i].ToString(), TitleSize, new Color(1, 1, 1, 0), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140, 170), UiTheme.Display);
                letterW[i] = TitleStr[i] == ' ' ? TitleSize * 0.24f : Mathf.Max(20f, lt0.preferredWidth);
                titleW += letterW[i] + (i < TitleStr.Length - 1 ? LetterGap : 0f);
                letters[i] = lt0;
                var g = lt0.gameObject.AddComponent<TextGradient>(); g.top = new Color(1f, 0.97f, 0.72f); g.bottom = new Color(0.96f, 0.52f, 0.07f); g.shine = -1f; letterGrad[i] = g;
                var o = lt0.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.27f, 0.1f, 0.01f, 1f); o.effectDistance = new Vector2(4f, -4f);
                var sh = lt0.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.55f); sh.effectDistance = new Vector2(0, -10);
            }
            { float xx = -titleW * 0.5f; for (int i = 0; i < TitleStr.Length; i++) { letterX[i] = xx + letterW[i] * 0.5f; xx += letterW[i] + LetterGap; } }
            var underText = Txt(logoRt, "UNDERCOVER", 38, new Color(1f, 0.95f, 0.84f, 0f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -66), new Vector2(900, 56), UiTheme.Bold);
            var underSpacing = underText.gameObject.AddComponent<LetterSpacing>(); underSpacing.spacing = 40f; underText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.85f);
            var lineL = Img(logoRt, "LineL", new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, -66), new Vector2(0, 3), null, new Color(1f, 0.78f, 0.3f, 0.95f));
            var lineR = Img(logoRt, "LineR", new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(250, -66), new Vector2(0, 3), null, new Color(1f, 0.78f, 0.3f, 0.95f));
            var tagline = Txt(logoRt, "DRIVE.  HIDE.  SURVIVE.", 24, new Color(1f, 0.94f, 0.8f, 0f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -124), new Vector2(800, 34), UiTheme.Semi);
            tagline.gameObject.AddComponent<LetterSpacing>().spacing = 9f; tagline.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.85f);
            var stars = new Image[6]; var starPos = new[] { new Vector2(-170, 330), new Vector2(180, 340), new Vector2(-330, 180), new Vector2(340, 200), new Vector2(-420, 20), new Vector2(430, 40) };
            for (int i = 0; i < stars.Length; i++) stars[i] = Img(logoRt, "Star" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), starPos[i], new Vector2(64, 64), StarSprite(), new Color(1f, 0.95f, 0.75f, 0f));
            var lightStreak = Img(root, "LightStreak", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(2600, 90), StreakSprite(), new Color(1f, 0.9f, 0.65f, 0f));
            float logoT0 = 0f;

            // ---- loading panel: status + percentage, slim segmented bar with a travelling glint, rotating tips; "TAP TO START" replaces it when ready
            var barRoot = new GameObject("LoadingBar", typeof(RectTransform), typeof(CanvasGroup)); barRoot.transform.SetParent(root, false); var barGroup = barRoot.GetComponent<CanvasGroup>(); barGroup.alpha = 0f;
            var brt = (RectTransform)barRoot.transform; brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0); brt.pivot = new Vector2(0.5f, 0); brt.anchoredPosition = new Vector2(0, 118); brt.sizeDelta = new Vector2(1240, 250);
            var statusRoot = new GameObject("Status", typeof(RectTransform), typeof(CanvasGroup)); statusRoot.transform.SetParent(brt, false); var statusGroup = statusRoot.GetComponent<CanvasGroup>();
            var statusRt = (RectTransform)statusRoot.transform; statusRt.anchorMin = Vector2.zero; statusRt.anchorMax = Vector2.one; statusRt.offsetMin = statusRt.offsetMax = Vector2.zero;
            var statusMain = Txt(statusRt, "LOADING", 32, new Color(1f, 0.95f, 0.82f), TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 128), new Vector2(700, 44), UiTheme.Bold);
            statusMain.gameObject.AddComponent<LetterSpacing>().spacing = 9f; statusMain.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            var statusSub = Txt(statusRt, "Laying out the roads", 24, new Color(0.78f, 0.82f, 0.9f), TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 96), new Vector2(700, 32), UiTheme.Semi);
            statusSub.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            var pct = Txt(statusRt, "0%", 104, Color.white, TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0, 76), new Vector2(420, 112), UiTheme.Display);
            var pctGrad = pct.gameObject.AddComponent<TextGradient>(); pctGrad.top = new Color(1f, 0.96f, 0.7f); pctGrad.bottom = new Color(0.97f, 0.62f, 0.1f); pct.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            var outer = Img(brt, "TrackOuter", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(1240, 22), PillSprite(), new Color(0.98f, 0.78f, 0.3f, 0.6f)); outer.type = Image.Type.Sliced;
            var track = Img(outer.rectTransform, "Track", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1232, 14), PillSprite(), new Color(0.02f, 0.03f, 0.05f, 0.96f)); track.type = Image.Type.Sliced;
            var fill = Img(track.rectTransform, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 14), PillSprite(), new Color(1f, 0.74f, 0.1f, 1f)); fill.type = Image.Type.Sliced;
            var fillHi = Img(fill.rectTransform, "Hi", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 6), PillSprite(), new Color(1f, 0.96f, 0.65f, 0.85f)); fillHi.type = Image.Type.Sliced;
            fillHi.rectTransform.anchorMin = new Vector2(0, 1); fillHi.rectTransform.anchorMax = new Vector2(1, 1); fillHi.rectTransform.offsetMin = new Vector2(5, -7); fillHi.rectTransform.offsetMax = new Vector2(-5, -1);
            var glint = Img(track.rectTransform, "Glint", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 40), RadialGlow(), new Color(1f, 1f, 0.9f, 0f));
            var head = Img(track.rectTransform, "Head", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 110), RadialGlow(), new Color(1f, 0.9f, 0.5f, 0.9f));
            for (int i = 1; i < 31; i++) Img(track.rectTransform, "Tick", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1232f * i / 31f, 0), new Vector2(2.5f, 14), null, new Color(0.02f, 0.03f, 0.05f, 0.55f));
            var tipPill = Img(statusRt, "TipPill", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 8), new Vector2(76, 38), PillSprite(), new Color(0.98f, 0.76f, 0.12f, 1f)); tipPill.type = Image.Type.Sliced;
            Txt(tipPill.rectTransform, "TIP", 22, new Color(0.1f, 0.08f, 0.04f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76, 38), UiTheme.Bold).gameObject.AddComponent<LetterSpacing>().spacing = 3f;
            var tip = Txt(statusRt, Tips[0], 27, new Color(1f, 0.96f, 0.88f), TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(94, 8), new Vector2(1140, 38), UiTheme.Semi);
            tip.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            var tapText = Txt(brt, "TAP TO START", 96, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(1000, 120), UiTheme.Display);
            tapText.gameObject.AddComponent<LetterSpacing>().spacing = 10f;
            var tapGrad = tapText.gameObject.AddComponent<TextGradient>(); tapGrad.top = new Color(1f, 0.96f, 0.7f); tapGrad.bottom = new Color(0.97f, 0.6f, 0.08f);
            var tapOl = tapText.gameObject.AddComponent<Outline>(); tapOl.effectColor = new Color(0.25f, 0.1f, 0.02f, 0.9f); tapOl.effectDistance = new Vector2(3, -3);
            tapText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            tapText.color = new Color(1, 1, 1, 0);

            var barTop = Img(root, "BarTop", new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(2400, 0), null, Color.black);
            var barBottom = Img(root, "BarBottom", new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(2400, 0), null, Color.black);
            var flash = FullImage(root, "Flash", new Color(1f, 0.96f, 0.85f, 0f));

            // studio card and the black fade on top of everything
            var fade = FullImage(root, "Fade", Color.black);
            var studio = new GameObject("Studio", typeof(RectTransform)); studio.transform.SetParent(root, false);
            var sr = (RectTransform)studio.transform; sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f); sr.sizeDelta = new Vector2(1200, 300);
            RawImage splash = null;
            var splashTex = Resources.Load<Texture2D>("Branding/KarthiLab_splash");
            if (splashTex != null)
            {
                var sg = new GameObject("Splash", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter)); sg.transform.SetParent(root, false);
                var splashRt = (RectTransform)sg.transform; splashRt.anchorMin = Vector2.zero; splashRt.anchorMax = Vector2.one; splashRt.offsetMin = splashRt.offsetMax = Vector2.zero;
                splash = sg.GetComponent<RawImage>(); splash.texture = splashTex; splash.raycastTarget = false; splash.color = new Color(1, 1, 1, 0);
                var fit = sg.GetComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = splashTex.width / (float)splashTex.height;
            }
            var studioName = Label(sr, "K A R T H I   L A B", 74, new Color(0.96f, 0.86f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0, 26), new Vector2(1200, 100), FontStyle.Bold);
            var studioSub = Label(sr, "P R E S E N T S", 30, new Color(0.8f, 0.78f, 0.74f), TextAnchor.MiddleCenter, new Vector2(0, -44), new Vector2(1200, 50), FontStyle.Normal);

            var skipHint = Label(root, "TAP TO SKIP", 26, Color.white, TextAnchor.LowerRight, new Vector2(-40, 30), new Vector2(520, 44), FontStyle.Bold); skipHint.rectTransform.anchorMin = skipHint.rectTransform.anchorMax = new Vector2(1, 0); skipHint.rectTransform.pivot = new Vector2(1, 0); skipHint.color = new Color(1, 1, 1, 0);
            float t = 0f, progressShown = 0f, tipClock = 0f; int tipIndex = 0; float flashT = -1f;
            Stage = "studio";
            while (true)
            {
                float dt = Time.unscaledDeltaTime; t += dt; Elapsed = t;
                float ct = Mathf.Max(0f, t - StudioSeconds);                                                   // camera clock

                // ---- studio card -> reveal of the first shot
                float cardA = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((StudioSeconds - 0.3f - t) / 0.6f);
                studioName.color = new Color(studioName.color.r, studioName.color.g, studioName.color.b, splash != null ? 0f : cardA); if (splash != null) splash.color = new Color(1, 1, 1, cardA); if (splash != null) splash.rectTransform.localScale = Vector3.one * (1f + 0.07f * Mathf.Clamp01(t / StudioSeconds)); studioSub.color = new Color(0.8f, 0.78f, 0.74f, splash != null ? 0f : cardA * 0.9f);
                float reveal = Mathf.Clamp01((t - (StudioSeconds - 0.3f)) / 1.6f);
                if (Stage == "studio" && t > StudioSeconds) Stage = "flight";
                fade.color = new Color(0, 0, 0, 1f - Mathf.SmoothStep(0f, 1f, reveal));
                float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 1.2f) / 1.6f)) * 92f; barTop.rectTransform.sizeDelta = new Vector2(2400, bars); barBottom.rectTransform.sizeDelta = new Vector2(2400, bars);

                // ---- camera
                UpdateReference(dt);
                EvaluateCamera(ct);
                ShotIndex = ct < KeyTime[2] ? (ct < KeyTime[1] ? 1 : 2) : 3;
                if (!CameraControlled && cam != null) CameraControlled = true;

                // ---- logo reveal: flash + light streak, the badge rim draws itself, the monogram pops, letters fly in one by one, UNDERCOVER widens, gold glint
                if (ct > LogoAt && !LogoShown) { LogoShown = true; flashT = 0f; GameAudio.PlayCard(); logoT0 = t; }
                if (LogoShown)
                {
                    float lt = t - logoT0;
                    float float01 = Mathf.Sin(t * 0.9f) * 5f; logoRt.anchoredPosition = new Vector2(0f, 28f + float01);
                    float ks = Mathf.Clamp01(lt / 0.7f);
                    lightStreak.rectTransform.localScale = new Vector3(Mathf.Lerp(0.05f, 1f, UiTheme.EaseOutCubic(ks)), 1f, 1f); lightStreak.color = new Color(1f, 0.9f, 0.65f, lt < 1.0f ? 0.85f * Mathf.Sin(Mathf.Clamp01(lt / 1.0f) * Mathf.PI) : 0f);
                    ringImg.fillAmount = UiTheme.EaseOutCubic(Mathf.Clamp01(lt / 0.85f));
                    float kfa = Mathf.Clamp01((lt - 0.5f) / 0.55f);
                    faceImg.color = new Color(1, 1, 1, kfa); emRt.localScale = Vector3.one * Mathf.LerpUnclamped(0.82f, 1f, UiTheme.EaseOutBack(kfa)); emRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-7f, 0f, UiTheme.EaseOutCubic(kfa)));
                    float breathe = 0.5f + 0.5f * Mathf.Sin(t * 1.7f);
                    badgeGlow.color = new Color(1f, 0.6f, 0.16f, Mathf.Clamp01(lt / 0.8f) * (0.34f + 0.16f * breathe));
                    rays.color = new Color(1f, 0.82f, 0.5f, 0.30f * Mathf.Clamp01((lt - 0.3f) / 0.8f) * (0.8f + 0.2f * Mathf.Sin(t * 1.4f))); rays.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 5f);
                    float eg = Mathf.Repeat(lt - 1.4f, 4.5f); emblemShine.color = new Color(1f, 1f, 1f, lt > 1.4f && eg < 0.9f ? 0.55f * Mathf.Sin(eg / 0.9f * Mathf.PI) : 0f); emblemShine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-170f, 170f, Mathf.Clamp01(eg / 0.9f)), 0f);

                    float gl = lt > 1.7f ? Mathf.Repeat(lt - 1.7f, 5.5f) : 99f, G = gl < 1.3f ? Mathf.Lerp(-0.2f, 1.2f, gl / 1.3f) : -1f;
                    float xAcc = 0f;
                    for (int i = 0; i < letters.Length; i++)
                    {
                        float kl = Mathf.Clamp01((lt - (0.55f + i * 0.055f)) / 0.5f), e = UiTheme.EaseOutCubic(kl);
                        letters[i].color = new Color(1, 1, 1, kl);
                        letters[i].rectTransform.anchoredPosition = new Vector2(letterX[i], 36f - 56f * (1f - e));
                        letters[i].rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.5f, 1f, UiTheme.EaseOutBack(kl));
                        float sh = G < 0f ? -1f : (G * titleW - xAcc) / letterW[i];
                        if (!Mathf.Approximately(letterGrad[i].shine, sh)) { letterGrad[i].shine = sh; letterGrad[i].shineWidth = 70f / letterW[i]; letters[i].SetVerticesDirty(); }
                        xAcc += letterW[i] + 7f;
                    }
                    float ku = Mathf.Clamp01((lt - 1.25f) / 0.8f), eu = UiTheme.EaseOutCubic(ku);
                    underText.color = new Color(1f, 0.95f, 0.84f, ku); underSpacing.spacing = Mathf.Lerp(46f, 13f, eu); underText.SetVerticesDirty();
                    float lw = 230f * eu; lineL.rectTransform.sizeDelta = new Vector2(lw, 3f); lineR.rectTransform.sizeDelta = new Vector2(lw, 3f);
                    tagline.color = new Color(1f, 0.94f, 0.8f, 0.85f * Mathf.Clamp01((lt - 1.75f) / 0.6f));
                    for (int i = 0; i < stars.Length; i++)
                    {
                        float tw = Mathf.Max(0f, Mathf.Sin(t * (1.6f + i * 0.37f) + i * 1.9f)); tw *= tw;
                        stars[i].rectTransform.localScale = Vector3.one * (0.25f + 1.1f * tw); stars[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 12f * i);
                        stars[i].color = new Color(1f, 0.95f, 0.75f, Mathf.Clamp01(lt / 0.6f) * tw);
                    }
                }
                if (flashT >= 0f) { flashT += dt; flash.color = new Color(1f, 0.96f, 0.85f, 0.55f * Mathf.Clamp01(1f - flashT / 0.6f)); if (flashT > 0.6f) flashT = -1f; }
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 0.9f);
                sun.color = new Color(1f, 0.78f, 0.35f, (0.22f + 0.1f * pulse) * reveal);
                streak.color = new Color(1f, 0.82f, 0.5f, (0.16f + 0.1f * pulse) * reveal);

                // ---- dust
                for (int i = 0; i < DustN; i++)
                {
                    dustPos[i] += dustVel[i] * dt; if (dustPos[i].y > 1120f) dustPos[i].y = -40f; if (dustPos[i].x < -40f) dustPos[i].x = 1960f; if (dustPos[i].x > 1960f) dustPos[i].x = -40f;
                    dust[i].rectTransform.anchoredPosition = dustPos[i] + new Vector2(Mathf.Sin(t * 0.6f + dustPhase[i]) * 14f, 0f);
                    dust[i].color = new Color(1f, 0.86f, 0.6f, dustBase[i] * reveal * (0.6f + 0.4f * Mathf.Sin(t * 1.3f + dustPhase[i])));
                }

                // ---- real loading progress: streamed chunks around the spawn + the car (the bar never jumps, and the cinematic is always seen)
                float target = RealProgress();
                progressShown = Mathf.MoveTowards(progressShown, Mathf.Max(target, 0f), dt * 0.45f);
                if (t < minSeconds) progressShown = Mathf.Min(progressShown, t / minSeconds);
                Progress = progressShown;
                const float FullW = 1232f;
                float w = Mathf.Max(16f, FullW * Progress);
                fill.rectTransform.sizeDelta = new Vector2(w, 14f);
                head.rectTransform.anchoredPosition = new Vector2(w, 0f);
                bool ready = Progress >= 0.999f;
                head.color = new Color(1f, 0.9f, 0.5f, ready ? 0.35f + 0.25f * Mathf.Sin(t * 5f) : 0.9f);
                float gx = Mathf.Repeat(t * 520f, w + 220f) - 110f;                                           // a soft glint travels along the filled part
                glint.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(gx, 0f, w), 0f); glint.color = new Color(1f, 1f, 0.9f, gx > 0f && gx < w ? 0.5f : 0f);
                int pctNow = Mathf.RoundToInt(Progress * 100f); string pctText = pctNow + "%"; if (pct.text != pctText) pct.text = pctText;
                string sub = Progress < 0.25f ? "Laying out the roads" : Progress < 0.55f ? "Waking up the traffic" : Progress < 0.85f ? "Fuelling your car" : "Almost there";
                if (statusSub.text != sub) statusSub.text = sub;
                if (!ready) { statusGroup.alpha = 1f; tapText.color = new Color(1, 1, 1, 0f); }
                else
                {
                    Stage = "ready"; float pp = 0.5f + 0.5f * Mathf.Sin(t * 4.5f);
                    statusGroup.alpha = Mathf.MoveTowards(statusGroup.alpha, 0f, dt * 4f);
                    float ta = Mathf.Clamp01(1f - statusGroup.alpha);
                    tapText.color = new Color(1f, 1f, 1f, ta * (0.62f + 0.38f * pp)); tapText.rectTransform.localScale = Vector3.one * (1f + 0.035f * pp);
                }
                float barA = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 2.6f) / 1f)); barGroup.alpha = barA;

                tipClock += dt;
                if (tipClock > 3.8f) { tipClock = 0f; tipIndex = (tipIndex + 1) % Tips.Length; tip.text = Tips[tipIndex]; TipsShown++; }
                tip.color = new Color(1f, 0.96f, 0.88f, Mathf.Clamp01(Mathf.Min(tipClock / 0.4f, (3.8f - tipClock) / 0.4f)));

                if (!ready && t > 2.6f && AnyTap()) minSeconds = 0f;              // tap during the cinematic = skip ahead: the bar fills as fast as the city loads
                skipHint.color = new Color(1f, 1f, 1f, ready ? 0f : 0.7f * Mathf.Clamp01((t - 2.6f) / 0.8f));
                if (ready) { if (AnyTap() || t > minSeconds + 12f) break; }
                else if (t > minSeconds + 25f) break;                                                            // loading stuck: let the player in anyway
                yield return null;
            }

            // ------------------------------------------------------------------------------------------ outro: fade, hand everything back
            Stage = "outro";
            for (float f = 0f; f < 0.7f; f += Time.unscaledDeltaTime) { fade.color = new Color(0, 0, 0, Mathf.Clamp01(f / 0.7f)); yield return null; }
            CameraControlled = false;
            if (cam != null) cam.fieldOfView = oldFov;
            if (camCtl != null) { camCtl.enabled = true; camCtl.ResetCamera(); }
            foreach (var c in hidden) if (c != null) c.enabled = true;
            if (env != null) env.RestoreFromSettings();
            if (volGo != null) Destroy(volGo);
            if (camData != null) camData.renderPostProcessing = oldPost;
            PostFxActive = false;
            Destroy(canvasGo);
            Playing = false; Stage = "done";
            onDone?.Invoke();
        }

        // ----------------------------------------------------------------------------------------------- camera work
        private void UpdateReference(float dt)
        {
            if (spawner == null && Time.unscaledTime >= nextSpawnerSearch) { spawner = UnityEngine.Object.FindFirstObjectByType<PlayerSpawner>(); nextSpawnerSearch = Time.unscaledTime + 0.5f; }
            var v = spawner != null ? spawner.Vehicle : null;
            Vector3 wantPos = v != null ? v.transform.position : anchor;
            Vector3 wantFwd = v != null ? v.transform.forward : refFwd; wantFwd.y = 0f;
            if (wantFwd.sqrMagnitude < 0.01f) wantFwd = refFwd; wantFwd.Normalize();
            float k = 1f - Mathf.Exp(-4f * dt);
            refPos = Vector3.Lerp(refPos, wantPos, k); refFwd = Vector3.Slerp(refFwd, wantFwd, k).normalized;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        // three continuous shots: high aerial -> swoop down the road -> hero orbit around the car, with roll, lens change and a little hand-held shake
        private void EvaluateCamera(float ct)
        {
            Vector3 fwd = refFwd, right = Vector3.Cross(Vector3.up, fwd), up = Vector3.up;
            Vector3[] P =
            {
                refPos - fwd * 300f + right * -130f + up * 140f,
                refPos - fwd * 140f + right * -30f + up * 60f,
                refPos - fwd * 28f + right * -4f + up * 9f,
                refPos - fwd * 9f + right * 4.5f + up * 2.0f,
            };
            Vector3[] L =
            {
                refPos + fwd * 140f + up * 10f,
                refPos + fwd * 80f + up * 8f,
                refPos + fwd * 30f + up * 3f,
                refPos + fwd * 1.5f + up * 1.0f,
            };
            Vector3 pos, look; float fov;
            if (ct < KeyTime[3])
            {
                int s = ct < KeyTime[1] ? 0 : ct < KeyTime[2] ? 1 : 2;
                float u = Mathf.Clamp01((ct - KeyTime[s]) / (KeyTime[s + 1] - KeyTime[s]));
                int i0 = Mathf.Max(0, s - 1), i3 = Mathf.Min(3, s + 2);
                pos = CatmullRom(P[i0], P[s], P[s + 1], P[i3], u); look = CatmullRom(L[i0], L[s], L[s + 1], L[i3], u);
                fov = Mathf.Lerp(KeyFov[s], KeyFov[s + 1], Mathf.SmoothStep(0f, 1f, u));
            }
            else
            {
                float o = ct - KeyTime[3];
                Vector3 off = P[3] - refPos; float h = Mathf.Lerp(off.y, 1.5f, Mathf.Clamp01(o / 8f)); off.y = 0f;
                off = Quaternion.AngleAxis(Mathf.Min(o * 4.5f, 130f), Vector3.up) * off * Mathf.Lerp(1f, 0.92f, Mathf.Clamp01(o / 10f));
                pos = refPos + off + up * h; look = refPos + up * 1.0f + fwd * 1.5f; fov = KeyFov[3];
            }
            float shake = Mathf.Lerp(0.07f, 0.03f, Mathf.Clamp01(ct / 12f));
            pos += new Vector3(Mathf.PerlinNoise(ct * 0.9f, 3.1f) - 0.5f, Mathf.PerlinNoise(ct * 0.8f, 7.7f) - 0.5f, Mathf.PerlinNoise(ct * 0.7f, 1.3f) - 0.5f) * shake * 2f;
            Quaternion rot = Quaternion.LookRotation((look - pos).sqrMagnitude < 0.01f ? fwd : look - pos, up);
            float roll = Mathf.Lerp(5f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ct / 10f)));
            camPos = pos; camRot = rot * Quaternion.AngleAxis(roll, Vector3.forward); camFov = fov;
        }

        private float RealProgress()
        {
            var streamer = UnityEngine.Object.FindFirstObjectByType<ChunkStreamer>();
            float chunks = streamer != null ? streamer.ReadyFraction(refPos) : 1f;
            float car = spawner != null && spawner.Vehicle != null ? 1f : 0f;
            return Mathf.Clamp01(0.8f * chunks + 0.2f * car);
        }

        private static bool AnyTap()
        {
            var ts = Touchscreen.current; if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            var m = Mouse.current; if (m != null && m.leftButton.wasPressedThisFrame) return true;
            var k = Keyboard.current; if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) return true;
            return false;
        }

        // ----------------------------------------------------------------------------------------------- UI helpers
        private static void SetAlpha(RectTransform rt, float a) { foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) { var c = g.color; if (g.name.StartsWith("Star") || g.name == "Rays") continue; g.color = new Color(c.r, c.g, c.b, a); } }

        private Image FullImage(RectTransform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var i = go.GetComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
        }

        private Image Img(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Sprite sprite, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            var i = go.GetComponent<Image>(); i.sprite = sprite; i.color = c; i.raycastTarget = false; return i;
        }

        private Text Label(RectTransform parent, string text, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = pos; r.sizeDelta = box;
            var t = go.GetComponent<Text>(); t.font = (style & FontStyle.Bold) != 0 ? UiTheme.Bold : UiTheme.Semi; t.fontSize = size; t.color = color; t.alignment = align; t.text = text; t.fontStyle = style & ~FontStyle.Bold; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // gold lettering: dark outline, orange body, light highlight offset upwards (a cheap bevel)
        private Text GoldText(RectTransform parent, string text, int size, Vector2 pos, float width)
        {
            var host = new GameObject("Gold_" + text, typeof(RectTransform)); host.transform.SetParent(parent, false);
            var hr = (RectTransform)host.transform; hr.anchorMin = hr.anchorMax = new Vector2(1, 1); hr.pivot = new Vector2(1, 1); hr.anchoredPosition = pos; hr.sizeDelta = new Vector2(width, size + 20);
            Text Layer(Color c, Vector2 off, bool outline)
            {
                var t = Label(hr, text, size, c, TextAnchor.MiddleRight, off, new Vector2(width, size + 20), FontStyle.Bold);
                if (outline) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.28f, 0.12f, 0.02f, 1f); o.effectDistance = new Vector2(5, -5); }
                return t;
            }
            Layer(new Color(0.34f, 0.15f, 0.03f), new Vector2(0, -7), true);
            Layer(new Color(0.93f, 0.55f, 0.08f), new Vector2(0, -2), false);
            return Layer(new Color(1f, 0.9f, 0.45f), new Vector2(0, 3), false);
        }

        // text with any anchor/pivot and a chosen font (Label above always centres its rect)
        private static Text Txt(RectTransform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, Font f)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = box;
            var t = go.GetComponent<Text>(); t.font = f; t.fontSize = size; t.color = color; t.alignment = align; t.text = text; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // big gold title line: Bebas Neue, vertical gold gradient, dark edge and a soft drop shadow
        private static Text TitleText(RectTransform parent, string text, int size, Vector2 pos, out TextGradient grad)
        {
            var t = Txt(parent, text, size, new Color(1, 1, 1, 0), TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), pos, new Vector2(700, size * 0.9f), UiTheme.Display);
            t.gameObject.AddComponent<LetterSpacing>().spacing = 4f;
            grad = t.gameObject.AddComponent<TextGradient>(); grad.top = new Color(1f, 0.97f, 0.72f); grad.bottom = new Color(0.96f, 0.52f, 0.07f); grad.shine = -1f;
            var ol = t.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.27f, 0.1f, 0.01f, 1f); ol.effectDistance = new Vector2(4.5f, -4.5f);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.55f); sh.effectDistance = new Vector2(0, -12);
            return t;
        }

        private static Sprite ribbonS;
        // red parallelogram ribbon with gold edge lines
        private static Sprite RibbonSprite() => ribbonS != null ? ribbonS : ribbonS = Build(512, 64, (u, v) =>
        {
            float skew = 0.06f * v;                                       // lean the ends
            float left = -0.955f + skew, right = 0.955f + skew;
            float edge = Mathf.Min(u - left, right - u) * 256f;          // pixels from the slanted ends
            float a = Mathf.Clamp01(edge) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 32f);
            if (a <= 0f) return Color.clear;
            float hx = Mathf.InverseLerp(-1f, 1f, u);
            Color body = Color.Lerp(new Color(0.42f, 0.03f, 0.07f), new Color(0.86f, 0.1f, 0.15f), Mathf.SmoothStep(0f, 1f, hx));
            body *= 0.78f + 0.22f * Mathf.Clamp01(v * 0.5f + 0.6f);
            float lineTop = Mathf.Clamp01((Mathf.Abs(v - 0.8f) < 0.075f ? 1f : 0f)), lineBot = Mathf.Abs(v + 0.8f) < 0.075f ? 1f : 0f;
            Color gold = new Color(1f, 0.82f, 0.3f);
            Color c = Color.Lerp(body, gold, Mathf.Max(lineTop, lineBot) * 0.95f); c.a = a;
            return c;
        });

        // ----------------------------------------------------------------------------------------------- procedural sprites (FullRect meshes so soft edges are never trimmed)
        private static Sprite Build(int w, int h, Func<float, float, Color> f, Vector4 border = default, bool mip = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mip, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f);
            tex.SetPixels(px); tex.Apply(mip);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Sprite glowS, vignS, gradS, streakS, raysS, starS, pillS, shieldS;
        private static Sprite RadialGlow() => glowS != null ? glowS : glowS = Build(128, 128, (u, v) => { float d = Mathf.Sqrt(u * u + v * v); return new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f)); });
        private static Sprite VignetteSprite() => vignS != null ? vignS : vignS = Build(256, 256, (u, v) => { float d = Mathf.Sqrt(u * u + v * v); return new Color(1, 1, 1, Mathf.SmoothStep(0.55f, 1.4f, d)); });
        private static Sprite GradientSprite() => gradS != null ? gradS : gradS = Build(4, 128, (u, v) => new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - (v * 0.5f + 0.5f)), 1.6f)));
        private static Sprite StreakSprite() => streakS != null ? streakS : streakS = Build(256, 32, (u, v) => new Color(1, 1, 1, Mathf.Exp(-Mathf.Abs(u) * 3.2f) * Mathf.Exp(-v * v * 40f)));
        private static Sprite StarSprite() => starS != null ? starS : starS = Build(128, 128, (u, v) =>
        {
            float f = Mathf.Sqrt(Mathf.Abs(u)) + Mathf.Sqrt(Mathf.Abs(v)); float a = Mathf.Pow(Mathf.Clamp01(1f - f), 1.7f);
            float core = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 3f), 2f); return new Color(1, 1, 1, Mathf.Clamp01(a + core));
        });
        private static Sprite RaysSprite() => raysS != null ? raysS : raysS = Build(256, 256, (u, v) =>
        {
            float d = Mathf.Sqrt(u * u + v * v); float ang = Mathf.Atan2(v, u);
            float ray = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 14f), 4f) * 0.7f + Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 5f + 1f), 6f) * 0.5f;
            return new Color(1, 1, 1, Mathf.Clamp01(ray) * Mathf.Pow(Mathf.Clamp01(1f - d), 1.4f) * Mathf.SmoothStep(0f, 0.12f, d));
        });
        private static Sprite PillSprite() => pillS != null ? pillS : pillS = Build(64, 32, (u, v) =>
        {
            float x = (u + 1f) * 32f, y = (v + 1f) * 16f; float rx = Mathf.Clamp(x, 16f, 48f); float d = Mathf.Sqrt((x - rx) * (x - rx) + (y - 16f) * (y - 16f));
            return new Color(1, 1, 1, Mathf.Clamp01(16.5f - d));
        }, new Vector4(15, 15, 15, 15));

        /// <summary>The badge texture (also used by the editor tool that generates the Android launcher icon): rounded hexagon, gold rim, "CD" monogram.</summary>
        public static Texture2D EmblemTexture() => EmblemSprite(0).texture;

        private static readonly Sprite[] emblemS = new Sprite[3];

        // hexagon with flat sides left / right and points at the top and bottom
        private static float HexD(float x, float y, float ap) => Mathf.Max(Mathf.Abs(x), 0.5f * Mathf.Abs(x) + 0.866f * Mathf.Abs(y)) - ap;
        private static float RBox(float x, float y, float hx, float hy, float r) { float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r; return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r; }

        /// <summary>layer 0 = everything, 1 = the gold rim only (animated as a drawing ring), 2 = the dark face with the monogram.</summary>
        public static Sprite EmblemSprite(int layer)
        {
            if (emblemS[layer] != null) return emblemS[layer];
            const int W = 420, H = 500; var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            const float A = 206f;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f - W * 0.5f, fy = y + 0.5f - H * 0.5f;
                float d = HexD(fx, fy, A - 16f) - 16f;
                float aa = Mathf.Clamp01(0.5f - d); if (aa <= 0f) { px[y * W + x] = Color.clear; continue; }
                float s = -d; bool rimZone = s < 15f;
                if ((layer == 1 && !rimZone) || (layer == 2 && rimZone)) { px[y * W + x] = Color.clear; continue; }
                Color c;
                float u = fx / A, v = fy / A;
                if (rimZone)
                {
                    float diag = Mathf.Clamp01(0.5f + (v * 0.5f - u * 0.5f) * 0.8f);
                    c = Color.Lerp(new Color(0.55f, 0.30f, 0.05f), new Color(1f, 0.9f, 0.52f), diag);
                    c = Color.Lerp(c, new Color(1f, 0.98f, 0.84f), Mathf.Clamp01(1f - s / 2.6f) * 0.6f);
                    c = Color.Lerp(c, new Color(0.4f, 0.2f, 0.04f), Mathf.Clamp01((s - 10f) / 5f) * 0.65f);
                }
                else
                {
                    float r2 = u * u + v * v;
                    c = Color.Lerp(new Color(0.02f, 0.015f, 0.06f), new Color(0.15f, 0.09f, 0.25f), Mathf.SmoothStep(0f, 1f, v * 0.5f + 0.5f));
                    c += new Color(0.34f, 0.13f, 0.07f) * Mathf.Exp(-r2 * 2.2f);
                    float line = Mathf.Clamp01(1.2f - Mathf.Abs(s - 28f)); c = Color.Lerp(c, new Color(1f, 0.8f, 0.4f), line * 0.55f);          // fine inner gold line
                    // monogram "CD" (italic, slash through it)
                    float m = 1f / 0.88f, mu = u * m, mv = v * m;
                    float u0 = mu - 0.16f * mv;                                                                                         // lean to the right
                    float rC = Mathf.Sqrt((u0 + 0.30f) * (u0 + 0.30f) + mv * mv), angC = Mathf.Atan2(mv, u0 + 0.30f);
                    float dC = Mathf.Max(Mathf.Abs(rC - 0.415f) - 0.105f, (0.74f - Mathf.Abs(angC)) * rC);
                    float dStem = RBox(u0 - 0.265f, mv, 0.09f, 0.52f, 0.03f);
                    float rD = Mathf.Sqrt((u0 - 0.30f) * (u0 - 0.30f) + mv * mv);
                    float dBowl = Mathf.Max(Mathf.Abs(rD - 0.415f) - 0.105f, 0.30f - u0);
                    float dm = Mathf.Min(dC, Mathf.Min(dStem, dBowl));
                    float slash = Mathf.Abs(mu * 0.55f + mv * 0.83f) - 0.022f;                                                         // thin diagonal cut: the "undercover" mark
                    dm = Mathf.Max(dm, -slash);
                    float shadowA = Mathf.Clamp01(0.5f - (dm - 0.02f) * A * 0.35f) * 0.5f;
                    c = Color.Lerp(c, Color.black, shadowA);
                    float ma = Mathf.Clamp01(0.5f - dm * A * 0.88f);
                    float tcol = Mathf.Clamp01(mv * 0.5f + 0.5f);
                    Color mono = tcol > 0.5f ? Color.Lerp(new Color(1f, 0.62f, 0.14f), new Color(1f, 0.94f, 0.62f), (tcol - 0.5f) * 2f) : Color.Lerp(new Color(0.9f, 0.16f, 0.22f), new Color(1f, 0.62f, 0.14f), tcol * 2f);
                    c = Color.Lerp(c, mono, ma);
                    float gloss = Mathf.Clamp01((v * 0.95f - u * 0.5f - 0.4f) * 3f) * 0.14f; c = Color.Lerp(c, Color.white, gloss);
                }
                c.a = aa; px[y * W + x] = c;
            }
            tex.SetPixels(px); tex.Apply(true);
            return emblemS[layer] = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
