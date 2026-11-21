using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CityDriver.CameraSystem;
using CityDriver.Controls;
using CityDriver.Game;
using CityDriver.Missions;
using CityDriver.Navigation;
using CityDriver.Player;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Records the promo trailer from the real game: fixed 30 fps, 1920x1080, 2.39:1 letterbox (Bridge/trailer/f_#####.jpg) plus
    /// Bridge/trailer_timeline.json (shot starts, hit points, sections) that the score generator follows.
    /// The story is told only with pictures: one car, one city, a warm ordinary day that turns into a cold wet night, cut together with
    /// match cuts and a flicker between the two worlds; the title splits the same way ("CITY DRIVER" in daylight, "UNDERCOVER" in the dark).
    /// Look: ACES tonemapping, teal/orange split toning, bloom, depth of field, motion blur, grain, chromatic aberration, slow motion,
    /// flashing police lights and glow sprites on lamps. All rendered offline and deterministically (Time.captureFramerate).
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerCapture
    {
        private const string PendingKey = "CityDriver.Trailer.Pending";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";
        private const int W = 1920, H = 1080, Fps = 30;

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
        private static string FrameDir => Path.Combine(Dir, "trailer");

        private static IEnumerator routine;
        private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private static RenderTexture rt; private static Texture2D tex;
        private static int frameNo, lastFrameCount = -1; private static bool recording;
        private static Camera cam; private static VehicleController car; private static PlayerSpawner spawner;
        private static VehicleCameraController camCtl; private static double started;
        private static System.Reflection.FieldInfo handlerField; private static object savedHandler;
        private static GameObject volGo, canvasGo, rigGo;
        private static DepthOfField dof; private static MotionBlur motion; private static Bloom bloom; private static ColorAdjustments grade; private static SplitToning split;
        private static FilmGrain grain; private static ChromaticAberration chroma; private static Vignette vig; private static LensDistortion lens;
        private static Image blackout, barTop, barBottom, flash;
        private static Text studioMain, studioSub, titleA, titleB, tagline; private static Text[] titleLayers = new Text[0];
        private static int origQuality; private static float origLod, origShadow;
        private static RectTransform logoGroup; private static Image shield; private static Text[] ribbonTexts = new Text[0];
        private static Vector3 posNow, lookNow; private static float fovNow = 60f, rollNow, dofDistance = 8f; private static bool dofOn = true;
        private static StringBuilder log, timeline;
        private static Sprite glowSprite; private static Font fontCond, fontTitle;
        private static Light keyLight, rimLight, moon; private static bool rigOn;
        private static readonly List<SpriteRenderer> glows = new List<SpriteRenderer>();
        private static SpriteRenderer[] tailGlow = new SpriteRenderer[2], headGlow = new SpriteRenderer[2];
        private static float convoyDist; private static float flashAlpha; private static float exposureOverride = float.NaN;
        private static TimeOfDay curTod = TimeOfDay.Sunset; private static int nightFlag;

        static TrailerCapture()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode) TryStart(); };
            if (EditorApplication.isPlaying) TryStart();
        }

        [BridgeCommand, MenuItem("City Driver/Promo/Record trailer frames", priority = 300)]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Trailer] Already playing."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            if (Directory.Exists(FrameDir)) Directory.Delete(FrameDir, true);
            Directory.CreateDirectory(FrameDir);
            File.Delete(Path.Combine(Dir, "trailer_done.txt"));
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void TryStart()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            log = new StringBuilder(); timeline = new StringBuilder(); frameNo = 0; lastFrameCount = -1; recording = false; started = EditorApplication.timeSinceStartup;
            routine = Run(); stack.Clear();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        private static bool Step()
        {
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            return false;
        }

        private static void Tick()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            if (EditorApplication.timeSinceStartup - started > 3 * 3600) { log.AppendLine("global timeout"); Finish(); return; }
            if (Time.frameCount == lastFrameCount) return;
            lastFrameCount = Time.frameCount;
            try
            {
                if (recording) SaveFrame();
                if (!Step()) { Finish(); return; }
                if (recording) UpdateRig();
                ApplyPose();
            }
            catch (Exception e) { log.AppendLine("EXCEPTION: " + e); Finish(); }
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick; recording = false;
            Time.captureFramerate = 0; Time.timeScale = 1f;
            try { if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u2 && origShadow > 0f) u2.shadowDistance = origShadow; QualitySettings.lodBias = origLod > 0f ? origLod : 1f; QualitySettings.SetQualityLevel(origQuality, true); } catch (Exception) { }
            if (handlerField != null && car != null) handlerField.SetValue(car, savedHandler);
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            File.WriteAllText(Path.Combine(Dir, "trailer_timeline.json"), "{\n\"fps\": " + Fps + ",\n\"frames\": " + frameNo + ",\n\"events\": [\n" + timeline.ToString().TrimEnd(',', '\n', '\r') + "\n]\n}\n");
            File.WriteAllText(Path.Combine(Dir, "trailer_done.txt"), $"frames={frameNo}\n{log}");
            EditorApplication.isPlaying = false;
        }

        private static void SaveFrame()
        {
            if (frameNo == 0 && Time.frameCount < 3) return;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false); RenderTexture.active = null;
            frameNo++;
            File.WriteAllBytes(Path.Combine(FrameDir, $"f_{frameNo:00000}.jpg"), tex.EncodeToJPG(92));
            if (frameNo % 60 == 0) File.WriteAllText(Path.Combine(Dir, "trailer_progress.txt"), $"{DateTime.Now:HH:mm:ss} frame {frameNo}");
        }

        private static void ApplyPose()
        {
            UpdateSubtitle();
            if (cam == null) return;
            Quaternion rot = Quaternion.LookRotation((lookNow - posNow).sqrMagnitude < 0.01f ? Vector3.forward : lookNow - posNow, Vector3.up) * Quaternion.AngleAxis(rollNow, Vector3.forward);
            cam.transform.SetPositionAndRotation(posNow, rot); cam.fieldOfView = fovNow;
            if (dof != null) { dof.active = dofOn; dof.focusDistance.Override(Mathf.Max(0.5f, dofDistance)); }
            flashAlpha = Mathf.Max(0f, flashAlpha - 0.22f); flash.color = new Color(1f, 0.97f, 0.9f, flashAlpha);
        }


        // ---------------------------------------------------------------------------------------------- dialogue subtitles (same JSON drives the spoken lines)
        [Serializable] private class DLine { public string id, voice, text, fx; public float t, dur; }
        [Serializable] private class DDoc { public DLine[] lines; }
        private static DLine[] dlg = new DLine[0]; private static Text subtitle; private static float subAlpha;

        private static void LoadDialogue(string name)
        {
            try { var path = Path.Combine(Dir, "trailer_tools", name); if (File.Exists(path)) dlg = JsonUtility.FromJson<DDoc>(File.ReadAllText(path).TrimStart('\uFEFF')).lines; }
            catch (Exception e) { log?.AppendLine("dialogue: " + e.Message); }
        }

        private static void UpdateSubtitle()
        {
            if (subtitle == null) return;
            float t = frameNo / (float)Fps; DLine cur = null;
            if (recording && dlg != null) foreach (var l in dlg) if (t >= l.t && t < l.t + l.dur + 0.25f) { cur = l; break; }
            if (cur == null) { subAlpha = Mathf.Max(0f, subAlpha - 0.2f); if (subAlpha <= 0f) subtitle.text = ""; }
            else
            {
                subAlpha = Mathf.Min(1f, subAlpha + 0.2f);
                subtitle.text = (cur.voice == "hazel" ? "RADIO   " : "") + cur.text;
                subtitle.color = cur.voice == "david" ? new Color(1f, 1f, 1f, 1f) : cur.voice == "zira" ? new Color(1f, 0.88f, 0.6f, 1f) : new Color(0.65f, 0.88f, 1f, 1f);
            }
            var c = subtitle.color; subtitle.color = new Color(c.r, c.g, c.b, subAlpha);
        }

        // ============================================================================================ small helpers
        private static void Pose(Vector3 pos, Vector3 look, float fov, float roll = 0f) { posNow = pos; lookNow = look; fovNow = fov; rollNow = roll; }
        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        private static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
        private static float Lin(float a, float b, float u) => Mathf.Lerp(a, b, Mathf.Clamp01(u));
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }
        private static float Win(float f, float a, float b, float fade = 12f) => Mathf.Clamp01((f - a) / fade) * Mathf.Clamp01((b - f) / fade);
        private static float Shake(float t, float amp, float seed) => (Mathf.PerlinNoise(t * 2.1f, seed) - 0.5f) * 2f * amp;

        private static void Mark(string kind, float strength = 1f, string tag = "") =>
            timeline.AppendLine("{\"t\": " + (frameNo / (float)Fps).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ", \"kind\": \"" + kind + "\", \"s\": " + strength.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ", \"tag\": \"" + tag + "\"},");

        // ============================================================================================ autopilot (pure pursuit along the real GPS route)
        private static int apIndex, apRouteVersion = -1;
        private static void Drive(float targetSpeed)
        {
            targetSpeed = Mathf.Min(targetSpeed, 15.5f);
            var nav = NavigationService.Instance; var route = nav != null ? nav.Route : null;
            var input = new VehicleInput();
            if (route == null || route.points.Count < 3 || car == null) { input.Brake = 0.6f; car?.SetManualInput(input); return; }
            if (nav.RouteVersion != apRouteVersion) { apRouteVersion = nav.RouteVersion; apIndex = 0; }
            if (nav.DistanceRemaining < 70f) PickFarDestination(500f);
            var pts = route.points; Vector3 p = car.transform.position;
            int best = apIndex; float bd = float.MaxValue;
            for (int i = Mathf.Max(0, apIndex - 4); i < Mathf.Min(pts.Count, apIndex + 50); i++) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            apIndex = best;
            float look = 8f + car.SpeedKmh * 0.12f, acc = 0f; Vector3 target = pts[pts.Count - 1], prev = pts[best];
            for (int i = best; i < pts.Count - 1; i++) { acc += Vector3.Distance(pts[i], pts[i + 1]); if (acc >= look) { target = pts[i + 1]; prev = pts[i]; break; } }
            Vector3 dir = Flat(target - prev);
            target += Vector3.Cross(dir, Vector3.up) * 1.9f;                                   // left lane (left-hand traffic)
            Vector3 to = target - p; to.y = 0f;
            float ang = Vector3.SignedAngle(car.transform.forward, to, Vector3.up);
            input.Steer = Mathf.Clamp(ang / 30f, -1f, 1f);
            float v = car.ForwardSpeed, vt = targetSpeed * (1f - 0.55f * Mathf.Clamp01(Mathf.Abs(ang) / 55f));
            input.Throttle = Mathf.Clamp01((vt - v) / 3f); input.Brake = v > vt + 3f ? Mathf.Clamp01((v - vt) / 8f) : 0f;
            car.SetManualInput(input);
        }

        private static void PickFarDestination(float minMetres)
        {
            var nav = NavigationService.Instance; if (nav == null || car == null) return;
            var markers = UnityEngine.Object.FindObjectsByType<LocationMarker>();
            Vector3 p = car.transform.position; var order = new List<LocationMarker>(markers);
            order.Sort((a, b) => Vector3.Distance(b.transform.position, p).CompareTo(Vector3.Distance(a.transform.position, p)));
            foreach (var m in order)
            {
                if (Vector3.Distance(m.transform.position, p) < minMetres) continue;
                if (nav.SetDestination(m.transform.position, "trailer") && nav.Route != null && nav.Route.Length >= minMetres) { apIndex = 0; apRouteVersion = -1; return; }
            }
        }

        private static void Convoy(float dist)
        {
            convoyDist = dist; var pm = PursuitManager.Instance; if (pm != null) pm.DebugTeleportPursuers(dist, 9999f);
        }

        private static void HoldConvoy()
        {
            var pm = PursuitManager.Instance; if (pm == null || convoyDist <= 0f || car == null) return;
            Vector3 cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf); int i = 0;
            foreach (Transform c in pm.transform)
            {
                if (c.name != "Pursuer" || !c.gameObject.activeSelf) continue;
                float lat = (i % 2 == 0 ? -1.4f : 1.5f) + Mathf.Sin(frameNo * 0.05f + i) * 0.5f;
                Vector3 pos = car.transform.position - cf * (convoyDist + i * 7f) + cr * lat; pos.y = car.transform.position.y;
                c.SetPositionAndRotation(pos, Quaternion.LookRotation(Quaternion.AngleAxis(lat * 2f, Vector3.up) * cf)); i++;
            }
        }

        private static Transform Chaser()
        {
            var pm = PursuitManager.Instance; if (pm == null) return null;
            foreach (Transform c in pm.transform) if (c.gameObject.activeSelf && c.name == "Pursuer") return c;
            return null;
        }

        // ============================================================================================ look (grade) presets
        private static void Look(TimeOfDay tod)
        {
            curTod = tod;
            switch (tod)
            {
                case TimeOfDay.Morning:
                case TimeOfDay.Day:
                    grade.postExposure.Override(tod == TimeOfDay.Morning ? 1.9f : 1.35f); grade.contrast.Override(12f); grade.saturation.Override(10f); grade.colorFilter.Override(new Color(1f, 0.97f, 0.92f));
                    split.shadows.Override(new Color(0.32f, 0.46f, 0.62f)); split.highlights.Override(new Color(1f, 0.8f, 0.55f)); split.balance.Override(10f);
                    bloom.intensity.Override(0.75f); bloom.threshold.Override(0.95f); grain.intensity.Override(0.22f); chroma.intensity.Override(0.06f); vig.intensity.Override(0.26f); break;
                case TimeOfDay.Sunset:
                    grade.postExposure.Override(1.6f); grade.contrast.Override(18f); grade.saturation.Override(18f); grade.colorFilter.Override(new Color(1f, 0.93f, 0.86f));
                    split.shadows.Override(new Color(0.16f, 0.34f, 0.52f)); split.highlights.Override(new Color(1f, 0.62f, 0.28f)); split.balance.Override(0f);
                    bloom.intensity.Override(1.0f); bloom.threshold.Override(0.85f); grain.intensity.Override(0.3f); chroma.intensity.Override(0.1f); vig.intensity.Override(0.32f); break;
                default:
                    grade.postExposure.Override(2.6f); grade.contrast.Override(22f); grade.saturation.Override(-6f); grade.colorFilter.Override(new Color(0.9f, 0.95f, 1.06f));
                    split.shadows.Override(new Color(0.08f, 0.24f, 0.5f)); split.highlights.Override(new Color(1f, 0.66f, 0.36f)); split.balance.Override(-18f);
                    bloom.intensity.Override(0.9f); bloom.threshold.Override(1.0f); grain.intensity.Override(0.42f); chroma.intensity.Override(0.2f); vig.intensity.Override(0.4f); break;
            }
            bool night = tod == TimeOfDay.Night; nightFlag = night ? 1 : 0;
        }

        // ============================================================================================ setup
        private static void BuildPost()
        {
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High;
            volGo = new GameObject("TrailerVolume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            bloom = prof.Add<Bloom>(true); bloom.scatter.Override(0.78f); bloom.tint.Override(new Color(1f, 0.92f, 0.82f)); bloom.threshold.Override(0.8f); bloom.intensity.Override(1f);
            grade = prof.Add<ColorAdjustments>(true);
            split = prof.Add<SplitToning>(true);
            vig = prof.Add<Vignette>(true); vig.smoothness.Override(0.6f);
            grain = prof.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin2); grain.response.Override(0.8f);
            chroma = prof.Add<ChromaticAberration>(true);
            lens = prof.Add<LensDistortion>(true); lens.intensity.Override(-0.12f);
            dof = prof.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focalLength.Override(58f); dof.aperture.Override(3.6f); dof.focusDistance.Override(10f);
            motion = prof.Add<MotionBlur>(true); motion.intensity.Override(0.3f); motion.quality.Override(MotionBlurQuality.Medium);
            vol.sharedProfile = prof;
            Look(TimeOfDay.Sunset);
        }

        private static Text Txt(Transform parent, string name, Font font, int size, FontStyle style, TextAnchor anchor, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = pos; r.sizeDelta = box;
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; return t;
        }

        private static Image Full(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var i = go.GetComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
        }

        private static Image Bar(Transform parent, string name, float anchorY, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = new Vector2(0, anchorY); r.anchorMax = new Vector2(1, anchorY); r.pivot = new Vector2(0.5f, anchorY); r.sizeDelta = new Vector2(0, height); r.anchoredPosition = Vector2.zero;
            var i = go.GetComponent<Image>(); i.color = Color.black; i.raycastTarget = false; return i;
        }

        private static Text GoldLine(Transform parent, string name, string text, int size, Vector2 pos, Font font, out Text[] layers)
        {
            var list = new List<Text>(); Text top = null;
            foreach (var (c, off, outline) in new[] { (new Color(0.30f, 0.12f, 0.02f), -8f, true), (new Color(0.93f, 0.55f, 0.08f), -3f, false), (new Color(1f, 0.9f, 0.5f), 3f, false) })
            {
                var t = Txt(parent, name, font, size, FontStyle.Normal, TextAnchor.MiddleCenter, pos + new Vector2(0, off), new Vector2(1700, size + 40)); t.text = text; t.color = c;
                if (outline) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.25f, 0.1f, 0.02f); o.effectDistance = new Vector2(5, -5); }
                list.Add(t); top = t;
            }
            layers = list.ToArray(); return top;
        }

        private static void BuildOverlay()
        {
            fontCond = Font.CreateDynamicFontFromOSFont("Bahnschrift SemiBold Condensed", 96); fontTitle = Font.CreateDynamicFontFromOSFont("Copperplate Gothic Bold", 96);
            if (fontTitle == null) fontTitle = fontCond; if (fontCond == null) fontCond = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasGo = new GameObject("TrailerCanvas", typeof(RectTransform)); var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f; canvas.sortingOrder = 10;
            var sc = canvasGo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(W, H); sc.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;
            flash = Full(root, "Flash", new Color(1, 0.97f, 0.9f, 0));
            blackout = Full(root, "Blackout", Color.black);
            studioMain = Txt(root, "StudioMain", fontCond, 66, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0, 24), new Vector2(1700, 100));
            studioSub = Txt(root, "StudioSub", fontCond, 28, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0, -42), new Vector2(1700, 50));
            subtitle = Txt(root, "Subtitle", fontCond, 46, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0, -338), new Vector2(1700, 70)); var so = subtitle.gameObject.AddComponent<Outline>(); so.effectColor = new Color(0, 0, 0, 0.9f); so.effectDistance = new Vector2(3, -3);
            // finale: emblem, CITY DRIVER (day), UNDERCOVER (night), COMING SOON
            var lg = new GameObject("Logo", typeof(RectTransform)); lg.transform.SetParent(root, false); logoGroup = (RectTransform)lg.transform; logoGroup.anchorMin = logoGroup.anchorMax = new Vector2(0.5f, 0.5f); logoGroup.sizeDelta = new Vector2(10, 10);
            var sh = new GameObject("Shield", typeof(RectTransform), typeof(Image)); sh.transform.SetParent(logoGroup, false);
            var sr = (RectTransform)sh.transform; sr.sizeDelta = new Vector2(176, 210); sr.anchoredPosition = new Vector2(0, 150);
            var tx = IntroSequence.EmblemTexture(); shield = sh.GetComponent<Image>(); shield.sprite = Sprite.Create(tx, new Rect(0, 0, tx.width, tx.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            titleA = GoldLine(logoGroup, "CityDriver", "CITY DRIVER", 132, new Vector2(0, -8), fontTitle, out titleLayers);
            var rib = new GameObject("Ribbon", typeof(RectTransform), typeof(Image)); rib.transform.SetParent(logoGroup, false); var rr = (RectTransform)rib.transform; rr.sizeDelta = new Vector2(700, 78); rr.anchoredPosition = new Vector2(0, -122); rib.GetComponent<Image>().color = new Color(0.55f, 0.06f, 0.09f);
            titleB = Txt(rib.transform, "Undercover", fontTitle, 52, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(700, 78)); titleB.text = "UNDERCOVER"; titleB.color = new Color(1f, 0.92f, 0.8f);
            tagline = Txt(logoGroup, "Soon", fontCond, 40, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0, -232), new Vector2(1200, 60)); tagline.color = new Color(0.95f, 0.85f, 0.62f);
            ribbonTexts = new[] { titleB, tagline };
            SetLogo(0f, 0f, 0f, 0f);
            float bar = (H - W / 2.39f) * 0.5f; barTop = Bar(root, "BarTop", 1, bar); barBottom = Bar(root, "BarBottom", 0, bar);
        }

        // alpha per element: emblem, CITY DRIVER, UNDERCOVER ribbon, tagline
        private static void SetLogo(float aShield, float aTitle, float aRibbon, float aTag)
        {
            Fade(shield, aShield);
            foreach (var t in logoGroup.GetComponentsInChildren<Text>(true)) if (t == titleB || t == tagline) continue; else { var c = t.color; t.color = new Color(c.r, c.g, c.b, aTitle); }
            var rib = titleB.transform.parent.GetComponent<Image>(); Fade(rib, aRibbon);
            var cb = titleB.color; titleB.color = new Color(cb.r, cb.g, cb.b, aRibbon);
            var ct = tagline.color; tagline.color = new Color(ct.r, ct.g, ct.b, aTag);
        }
        private static void Fade(Image i, float a) { var c = i.color; i.color = new Color(c.r, c.g, c.b, a); }
        private static string Tracked(string s, int spaces) { var sb = new StringBuilder(); for (int i = 0; i < s.Length; i++) { sb.Append(s[i]); if (i < s.Length - 1) sb.Append(s[i] == ' ' ? new string(' ', spaces * 2) : new string(' ', spaces)); } return sb.ToString(); }

        // ---------------------------------------------------------------------------------------------- lights & glow
        private static Sprite Glow()
        {
            if (glowSprite != null) return glowSprite;
            const int S = 128; var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f; float d = Mathf.Sqrt(u * u + v * v); float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.4f); t.SetPixel(x, y, new Color(1, 1, 1, a)); }
            t.Apply(); return glowSprite = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static SpriteRenderer MakeGlow(Transform parent, Vector3 local, float size, Color c)
        {
            var go = new GameObject("TGlow"); go.transform.SetParent(parent, false); go.transform.localPosition = local; go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = Glow(); sr.color = c; sr.sortingOrder = 50; glows.Add(sr); return sr;
        }

        private static void BuildRig()
        {
            rigGo = new GameObject("TrailerRig");
            keyLight = new GameObject("Key").AddComponent<Light>(); keyLight.transform.SetParent(rigGo.transform); keyLight.type = LightType.Spot; keyLight.spotAngle = 70f; keyLight.range = 26f; keyLight.intensity = 90f; keyLight.color = new Color(1f, 0.78f, 0.55f); keyLight.shadows = LightShadows.None;
            rimLight = new GameObject("Rim").AddComponent<Light>(); rimLight.transform.SetParent(rigGo.transform); rimLight.type = LightType.Spot; rimLight.spotAngle = 80f; rimLight.range = 24f; rimLight.intensity = 70f; rimLight.color = new Color(0.4f, 0.65f, 1f); rimLight.shadows = LightShadows.None;
            moon = new GameObject("Moon").AddComponent<Light>(); moon.transform.SetParent(rigGo.transform); moon.type = LightType.Directional; moon.color = new Color(0.55f, 0.62f, 0.85f); moon.intensity = 0.0f; moon.shadows = LightShadows.None; moon.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            tailGlow[0] = MakeGlow(car.transform, new Vector3(-0.72f, 0.92f, -2.15f), 1.5f, new Color(1f, 0.1f, 0.05f, 0.9f)); tailGlow[1] = MakeGlow(car.transform, new Vector3(0.72f, 0.92f, -2.15f), 1.5f, new Color(1f, 0.1f, 0.05f, 0.9f));
            headGlow[0] = MakeGlow(car.transform, new Vector3(-0.68f, 0.82f, 2.2f), 0.95f, new Color(1f, 0.95f, 0.8f, 0.55f)); headGlow[1] = MakeGlow(car.transform, new Vector3(0.68f, 0.82f, 2.2f), 0.95f, new Color(1f, 0.95f, 0.8f, 0.55f));
        }

        private static readonly Dictionary<Transform, SpriteRenderer[]> policeGlow = new Dictionary<Transform, SpriteRenderer[]>();
        private static readonly Dictionary<Transform, Light[]> policeLight = new Dictionary<Transform, Light[]>();

        private static void UpdateRig()
        {
            if (rigGo == null || cam == null) return;
            HoldConvoy();
            bool night = nightFlag == 1;
            Vector3 cp = car.transform.position, cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf);
            keyLight.enabled = rimLight.enabled = rigOn; moon.intensity = night ? 0.65f : 0f;
            if (rigOn)
            {
                keyLight.transform.position = cp + cf * 6f + cr * 4f + Vector3.up * 4.5f; keyLight.transform.rotation = Quaternion.LookRotation(cp + Vector3.up * 0.8f - keyLight.transform.position);
                rimLight.transform.position = cp - cf * 5f - cr * 4.5f + Vector3.up * 3.5f; rimLight.transform.rotation = Quaternion.LookRotation(cp + Vector3.up * 0.8f - rimLight.transform.position);
            }
            foreach (var g in tailGlow) { g.enabled = true; g.color = new Color(1f, 0.1f, 0.05f, night ? 0.8f : 0.2f); g.transform.localScale = Vector3.one * (night ? 0.8f : 0.45f); }
            foreach (var g in headGlow) { g.enabled = night; }
            // police: flashing red/blue lights + glow on every active pursuer
            int phase = (frameNo / 4) % 2; var pm = PursuitManager.Instance;
            if (pm != null)
                foreach (Transform c in pm.transform)
                {
                    if (c.name != "Pursuer") continue;
                    if (!policeGlow.TryGetValue(c, out var gl))
                    {
                        gl = new[] { MakeGlow(c, new Vector3(-0.45f, 1.85f, 0.2f), 1.4f, new Color(1f, 0.1f, 0.1f, 1f)), MakeGlow(c, new Vector3(0.45f, 1.85f, 0.2f), 1.4f, new Color(0.15f, 0.35f, 1f, 1f)) };
                        var lr = new GameObject("TLightR").AddComponent<Light>(); lr.transform.SetParent(c, false); lr.transform.localPosition = new Vector3(-0.5f, 2.2f, 0.2f); lr.type = LightType.Point; lr.range = 15f; lr.color = new Color(1f, 0.1f, 0.1f); lr.intensity = 60f;
                        var lb = new GameObject("TLightB").AddComponent<Light>(); lb.transform.SetParent(c, false); lb.transform.localPosition = new Vector3(0.5f, 2.2f, 0.2f); lb.type = LightType.Point; lb.range = 15f; lb.color = new Color(0.15f, 0.35f, 1f); lb.intensity = 60f;
                        policeGlow[c] = gl; policeLight[c] = new[] { lr, lb };
                    }
                    bool on = c.gameObject.activeSelf && night;
                    gl[0].enabled = on && phase == 0; gl[1].enabled = on && phase == 1; policeLight[c][0].enabled = on && phase == 0; policeLight[c][1].enabled = on && phase == 1;
                }
            foreach (var g in glows) if (g != null && g.enabled) g.transform.rotation = cam.transform.rotation;
        }

        // ============================================================================================ shot machinery
        private delegate void CamFn(int f, int n);
        private static Vector3 anchorPos; private static Vector3 anchorLook;
        private static int shotCounter;

        /// <summary>Plays one shot of n frames. The first frame is a cut (flash + hit marker); everything is driven by <paramref name="cam"/> each frame.</summary>
        private static IEnumerator Shot(string tag, int n, CamFn camFn, float speed = 14f, TimeOfDay? tod = null, Weather? weather = null, float hit = 0f, Func<float, float> timeScale = null, bool lights = false, float flashOnCut = 0.5f, float fadeIn = 0f)
        {
            var env = WorldEnvironment.Instance; int blackFrames = 0;
            if (tod.HasValue && (tod.Value != curTod)) { blackFrames = 3; env.SetTimeOfDay(tod.Value, 0.01f); Look(tod.Value); }
            else if (tod.HasValue) Look(tod.Value);
            if (weather.HasValue) env.SetWeather(weather.Value, 0.01f);
            rigOn = lights; shotCounter++; dof.aperture.Override(3.6f);
            if (!float.IsNaN(exposureOverride)) grade.postExposure.Override(exposureOverride);
            Mark("cut", hit, tag); if (hit > 0f) Mark("hit", hit, tag);
            if (flashOnCut > 0f) flashAlpha = flashOnCut;
            for (int f = 0; f < n; f++)
            {
                float u = f / (float)Mathf.Max(1, n - 1);
                Time.timeScale = timeScale != null ? Mathf.Max(0.05f, timeScale(u)) : 1f;
                Drive(speed); camFn(f, n);
                blackout.color = new Color(0, 0, 0, f < blackFrames ? 1f : fadeIn > 0f ? Mathf.Clamp01(1f - f / fadeIn) : 0f);
                yield return null;
            }
            Time.timeScale = 1f;
        }

        // ---- camera moves relative to the car
        private static CamFn Chase(float back, float height, float ahead, float fov0, float fov1, float sway = 0.4f) => (f, n) =>
        {
            Vector3 cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position; float t = f / 30f;
            Pose(cp - cf * back + Vector3.up * height + cr * (Mathf.Sin(t * 1.3f) * sway), cp + cf * ahead + Vector3.up * 0.9f, Lin(fov0, fov1, f / (float)n), Mathf.Sin(t * 0.9f) * 0.8f);
            dofOn = true; dofDistance = back + 1f;
        };
        private static CamFn FrontTrack(float dist, float side0, float side1, float h, float fov) => (f, n) =>
        {
            Vector3 cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position; float u = f / (float)n;
            Pose(cp + cf * dist + cr * Lin(side0, side1, Ease(u)) + Vector3.up * h, cp + Vector3.up * 0.75f, fov + Mathf.Sin(f * 0.05f) * 1.5f, Lin(-2f, 2f, u));
            dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
        };
        private static CamFn SideTrack(float side, float fwd0, float fwd1, float h, float fov) => (f, n) =>
        {
            Vector3 cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position; float u = f / (float)n;
            Pose(cp + cr * side + cf * Lin(fwd0, fwd1, u) + Vector3.up * h, cp + cf * 1.2f + Vector3.up * 0.7f, fov);
            dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
        };
        private static CamFn PassBy(float ahead, float side, float h, float fov, bool dolly = false) => (f, n) =>
        {
            if (f == 0) { Vector3 cf0 = Flat(car.transform.forward), cr0 = Vector3.Cross(Vector3.up, cf0); anchorPos = car.transform.position + cf0 * ahead + cr0 * side + Vector3.up * h; }
            Vector3 cp = car.transform.position;
            Pose(anchorPos, cp + Vector3.up * 0.8f, dolly ? Lin(fov, fov * 0.55f, f / (float)n) : fov);
            dofOn = true; dofDistance = Vector3.Distance(anchorPos, cp);
        };
        private static CamFn Wheel(float fov) => (f, n) =>
        {
            Vector3 cf = Flat(car.transform.forward), cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position; float u = f / (float)n;
            Vector3 wheel = cp + cf * 1.35f + cr * 0.95f + Vector3.up * 0.38f;
            Pose(wheel + cr * Lin(1.9f, 1.3f, u) + cf * Lin(-1.0f, 1.2f, u) + Vector3.up * 0.15f, wheel, fov);
            dofOn = true; dofDistance = Vector3.Distance(posNow, wheel); dof.aperture.Override(1.6f);
        };
        private static CamFn RoadLevel(float fov) => (f, n) =>
        {
            Vector3 cf = Flat(car.transform.forward), cp = car.transform.position;
            Pose(cp - cf * 6f + Vector3.up * 0.28f + Vector3.Cross(Vector3.up, cf) * 0.4f, cp + cf * 4f + Vector3.up * 0.55f, fov, Lin(0f, 4f, f / (float)n));
            dofOn = true; dofDistance = 6.5f;
        };
        private static CamFn TopDown(float h0, float h1, float spin) => (f, n) =>
        {
            Vector3 cp = car.transform.position, cf = Flat(car.transform.forward); float u = f / (float)n;
            Pose(cp + Vector3.up * Lin(h0, h1, u) - cf * 1.5f, cp, 38f, f * spin); dofOn = false;
        };
        private static CamFn Orbit(float radius, float height, float deg0, float deg1, float fov) => (f, n) =>
        {
            Vector3 cp = car.transform.position, cf = Flat(car.transform.forward); float u = f / (float)n;
            Vector3 off = Quaternion.AngleAxis(Lin(deg0, deg1, Ease(u)), Vector3.up) * -cf * radius;
            Pose(cp + off + Vector3.up * height, cp + Vector3.up * 0.9f, fov); dofOn = true; dofDistance = radius;
        };
        private static CamFn ChaserCam(float behind, float height, float fov) => (f, n) =>
        {
            var c = Chaser(); Vector3 cp = car.transform.position;
            if (c == null) { Chase(9f, 3f, 8f, 60f, 60f)(f, n); return; }
            Vector3 cf = Flat(c.forward); Pose(c.position - cf * behind + Vector3.up * height + Vector3.Cross(Vector3.up, cf) * Mathf.Sin(f * 0.1f) * 0.3f, cp + Vector3.up * 0.9f, fov);
            dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
        };
        private static CamFn Crane(float h0, float h1, float dist0, float dist1, float fov) => (f, n) =>
        {
            Vector3 cp = car.transform.position, cf = Flat(car.transform.forward); float u = Ease(f / (float)n);
            Pose(cp - cf * Lin(dist0, dist1, u) + Vector3.up * Lin(h0, h1, u) + Vector3.Cross(Vector3.up, cf) * 0f, cp + cf * 4f + Vector3.up * 1f, fov);
            dofOn = true; dofDistance = Lin(dist0, dist1, u);
        };

        // ============================================================================================ the film
        private static IEnumerator Run()
        {
            while ((spawner = UnityEngine.Object.FindAnyObjectByType<PlayerSpawner>()) == null || spawner.Vehicle == null)
            {
                if (EditorApplication.timeSinceStartup - started > 300) { log.AppendLine("player car never spawned"); yield break; }
                yield return null;
            }
            car = spawner.Vehicle; cam = Camera.main;
            // highest quality level, long shadows, full LODs: this is an offline render
            origQuality = QualitySettings.GetQualityLevel(); origLod = QualitySettings.lodBias;
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true); QualitySettings.lodBias = 2.5f;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) { origShadow = urp.shadowDistance; urp.shadowDistance = 150f; }
            log.AppendLine("quality: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            camCtl = UnityEngine.Object.FindAnyObjectByType<VehicleCameraController>(); if (camCtl != null) camCtl.enabled = false;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>()) if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) c.enabled = false;
            handlerField = typeof(VehicleController).GetField("inputHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            savedHandler = handlerField?.GetValue(car); handlerField?.SetValue(car, null);
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 4 }; rt.Create(); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            cam.targetTexture = rt; BuildPost(); BuildOverlay(); BuildRig();
            var env = WorldEnvironment.Instance; env.AutoCycle = false; env.SetTimeOfDay(TimeOfDay.Morning, 0.01f); env.SetWeather(Weather.Clear, 0.01f); curTod = TimeOfDay.Morning; Look(TimeOfDay.Morning);
            Time.captureFramerate = Fps; Time.timeScale = 1f;
            Vector3 fwd = Flat(car.transform.forward); Vector3 right = Vector3.Cross(Vector3.up, fwd); Vector3 refPos = car.transform.position;
            Pose(refPos + Vector3.up * 100f - fwd * 250f, refPos + fwd * 100f, 55f); blackout.color = Color.black;
            PickFarDestination(600f);
            yield return Frames(24);
            LoadDialogue("dialogue_cinematic.json"); recording = true; Mark("start"); log.AppendLine("recording started");

            // ======================================================== studio card (2.7 s)
            for (int f = 0; f < 80; f++)
            {
                float a = Win(f, 6, 74, 18); int sp = Mathf.RoundToInt(Lin(5, 1, Ease(f / 60f)));
                studioMain.text = Tracked("KARTHI LAB", sp); studioSub.text = Tracked("PRESENTS", sp > 1 ? 3 : 2);
                studioMain.color = new Color(0.96f, 0.88f, 0.66f, a); studioSub.color = new Color(0.8f, 0.78f, 0.74f, a * 0.9f);
                yield return null;
            }
            studioMain.color = studioSub.color = new Color(0, 0, 0, 0);

            // ======================================================== ACT I - an ordinary day (warm, calm)
            Mark("section", 1f, "day");
            // 1 aerial dawn, crane down over the skyline into the street
            Vector3[] P = { refPos - fwd * 300f + right * -50f + Vector3.up * 170f, refPos - fwd * 160f + Vector3.up * 90f, refPos - fwd * 62f + Vector3.up * 34f, refPos - fwd * 22f + Vector3.up * 8f };
            Vector3[] L = { refPos + fwd * 140f + Vector3.up * 10f, refPos + fwd * 90f + Vector3.up * 8f, refPos + fwd * 40f + Vector3.up * 4f, refPos + fwd * 12f + Vector3.up * 1.5f };
            yield return Shot("aerial dawn", 150, (f, n) =>
            {
                float u = f / (float)n * 3f; int s = Mathf.Min(2, (int)u); float lu = u - s; int i0 = Mathf.Max(0, s - 1), i3 = Mathf.Min(3, s + 2);
                Pose(CatmullRom(P[i0], P[s], P[s + 1], P[i3], lu), CatmullRom(L[i0], L[s], L[s + 1], L[i3], lu), Lin(52f, 40f, f / (float)n), Lin(3f, 0f, f / (float)n)); dofOn = false;
            }, 9f, TimeOfDay.Morning, Weather.Clear, 0f, null, false, 0f, 14f);
            yield return Shot("morning pass-by", 80, PassBy(34f, -3.8f, 1.1f, 30f), 13f, TimeOfDay.Morning, null, 0.25f);
            yield return Shot("morning side track", 66, SideTrack(5.2f, 3f, -1f, 1.15f, 40f), 13f, TimeOfDay.Day, null, 0.2f);
            yield return Shot("day grille macro", 40, FrontTrack(3.8f, 1.7f, -1.3f, 0.8f, 34f), 13f, TimeOfDay.Day, null, 0.2f, u => Mathf.Lerp(0.45f, 0.3f, u));
            yield return Shot("day front low", 70, FrontTrack(6.8f, 3.2f, -2.6f, 0.95f, 44f), 14f, TimeOfDay.Day, null, 0.2f);
            yield return Shot("day rear chase", 84, Chase(8.2f, 2.6f, 8f, 58f, 66f), 15f, TimeOfDay.Day, null, 0.2f);
            yield return Shot("day top-down", 54, TopDown(44f, 30f, 0.1f), 15f, TimeOfDay.Day, null, 0.15f);
            yield return Shot("day crane", 72, Crane(0.8f, 6f, 9f, 11f, 50f), 14f, TimeOfDay.Day, null, 0.15f);

            // ======================================================== dusk: the light starts to turn
            Mark("section", 1f, "dusk");
            yield return Shot("sunset pass-by", 78, PassBy(32f, -3.4f, 1.0f, 28f, true), 14f, TimeOfDay.Sunset, null, 0.4f, null, true);
            yield return Shot("sunset orbit", 84, Orbit(9f, 2.0f, -150f, -60f, 42f), 14f, TimeOfDay.Sunset, null, 0.2f, null, true);

            // ======================================================== ACT II - the same street, at night
            Mark("section", 1f, "night");
            var pm = PursuitManager.Instance; var plan = PursuitPlan.ForLevel(5); plan.speedScale = 0.5f; if (pm != null) pm.Begin(plan);
            yield return Shot("night match-cut pass-by", 72, PassBy(34f, -3.8f, 1.1f, 30f), 15f, TimeOfDay.Night, Weather.Rain, 1f, null, true, 0.9f);       // same framing as the morning pass-by
            yield return Shot("night front low", 60, FrontTrack(6.5f, 3.0f, -2.8f, 0.9f, 42f), 16f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("night grille macro slow-mo", 44, FrontTrack(3.6f, 1.6f, -1.3f, 0.75f, 34f), 16f, TimeOfDay.Night, null, 0.5f, u => Mathf.Lerp(0.4f, 0.25f, u), true);
            Convoy(19f);
            yield return Shot("chaser cam", 78, ChaserCam(7f, 2.4f, 52f), 17f, TimeOfDay.Night, null, 0.6f, null, true);
            Convoy(17f);
            yield return Shot("night rear chase high", 78, Chase(9f, 3.6f, 9f, 66f, 74f), 17f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("night road-level", 48, RoadLevel(62f), 17f, TimeOfDay.Night, null, 0.4f, null, true);
            yield return Shot("night side track", 54, SideTrack(5.0f, 3f, -2f, 1.0f, 40f), 17f, TimeOfDay.Night, null, 0.4f, null, true);
            Convoy(15f);
            yield return Shot("chaser cam 2", 54, ChaserCam(5.5f, 1.8f, 60f), 18f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("night top-down", 42, TopDown(40f, 26f, 0.5f), 18f, TimeOfDay.Night, null, 0.4f);
            yield return Shot("night pass-by 2", 66, PassBy(28f, -3.4f, 0.9f, 26f, true), 18f, TimeOfDay.Night, null, 0.6f, u => Mathf.Lerp(1f, 0.3f, Ease(u * 1.5f)), true);

            // ======================================================== climax: quick cuts
            Mark("section", 1f, "climax");
            Convoy(14f);
            yield return Shot("cut1", 22, ChaserCam(6f, 1.6f, 58f), 16f, TimeOfDay.Night, null, 0.7f, null, true);
            yield return Shot("cut2", 22, RoadLevel(70f), 19f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("cut3", 22, ChaserCam(4f, 1.4f, 66f), 19f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("cut4", 20, FrontTrack(4.6f, 2.0f, -2.0f, 0.7f, 52f), 20f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("cut5", 20, SideTrack(3.8f, 2.5f, -2.5f, 0.8f, 46f), 20f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("cut6", 18, TopDown(22f, 18f, 1.2f), 20f, TimeOfDay.Night, null, 0.5f);
            yield return Shot("cut7", 18, Chase(5.2f, 1.5f, 7f, 74f, 80f, 0.8f), 21f, TimeOfDay.Night, null, 0.5f, null, true);
            yield return Shot("slow-mo hero", 96, Orbit(7.5f, 1.4f, -35f, 40f, 36f), 17f, TimeOfDay.Night, null, 1f, u => Mathf.Lerp(0.5f, 0.18f, u), true);

            // ======================================================== the flicker between the two lives, then the title
            Mark("section", 1f, "finale");
            convoyDist = 0f; if (pm != null) pm.End();
            // same framing (rear chase) alternating day / night, faster each time
            var rear = Chase(8f, 2.4f, 8f, 56f, 56f, 0f);
            int[] flickerLens = { 9, 8, 6, 5, 4, 4 }; bool nightFlip = false; var finalStart = frameNo;
            foreach (int len in flickerLens)
            {
                yield return Shot(nightFlip ? "flicker night" : "flicker day", len, rear, 8f, nightFlip ? TimeOfDay.Night : TimeOfDay.Day, nightFlip ? Weather.Rain : Weather.Clear, nightFlip ? 0.4f : 0.2f, null, nightFlip, 0.5f);
                nightFlip = !nightFlip;
            }
            // title: emblem + CITY DRIVER over the day frame ... UNDERCOVER over the night frame
            exposureOverride = 1.0f; yield return Shot("title day", 54, (f, n) => { rear(f, n); dofOn = true; float a = Ease(f / 22f); SetLogo(a, a, 0f, 0f); foreach (var tl in titleLayers) tl.text = Tracked("CITY DRIVER", Mathf.RoundToInt(Lin(2, 0, Ease(f / 40f)))); }, 0f, TimeOfDay.Day, Weather.Clear, 0.8f);
            Mark("title", 1f, "CITY DRIVER");
            exposureOverride = 0.9f; yield return Shot("title night", 66, (f, n) => { rear(f, n); float a = Ease(f / 16f); SetLogo(1f, 1f, a, 0f); }, 0f, TimeOfDay.Night, Weather.Rain, 1f, null, true, 0.9f);
            Mark("title", 1f, "UNDERCOVER");
            grade.postExposure.Override(0.8f); bloom.intensity.Override(0.5f);
            // hold on black with the finished title card
            for (int f = 0; f < 110; f++)
            {
                float k = Ease(f / 20f); blackout.color = new Color(0, 0, 0, Mathf.Lerp(0f, 0.86f, k));
                float tg = Ease((f - 30) / 20f); SetLogo(1f, 1f, 1f, tg); tagline.text = Tracked("COMING SOON  -  ANDROID", Mathf.RoundToInt(Lin(3, 1, Ease((f - 30) / 40f))));
                if (f > 94) blackout.color = new Color(0, 0, 0, Mathf.Lerp(0.86f, 1f, (f - 94) / 16f));
                rear(f, 110);
                yield return null;
            }
            Mark("end");
            yield return Frames(2);
            log.AppendLine($"done, frames={frameNo}");
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }
    }
}
