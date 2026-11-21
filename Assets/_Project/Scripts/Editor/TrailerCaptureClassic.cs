using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CityDriver.CameraSystem;
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
    /// Records the promo trailer from the real game: a fixed 30 fps, 1920x1080, 2.39:1-letterboxed frame sequence (Bridge/trailer/f_00001.jpg ...)
    /// rendered from the live city with scripted camera shots, an autopilot driving the player's car along real GPS routes, bloom /
    /// depth of field / motion blur / colour grade, and title cards. Output is deterministic (Time.captureFramerate), so it is not
    /// affected by how slowly the Editor renders. Frames are muxed with the synthesised score by Bridge/trailer_tools/make_trailer.py.
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerCaptureClassic
    {
        private const string PendingKey = "CityDriver.TrailerClassic.Pending";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";
        private const int W = 1920, H = 1080, Fps = 30;

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
        private static string FrameDir => Path.Combine(Dir, "trailer_classic");

        private static IEnumerator routine;
        private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private static RenderTexture rt; private static Texture2D tex;
        private static int frameNo, lastFrameCount = -1; private static bool recording;
        private static Camera cam; private static VehicleController car; private static PlayerSpawner spawner;
        private static VehicleCameraController camCtl; private static double started;
        private static System.Reflection.FieldInfo handlerField; private static object savedHandler;
        private static GameObject volGo, canvasGo; private static DepthOfField dof; private static MotionBlur motion; private static Bloom bloom; private static ColorAdjustments grade;
        private static Image blackout, barTop, barBottom, flash; private static Text titleText, subText;
        private static RectTransform logoGroup; private static Text[] logoLines = new Text[0];
        private static Vector3 posNow, lookNow; private static float fovNow = 60f, rollNow, dofDistance = 8f; private static bool dofOn = true;
        private static StringBuilder log;

        static TrailerCaptureClassic()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode) TryStart(); };
            if (EditorApplication.isPlaying) TryStart();
        }

        [BridgeCommand, MenuItem("City Driver/Promo/Record classic trailer frames", priority = 301)]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Trailer] Already playing."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            if (Directory.Exists(FrameDir)) Directory.Delete(FrameDir, true);
            Directory.CreateDirectory(FrameDir);
            File.Delete(Path.Combine(Dir, "trailer_classic_done.txt"));
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void TryStart()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            log = new StringBuilder(); frameNo = 0; lastFrameCount = -1; recording = false; started = EditorApplication.timeSinceStartup;
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
            // one step per rendered game frame: the routine sets the pose for this frame, the previous frame's picture is read back
            if (Time.frameCount == lastFrameCount) return;
            lastFrameCount = Time.frameCount;
            try
            {
                if (recording) SaveFrame();
                if (!Step()) { Finish(); return; }
                ApplyPose();
            }
            catch (Exception e) { log.AppendLine("EXCEPTION: " + e); Finish(); }
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick; recording = false;
            Time.captureFramerate = 0;
            if (handlerField != null && car != null) handlerField.SetValue(car, savedHandler);
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            File.WriteAllText(Path.Combine(Dir, "trailer_classic_done.txt"), $"frames={frameNo}\n{log}");
            EditorApplication.isPlaying = false;
        }

        private static void SaveFrame()
        {
            if (frameNo == 0 && Time.frameCount < 3) return;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false); RenderTexture.active = null;
            frameNo++;
            File.WriteAllBytes(Path.Combine(FrameDir, $"f_{frameNo:00000}.jpg"), tex.EncodeToJPG(90));
            if (frameNo % 60 == 0) File.WriteAllText(Path.Combine(Dir, "trailer_progress.txt"), $"{DateTime.Now:HH:mm:ss} frame {frameNo}");
        }

        private static void ApplyPose()
        {
            UpdateSubtitle();
            if (cam == null) return;
            Quaternion rot = Quaternion.LookRotation((lookNow - posNow).sqrMagnitude < 0.01f ? Vector3.forward : lookNow - posNow, Vector3.up) * Quaternion.AngleAxis(rollNow, Vector3.forward);
            cam.transform.SetPositionAndRotation(posNow, rot); cam.fieldOfView = fovNow;
            if (dof != null) { dof.active = dofOn; dof.focusDistance.Override(Mathf.Max(0.5f, dofDistance)); }
        }

        // ============================================================================================ helpers for the shots

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

        private static void Pose(Vector3 pos, Vector3 look, float fov, float roll = 0f) { posNow = pos; lookNow = look; fovNow = fov; rollNow = roll; }
        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        private static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        private static float Win(float f, float a, float b, float fade = 18f) => Mathf.Clamp01((f - a) / fade) * Mathf.Clamp01((b - f) / fade);   // fade in at a, out at b

        private static void SetTitle(string main, string sub, float alpha)
        {
            titleText.text = main; subText.text = sub;
            titleText.color = new Color(1f, 0.95f, 0.86f, alpha); subText.color = new Color(0.95f, 0.78f, 0.45f, alpha * 0.95f);
            var ts = titleText.GetComponent<Outline>(); ts.effectColor = new Color(0, 0, 0, alpha * 0.8f);
        }

        // ============================================================================================ autopilot (pure pursuit along the real GPS route)
        private static int apIndex, apRouteVersion = -1;
        private static void Drive(float targetSpeed)
        {
            var nav = NavigationService.Instance; var route = nav != null ? nav.Route : null;
            var input = new CityDriver.Controls.VehicleInput();
            if (route == null || route.points.Count < 3 || car == null) { input.Brake = 0.6f; car?.SetManualInput(input); return; }
            if (nav.RouteVersion != apRouteVersion) { apRouteVersion = nav.RouteVersion; apIndex = 0; }
            var pts = route.points; Vector3 p = car.transform.position;
            int best = apIndex; float bd = float.MaxValue;
            for (int i = Mathf.Max(0, apIndex - 4); i < Mathf.Min(pts.Count, apIndex + 50); i++) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            apIndex = best;
            float look = 8f + car.SpeedKmh * 0.12f, acc = 0f; Vector3 target = pts[pts.Count - 1], prev = pts[best];
            for (int i = best; i < pts.Count - 1; i++) { acc += Vector3.Distance(pts[i], pts[i + 1]); if (acc >= look) { target = pts[i + 1]; prev = pts[i]; break; } }
            Vector3 dir = target - prev; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : car.transform.forward;
            target += Vector3.Cross(dir, Vector3.up) * 1.9f;                                   // keep to the left lane (left-hand traffic)
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

        // ============================================================================================ setup
        private static void BuildPost()
        {
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High;
            volGo = new GameObject("TrailerVolume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            bloom = prof.Add<Bloom>(true); bloom.intensity.Override(0.9f); bloom.threshold.Override(0.8f); bloom.scatter.Override(0.75f); bloom.tint.Override(new Color(1f, 0.92f, 0.8f));
            grade = prof.Add<ColorAdjustments>(true); grade.postExposure.Override(0.55f); grade.contrast.Override(18f); grade.saturation.Override(16f); grade.colorFilter.Override(new Color(1f, 0.94f, 0.88f));
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.3f); vig.smoothness.Override(0.6f);
            dof = prof.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focalLength.Override(55f); dof.aperture.Override(4.2f); dof.focusDistance.Override(10f);
            motion = prof.Add<MotionBlur>(true); motion.intensity.Override(0.28f); motion.quality.Override(MotionBlurQuality.Medium);
            vol.sharedProfile = prof;
        }

        private static Text Txt(Transform parent, string name, int size, FontStyle style, TextAnchor anchor, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = pos; r.sizeDelta = box;
            var t = go.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.fontStyle = style; t.alignment = anchor; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; return t;
        }

        private static Image Full(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var i = go.GetComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
        }

        private static void BuildOverlay()
        {
            canvasGo = new GameObject("TrailerCanvas", typeof(RectTransform)); var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f; canvas.sortingOrder = 10;
            var sc = canvasGo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(W, H); sc.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;
            titleText = Txt(root, "Title", 92, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0, 285), new Vector2(1800, 140)); titleText.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4, -4);
            subText = Txt(root, "Sub", 38, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0, 205), new Vector2(1800, 60));
            subtitle = Txt(root, "Subtitle", 46, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0, -338), new Vector2(1700, 70)); var so = subtitle.gameObject.AddComponent<Outline>(); so.effectColor = new Color(0, 0, 0, 0.9f); so.effectDistance = new Vector2(3, -3);
            flash = Full(root, "Flash", new Color(1, 0.96f, 0.85f, 0));
            blackout = Full(root, "Blackout", Color.black);
            float bar = (H - W / 2.39f) * 0.5f;
            barTop = Img(root, "BarTop", new Vector2(0.5f, 1), bar); barBottom = Img(root, "BarBottom", new Vector2(0.5f, 0), bar);
            // finale logo (shield + gold lettering), hidden until the end
            var lg = new GameObject("Logo", typeof(RectTransform)); lg.transform.SetParent(root, false); logoGroup = (RectTransform)lg.transform; logoGroup.anchorMin = logoGroup.anchorMax = new Vector2(0.5f, 0.5f); logoGroup.sizeDelta = new Vector2(10, 10);
            var shield = new GameObject("Shield", typeof(RectTransform), typeof(Image)); shield.transform.SetParent(logoGroup, false);
            var sr = (RectTransform)shield.transform; sr.sizeDelta = new Vector2(190, 226); sr.anchoredPosition = new Vector2(0, 150);
            var tx = IntroSequence.EmblemTexture(); shield.GetComponent<Image>().sprite = Sprite.Create(tx, new Rect(0, 0, tx.width, tx.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            var lines = new List<Text>();
            lines.AddRange(Gold(logoGroup, "CITY DRIVER", 150, new Vector2(0, -10)));
            var rib = new GameObject("Ribbon", typeof(RectTransform), typeof(Image)); rib.transform.SetParent(logoGroup, false); var rr = (RectTransform)rib.transform; rr.sizeDelta = new Vector2(620, 74); rr.anchoredPosition = new Vector2(0, -120); rib.GetComponent<Image>().color = new Color(0.62f, 0.07f, 0.1f);
            var ut = Txt(rib.transform, "U", 54, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(620, 74)); ut.text = "UNDERCOVER"; ut.color = new Color(1f, 0.93f, 0.8f); lines.Add(ut);
            var cs = Txt(logoGroup, "Soon", 40, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0, -230), new Vector2(1200, 60)); cs.text = "COMING SOON  ·  ANDROID"; cs.color = new Color(0.95f, 0.85f, 0.6f); lines.Add(cs);
            logoLines = lines.ToArray();
            SetLogo(0f);
        }

        private static Image Img(Transform parent, string name, Vector2 anchor, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = new Vector2(0, anchor.y); r.anchorMax = new Vector2(1, anchor.y); r.pivot = new Vector2(0.5f, anchor.y); r.sizeDelta = new Vector2(0, height); r.anchoredPosition = Vector2.zero;
            var i = go.GetComponent<Image>(); i.color = Color.black; i.raycastTarget = false; return i;
        }

        private static List<Text> Gold(Transform parent, string text, int size, Vector2 pos)
        {
            var list = new List<Text>();
            foreach (var (c, off, outline) in new[] { (new Color(0.34f, 0.15f, 0.03f), -9f, true), (new Color(0.93f, 0.55f, 0.08f), -3f, false), (new Color(1f, 0.9f, 0.45f), 4f, false) })
            {
                var t = Txt(parent, "G", size, FontStyle.Bold, TextAnchor.MiddleCenter, pos + new Vector2(0, off), new Vector2(1500, size + 30)); t.text = text; t.color = c;
                if (outline) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.28f, 0.12f, 0.02f); o.effectDistance = new Vector2(6, -6); }
                list.Add(t);
            }
            return list;
        }

        private static void SetLogo(float a)
        {
            foreach (var t in logoLines) { var c = t.color; t.color = new Color(c.r, c.g, c.b, a); }
            var img = logoGroup.GetComponentsInChildren<Image>(true); foreach (var i in img) { var c = i.color; i.color = new Color(c.r, c.g, c.b, a); }
            logoGroup.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, a);
        }

        // ============================================================================================ the film
        private static IEnumerator Run()
        {
            while ((spawner = UnityEngine.Object.FindAnyObjectByType<PlayerSpawner>()) == null || spawner.Vehicle == null)
            {
                if (EditorApplication.timeSinceStartup - started > 300) { log.AppendLine("player car never spawned"); yield break; }
                yield return null;
            }
            car = spawner.Vehicle; cam = Camera.main;
            camCtl = UnityEngine.Object.FindAnyObjectByType<VehicleCameraController>(); if (camCtl != null) camCtl.enabled = false;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>()) if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) c.enabled = false;   // no HUD in the film
            handlerField = typeof(VehicleController).GetField("inputHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            savedHandler = handlerField?.GetValue(car); handlerField?.SetValue(car, null);
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; rt.Create(); tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            cam.targetTexture = rt; BuildPost(); BuildOverlay();
            var env = WorldEnvironment.Instance; env.AutoCycle = false; env.SetTimeOfDay(TimeOfDay.Sunset, 0.01f); env.SetWeather(Weather.Clear, 0.01f);
            Time.captureFramerate = Fps; Time.timeScale = 1f;
            Vector3 fwd = car.transform.forward; fwd.y = 0f; fwd.Normalize(); Vector3 right = Vector3.Cross(Vector3.up, fwd); Vector3 refPos = car.transform.position;
            Pose(refPos + Vector3.up * 100f - fwd * 250f, refPos + fwd * 100f, 55f);
            blackout.color = Color.black; SetTitle("", "", 0f);
            yield return Frames(20);                                                                   // let the world settle
            LoadDialogue("dialogue_classic.json"); recording = true; log.AppendLine("recording started");

            // ---------------------------------------------------------------- 1. studio card (3 s)
            for (int f = 0; f < 90; f++)
            {
                float a = Win(f, 10, 80, 20);
                SetTitle("K A R T H I   L A B", "P R E S E N T S", a); titleText.rectTransform.anchoredPosition = new Vector2(0, 20); subText.rectTransform.anchoredPosition = new Vector2(0, -60);
                yield return null;
            }
            titleText.rectTransform.anchoredPosition = new Vector2(0, 285); subText.rectTransform.anchoredPosition = new Vector2(0, 205);

            // ---------------------------------------------------------------- 2. aerial -> swoop -> hero (17 s)
            Vector3[] P = { refPos - fwd * 300f + right * -130f + up(140f), refPos - fwd * 140f + right * -30f + up(60f), refPos - fwd * 28f + right * -4f + up(9f), refPos - fwd * 9f + right * 4.5f + up(2f) };
            Vector3[] L = { refPos + fwd * 140f + up(10f), refPos + fwd * 80f + up(8f), refPos + fwd * 30f + up(3f), refPos + fwd * 1.5f + up(1f) };
            float[] KT = { 0f, 5.5f, 10.5f, 14f }, KF = { 56f, 50f, 64f, 40f };
            dofOn = false;
            for (int f = 0; f < 17 * Fps; f++)
            {
                float ct = f / (float)Fps; Vector3 pos, look; float fov;
                if (ct < KT[3])
                {
                    int s = ct < KT[1] ? 0 : ct < KT[2] ? 1 : 2; float u = Mathf.Clamp01((ct - KT[s]) / (KT[s + 1] - KT[s])); int i0 = Mathf.Max(0, s - 1), i3 = Mathf.Min(3, s + 2);
                    pos = CatmullRom(P[i0], P[s], P[s + 1], P[i3], u); look = CatmullRom(L[i0], L[s], L[s + 1], L[i3], u); fov = Mathf.Lerp(KF[s], KF[s + 1], Ease(u));
                }
                else
                {
                    float o = ct - KT[3]; Vector3 off = P[3] - refPos; float h = Mathf.Lerp(off.y, 1.4f, Mathf.Clamp01(o / 3f)); off.y = 0f;
                    off = Quaternion.AngleAxis(o * 14f, Vector3.up) * off; pos = refPos + off + Vector3.up * h; look = refPos + Vector3.up * 1f + fwd * 1.5f; fov = 40f;
                    dofOn = true; dofDistance = Vector3.Distance(pos, look);
                }
                pos += new Vector3(Mathf.PerlinNoise(ct * 0.9f, 3f) - 0.5f, Mathf.PerlinNoise(ct * 0.8f, 7f) - 0.5f, Mathf.PerlinNoise(ct * 0.7f, 1f) - 0.5f) * 0.12f;
                Pose(pos, look, fov, Mathf.Lerp(5f, 0f, Ease(ct / 10f)));
                blackout.color = new Color(0, 0, 0, 1f - Mathf.Clamp01(f / 24f) - 0f);
                if (f == 14 * Fps) flash.color = new Color(1, 0.96f, 0.85f, 0.7f); else flash.color = new Color(1, 0.96f, 0.85f, Mathf.Max(0f, flash.color.a - 0.05f));
                if (f < 5 * Fps) SetTitle("ONE CITY.", "", Win(f, 40, 130));
                else if (f < 10 * Fps) SetTitle("A THOUSAND SECRETS.", "", Win(f, 5 * Fps + 20, 9 * Fps + 15));
                else SetTitle("", "", 0f);
                yield return null;
            }
            // ---------------------------------------------------------------- 3. daytime drive (7 s)
            yield return Cut(env, TimeOfDay.Day, Weather.Clear);
            PickFarDestination(500f);
            for (int f = 0; f < 7 * Fps; f++)
            {
                Drive(14f); ChaseCam(car, 7.4f, 2.5f, 7f, 62f + car.SpeedKmh * 0.12f, f);
                blackout.color = new Color(0, 0, 0, Mathf.Clamp01(1f - f / 10f));
                SetTitle("EVERY RIDE HAS A PASSENGER", "", Win(f, 40, 190));
                yield return null;
            }
            // ---------------------------------------------------------------- 4. low front tracking shot, sunset (5 s)
            yield return Cut(env, TimeOfDay.Sunset, Weather.Clear);
            var pm = PursuitManager.Instance;
            for (int f = 0; f < 5 * Fps; f++)
            {
                if (f == 5 && pm != null) pm.Begin(PursuitPlan.ForLevel(5));
                Drive(15f); Vector3 cf = car.transform.forward, cr = Vector3.Cross(Vector3.up, cf); Vector3 cp = car.transform.position;
                float sweep = Mathf.Lerp(3.4f, -3.0f, Ease(f / 150f));
                Pose(cp + cf * 6.5f + cr * sweep + Vector3.up * 0.95f, cp + Vector3.up * 0.75f, 46f + Mathf.Sin(f * 0.05f) * 2f, Mathf.Lerp(-2f, 2f, f / 150f));
                dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
                blackout.color = new Color(0, 0, 0, Mathf.Clamp01(1f - f / 10f));
                SetTitle("EVERY RIDE HAS A SECRET", "", Win(f, 20, 130));
                yield return null;
            }
            // ---------------------------------------------------------------- 5. night, rain: the chase (9 s)
            yield return Cut(env, TimeOfDay.Night, Weather.HeavyRain);
            PickFarDestination(600f);
            for (int f = 0; f < 9 * Fps; f++)
            {
                if ((f == 3 || f == 50 || f == 100) && pm != null) pm.DebugTeleportPursuers(f == 3 ? 12f : 14f);
                Drive(17f);
                if (f < 150)
                {   // look back over the player's car at the pursuers
                    Vector3 cf = car.transform.forward, cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position; float sw = Mathf.Lerp(2.2f, -1.6f, Ease(f / 150f));
                    Pose(cp + cf * 7.5f + cr * sw + Vector3.up * 1.7f, cp - cf * 5f + Vector3.up * 0.9f, 58f, Mathf.Sin(f * 0.05f) * 1.2f); dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
                }
                else
                {   // cut to a low side-tracking angle
                    Vector3 cf = car.transform.forward, cr = Vector3.Cross(Vector3.up, cf), cp = car.transform.position;
                    float sw = Mathf.Lerp(6.2f, 5.0f, (f - 150) / 90f);
                    Pose(cp + cr * sw + cf * Mathf.Lerp(4f, -2f, (f - 150) / 90f) + Vector3.up * 1.1f, cp + cf * 1.2f + Vector3.up * 0.7f, 42f); dofOn = true; dofDistance = Vector3.Distance(posNow, cp);
                }
                blackout.color = new Color(0, 0, 0, Mathf.Clamp01(1f - f / 10f));
                SetTitle("SHADOW RUN", "ONE NIGHT.  NO SECOND CHANCES.", Win(f, 30, 220));
                yield return null;
            }
            // ---------------------------------------------------------------- 6. top-down burst (3 s)
            for (int f = 0; f < 3 * Fps; f++)
            {
                if (f == 0) grade.postExposure.Override(2.1f);
                Drive(18f); Vector3 cp = car.transform.position;
                Pose(cp + Vector3.up * Mathf.Lerp(46f, 30f, f / 90f) + car.transform.forward * -2f, cp, 38f, f * 0.4f); dofOn = false;
                SetTitle("", "", 0f);
                if (f == 0) blackout.color = new Color(0, 0, 0, 0.5f); else blackout.color = new Color(0, 0, 0, Mathf.Clamp01(blackout.color.a - 0.1f));
                yield return null;
            }
            if (pm != null) pm.End();
            // ---------------------------------------------------------------- 7. finale (8 s)
            for (int f = 0; f < 8 * Fps; f++)
            {
                Drive(0f); var inp = new CityDriver.Controls.VehicleInput { Brake = 1f }; car.SetManualInput(inp);
                Vector3 cp = car.transform.position; float o = f / (float)Fps;
                Vector3 off = Quaternion.AngleAxis(150f + o * 6f, Vector3.up) * Vector3.forward * 9f;
                Pose(cp + off + Vector3.up * 1.7f, cp + Vector3.up * 1f, 38f); dofOn = true; dofDistance = 9f;
                float dark = Mathf.Lerp(0.0f, 0.78f, Ease(f / 36f)); blackout.color = new Color(0, 0, 0, f > 7 * Fps + 6 ? Mathf.Clamp01((f - (7 * Fps + 6)) / 24f) : dark);
                SetTitle("", "", 0f);
                float a = Ease((f - 40) / 24f); SetLogo(a);
                if (f == 40) flash.color = new Color(1, 0.96f, 0.85f, 0.6f); else flash.color = new Color(1, 0.96f, 0.85f, Mathf.Max(0f, flash.color.a - 0.04f));
                yield return null;
            }
            recording = true; yield return Frames(3);
            log.AppendLine($"done, frames={frameNo}");
        }

        private static Vector3 up(float y) => Vector3.up * y;

        private static IEnumerator Cut(WorldEnvironment env, TimeOfDay t, Weather w)
        {
            blackout.color = Color.black; SetTitle("", "", 0f); SetLogo(0f); flash.color = new Color(1, 0.96f, 0.85f, 0);
            env.SetTimeOfDay(t, 0.01f); env.SetWeather(w, 0.01f);
            grade.postExposure.Override(t == TimeOfDay.Night ? 1.35f : t == TimeOfDay.Day ? 0.35f : 0.55f);
            yield return Frames(14);                                                                  // black frames while the sky / weather swap (hidden cut)
        }

        private static void ChaseCam(VehicleController c, float back, float height, float ahead, float fov, int f)
        {
            Vector3 cf = c.transform.forward; cf.y = 0f; cf.Normalize(); Vector3 cp = c.transform.position;
            Vector3 pos = cp - cf * back + Vector3.up * height + Vector3.Cross(Vector3.up, cf) * Mathf.Sin(f * 0.04f) * 0.5f;
            Pose(pos, cp + cf * ahead + Vector3.up * 0.9f, fov, Mathf.Sin(f * 0.03f) * 0.8f); dofOn = true; dofDistance = back + 1f;
        }
    }
}
