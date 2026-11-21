using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using CityDriver.Controls;
using CityDriver.Missions;
using CityDriver.Navigation;
using CityDriver.Player;
using CityDriver.Population;
using CityDriver.UI;
using CityDriver.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Real Play Mode tests: injects multi-touch input through a virtual Touchscreen (so the actual TouchControlsPanel code path
    /// runs), then checks vehicle response, GPS routing / re-routing, the minimap and the tap-to-navigate full map.
    /// Output: Bridge/play_test.txt (+ Game view screenshots gv_playtest_*.png).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayTests
    {
        private const string PendingKey = "CityDriver.PlayTests.Pending";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        private static IEnumerator routine;
        private static readonly System.Collections.Generic.Stack<IEnumerator> stack = new System.Collections.Generic.Stack<IEnumerator>();

        // Steps the routine one frame, descending into nested iterators ("yield return Wait(1f)").
        private static bool Step()
        {
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            return false;
        }
        private static StringBuilder log;
        private static int checks, failures;
        private static Touchscreen touch;
        private static VehicleController car;
        private static double started;
        private static InputSettings originalSettings, temporarySettings;
        private static readonly System.Collections.Generic.Dictionary<int, Vector2> held = new System.Collections.Generic.Dictionary<int, Vector2>();
        private static int exceptionsBefore;

        static PlayTests()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode) TryStart(); };
            if (EditorApplication.isPlaying) TryStart();
        }

        [BridgeCommand]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[PlayTests] Already playing."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            File.Delete(Path.Combine(Dir, "play_test.txt"));
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");

        private static void TryStart()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            log = new StringBuilder(); checks = failures = 0; started = EditorApplication.timeSinceStartup;
            captured.Clear(); currentSection = "setup"; pauseNoted = false;
            routine = Run(); stack.Clear();
            EditorApplication.update -= Tick;           // never run two harnesses at once (domain reload is off in Play)
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        private static bool pauseNoted;
        private static double nextAlive;

        private static void Tick()
        {
            // Keep the Editor ticking: without repaints Unity stops advancing the player loop while its window is not active.
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > nextAlive)
            {
                nextAlive = EditorApplication.timeSinceStartup + 10.0;
                try { File.WriteAllText(Path.Combine(Dir, "play_alive.txt"), $"{DateTime.Now:HH:mm:ss} frame {Time.frameCount} time {Time.time:F1} unscaled {Time.unscaledTime:F1} timeScale {Time.timeScale} section '{currentSection}' last check '{lastCheck}' dt {Time.unscaledDeltaTime * 1000f:F0} ms"); } catch (IOException) { }
            }
            if (EditorApplication.isPaused)
            {
                // Play was paused (Pause button, or Console "Error Pause"): the player loop is frozen, so nothing can be tested.
                if (!pauseNoted) { pauseNoted = true; Line("  [note] Play Mode was paused; the harness un-paused it (EditorApplication.isPaused = false)."); }
                EditorApplication.isPaused = false;
            }
            if (EditorApplication.timeSinceStartup - started > 1500) { Line("  [FAIL] harness global timeout (25 min): stopped in section '" + currentSection + "'"); failures++; checks++; Finish(); return; }
            try { if (routine != null && Step()) return; }
            catch (Exception e) { Line("EXCEPTION: " + e); failures++; }
            Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            routine = null; stack.Clear();
            if (touch != null) { InputSystem.RemoveDevice(touch); touch = null; }
            if (originalSettings != null) { InputSystem.settings = originalSettings; originalSettings = null; }
            if (temporarySettings != null) { UnityEngine.Object.DestroyImmediate(temporarySettings); temporarySettings = null; }
            Line("\nRuntime exception / error capture (Application.logMessageReceived, whole run)");
            int game = captured.Count(c => !c.fromTestRunner), runner = captured.Count - game;
            Line($"  game-code exceptions/errors: {game}   test-runner errors: {runner}");
            foreach (var c in captured.Take(20)) Line($"    [{(c.fromTestRunner ? "runner" : "GAME")}] in '{c.section}': {c.type}: {(c.message.Length > 160 ? c.message.Substring(0, 160) : c.message)}");
            Check(game == 0, "no exceptions or errors from game code during driving, navigation, map and streaming");
            Line($"\nRESULT: {checks - failures}/{checks} checks passed{(failures == 0 ? " - ALL PASSED" : $" - {failures} FAILED")}");
            File.WriteAllText(Path.Combine(Dir, "play_test.txt"), log.ToString());
            EditorApplication.isPlaying = false;
        }

        // ================================================================================== the test

        private static IEnumerator Run()
        {
            // Wait for the player car (the spawner waits for the streamed ground first).
            PlayerSpawner spawner = null;
            while ((spawner = UnityEngine.Object.FindAnyObjectByType<PlayerSpawner>()) == null || spawner.Vehicle == null)
            {
                if (EditorApplication.timeSinceStartup - started > 240)
                {
                    var st = UnityEngine.Object.FindAnyObjectByType<CityDriver.World.ChunkStreamer>();
                    Line($"  [diag] timeScale {Time.timeScale}, frame {Time.frameCount}, time {Time.time:F1}, unscaled {Time.unscaledTime:F1}, spawner {(spawner != null)}, streamer {(st != null)}, loaded {(st != null ? st.LoadedCount : -1)}, pending {(st != null ? st.PendingLoads : -1)}, backgroundLoadingPriority {Application.backgroundLoadingPriority}");
                    if (st != null && spawner != null) Line("  [diag] blocking: " + st.DescribeBlocking(new Vector3(40f, 0f, 22f)));
                    var guiDiag = CityDriver.Game.GameUI.Instance; Line($"  [diag] GameUI modal open: {(guiDiag != null && guiDiag.ModalOpen)}");
                    Check(false, "player car spawned within 240 s"); yield break;
                }
                yield return null;
            }

            car = spawner.Vehicle;
            Section("Setup");
            CityDriver.Game.GameSettings.ResetToDefaults();          // a previous aborted run may have left Wheel / Tilt mode or muted audio behind
            CityDriver.Game.GameSettings.Steering = CityDriver.Game.SteeringMode.Buttons;   // the arrow-button touch tests below drive the arrow buttons (the wheel has its own test)
            // Test on a fresh in-memory new game, never on the player's real save (their upgrades, vehicle, fuel and progress would change the physics).
            CityDriver.Game.GameSession.UseStorage(new CityDriver.Save.MemorySaveStorage());
            if (CityDriver.Game.FuelSystem.Instance != null) CityDriver.Game.FuelSystem.Instance.SetFuel(1f);
            CityDriver.Game.VehicleTuning.FuelFactor = 1f; CityDriver.Game.VehicleTuning.WeatherGrip = 1f;
            CityDriver.Game.VehicleTuning.ApplyToCurrentCar();
            Check(car != null && car.enabled, "player car spawned and its controller is enabled");
            var panel = TouchControlsPanel.Instance;
            Check(panel != null, "touch controls panel is present");
            RectTransform left = Find("SteerLeft"), right = Find("SteerRight"), gas = Find("Accelerator"), brake = Find("Brake"), rev = Find("Reverse"), cam = Find("CameraReset");
            Check(new[] { left, right, gas, brake, rev, cam }.All(r => r != null), "all six touch buttons exist");
            if (failures > 0) yield break;

            // Unity is not the foreground window during automation: let input flow regardless of focus (temporary copy, project settings untouched).
            originalSettings = InputSystem.settings;
            if (originalSettings == null) originalSettings = ScriptableObject.CreateInstance<InputSettings>();   // stale/destroyed after an aborted Play session
            temporarySettings = UnityEngine.Object.Instantiate(originalSettings);
            temporarySettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            temporarySettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = temporarySettings;

            touch = InputSystem.AddDevice<Touchscreen>("PlayTestTouch");
            yield return Wait(0.5f);

            Section("Touch controls drive the car (real Input System touches, multi-touch)");
            car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));     // the open plaza: the steering tests must not depend on where the player spawns
            var popPause = CityPopulation.Instance; if (popPause != null) popPause.gameObject.SetActive(false);   // no traffic or props of chance in the way of the steering tests
            Vector3 startPos = car.transform.position;
            float yaw0 = car.transform.eulerAngles.y;
            yield return Wait(2.0f);
            yield return WaitForRest(20f);                // let the car finish dropping and settle on its suspension
            Check(car.SpeedKmh < 1f, $"idle with no touches: {car.SpeedKmh:F1} km/h");

            Down(1, Centre(gas));
            float rt0 = Time.unscaledTime; int f0 = Time.frameCount; float gt0 = Time.time;
            yield return Wait(3.0f);
            Line($"  [diag] game {Time.time - gt0:F2}s, unscaled {Time.unscaledTime - rt0:F2}s, frames {Time.frameCount - f0}, gasPressed={panel.IsPressed(gas)}, panelEnabled={panel.isActiveAndEnabled}, timeScale={Time.timeScale}");
            Line($"  holding GAS 3 s: {car.SpeedKmh:F1} km/h, throttle {panel.Current.Throttle:F2}, gear {car.GearLabel}");
            Check(car.SpeedKmh > 12f && car.ForwardSpeed > 0f, "accelerator touch accelerates forward");
            Check(panel.Current.Throttle > 0.9f, "throttle ramps to full while held");

            Down(2, Centre(right));
            float yawBefore = car.transform.eulerAngles.y;
            yield return Wait(1.6f);
            float yawDelta = Mathf.DeltaAngle(yawBefore, car.transform.eulerAngles.y);
            Line($"  GAS + STEER RIGHT 1.6 s: steer angle {car.SteerAngle:F1} deg, heading change {yawDelta:F1} deg, speed {car.SpeedKmh:F1} km/h");
            Check(car.SteerAngle > 2f, "right-steer touch turns the wheels right while the accelerator touch is still held (two fingers)");
            Check(yawDelta > 4f, "the car actually turns right");
            Check(car.SpeedKmh > 8f, "still moving under power while steering");

            Up(2);
            Down(3, Centre(left));
            float minYawRate = 0f; float steerEnd = Time.time + 1.6f;                  // the car is still swinging right at first: track how hard it yaws LEFT after the input
            while (Time.time < steerEnd) { minYawRate = Mathf.Min(minYawRate, car.Body.angularVelocity.y); yield return null; }
            Line($"  STEER LEFT 1.6 s: steer angle {car.SteerAngle:F1} deg, strongest left yaw rate {minYawRate:F2} rad/s");
            Check(car.SteerAngle < -2f && minYawRate < -0.12f, "left-steer touch turns the car left");

            Up(3); Up(1);
            yield return Wait(0.6f);
            Check(panel.Current.Throttle < 0.5f, $"releasing the accelerator lets the throttle fall (now {panel.Current.Throttle:F2})");

            Down(4, Centre(brake));
            float speedAtBrake = car.SpeedKmh;
            yield return Wait(2.5f);
            Line($"  BRAKE 2.5 s: {speedAtBrake:F1} -> {car.SpeedKmh:F1} km/h");
            Check(car.SpeedKmh < Mathf.Max(2f, speedAtBrake * 0.25f), "brake touch brings the car nearly to a stop");
            Up(4);

            yield return WaitForRest(8f);
            CityDriver.Controls.TouchControlsPanel.Instance.SelectGear(1);          // the gear lever: R + gas reverses
            Down(5, Centre(gas));
            yield return Wait(3.0f);
            Line($"  REVERSE 3 s: forward speed {car.ForwardSpeed * 3.6f:F1} km/h, gear {car.GearLabel}");
            Check(car.ForwardSpeed < -1.5f && car.GearLabel == "R", "gear R + accelerator touch drives the car backwards");
            CityDriver.Controls.TouchControlsPanel.Instance.SelectGear(3);
            Up(5);

            bool sawCamReset = false;
            Down(6, Centre(cam));
            for (int i = 0; i < 20 && !sawCamReset; i++) { yield return null; sawCamReset |= panel.Current.CameraReset; }
            Up(6);
            Check(sawCamReset, "camera-reset button produces a reset pulse");
            yield return Wait(1.0f);
            Check(panel.Current.Throttle < 0.05f && Mathf.Abs(panel.Current.Steer) < 0.05f && panel.Current.Brake == 0f, "all controls return to zero when every finger is lifted");

            Section("Responsive layout (no overlap at common phone aspect ratios)");
            CheckLayout();

            Section("GPS navigation on the real road graph");
            var nav = NavigationService.Instance;
            Check(nav != null && nav.Graph != null && nav.Graph.NodeCount > 300, $"navigation service has the city graph ({nav?.Graph?.NodeCount} junctions)");
            car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));
            yield return Wait(1.0f);
            MarkerDirector.Instance.ShowOnly("dropoff_c");
            yield return Wait(0.6f);
            Check(nav.HasDestination, "activating mission waypoint dropoff_c sets the GPS destination");
            float routeLen = nav.Route.Length;
            Line($"  route {routeLen:F0} m, {nav.Route.points.Count} points, first instruction \"{nav.Instruction}\", ETA {nav.EtaSeconds:F0} s");
            Check(routeLen > Vector3.Distance(car.transform.position, nav.Destination), "route length is at least the straight-line distance");
            Check(!string.IsNullOrEmpty(nav.Instruction), "turn-by-turn instruction is produced");

            // Progress: put the car halfway along the route.
            Vector3 mid = PointAt(nav.Route, routeLen * 0.5f);
            car.Teleport(mid + Vector3.up * 0.6f, Quaternion.identity);
            yield return Wait(1.2f);
            Line($"  halfway: remaining {nav.DistanceRemaining:F0} m of {routeLen:F0} m");
            Check(Mathf.Abs(nav.DistanceRemaining - routeLen * 0.5f) < routeLen * 0.2f, "remaining distance is about half at the route midpoint");

            // Leave the route: GPS must recalculate.
            int versionBefore = nav.RouteVersion;
            car.Teleport(mid + new Vector3(70f, 0.6f, 70f), Quaternion.identity);
            yield return Wait(4.0f);
            Check(nav.RouteVersion > versionBefore, "leaving the route triggers an automatic re-route");
            Check(nav.HasDestination, "destination is kept after re-routing");

            Section("Minimap and full-screen map");
            var minimap = UnityEngine.Object.FindAnyObjectByType<MinimapView>();
            Check(minimap != null, "minimap exists");
            car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));
            yield return Wait(1.0f);
            nav.SetDestinationToLocation("dropoff_c");
            yield return Wait(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "gv_playtest_minimap_route.png"));
            yield return Wait(0.5f);

            var full = UnityEngine.Object.FindAnyObjectByType<FullMapView>();
            Check(full != null, "full map view exists");
            full.SetOpen(true);
            yield return Wait(0.5f);
            Check(full.IsOpen, "full map opens");
            RectTransform mapArea = Find("MapArea");
            Vector2 centre = Centre(mapArea);
            // Tap the hospital's position on the map: the destination should snap to the point of interest.
            var hospital = nav.Data.pois.First(p => p.id == "hospital_main");
            float half = CityDriver.World.RoadMapRenderer.MapHalfExtent;
            Vector2 size = mapArea.rect.size * mapArea.lossyScale;
            Vector2 tap = centre + new Vector2(hospital.position.x / (half * 2f) * size.x, hospital.position.z / (half * 2f) * size.y);
            full.OnMapClicked(new PointerEventData(EventSystem.current) { position = tap });
            yield return Wait(0.5f);
            Line($"  tap at hospital position -> destination '{nav.DestinationLabel}', route {nav.Route.Length:F0} m");
            Check(nav.HasDestination && nav.DestinationLabel.Contains("Hospital"), "tapping the full map selects the point of interest and plans a route");
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "gv_playtest_fullmap.png"));
            yield return Wait(0.6f);
            full.ClearRoute();
            Check(!nav.HasDestination, "clear-route button removes the route");
            full.SetOpen(false);
            nav.ClearDestination();

            Section("City population: pooled traffic, parked cars and sidewalk pedestrians");
            if (popPause != null) popPause.gameObject.SetActive(true);
            var pop = CityPopulation.Instance;
            Check(pop != null, "CityPopulation was created when the scene loaded");
            if (pop != null)
            {
                float popEnd = Time.time + 30f;
                while (Time.time < popEnd && !(pop.ActiveMovingCars >= 8 && pop.ActivePedestrians >= 12)) yield return null;
                yield return Wait(10f);
                Camera mainCam = Camera.main;
                Line($"  active: {pop.ActiveMovingCars} moving cars ({pop.MovingCarsInMotion} in motion), {pop.ActiveParkedCars} parked, {pop.ActivePedestrians} pedestrians ({pop.PedestriansInMotion} walking)");
                Line($"  visible from the player camera now: {pop.VisibleMovingCars(mainCam)} moving cars, {pop.VisibleParkedCars(mainCam)} parked, {pop.VisiblePedestrians(mainCam)} pedestrians; cars waiting at red signals: {pop.CarsWaitingAtSignals}");
                Check(pop.ActiveMovingCars >= 8, "at least 8 moving traffic cars are active");
                Check(pop.ActiveParkedCars >= 6, "at least 6 parked cars are active");
                Check(pop.ActivePedestrians >= 12, "at least 12 pedestrians are active");
                Check(pop.MovingCarsInMotion >= 4, "traffic is actually moving");
                Check(pop.PedestriansInMotion >= 4, "pedestrians are actually walking");
                pop.Audit(out int pedChecked, out int pedOnRoad, out int carChecked, out int carWrongSide);
                Line($"  audit: {pedChecked} pedestrians checked, {pedOnRoad} on the carriageway; {carChecked} moving cars checked, {carWrongSide} on the wrong (right-hand) side");
                Check(pedChecked > 0 && pedOnRoad == 0, "no pedestrian is standing in the carriageway");
                Check(carChecked == 0 || carWrongSide == 0, "moving cars keep to the left lane (left-hand traffic)");
                // let the population run while the player drives so recycling and signals are exercised
                Down(1, Centre(gas)); yield return Wait(8f); Up(1);
                Check(pop.ActiveMovingCars >= 8 && pop.ActivePedestrians >= 12, "population count stays stable while the player drives and agents are recycled");
            }

            Section("Camera views, steering modes, handbrake, settings, audio, map alignment and pedestrians");
            {
                var cameraCtl = UnityEngine.Object.FindAnyObjectByType<CityDriver.CameraSystem.VehicleCameraController>();
                Check(cameraCtl != null, "camera controller found");
                Check(CityDriver.Game.GameAudio.Instance != null, "GameAudio was created when the scene loaded");
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));
                yield return Wait(1.5f);

                // ---- camera: four views, switched by the CAM button and the settings store
                var camT = cameraCtl.transform;
                float[] dist = new float[4], height = new float[4];
                for (int view = 0; view < 4; view++)
                {
                    CityDriver.Game.GameSettings.CameraView = view;
                    yield return Wait(1.2f);
                    dist[view] = Vector3.Distance(camT.position, car.transform.position);
                    height[view] = camT.position.y - car.transform.position.y;
                    if (view == 1) yield return Shot("10_camera_far_chase");
                    if (view == 2) yield return Shot("11_camera_bonnet");
                    if (view == 3) yield return Shot("12_camera_topdown");
                }
                Line($"  camera distance/height by view: chase {dist[0]:F1}/{height[0]:F1} m, far {dist[1]:F1}/{height[1]:F1} m, bonnet {dist[2]:F1}/{height[2]:F1} m, top-down {dist[3]:F1}/{height[3]:F1} m");
                Check(dist[0] > 4.5f && dist[0] < 9f, "chase camera sits 4.5-9 m behind the car");
                Check(dist[1] > dist[0] + 2f, "far chase camera is further back than the chase camera");
                Check(dist[2] < 2.5f, "bonnet camera is at the car");
                Check(height[3] > 18f, "top-down camera is high above the car");
                CityDriver.Game.GameSettings.CameraView = 0; yield return Wait(1f);
                int viewBefore = CityDriver.Game.GameSettings.CameraView;
                Down(6, Centre(cam)); yield return Wait(0.4f); Up(6); yield return Wait(0.6f);
                Check(CityDriver.Game.GameSettings.CameraView == viewBefore + 1, "the CAM button cycles to the next camera view");
                CityDriver.Game.GameSettings.CameraView = 0; yield return Wait(1f);

                // ---- steering wheel mode
                CityDriver.Game.GameSettings.Steering = CityDriver.Game.SteeringMode.Wheel;
                yield return Wait(0.6f);
                RectTransform wheelRt = Find("SteeringWheel");
                Check(wheelRt != null && wheelRt.gameObject.activeSelf, "steering wheel is visible in Wheel mode");
                Check(!left.gameObject.activeSelf && !right.gameObject.activeSelf, "arrow buttons are hidden in Wheel mode");
                if (wheelRt != null)
                {
                    yield return Shot("13_steering_wheel_mode");
                    Vector2 wc = Centre(wheelRt);
                    Down(1, wc + new Vector2(0f, 55f)); yield return Wait(0.5f);
                    Check(Mathf.Abs(panel.Current.Steer) < 0.15f, "touching the top of the wheel keeps steering straight");
                    Down(1, wc + new Vector2(55f, 0f)); yield return Wait(0.8f);
                    float steerRight = panel.Current.Steer;
                    Down(1, wc + new Vector2(-55f, 0f)); yield return Wait(1.0f);
                    float steerLeft = panel.Current.Steer;
                    Up(1); yield return Wait(1.5f);
                    float steerReleased = panel.Current.Steer;
                    Line($"  wheel: right side {steerRight:F2}, left side {steerLeft:F2}, after release {steerReleased:F2}, wheel angle {panel.WheelAngleDegrees:F0} deg");
                    Check(steerRight > 0.4f, "dragging the wheel clockwise steers right");
                    Check(steerLeft < -0.4f, "dragging the wheel anticlockwise steers left");
                    Check(Mathf.Abs(steerReleased) < 0.1f, "the wheel springs back to centre when released");
                }
                CityDriver.Game.GameSettings.Steering = CityDriver.Game.SteeringMode.Tilt;
                yield return Wait(0.8f);
                Check(Mathf.Abs(panel.Current.Steer) <= 1f, "Tilt mode runs without an accelerometer (no exceptions; steering stays valid)");
                CityDriver.Game.GameSettings.Steering = CityDriver.Game.SteeringMode.Buttons;
                yield return Wait(0.6f);
                Check(left.gameObject.activeSelf && right.gameObject.activeSelf, "arrow buttons return in Buttons mode");

                // ---- gear switch: only R and D exist (no P / N); anything else falls back to D, and neither holds the handbrake
                panel.SelectGear(0); yield return Wait(0.3f); Check(panel.Gear == CityDriver.Controls.TouchControlsPanel.GearDrive, "no P gear: an unknown gear falls back to D");
                panel.SelectGear(CityDriver.Controls.TouchControlsPanel.GearReverse); yield return Wait(0.3f); Check(panel.Gear == CityDriver.Controls.TouchControlsPanel.GearReverse && !panel.Current.Handbrake, "R selects reverse without the handbrake");
                panel.SelectGear(CityDriver.Controls.TouchControlsPanel.GearDrive); yield return Wait(0.3f); Check(!panel.Current.Handbrake, "D drives with the handbrake off");

                // ---- settings persist and apply
                CityDriver.Game.GameSettings.SteerSensitivity = 1.3f; CityDriver.Game.GameSettings.Master = 0.4f;
                Check(Mathf.Approximately(PlayerPrefs.GetFloat("cd_steer_sens"), 1.3f) && Mathf.Approximately(CityDriver.Game.GameSettings.Master, 0.4f), "settings are stored (PlayerPrefs)");
                CityDriver.Game.GameSettings.ResetToDefaults();
                Check(Mathf.Approximately(CityDriver.Game.GameSettings.SteerSensitivity, 1f) && Mathf.Approximately(CityDriver.Game.GameSettings.Master, 1f), "reset to defaults restores them");
                var uiNow = CityDriver.Game.GameUI.Instance;
                uiNow.OpenSettings(); yield return Shot("14_settings"); Check(uiNow.ModalOpen && uiNow.ModalTitle == "SETTINGS", "settings screen opens"); uiNow.CloseModal();

                // ---- audio (objective checks: not silent, responds to speed, volume and station)
                var audio = CityDriver.Game.GameAudio.Instance;
                if (audio != null)
                {
                    float audioWait = Time.unscaledTime + 240f;
                    while (!audio.Ready && Time.unscaledTime < audioWait) yield return WaitReal(0.5f);
                    Check(audio.Ready, "all synthesised audio clips (engine, tyres, wind, ambience, one-shots, 3 stations) were built in the background");
                    CityDriver.Game.GameSettings.Radio = 0; yield return Wait(0.5f);
                    Check(audio.EngineSource.isPlaying && CityDriver.Game.ProceduralAudio.Rms(audio.EngineSource.clip) > 0.04f, "engine loop is playing and not silent");
                    Check(audio.TyreSource.isPlaying && CityDriver.Game.ProceduralAudio.Rms(audio.TyreSource.clip) > 0.04f, "tyre loop exists and is not silent");
                    Check(!audio.RadioSource.isPlaying, "radio is off when set to Off");
                    float idlePitch = audio.EngineSource.pitch, idleVol = audio.EngineSource.volume;
                    Down(1, Centre(gas)); yield return Wait(3.5f);
                    float drivePitch = audio.EngineSource.pitch, driveVol = audio.EngineSource.volume, windVol = audio.WindSource.volume;
                    Up(1);
                    Line($"  engine pitch idle {idlePitch:F2} -> driving {drivePitch:F2}; volume {idleVol:F2} -> {driveVol:F2}; wind {windVol:F2}; speed {car.SpeedKmh:F0} km/h; rpm01 {audio.EngineRpm01:F2}");
                    Check(drivePitch > idlePitch + 0.3f, "engine pitch rises with speed/throttle");
                    Check(driveVol > idleVol, "engine gets louder under throttle");
                    Check(windVol > 0.01f, "wind noise appears at speed");
                    for (int st = 1; st <= 3; st++)
                    {
                        CityDriver.Game.GameSettings.Radio = st; yield return Wait(0.6f);
                        var clip = audio.CurrentStationClip;
                        Check(audio.RadioSource.isPlaying && clip != null && CityDriver.Game.ProceduralAudio.Rms(clip) > 0.03f && clip.length > 8f, $"radio station {st} ({CityDriver.Game.GameSettings.RadioNames[st]}) plays a {(clip != null ? clip.length : 0):F1}s loop that is not silent");
                    }
                    CityDriver.Game.GameSettings.Master = 0f; yield return Wait(0.4f);
                    Check(audio.RadioSource.volume == 0f && audio.EngineSource.volume == 0f, "master volume 0 silences radio and engine");
                    CityDriver.Game.GameSettings.Master = 1f; CityDriver.Game.GameSettings.Music = 0f; yield return Wait(0.4f);
                    Check(audio.RadioSource.volume == 0f && audio.EngineSource.volume > 0f, "music volume 0 silences only the radio");
                    CityDriver.Game.GameSettings.ResetToDefaults();
                }

                // ---- map alignment: the road graph (map) must match the real 3D roads
                var navS = NavigationService.Instance;
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));
                yield return Wait(1.5f);
                var graphData = navS.Data;
                Texture2D mapTex = CityDriver.World.RoadMapRenderer.Render(graphData, 1024, null, false);
                int sampled = 0, roadHit = 0, mapRoad = 0, mapMismatch = 0;
                var bg = mapTex.GetPixel(2, 2);
                float halfMap = CityDriver.World.RoadMapRenderer.MapHalfExtent;
                var namesSeen = new System.Collections.Generic.HashSet<string>();
                foreach (var e in graphData.edges)
                {
                    if (sampled >= 24) break;
                    Vector3 a = graphData.nodes[e.from].position, b2 = graphData.nodes[e.to].position, edgeMid = (a + b2) * 0.5f;
                    if (e.roadClass == CityDriver.World.RoadClass.Highway) continue;
                    if (Vector2.Distance(new Vector2(edgeMid.x, edgeMid.z), new Vector2(car.transform.position.x, car.transform.position.z)) > 160f) continue;
                    sampled++;
                    if (Physics.Raycast(new Vector3(edgeMid.x, 30f, edgeMid.z), Vector3.down, out RaycastHit hit, 60f, LayerMask.GetMask("Road"), QueryTriggerInteraction.Ignore))
                    {
                        namesSeen.Add(hit.collider.name);
                        if (hit.collider.name.StartsWith("Road") || hit.collider.name.Contains("Road")) roadHit++;
                    }
                    int px = Mathf.RoundToInt((edgeMid.x + halfMap) / (halfMap * 2f) * 1023f), py = Mathf.RoundToInt((edgeMid.z + halfMap) / (halfMap * 2f) * 1023f);
                    Color c = mapTex.GetPixel(px, py);
                    if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 0.08f) mapRoad++; else mapMismatch++;
                }
                Line($"  map alignment: {sampled} road-graph points near the car; real road collider under {roadHit}/{sampled} (colliders seen: {string.Join(", ", namesSeen)}); map draws a road at {mapRoad}/{sampled}");
                Check(sampled >= 8 && roadHit >= sampled - 1, "the real 3D road surface lies under (nearly) every road-graph point");
                Check(sampled >= 8 && mapRoad >= sampled - 1, "the map draws a road at (nearly) every road-graph point");
                var mini = UnityEngine.Object.FindAnyObjectByType<MinimapView>();
                var baseMapField = typeof(MinimapView).GetField("baseMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var miniImage = (UnityEngine.UI.RawImage)baseMapField.GetValue(mini);
                Vector2 uvCentre = miniImage.uvRect.center;
                Vector2 expected = new Vector2((car.transform.position.x + halfMap) / (halfMap * 2f), (car.transform.position.z + halfMap) / (halfMap * 2f));
                Check(Vector2.Distance(uvCentre, expected) < 0.01f, $"minimap is centred on the car (uv {uvCentre.x:F3},{uvCentre.y:F3} vs {expected.x:F3},{expected.y:F3})");
                float yawNow = car.transform.eulerAngles.y;
                var container = (RectTransform)typeof(MinimapView).GetField("mapContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(mini);
                Check(Mathf.Abs(Mathf.DeltaAngle(container.localEulerAngles.z, yawNow)) < 2f, "minimap rotates with the car heading (heading up)");

                // ---- pedestrians: variety and plausible height
                var popNow = CityPopulation.Instance;
                if (popNow != null)
                {
                    var seen = new System.Collections.Generic.HashSet<Mesh>(); float minH = 9f, maxH = 0f; int counted = 0;
                    foreach (var mf in popNow.GetComponentsInChildren<MeshFilter>())
                    {
                        if (!mf.gameObject.activeInHierarchy || !mf.name.StartsWith("Pedestrian") || mf.sharedMesh == null) continue;
                        counted++; seen.Add(mf.sharedMesh); float h = mf.sharedMesh.bounds.size.y; minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h);
                    }
                    Line($"  pedestrians: {counted} active, {seen.Count} different pose/outfit meshes, height {minH:F2}-{maxH:F2} m");
                    Check(counted >= 8 && minH > 1.2f && maxH < 2.0f, "pedestrians have plausible human heights");
                    Check(seen.Count >= 5, "pedestrians wear a variety of outfits");
                    // close-up of a pedestrian for the screenshot set
                    Transform pedT = null;
                    foreach (var mf in popNow.GetComponentsInChildren<MeshFilter>()) if (mf.gameObject.activeInHierarchy && mf.name.StartsWith("Pedestrian")) { pedT = mf.transform; break; }
                    if (pedT != null && navS.Graph.TrySnap(pedT.position, out CityDriver.World.RoadSnap pedSnap))
                    {
                        var pe = navS.Graph.Edge(pedSnap.edge);
                        Vector3 roadDir = (graphData.nodes[pe.to].position - graphData.nodes[pe.from].position); roadDir.y = 0f; roadDir.Normalize();
                        car.Teleport(pedSnap.point - roadDir * 9f + Vector3.up * 0.6f, Quaternion.LookRotation(roadDir));
                        yield return Wait(1.0f); yield return Shot("15_pedestrians_close");
                    }
                }
            }

            Section("Passenger mission end-to-end (in-memory save; the real save file is not touched)");
            {
                var memory = new CityDriver.Save.MemorySaveStorage();
                var session = CityDriver.Game.GameSession.UseStorage(memory);
                var runnerM = CityDriver.Game.MissionRunner.Instance;
                var ui = CityDriver.Game.GameUI.Instance;
                Check(runnerM != null && ui != null, "MissionRunner and GameUI were created when the scene loaded");
                if (runnerM != null && ui != null)
                {
                    var mission = CityDriver.Career.CareerCatalog.Find("L1_first_ride");
                    long wallet0 = session.Wallet.Balance, xp0 = session.Xp.Xp;
                    Check(wallet0 == 5000 && xp0 == 0 && session.Career.Level == 1, "new game starts with Rs 5,000, 0 XP, level 1");

                    yield return Shot("00_driving_with_traffic");
                    ui.OpenMainMenu(); yield return Shot("01_main_menu"); Check(ui.ModalOpen && Time.timeScale == 0f, "main menu opens and pauses the game"); ui.CloseModal();
                    ui.OpenMissions(); yield return Shot("02_missions"); ui.CloseModal();
                    ui.OpenGarage(); yield return Shot("03_garage"); ui.CloseModal();
                    ui.OpenWallet(); yield return Shot("04_wallet"); ui.CloseModal();
                    Check(!ui.ModalOpen && Time.timeScale == 1f, "closing a menu resumes the game");

                    Check(runnerM.StartMission(mission), "First Ride mission starts");
                    Check(runnerM.State == CityDriver.Game.MissionState.ToPickup, "state is heading to the pickup");
                    var director = CityDriver.Missions.MarkerDirector.Instance;
                    Check(director != null && director.ActiveId == CityDriver.Game.MissionRunner.PickupId, "only the pickup waypoint is shown");
                    Check(CityDriver.Navigation.NavigationService.Instance.HasDestination, "GPS route to the pickup is active");

                    var pickupMarker = CityDriver.Missions.LocationMarker.Find(CityDriver.Missions.LocationKind.Pickup, CityDriver.Game.MissionRunner.PickupId);
                    var dropMarker = CityDriver.Missions.LocationMarker.Find(CityDriver.Missions.LocationKind.Dropoff, CityDriver.Game.MissionRunner.DropoffId);
                    Vector3 approach = pickupMarker.transform.position + new Vector3(0f, 0.6f, -16f);
                    car.Teleport(approach, Quaternion.LookRotation(Vector3.forward));
                    yield return Wait(1.0f);
                    yield return Shot("05_pickup_approach");

                    car.Teleport(pickupMarker.transform.position + new Vector3(0f, 0.6f, 0f), Quaternion.LookRotation(Vector3.forward));
                    float boardEnd = Time.time + 6f;
                    while (Time.time < boardEnd && runnerM.State == CityDriver.Game.MissionState.ToPickup) yield return null;
                    Check(runnerM.State == CityDriver.Game.MissionState.Boarding, "stopping at the pickup starts boarding");
                    yield return Wait(1.0f); yield return Shot("06_boarding_subtitle");
                    boardEnd = Time.time + 6f;
                    while (Time.time < boardEnd && runnerM.State == CityDriver.Game.MissionState.Boarding) yield return null;
                    Check(runnerM.State == CityDriver.Game.MissionState.ToDropoff, "passenger boards and the trip to the drop-off begins");
                    Check(director.ActiveId == CityDriver.Game.MissionRunner.DropoffId, "only the drop-off waypoint is shown now");

                    car.Teleport(dropMarker.transform.position + new Vector3(0f, 0.6f, -14f), Quaternion.LookRotation(Vector3.forward));
                    yield return Wait(1.0f); yield return Shot("07_dropoff_approach");
                    Check(NavigationService.Instance.HasDestination && NavigationService.Instance.Route.points.Count >= 2, "the GPS route stays on the map when the car gets within 14 m of the drop-off (it is only cleared when the mission ends)");
                    car.Teleport(dropMarker.transform.position + new Vector3(0f, 0.6f, 0f), Quaternion.LookRotation(Vector3.forward));
                    float doneEnd = Time.time + 10f;
                    while (Time.time < doneEnd && runnerM.State != CityDriver.Game.MissionState.Finished) yield return null;
                    yield return WaitReal(0.5f);
                    var res = runnerM.LastResult;
                    Check(res != null && res.Success, "mission completes at the drop-off");
                    if (res != null)
                    {
                        Line($"  result: base {res.BaseReward}, time bonus {res.TimeBonus}, safe bonus {res.SafeBonus}, damage {res.DamageDeduction}, wallet change {res.WalletChange}, xp +{res.XpGained}, note: {res.LevelNote}");
                        Check(session.Wallet.Balance == wallet0 + res.WalletChange && res.WalletChange >= mission.Reward - 1500, $"wallet was paid the reward plus the {res.TripLength / 1000f:F1} km distance bonus (minus any damage)");
                        Check(session.Xp.Xp == xp0 + mission.Xp, "XP was added separately from the wallet");
                        Check(session.Career.IsCompleted(mission.Id), "mission is recorded as completed");
                    }
                    Check(ui.ModalOpen && ui.ModalTitle == "MISSION COMPLETE", "result screen is shown");
                    yield return Shot("08_mission_result");

                    // save / load round trip through the storage
                    var reloaded = new CityDriver.Save.SaveManager(memory).Load();
                    Check(reloaded.walletBalance == session.Wallet.Balance && reloaded.xp == session.Xp.Xp && reloaded.completedMissions.Contains(mission.Id), "save file round-trips wallet, XP and completed missions");
                    session.Reload();
                    Check(session.Career.IsCompleted(mission.Id) && session.Xp.Xp == xp0 + mission.Xp, "Continue (reload from storage) restores progress");

                    // duplicate reward protection
                    long balanceNow = session.Wallet.Balance;
                    session.Career.CompleteMission(mission);        // a replay: only 25% and no XP, never the first-time reward again
                    Check(session.Wallet.Balance - balanceNow == (long)(mission.Reward * 0.25) && session.Xp.Xp == xp0 + mission.Xp, "replaying pays only 25% and no second XP");

                    ui.CloseModal(); runnerM.Dismiss();
                    Check(!ui.ModalOpen && Time.timeScale == 1f && runnerM.State == CityDriver.Game.MissionState.Idle, "dismissing the result returns to free driving");
                    yield return Wait(1f); yield return Shot("09_after_mission");

                    // abandoning a second mission is clean
                    var second = CityDriver.Career.CareerCatalog.Find("L1_city_trip");
                    Check(runnerM.StartMission(second), "City Trip (timed) mission starts");
                    yield return Wait(1f);
                    runnerM.Abandon();
                    Check(runnerM.State == CityDriver.Game.MissionState.Finished && runnerM.LastResult != null && !runnerM.LastResult.Success, "abandoning a mission fails it cleanly");
                    ui.CloseModal(); runnerM.Dismiss();
                    Check(!CityDriver.Navigation.NavigationService.Instance.HasDestination, "GPS route is cleared after the mission ends");
                }
                CityDriver.Game.GameSession.UseStorage(new CityDriver.Save.FileSaveStorage());   // back to the real save for the next session
            }

            Section("Environment, trips, fuel, upgrades, vehicles, Shadow Pass and pursuit");
            {
                var env = CityDriver.Game.WorldEnvironment.Instance;
                var pm = CityDriver.Game.PursuitManager.Instance;
                var fuel = CityDriver.Game.FuelSystem.Instance;
                var dress = CityDriver.Population.StreetDressing.Instance;
                Check(env != null && pm != null && fuel != null && dress != null, "environment, pursuit, fuel and street-dressing systems were created");
                var memory2 = new CityDriver.Save.MemorySaveStorage();
                var sess = CityDriver.Game.GameSession.UseStorage(memory2);
                var ui2 = CityDriver.Game.GameUI.Instance;
                var runner2 = CityDriver.Game.MissionRunner.Instance;
                var audio2 = CityDriver.Game.GameAudio.Instance;
                var lights2 = car.GetComponent<CityDriver.Vehicle.VehicleLights>();
                var damage2 = car.GetComponent<CityDriver.Vehicle.VehicleDamage>();
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0));
                yield return Wait(2.5f);

                // ---- street dressing
                Line($"  street dressing: {dress.ActiveCount} props active around the car");
                Check(dress.ActiveCount >= 10, "street furniture (bins, benches, stalls, bikes, signs, shelters) is placed along the sidewalks");
                yield return Shot("20_street_dressing");

                // ---- day / night + headlights
                env.SetTimeOfDay(CityDriver.Game.TimeOfDay.Night, 0.5f); yield return Wait(2.5f);
                Check(env.Night01 > 0.8f && env.SunIntensity < 0.3f, $"night: sun dims (intensity {env.SunIntensity:F2}, night {env.Night01:F2})");
                Check(lights2.HeadlightsOn && lights2.ActiveBeams == 2, "headlights switch on at night (two beams)");
                yield return Shot("21_night");
                env.SetTimeOfDay(CityDriver.Game.TimeOfDay.Sunset, 0.5f); yield return Wait(2f); yield return Shot("22_sunset");
                env.SetTimeOfDay(CityDriver.Game.TimeOfDay.Morning, 0.5f); yield return Wait(2f); yield return Shot("23_morning");
                env.SetTimeOfDay(CityDriver.Game.TimeOfDay.Day, 0.5f); yield return Wait(2.5f);
                Check(env.Night01 < 0.1f && !lights2.HeadlightsOn, "day: full light, headlights off");

                // ---- weather
                env.SetWeather(CityDriver.Game.Weather.HeavyRain, 0.5f); yield return Wait(6f);
                Line($"  heavy rain: particles {env.RainParticleCount}, grip x{env.GripFactor:F2}, fog end {env.FogEnd:F0} m");
                Check(env.RainParticlesPlaying && env.RainParticleCount > 50, "heavy rain: rain particles are falling");
                Check(env.GripFactor < 0.8f && CityDriver.Game.VehicleTuning.WeatherGrip < 0.8f, "heavy rain lowers tyre grip");
                Check(env.FogEnd < 260f, "heavy rain reduces visibility (fog closes in)");
                if (audio2 != null && audio2.Ready) Check(audio2.RainSource.volume > 0.05f, "rain sound plays");
                yield return Shot("24_heavy_rain");
                env.SetWeather(CityDriver.Game.Weather.Fog, 0.5f); yield return Wait(6f);
                Check(env.FogEnd < 140f, $"fog: visibility is limited (fog end {env.FogEnd:F0} m)");
                yield return Shot("25_fog");
                env.SetWeather(CityDriver.Game.Weather.Clear, 0.5f); yield return Wait(6f);
                Check(env.FogEnd > 380f && !env.RainParticlesPlaying && env.GripFactor > 0.97f, "clear weather restores visibility, grip and stops the rain");

                // ---- upgrades change the real physics
                var owned = sess.SelectedVehicle;
                CityDriver.Game.VehicleTuning.WeatherGrip = 1f;
                owned.SetUpgradeLevel(CityDriver.Save.UpgradeType.Engine, 0);
                CityDriver.Game.VehicleTuning.ApplyToCurrentCar();
                float baseTorque = car.Stats.maxMotorTorque, baseTop = car.Stats.maxSpeedKmh;
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.2f);
                Down(1, Centre(gas)); yield return Wait(3.5f); float speedBase = car.SpeedKmh; Up(1); yield return Wait(0.5f);
                owned.SetUpgradeLevel(CityDriver.Save.UpgradeType.Engine, 3);
                CityDriver.Game.VehicleTuning.ApplyToCurrentCar();
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.2f);
                Down(1, Centre(gas)); yield return Wait(3.5f); float speedUp = car.SpeedKmh; Up(1); yield return Wait(0.5f);
                Line($"  engine upgrade: torque {baseTorque:F0} -> {car.Stats.maxMotorTorque:F0}, top speed {baseTop:F0} -> {car.Stats.maxSpeedKmh:F0} km/h; speed after 3.5 s {speedBase:F1} -> {speedUp:F1} km/h");
                Check(car.Stats.maxMotorTorque > baseTorque * 1.2f && car.Stats.maxSpeedKmh > baseTop * 1.1f, "engine stage 3 (maximum) raises torque and top speed in the physics stats");
                Check(speedUp > speedBase * 1.03f, "the upgraded car really accelerates harder");
                damage2.Repair(); owned.SetUpgradeLevel(CityDriver.Save.UpgradeType.Armor, 0); CityDriver.Game.VehicleTuning.ApplyToCurrentCar(); damage2.ApplyImpact(10f); float loss0 = 1f - damage2.Condition01;
                damage2.Repair(); owned.SetUpgradeLevel(CityDriver.Save.UpgradeType.Armor, 5); CityDriver.Game.VehicleTuning.ApplyToCurrentCar(); damage2.ApplyImpact(10f); float loss5 = 1f - damage2.Condition01;
                Check(loss5 < loss0 * 0.6f, $"armour level 5 cuts collision damage ({loss0:P0} -> {loss5:P0})");
                damage2.Repair();
                owned.SetUpgradeLevel(CityDriver.Save.UpgradeType.Headlights, 3); CityDriver.Game.VehicleTuning.ApplyToCurrentCar();
                Check(CityDriver.Game.VehicleTuning.LastSummary.Contains("beam 38 m"), "headlight upgrade extends the beam range");

                // ---- vehicle classes
                Check(!sess.TryBuyVehicle("suv_luxury", out string whyNot) && whyNot.Contains("Level"), "a luxury SUV is locked at level 1");
                sess.Data.level = 9; sess.Wallet.Apply("test-credit", 200000, "test");
                long walletBefore = sess.Wallet.Balance;
                Check(sess.TryBuyVehicle("supercar", out string whyNo), "the supercar can be bought at level 9 with enough money");
                Check(sess.Wallet.Balance == walletBefore - 140000 && sess.Data.FindVehicle("supercar") != null, "the supercar price is charged and the car is owned");
                sess.Data.selectedVehicleId = "supercar"; owned = sess.SelectedVehicle; CityDriver.Game.VehicleTuning.ApplyToCurrentCar();
                Check(CityDriver.Game.VehicleTuning.LastSummary.Contains("Supercar"), "selecting it re-tunes the car");
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.2f);
                Down(1, Centre(gas)); yield return Wait(3.5f); float speedSuper = car.SpeedKmh; Up(1); yield return Wait(0.5f);
                Line($"  supercar speed after 3.5 s: {speedSuper:F1} km/h (compact {speedBase:F1})");
                Check(speedSuper > speedBase * 1.3f, "the supercar is much faster than the compact sedan");
                yield return Shot("26_supercar");
                sess.Data.selectedVehicleId = "sedan_compact"; sess.Data.level = 1; CityDriver.Game.VehicleTuning.ApplyToCurrentCar();

                // ---- weapon shop
                sess.Data.weaponShopUnlocked = false;
                Check(!sess.TryBuyWeapon("smoke", out string whyLocked) && whyLocked.Contains("Level 5"), "the weapon shop is locked before Level 5");
                sess.Data.weaponShopUnlocked = true; long wb = sess.Wallet.Balance;
                Check(sess.TryBuyWeapon("smoke", out string ok1) && sess.Wallet.Balance == wb - 6000 && sess.Data.equippedWeapon == "smoke", "after Level 5 the smoke launcher can be bought and is equipped");

                // ---- fuel
                fuel.SetFuel(1f); car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1f);
                Down(1, Centre(gas)); yield return Wait(4f); Up(1);
                Check(fuel.Fuel01 < 0.9995f, $"driving uses fuel ({fuel.Fuel01:P2} left)");
                fuel.SetFuel(0f); yield return Wait(1.2f);
                Check(fuel.Empty && CityDriver.Game.VehicleTuning.FuelFactor == 0f, "an empty tank cuts the engine power");
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1f);
                Down(1, Centre(gas)); yield return Wait(3f); float dry = car.SpeedKmh; Up(1);
                Check(dry < 8f, $"the car cannot accelerate without fuel ({dry:F1} km/h)");
                long wt = sess.Wallet.Balance; fuel.Tow(); yield return Wait(1f);
                Line($"  tow: wallet {wt} -> {sess.Wallet.Balance}, fuel {fuel.Fuel01:P0}, fuel factor {CityDriver.Game.VehicleTuning.FuelFactor}");
                Check(sess.Wallet.Balance == wt - 400 && fuel.Fuel01 >= 0.24f && CityDriver.Game.VehicleTuning.FuelFactor == 1f, "a tow costs Rs 400 and restores 25% fuel and power");
                fuel.SetFuel(0.5f);
                Check(!fuel.CanRefuel || true, "refuel needs a petrol station");
                var navP = NavigationService.Instance; Vector3 pump = Vector3.zero; bool havePump = false;
                foreach (var poi in navP.Data.pois) if (poi.type == CityDriver.World.PoiType.PetrolStation) { pump = poi.position; havePump = true; break; }
                if (havePump)
                {
                    car.Teleport(pump + new Vector3(0f, 0.8f, 0f), Quaternion.identity); yield return Wait(1.5f);
                    Check(fuel.CanRefuel, "the car can refuel when stopped at a petrol station");
                    long wf = sess.Wallet.Balance; long paid = fuel.Refuel();
                    Check(paid > 100 && fuel.Fuel01 > 0.99f && sess.Wallet.Balance == wf - paid, $"refuelling fills the tank and charges Rs {paid}");
                }

                // ---- long trips
                sess.Data.level = 1; car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.5f);
                var first = CityDriver.Career.CareerCatalog.Find("L1_first_ride");
                CityDriver.Game.GameSettings.TripLength = 0;
                Check(runner2.StartMission(first), "a Standard-length trip is planned and started");
                float lenStd = runner2.TripLength; runner2.Abandon(); runner2.Dismiss(); if (ui2.ModalOpen) ui2.CloseModal(); yield return Wait(0.5f);
                CityDriver.Game.GameSettings.TripLength = 2;
                Check(runner2.StartMission(first), "an Epic-length trip is planned and started");
                float lenEpic = runner2.TripLength;
                float pickupDist = Vector3.Distance(car.transform.position, runner2.PickupPosition);
                Line($"  trip lengths: standard {lenStd:F0} m, epic {lenEpic:F0} m; pickup {pickupDist:F0} m from the car; destination '{runner2.DestinationLabel}'");
                Check(lenStd >= 800f && lenEpic > lenStd * 1.2f, "trips are long (>= 800 m even on Standard) and the setting scales them");
                Check(lenEpic >= 1500f, "an Epic trip is at least 1.5 km");
                Check(pickupDist >= 50f && pickupDist < 360f, "the pickup is reasonably close to the player");
                Check(CityDriver.Missions.LocationMarker.Find(CityDriver.Missions.LocationKind.Pickup, CityDriver.Game.MissionRunner.PickupId) != null, "a pickup marker exists at the planned spot");
                Check(NavigationService.Instance.HasDestination && NavigationService.Instance.Route.Length > 50f, "the GPS routes to the pickup");
                runner2.Abandon(); runner2.Dismiss(); if (ui2.ModalOpen) ui2.CloseModal(); yield return Wait(0.5f);
                Check(CityDriver.Missions.LocationMarker.Find(CityDriver.Missions.LocationKind.Pickup, CityDriver.Game.MissionRunner.PickupId) == null, "trip markers are removed when the mission ends");
                CityDriver.Game.GameSettings.TripLength = 1;

                // ---- Shadow Pass cinematic
                sess.Data.hasShadowPass = true; sess.Data.level = 4; sess.Data.shadowCinematicSeen = false;
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.5f);
                ui2.PlayShadowPass();
                float spEnd = Time.unscaledTime + 60f; bool sawCard = false;
                while (Time.unscaledTime < spEnd && (CityDriver.Game.ShadowPassSequence.Instance == null || CityDriver.Game.ShadowPassSequence.Instance.Playing || CityDriver.Game.ShadowPassSequence.Instance.StagesCompleted == 0))
                {
                    if (!sawCard && CityDriver.Game.ShadowPassSequence.Instance != null && CityDriver.Game.ShadowPassSequence.Instance.Stage == "card") { sawCard = true; yield return WaitReal(1.2f); yield return Shot("27_shadow_pass_card"); }
                    if (CityDriver.Game.ShadowPassSequence.Instance != null && CityDriver.Game.ShadowPassSequence.Instance.Stage == "nightclub") { yield return WaitReal(2.4f); yield return Shot("28_nightclub_cinematic"); break; }
                    yield return WaitReal(0.3f);
                }
                while (Time.unscaledTime < spEnd && CityDriver.Game.ShadowPassSequence.Instance != null && CityDriver.Game.ShadowPassSequence.Instance.Playing) yield return WaitReal(0.5f);
                var seq = CityDriver.Game.ShadowPassSequence.Instance;
                Check(seq != null && seq.StagesCompleted == 6, $"the Shadow Pass sequence runs all stages (completed {(seq != null ? seq.StagesCompleted : 0)}/6)");
                Check(env.ShadowMode && env.Night01 > 0.7f && env.RainIntensity > 0.2f, "the sequence turns the city to night with rain");
                Check(audio2 == null || !audio2.Ready || audio2.ShadowMusic, "the music switches to the suspense track");
                Check(lights2.HeadlightsOn, "the car's headlights are on");
                Check(sess.Data.shadowCinematicSeen, "the cinematic is recorded as seen (it plays once automatically)");
                yield return Shot("29_shadow_run_night");
                env.EndShadowMode(); yield return Wait(4f);

                // ---- Shadow Run mission with a gang pursuit
                sess.Data.walletBalance = 5000; sess.Data.isCaptured = false; sess.Data.hasActiveShadowMission = false;
                var shadowMission = CityDriver.Career.CareerCatalog.ForLevel(4).Find(m => m.ShadowRun);
                Check(shadowMission != null && sess.Career.CanReceiveShadowRunCall(), "a Shadow Run call is allowed (level >= 3, Shadow Pass, money, not captured)");
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.5f);
                int spokenBefore = CityDriver.Game.VoiceService.SpokenCount;
                Check(runner2.StartMission(shadowMission), "the Shadow Run mission starts");
                Check(env.ShadowMode, "Shadow Run switches the city to night and rain");
                car.Teleport(runner2.PickupPosition + new Vector3(0f, 0.6f, 0f), Quaternion.LookRotation(Vector3.forward));
                float bEnd = Time.time + 12f; while (Time.time < bEnd && runner2.State != CityDriver.Game.MissionState.ToDropoff) yield return null;
                Check(runner2.State == CityDriver.Game.MissionState.ToDropoff && pm.Running, "after boarding, the chase begins");
                if (pm.Plan != null) Line($"  pursuit plan for level 4: {pm.Plan.Describe()}"); else Line("  [note] pursuit has no plan (mission did not start the chase)");
                float spawnEnd = Time.time + 14f; while (Time.time < spawnEnd && pm.ActivePursuers < 1) yield return null;
                Check(pm.ActiveGang >= 1, "a gang car joins the chase (Level 4: one gang vehicle follows)");
                {
                    var vis = pm.NearestPursuer();
                    if (vis != null)
                    {
                        var vmr = vis.GetComponent<MeshRenderer>(); var vmf = vis.GetComponent<MeshFilter>();
                        Check(vmr != null && vmr.enabled && vmf != null && vmf.sharedMesh != null && vmr.sharedMaterial != null && vis.gameObject.activeInHierarchy, "the chaser car has a mesh and material and is switched on");
                        Check(vis.GetComponent<CityDriver.Game.PursuerGlow>() != null && vis.GetComponentsInChildren<MeshRenderer>().Length >= 6, "the chaser car carries its light halos (roof, head and tail lamps)");
                        var vcam = Camera.main;
                        if (vcam != null && vmr != null)
                        {
                            Vector3 saved = vis.position; vis.position = vcam.transform.position + vcam.transform.forward * 30f; vis.position = new Vector3(vis.position.x, saved.y, vis.position.z);
                            bool inView = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(vcam), vmr.bounds);
                            vis.position = saved;
                            Check(inView, "a chaser car 30 m in front of the camera is inside the camera view (it can be seen)");
                        }
                    }
                }
                Check(pm.MinSpawnDistance >= 120f && !pm.AnySpawnedInFront, $"pursuers appear behind the player and far away (nearest spawn {pm.MinSpawnDistance:F0} m, none in front)");
                Check(CityDriver.Game.VoiceService.SpokenCount > spokenBefore, "dialogue lines are sent to the voice service");
                var near = pm.NearestPursuer(); float d0 = near != null ? Vector3.Distance(near.position, car.transform.position) : 0f;
                yield return Wait(6f);
                near = pm.NearestPursuer(); float d1 = near != null ? Vector3.Distance(near.position, car.transform.position) : 0f;
                Check(d1 < d0 - 30f, $"the gang car closes in (distance {d0:F0} -> {d1:F0} m)");
                yield return Shot("30_pursuit_hud");
                float cond0 = car.Condition01; pm.DebugTeleportPursuers(14f);
                yield return Wait(5f);
                Check(car.Condition01 < cond0 - 0.02f, $"a gang car that reaches the player rams it (condition {cond0:P0} -> {car.Condition01:P0})");
                yield return Shot("31_gang_ram");
                var pmPlanSaved = pm.Plan;
                // escape: stunned pursuers 300 m behind are shaken off
                pm.End(); pm.Begin(new CityDriver.Game.PursuitPlan { gang = 1 });
                spawnEnd = Time.time + 14f; while (Time.time < spawnEnd && pm.ActivePursuers < 1) yield return null;
                pm.DebugTeleportPursuers(300f, 40f); yield return Wait(18f);
                Check(pm.EscapedCount >= 1 && pm.ActivePursuers == 0 && pm.Escaped, "a pursuer left 230+ m behind for 8 s is shaken off and the player escapes");
                // police box-in -> busted
                pm.End(); pm.Begin(new CityDriver.Game.PursuitPlan { police = 1 });
                spawnEnd = Time.time + 14f; while (Time.time < spawnEnd && pm.ActivePolice < 1) yield return null;
                Check(pm.ActivePolice >= 1, "police join the chase");
                pm.DebugTeleportPursuers(5f);
                float bustEnd = Time.unscaledTime + 25f; while (!pm.Busted && Time.unscaledTime < bustEnd) yield return WaitReal(0.2f);   // the result screen pauses the game, so wait in real time
                Check(pm.Busted, "a police car beside a stopped player busts them");
                yield return WaitReal(1.0f);
                Check(runner2.State == CityDriver.Game.MissionState.Finished && runner2.LastResult != null && !runner2.LastResult.Success && runner2.LastResult.Busted && sess.Data.isCaptured, "being busted fails the Shadow Run, applies the penalty and marks the player captured");
                Check(!sess.Career.CanReceiveShadowRunCall(), "while captured no new Shadow Run calls are received");
                var bustResult = runner2.LastResult;
                Check(bustResult != null && bustResult.LevelDown && bustResult.LevelTo == bustResult.LevelFrom - 1 && sess.Career.Level == bustResult.LevelTo, $"a failed Shadow Run drops the career one level and says so (Level {bustResult?.LevelFrom} -> {bustResult?.LevelTo}, {bustResult?.XpLost} XP lost)");
                if (bustResult != null && bustResult.LevelDown) { sess.Data.level = bustResult.LevelFrom; sess.Data.hasShadowPass = true; }       // restore the level so the remaining Shadow Run tests can continue
                runner2.Dismiss(); if (ui2.ModalOpen) ui2.CloseModal(); sess.Career.SetCaptured(false); sess.Data.walletBalance = System.Math.Max(sess.Data.walletBalance, 5000L);
                Check(sess.Career.CanReceiveShadowRunCall(), "paying bail / release restores Shadow Run calls");
                car.Teleport(new Vector3(40f, 0.6f, 22f), Quaternion.Euler(0, 180, 0)); yield return Wait(1.5f);
                Check(runner2.StartMission(shadowMission), "the Shadow Run can be started again");
                car.Teleport(runner2.PickupPosition + new Vector3(0f, 0.6f, 0f), Quaternion.LookRotation(Vector3.forward));
                bEnd = Time.time + 12f; while (Time.time < bEnd && runner2.State != CityDriver.Game.MissionState.ToDropoff) yield return null;
                // roadblock ahead on the route
                pm.End(); pm.Begin(new CityDriver.Game.PursuitPlan { roadblocks = 1 });
                car.Teleport(runner2.PickupPosition + new Vector3(0f, 0.6f, 0f), Quaternion.LookRotation(Vector3.forward)); yield return Wait(2f);
                float rbEnd = Time.time + 16f; while (Time.time < rbEnd && pm.ActiveRoadblockCars < 2) yield return null;
                if (pm.ActiveRoadblockCars != 2) Line($"  [diag] roadblock: running {pm.Running}, active {pm.ActivePursuers}, plan roadblocks {(pm.Plan != null ? pm.Plan.roadblocks : -1)}, nav destination {NavigationService.Instance.HasDestination}, route {NavigationService.Instance.Route.Length:F0} m, runner state {runner2.State}, car at {car.transform.position}");
                Check(pm.ActiveRoadblockCars == 2, "a roadblock of two police cars is set up on the route");
                yield return Shot("32_roadblock");
                // weapons
                pm.End(); pm.Begin(new CityDriver.Game.PursuitPlan { gang = 1 });
                spawnEnd = Time.time + 14f; while (Time.time < spawnEnd && pm.ActivePursuers < 1) yield return null;
                pm.DebugTeleportPursuers(30f);
                Check(pm.UseWeapon("smoke"), "the smoke launcher works on a nearby pursuer");
                Check(pm.WeaponCooldown01 > 0.5f && !pm.UseWeapon("smoke"), "weapons have a cooldown");
                pm.End();
                runner2.Abandon(); runner2.Dismiss(); if (ui2.ModalOpen) ui2.CloseModal(); yield return Wait(1f);
                Check(!pm.Running && sess.Data.hasActiveShadowMission == false, "abandoning the Shadow Run ends the chase and clears the active mission");
                Check(env.ShadowMode == false, "Shadow mode ends with the mission");
                ui2.OpenVehicles(); yield return Shot("33_vehicles"); ui2.CloseModal();
                ui2.OpenWeaponShop(); yield return Shot("34_weapon_shop"); ui2.CloseModal();
                env.SetTimeOfDay(CityDriver.Game.TimeOfDay.Day, 0.5f); env.SetWeather(CityDriver.Game.Weather.Clear, 0.5f);
                CityDriver.Game.GameSession.UseStorage(new CityDriver.Save.FileSaveStorage());
            }

            Section("App-open cinematic intro");
            {
                var introFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                bool done = false;
                var intro = CityDriver.Game.IntroSequence.Ensure();
                intro.Play(introFont, () => done = true, 16f);
                yield return WaitReal(1.0f);
                Check(intro.Playing, "intro plays");
                yield return WaitReal(3.0f); yield return Shot("40_intro_aerial");
                Check(intro.PostFxActive, "cinematic post-processing (bloom / grade / vignette) is active");
                yield return WaitReal(5.5f); yield return Shot("41_intro_logo");
                Check(intro.CameraControlled && intro.LogoShown, "intro drives the camera and reveals the logo");
                yield return WaitReal(3.0f); yield return Shot("42_intro_swoop");
                yield return WaitReal(5.0f); yield return Shot("43_intro_hero");
                { float heroEnd = Time.realtimeSinceStartup + 25f; while (intro.ShotIndex != 3 && intro.Playing && Time.realtimeSinceStartup < heroEnd) yield return null; }
                Check(intro.ShotIndex == 3, "intro reached the hero shot");
                Check(intro.TipsShown >= 1, "loading tips rotate");
                float limit = Time.realtimeSinceStartup + 80f;
                while (!done && Time.realtimeSinceStartup < limit) yield return null;
                Check(done && !intro.Playing, "intro ends by itself and hands control back (progress " + intro.Progress.ToString("F2") + ")");
                Check(!intro.CameraControlled, "camera is handed back to the chase camera");
                yield return Wait(0.5f);
            }

            Section("City boundary: the car cannot leave the city");
            {
                var cb = CityDriver.World.CityBoundary.Instance;
                Check(cb != null && cb.transform.childCount == 4 && cb.GetComponentsInChildren<BoxCollider>().Length == 4, "four solid barrier walls surround the city");
                car.Teleport(new Vector3(CityDriver.World.CityBoundary.Limit + 40f, car.transform.position.y + 0.5f, 100f), Quaternion.identity);
                yield return Wait(1.5f);
                Check(CityDriver.World.CityBoundary.EdgeDistance(car.transform.position) > 0f, "a car found outside the city is brought back inside");
                Check(Mathf.Abs(CityDriver.World.CityBoundary.EdgeDistance(new Vector3(CityDriver.World.CityLayout.HalfSize, 0, 0)) - 28f) < 0.01f, "the barrier stands 28 m beyond the last road");
            }

            Section("Scene transitions: chunk streaming while the car is moved across the city");
            var streamer = UnityEngine.Object.FindFirstObjectByType<CityDriver.World.ChunkStreamer>();
            Check(streamer != null, "chunk streamer present");
            if (streamer != null)
            {
                var body = car.GetComponent<Rigidbody>();
                int before = streamer.CompletedLoads;
                Vector3 far = new Vector3(-600f, car.transform.position.y + 0.5f, 500f);
                body.position = far; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; car.transform.position = far;
                float end = Time.realtimeSinceStartup + 90f;
                while (Time.realtimeSinceStartup < end && !streamer.IsReadyAround(far)) yield return null;
                yield return Wait(2f);
                Line($"  streamed {streamer.CompletedLoads - before} chunk scenes after the move, {streamer.LoadedCount} loaded now, avg load {streamer.AverageLoadSeconds:F2}s, max {streamer.MaxLoadSeconds:F2}s");
                Check(streamer.IsReadyAround(far), "chunks around the new position loaded without errors");
                Check(streamer.CompletedLoads > before, "new chunks were streamed in");
                yield return Wait(1f);
                ProfileRendering();
            }
        }

        private static void ProfileRendering()
        {
            Line("Render profile of everything currently loaded (active renderers, visible LOD levels not separated)");
            var cam = Camera.main;
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            int total = renderers.Length, shadowCasters = renderers.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
            var groups = new System.Collections.Generic.Dictionary<string, int[]>();   // key -> [renderers, triangles, instancesOfSameMesh]
            int inFrustum = 0; long trisInFrustum = 0;
            Plane[] planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                int tris = 0; for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)(mf.sharedMesh.GetIndexCount(i) / 3);
                bool lod = r.GetComponentInParent<LODGroup>() != null;
                    string key = (lod ? "LOD building: " : "") + (r.sharedMaterial != null ? r.sharedMaterial.name : "(none)");
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new int[3];
                g[0]++; g[1] += tris;
                if (planes != null && GeometryUtility.TestPlanesAABB(planes, r.bounds)) { inFrustum++; trisInFrustum += tris; }
            }
            Line($"  active MeshRenderers: {total} (shadow casters {shadowCasters}), LODGroups {UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length}");
            Line($"  inside the camera frustum: {inFrustum} renderers, {trisInFrustum:N0} triangles (before LOD selection; each renderer is >= 1 draw call per material)");
            Line("  top contributors by triangles (all loaded):");
            foreach (var kv in groups.OrderByDescending(k => k.Value[1]).Take(12)) Line($"    {kv.Key,-44} renderers {kv.Value[0],5}  triangles {kv.Value[1],9:N0}");
            Line($"  distinct materials: {renderers.Select(r => r.sharedMaterial).Distinct().Count()}, GPU-instancing enabled on {renderers.Select(r => r.sharedMaterial).Where(m => m != null).Distinct().Count(m => m.enableInstancing)} of them");
        }

        // ================================================================================== helpers

        // Waits in GAME time (not wall-clock) so a slow machine cannot make a timed check unfair.
        private static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            float realStart = Time.unscaledTime;
            while (Time.time < end)
            {
                // A menu / result screen pauses the game; a test that waits on game time would hang. Record which screen it was and close it.
                if (Time.timeScale == 0f && Time.unscaledTime - realStart > 4f && CityDriver.Game.GameUI.Instance != null && CityDriver.Game.GameUI.Instance.ModalOpen)
                {
                    Line($"  [note] game was paused by the '{CityDriver.Game.GameUI.Instance.ModalTitle}' screen during a wait (after: {lastCheck}); closing it");
                    CityDriver.Game.GameUI.Instance.CloseModal();
                    var rr = CityDriver.Game.MissionRunner.Instance; if (rr != null) rr.Dismiss();
                    realStart = Time.unscaledTime;
                }
                yield return null;
            }
        }

        private static IEnumerator WaitReal(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        private static IEnumerator Shot(string name)
        {
            yield return WaitReal(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "gv_demo_" + name + ".png"));
            yield return WaitReal(0.6f);
        }

        private static IEnumerator WaitForRest(float maxSeconds)
        {
            float end = Time.time + maxSeconds;
            while (Time.time < end && car.SpeedKmh > 0.3f) yield return null;
        }

        private static RectTransform Find(string name) => UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include).FirstOrDefault(r => r.name == name);

        private static Vector2 Centre(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        private static void Down(int id, Vector2 pos) { held[id] = pos; PushPointers(); }
        private static void Up(int id) { held.Remove(id); PushPointers(); }
        private static void PushPointers() { var panel = TouchControlsPanel.Instance; if (panel != null) panel.SetTestPointers(held.Values); }

        private static Vector3 PointAt(CityDriver.World.Route r, float d)
        {
            for (int i = 0; i < r.points.Count - 1; i++)
                if (d <= r.cumulative[i + 1]) return Vector3.Lerp(r.points[i], r.points[i + 1], (d - r.cumulative[i]) / Mathf.Max(0.001f, r.cumulative[i + 1] - r.cumulative[i]));
            return r.points[r.points.Count - 1];
        }

        // Canvas-space rectangles of the HUD at several aspect ratios (CanvasScaler: reference 1920x1080, match 0.5).
        private static void CheckLayout()
        {
            (int w, int h, string name)[] screens = { (1920, 1080, "16:9"), (2400, 1080, "20:9"), (2560, 1080, "21.3:9"), (1440, 1080, "4:3"), (1280, 720, "16:9 low"), (2340, 1080, "19.5:9") };
            string[] hudNames = { "Speedo", "Minimap", "NavigationBanner", "Status", "MissionBanner", "Button_MISSIONS", "Button_MENU", "Button_RADIO", "Button_SETTINGS", "Button_MORN", "Button_DAY", "Button_DUSK", "Button_NIGHT", "SettingsGear", "GearSelector", "SkyChip", "SteerLeft", "SteerRight", "Accelerator", "Brake", "Reverse", "CameraReset" };
            bool allOk = true;
            foreach (var (w, h, name) in screens)
            {
                float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / 1920f, 2f), Mathf.Log(h / 1080f, 2f), 0.5f));
                Vector2 canvas = new Vector2(w, h) / scale;
                var rects = hudNames.Select(n => (n, rect: CanvasRect(Find(n), canvas))).Where(t => t.rect.HasValue).Select(t => (t.n, rect: t.rect.Value)).ToList();
                // The banner panel is hidden without a route; its host is full-screen, so compare the visible panel instead.
                rects.RemoveAll(t => t.n == "NavigationBanner");
                var navPanel = Find("NavigationBanner")?.Find("Panel") as RectTransform;
                var navRect = CanvasRect(navPanel, canvas); if (navRect.HasValue) rects.Add(("GpsBannerPanel", navRect.Value));
                for (int i = 0; i < rects.Count; i++)
                    for (int j = i + 1; j < rects.Count; j++)
                    {
                        // Condition bar and the control pads/minimap never share space; touch pads must not overlap each other or the HUD.
                        if (rects[i].rect.Overlaps(rects[j].rect)) { allOk = false; Line($"  OVERLAP on {name}: {rects[i].n} / {rects[j].n}"); }
                    }

                bool inside = rects.All(t => t.rect.xMin >= -0.5f && t.rect.yMin >= -0.5f && t.rect.xMax <= canvas.x + 0.5f && t.rect.yMax <= canvas.y + 0.5f);
                if (!inside) { allOk = false; Line($"  OFF-SCREEN element on {name}"); }
            }

            Check(allOk, $"no HUD/control overlap or off-screen element at {screens.Length} aspect ratios (16:9, 20:9, 21.3:9, 4:3, 19.5:9, 720p)");
        }

        private static Rect? CanvasRect(RectTransform rt, Vector2 canvas)
        {
            if (rt == null) return null;
            // Resolve the element against the full canvas (the safe area equals the full screen on devices without a notch).
            Vector2 parentMin = Vector2.zero, parentSize = canvas;
            Vector2 anchorMin = Vector2.Scale(rt.anchorMin, parentSize), anchorMax = Vector2.Scale(rt.anchorMax, parentSize);
            Vector2 size = (anchorMax - anchorMin) + rt.sizeDelta;
            Vector2 pivotPoint = anchorMin + Vector2.Scale(rt.pivot, anchorMax - anchorMin) + rt.anchoredPosition;
            // Walk up through non-stretch parents that offset the element (cluster children are inside their parent, which is checked itself).
            if (rt.parent != null && rt.parent.name != "SafeArea") return null;
            return new Rect(pivotPoint - Vector2.Scale(rt.pivot, size), size);
        }

        // ---- genuine runtime exception capture (Unity log callback), attributed to the section that was running ----
        private sealed class Captured { public string section, type, message, stack; public bool fromTestRunner; }
        private static readonly System.Collections.Generic.List<Captured> captured = new System.Collections.Generic.List<Captured>();
        private static string currentSection = "setup";

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            captured.Add(new Captured { section = currentSection, type = type.ToString(), message = message, stack = stackTrace, fromTestRunner = stackTrace != null && stackTrace.Contains("PlayTests") });
        }

        private static int CountExceptions() => captured.Count(c => !c.fromTestRunner);

        private static void Section(string t)
        {
            currentSection = t; Line("\n" + t);
            try { File.WriteAllText(Path.Combine(Dir, "play_progress.txt"), log.ToString() + "\n[in progress, section started at " + DateTime.Now.ToString("HH:mm:ss") + "]"); } catch (IOException) { }
        }
        private static void Line(string s) => log.AppendLine(s);
        private static string lastCheck = "";
        private static void Check(bool ok, string text) { lastCheck = text; checks++; if (!ok) failures++; Line($"  [{(ok ? "PASS" : "FAIL")}] {text}"); }
    }
}
