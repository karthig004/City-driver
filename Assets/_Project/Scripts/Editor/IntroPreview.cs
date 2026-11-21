using System;
using System.IO;
using CityDriver.Game;
using CityDriver.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Real-time visual check of the player's first minute: plays the app-open intro in the Game view and takes screenshots (Bridge/intro_*.png)
    /// of the studio card, the title reveal, the loading screen and TAP TO START, then of the first-run How To Play, the main menu and each
    /// settings tab. Writes Bridge/intro_done.txt when finished.
    /// </summary>
    [InitializeOnLoad]
    public static class IntroPreview
    {
        private const string Pending = "CD_IntroPreview";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";
        private static readonly (float t, string name)[] IntroShots = { (1.3f, "a_studio"), (5.5f, "b_flight"), (8.9f, "c_logo_in"), (9.5f, "c2_logo"), (10.2f, "c3_logo"), (11.0f, "c4_logo"), (12.5f, "d_logo_full"), (14.5f, "e_loading") };
        private static bool running, seen, readyShot; private static int stage, introIndex; private static double startTime, waitUntil;

        static IntroPreview()
        {
            EditorApplication.playModeStateChanged += c => { if (c == PlayModeStateChange.EnteredPlayMode) TryStart(); if (c == PlayModeStateChange.ExitingPlayMode) running = false; };
            if (EditorApplication.isPlaying) TryStart();
        }

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");

        [BridgeCommand]
        public static void Begin() { Start(true); }

        /// <summary>Same screens without the intro: first-run How To Play, main menu and the settings tabs.</summary>
        [BridgeCommand]
        public static void BeginUi() { Start(false); }

        /// <summary>World look check: night at the city edge, rain at night, then a daytime plaza (Bridge/intro_w_*.png).</summary>
        [BridgeCommand]
        public static void BeginWorld() { SessionState.SetBool(Pending + "World", true); Start(false); }

        private static void Start(bool withIntro)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[IntroPreview] Already in Play Mode."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { Debug.LogError("[IntroPreview] Active scene has unsaved changes."); return; }
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            }
            File.Delete(Path.Combine(Dir, "intro_done.txt"));
            if (withIntro) File.WriteAllText(Path.Combine(Dir, "show_intro.txt"), "1");
            SessionState.SetBool(Pending + "Ui", !withIntro);
            File.WriteAllText(Path.Combine(Dir, "show_menu.txt"), "1");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void TryStart()
        {
            if (!SessionState.GetBool(Pending, false) || running) return;
            SessionState.SetBool(Pending, false);
            bool world = SessionState.GetBool(Pending + "World", false); SessionState.SetBool(Pending + "World", false);
            running = true; seen = false; readyShot = false; stage = world ? 100 : SessionState.GetBool(Pending + "Ui", false) ? 1 : 0; introIndex = 0; startTime = EditorApplication.timeSinceStartup; waitUntil = EditorApplication.timeSinceStartup + (stage == 1 || stage == 100 ? 12 : 0);
            EditorApplication.update += Tick;
        }

        private static void Probe()
        {
            var sb = new System.Text.StringBuilder();
            var holder = GameObject.Find("Holder");
            if (holder == null) sb.AppendLine("no Holder");
            else
            {
                var cg = holder.GetComponent<CanvasGroup>(); sb.AppendLine("holder group: " + (cg != null ? cg.alpha.ToString("F3") : "none") + " scale " + holder.transform.localScale);
                sb.AppendLine("holder color " + holder.GetComponent<UnityEngine.UI.Image>().color);
                var panel = holder.transform.Find("Panel"); if (panel != null) sb.AppendLine("panel color " + panel.GetComponent<UnityEngine.UI.Image>().color + " rect " + ((RectTransform)panel).rect);
                var dim = holder.transform.parent; sb.AppendLine("dim color " + dim.GetComponent<UnityEngine.UI.Image>().color + " pop " + (dim.GetComponent<ModalPop>() != null));
                foreach (var g in holder.GetComponentsInParent<CanvasGroup>()) sb.AppendLine("parent group " + g.name + " " + g.alpha);
            }
            File.WriteAllText(Path.Combine(Dir, "intro_probe.txt"), sb.ToString());
        }

        private static void Shot(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "intro_" + name + ".png"), 1); }

        private static void Finish(string why)
        {
            running = false; EditorApplication.update -= Tick;
            try { File.Delete(Path.Combine(Dir, "show_intro.txt")); File.Delete(Path.Combine(Dir, "show_menu.txt")); } catch (IOException) { }
            File.WriteAllText(Path.Combine(Dir, "intro_done.txt"), DateTime.Now.ToString("o") + " " + why);
            EditorApplication.isPlaying = false;
        }

        private static void Tick()
        {
            if (!running) return;
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup - startTime > 260) { Finish("timeout at stage " + stage); return; }
            double now = EditorApplication.timeSinceStartup;
            var intro = IntroSequence.Instance;
            if (stage == 0)
            {
                if (intro != null && intro.Playing) seen = true;
                if (seen && intro != null && !intro.Playing) { stage = 1; waitUntil = now + 1.4; return; }
                if (!seen || intro == null) return;
                if (introIndex < IntroShots.Length && intro.Elapsed >= IntroShots[introIndex].t) { Shot(IntroShots[introIndex].name); introIndex++; }
                if (!readyShot && intro.Stage == "ready") { readyShot = true; waitUntil = now + 1.0; }
                else if (readyShot && waitUntil > 0 && now >= waitUntil) { Shot("f_ready"); waitUntil = 0; }
                return;
            }
            if (now < waitUntil) return;
            if (stage >= 100)
            {
                var wcar = UnityEngine.Object.FindFirstObjectByType<CityDriver.Vehicle.VehicleController>(); var wenv = WorldEnvironment.Instance;
                if (wcar == null || wenv == null) return;
                switch (stage)
                {
                    case 100: { var gu = GameUI.Instance; if (gu != null && gu.ModalOpen) gu.CloseModal(); } wenv.AutoCycle = false; wenv.SetTimeOfDay(TimeOfDay.Night, 0.01f); wenv.SetWeather(Weather.Clear, 0.01f); wcar.Teleport(new Vector3(100f, wcar.transform.position.y, 895f), Quaternion.LookRotation(Vector3.forward)); stage = 101; waitUntil = now + 7; break;
                    case 101: Shot("w_boundary_night"); stage = 102; waitUntil = now + 0.5; break;
                    case 102: wenv.SetWeather(Weather.Rain, 0.01f); stage = 103; waitUntil = now + 8; break;
                    case 103: Shot("w_rain_night"); stage = 104; waitUntil = now + 0.5; break;
                    case 104: wenv.SetWeather(Weather.Clear, 0.01f); wenv.SetTimeOfDay(TimeOfDay.Night, 0.01f); wcar.Teleport(new Vector3(40f, wcar.transform.position.y, 22f), Quaternion.Euler(0, 180, 0)); stage = 105; waitUntil = now + 7; break;
                    case 105: Shot("w_city_night"); stage = 106; waitUntil = now + 0.5; break;
                    case 106: wenv.SetTimeOfDay(TimeOfDay.Day, 0.01f); stage = 107; waitUntil = now + 5; break;
                    case 107: Shot("w_city_day"); wcar.Teleport(new Vector3(100f, wcar.transform.position.y, 955f), Quaternion.LookRotation(Vector3.forward)); stage = 108; waitUntil = now + 6; break;
                    case 108: Shot("w_boundary_day"); wcar.Teleport(new Vector3(100f, wcar.transform.position.y, 975f), Quaternion.LookRotation(Vector3.forward + Vector3.right * 0.3f)); stage = 109; waitUntil = now + 5; break;
                    case 109: Shot("w_boundary_day2"); stage = 110; waitUntil = now + 0.5; break;
                    default: Finish("ok world"); break;
                }
                return;
            }
            var ui = GameUI.Instance; if (ui == null) { Finish("no GameUI"); return; }
            switch (stage)
            {
                case 1: Shot("g_after_intro"); stage = 2; waitUntil = now + 3.0; break;
                case 2: ui.CloseModal(); ui.OpenMainMenu(); stage = 3; waitUntil = now + 4.0; break;
                case 3: Probe(); Shot("h_menu"); stage = 4; waitUntil = now + 0.4; break;
                case 4: case 6: case 8: case 10: ui.OpenSettingsTab((stage - 4) / 2); stage++; waitUntil = now + 3.2; break;
                case 5: case 7: case 9: case 11: Shot("i_settings_" + (stage - 5) / 2); stage++; waitUntil = now + 0.7; break;
                default: Finish("ok"); break;
            }
        }
    }
}
