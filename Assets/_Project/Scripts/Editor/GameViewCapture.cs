using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CityDriver.CameraSystem;
using CityDriver.Player;
using CityDriver.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Captures REAL Game view screenshots (HUD included) from fixed viewpoints. Enters Play Mode, waits for the player car
    /// to spawn, teleports it to each viewpoint, lets the follow camera settle, saves a screenshot, then exits Play Mode.
    /// Usage: write the tag ("before", "after_A" ...) into Bridge/gvc_tag.txt, then run GameViewCapture.Begin.
    /// Output: Bridge/gv_{tag}_{shot}.png and Bridge/gvc_done.txt. Identical viewpoints are used every time so results compare.
    /// </summary>
    [InitializeOnLoad]
    public static class GameViewCapture
    {
        private const string PendingKey = "CityDriver.GVC.Tag";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";
        private const int SettleFrames = 50;

        private struct Shot
        {
            public string Name; public Vector3 Pos; public float Yaw; public string MarkerId;
            public Shot(string name, Vector3 pos, float yaw, string marker) { Name = name; Pos = pos; Yaw = yaw; MarkerId = marker; }
        }

        private static readonly Shot[] Shots =
        {
            new Shot("spawn",             new Vector3(40f, 0.5f, 22f),        180f, "dropoff_a"),
            new Shot("pickup_c_approach", new Vector3(77.75f, 0.5f, 22f),       0f, "pickup_c"),
            new Shot("junction",          new Vector3(-2.25f, 0.5f, -16f),      0f, null),
            new Shot("downtown_street",   new Vector3(-2.25f, 0.5f, 130f),      0f, null),
            new Shot("residential",       new Vector3(-602.25f, 0.5f, -100f),   0f, null),
            new Shot("industrial",        new Vector3(-483.5f, 0.5f, -650f),    0f, null),
            new Shot("entertainment",     new Vector3(476.5f, 0.5f, 420f),      0f, null),
            new Shot("hospital_road",     new Vector3(330f, 0.5f, 2.25f),      90f, null),
            new Shot("highway",           new Vector3(957f, 0.5f, -200f),       0f, null),
        };

        private static bool running;
        private static int shotIndex, stage, markFrame;
        private static string tag;
        private static VehicleController car;
        private static VehicleCameraController cam;
        private static double startTime;
        private static bool retried;
        private static int measureFrames;
        private static double teleportClock;
        private static float settleMaxFrame;
        private static double waitClock = double.MaxValue;
        private static double measureTime;
        private static int measureStartFrame;
        private static int savedTargetFps;
        private static readonly System.Text.StringBuilder stats = new System.Text.StringBuilder();

        static GameViewCapture()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying) TryStart();
        }

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");

        [BridgeCommand]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[GVC] Already in Play Mode."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { Debug.LogError("[GVC] Active scene has unsaved changes."); return; }
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            }

            string tagFile = Path.Combine(Dir, "gvc_tag.txt");
            string t = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "shot";
            File.Delete(Path.Combine(Dir, "gvc_done.txt"));
            SessionState.SetString(PendingKey, t);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) TryStart();
            if (change == PlayModeStateChange.ExitingPlayMode) Stop(false);
        }

        private static void TryStart()
        {
            string t = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(t) || running) return;
            SessionState.SetString(PendingKey, "");
            waitClock = EditorApplication.timeSinceStartup; stats.Clear(); tag = t; running = true; shotIndex = 0; stage = 0; startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private static void Stop(bool exitPlay)
        {
            if (!running) return;
            running = false;
            EditorApplication.update -= Tick;
            SetGameViewGizmos(true);
            var st = UnityEngine.Object.FindAnyObjectByType<CityDriver.World.ChunkStreamer>();
            if (st != null) stats.AppendLine($"STREAMING: {st.CompletedLoads} chunk loads, average {st.AverageLoadSeconds:F2} s, slowest {st.MaxLoadSeconds:F2} s, loaded now {st.LoadedCount}/{st.TotalCount}");
            File.WriteAllText(Path.Combine(Dir, $"gvc_stats_{tag}.txt"), stats.ToString());
            File.WriteAllText(Path.Combine(Dir, "gvc_done.txt"), $"{DateTime.Now:o} tag={tag} shots={shotIndex}");
            if (exitPlay) EditorApplication.isPlaying = false;
        }

        private static void Tick()
        {
            if (!running) return;
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;   // a paused Play freezes the player loop
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup - startTime > 200) { Debug.LogError("[GVC] Timed out."); Stop(true); return; }

            switch (stage)
            {
                case 0: // wait for the player car
                {
                    var spawner = UnityEngine.Object.FindAnyObjectByType<PlayerSpawner>();
                    if (spawner == null || spawner.Vehicle == null) return;
                    car = spawner.Vehicle;
                    cam = UnityEngine.Object.FindAnyObjectByType<VehicleCameraController>();
                    SetGameViewGizmos(false); // gameplay view without editor gizmos
                    stage = 1;
                    break;
                }
                case 1: // teleport to the viewpoint
                {
                    if (shotIndex >= Shots.Length) { Stop(true); return; }
                    Shot s = Shots[shotIndex];
                    var streamer = UnityEngine.Object.FindAnyObjectByType<CityDriver.World.ChunkStreamer>();
                    if (streamer != null && !streamer.IsReadyAround(s.Pos))
                    {
                        if (EditorApplication.timeSinceStartup - waitClock > 25) { waitClock = EditorApplication.timeSinceStartup; Debug.LogWarning($"[GVC] waiting for chunks at {Shots[shotIndex].Name}: {streamer.DescribeBlocking(s.Pos)}"); }
                        return;
                    }               // stream the ground in first, then teleport onto it
                    car.Teleport(s.Pos, Quaternion.Euler(0f, s.Yaw, 0f));
                    if (cam != null) cam.ResetCamera();
                    ShowMarker(s.MarkerId);
                    teleportClock = EditorApplication.timeSinceStartup; settleMaxFrame = 0f;
                    markFrame = Time.frameCount; stage = 2;
                    break;
                }
                case 2: // let physics + camera settle, then measure real frame cost (frame cap lifted), then capture
                    settleMaxFrame = Mathf.Max(settleMaxFrame, Time.unscaledDeltaTime);
                    if (Time.frameCount - markFrame < SettleFrames) return;
                    stats.AppendLine($"  [{Shots[shotIndex].Name}] teleport->settled {EditorApplication.timeSinceStartup - teleportClock:F1} s, worst single frame {settleMaxFrame * 1000f:F0} ms");
                    savedTargetFps = Application.targetFrameRate;
                    Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
                    measureFrames = 0; measureTime = 0; measureStartFrame = Time.frameCount; stage = 4;
                    return;
                case 4:
                    measureTime += Time.unscaledDeltaTime; measureFrames++;
                    if (measureFrames < 45) return;
                    {
                        var active = UnityEngine.Object.FindAnyObjectByType<CityDriver.World.ChunkStreamer>();
                        stats.AppendLine($"{Shots[shotIndex].Name,-18} avg frame {measureTime / measureFrames * 1000.0:F1} ms ({measureFrames / measureTime:F0} fps) | draw calls {UnityEditor.UnityStats.drawCalls} | tris {UnityEditor.UnityStats.triangles:N0} | set-pass {UnityEditor.UnityStats.setPassCalls} | shadow casters {UnityEditor.UnityStats.shadowCasters} | gfx mem {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576} MB | mono+native {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB | active renderers {UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length} | loaded chunks {(active != null ? active.LoadedCount : -1)}/{(active != null ? active.TotalCount : -1)}");
                    }
                    Application.targetFrameRate = savedTargetFps;
                    stage = 5;
                    return;
                case 5:
                    File.Delete(Path.Combine(Dir, $"gv_{tag}_{Shots[shotIndex].Name}.png"));
                    ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"gv_{tag}_{Shots[shotIndex].Name}.png"));
                    markFrame = Time.frameCount; stage = 3;
                    break;
                case 3: // wait until the file really exists (retry once), then next viewpoint
                {
                    string file = Path.Combine(Dir, $"gv_{tag}_{Shots[shotIndex].Name}.png");
                    if (File.Exists(file) && Time.frameCount - markFrame >= 4) { shotIndex++; stage = 1; return; }
                    if (Time.frameCount - markFrame > 60)
                    {
                        if (retried) { Debug.LogError($"[GVC] Screenshot '{file}' was never written."); shotIndex++; stage = 1; retried = false; return; }
                        retried = true; ScreenCapture.CaptureScreenshot(file); markFrame = Time.frameCount;
                    }
                    break;
                }
            }
        }

        // MarkerDirector exists only after Phase A; reached by reflection so this tool works before and after it.
        private static void ShowMarker(string markerId)
        {
            Type t = Type.GetType("CityDriver.Missions.MarkerDirector, CityDriver.Runtime");
            if (t == null) return;
            object instance = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null) return;
            if (markerId == null) t.GetMethod("HideAll")?.Invoke(instance, null);
            else t.GetMethod("ShowOnly")?.Invoke(instance, new object[] { markerId });
        }

        private static void SetGameViewGizmos(bool on)
        {
            try
            {
                Type gv = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                var view = Resources.FindObjectsOfTypeAll(gv).FirstOrDefault();
                if (view == null) return;
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var prop = gv.GetProperty("showGizmos", all) ?? gv.GetProperty("gizmos", all);
                if (prop != null && prop.PropertyType == typeof(bool)) { prop.SetValue(view, on); return; }
                var field = gv.GetField("m_Gizmos", all);
                if (field != null && field.FieldType == typeof(bool)) field.SetValue(view, on);
            }
            catch (Exception e) { Debug.LogWarning("[GVC] Could not toggle Game view gizmos: " + e.Message); }
        }
    }
}
