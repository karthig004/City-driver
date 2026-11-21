using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CityDriver.CameraSystem;
using CityDriver.Controls;
using CityDriver.Missions;
using CityDriver.UI;
using CityDriver.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Automated physics checks for the vehicle. Steps the REAL physics engine in Edit Mode (Physics.Simulate) with
    /// scripted driver input and measures the result. This is a regression aid, NOT a substitute for driving the car by
    /// hand in Play Mode: it cannot judge feel, and edit-mode physics can differ slightly from the player.
    /// Output: Bridge/vehicle_test.txt and the Console.
    /// </summary>
    public static class VehicleTests
    {
        private const float Dt = 0.02f;
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        // South ring road, heading east in the left lane (left-hand traffic): flat, 156 m long.
        private const float LaneZ = -80f + 2.25f;
        private const float StartX = -78f;

        private static GameObject prefab;
        private static GameObject pad;
        private static readonly System.Collections.Generic.List<GameObject> spawned = new System.Collections.Generic.List<GameObject>();
        private static readonly Vector3 PadStart = new Vector3(1500f, 0.35f, 0f); // open flat test pad: no curbs to hit
        private static StringBuilder log;
        private static int failures;

        private sealed class Rig
        {
            public GameObject Go;
            public VehicleController C;
            public Rigidbody Rb => C.Body;
            public Vector3 Pos => Go.transform.position;
        }

        // ==================================================================================== entry points

        [BridgeCommand]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[VehicleTest] Exit Play Mode first."); return; }
            if (EditorSceneManager.GetActiveScene().path != CityScenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { Debug.LogError("[VehicleTest] Active scene has unsaved changes."); return; }
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            }

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.PrefabPath);
            if (prefab == null) { Debug.LogError("[VehicleTest] Sedan prefab missing. Run City Driver > Build > Phase 4."); return; }

            log = new StringBuilder();
            failures = 0;
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                PadOn();
                Spawning();
                Settle();
                Acceleration();
                Braking();
                ReverseTests();
                Steering();
                Stability();
                Suspension();
                TopSpeed();
                Collision();
                FlipRecovery();
                WheelAlignment();
                CameraFollow();
                Handling();
            }
            catch (Exception e) { Line($"EXCEPTION: {e}"); failures++; }
            finally
            {
                CleanupSpawned();
                PadOff();
                Physics.simulationMode = previousMode;
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.SaveScene(scene); // scene content is unchanged; clears the dirty flag set by test objects
            }

            Line($"\nRESULT: {(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED")}");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            File.WriteAllText(Path.Combine(dir, "vehicle_test.txt"), log.ToString());
            Debug.Log("[VehicleTest]\n" + log);
        }

        /// <summary>Renders reference screenshots of the car and HUD into Bridge/.</summary>
        [BridgeCommand]
        public static void CaptureVehicleShots()
        {
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.PrefabPath);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                Physics.SyncTransforms();
                var start = FindStart();
                Rig r = Spawn(start.position + Vector3.up * 0.3f, start.eulerAngles.y);
                Step(r, default, 1.5f);
                r.Go.GetComponent<VehicleWheelVisuals>().UpdateVisuals();

                Vector3 p = r.Pos;
                CityBuilder.Shot(dir, "car_front34", p + new Vector3(4.2f, 1.6f, 4.2f) * 1f, LookAt(p + new Vector3(4.2f, 1.6f, 4.2f), p + Vector3.up * 0.7f));
                CityBuilder.Shot(dir, "car_rear34", p + new Vector3(-4.2f, 1.8f, -4.2f), LookAt(p + new Vector3(-4.2f, 1.8f, -4.2f), p + Vector3.up * 0.7f));
                CityBuilder.Shot(dir, "car_side", p + new Vector3(6f, 0.9f, 0f), LookAt(p + new Vector3(6f, 0.9f, 0f), p + Vector3.up * 0.7f));

                // Follow-camera view + HUD (overlay UI is rendered through a camera-space canvas for the capture).
                var follow = UnityEngine.Object.FindAnyObjectByType<VehicleCameraController>();
                var cam = follow.GetComponent<Camera>();
                Vector3 savedPos = cam.transform.position; Quaternion savedRot = cam.transform.rotation; float savedFov = cam.fieldOfView;
                follow.SetTarget(r.Go.transform, r.Rb);
                follow.Tick(Dt);
                CityBuilder.Shot(dir, "car_follow_camera", cam.transform.position, cam.transform.rotation);

                var hud = UnityEngine.Object.FindAnyObjectByType<VehicleHUD>();
                hud.SetVehicle(r.C);
                Step(r, new VehicleInput { Throttle = 1f }, 2f);
                hud.Refresh();
                ShotWithHud(dir, "hud", follow, cam, hud);

                follow.SetTarget(null, null);
                cam.transform.SetPositionAndRotation(savedPos, savedRot); cam.fieldOfView = savedFov;
                Destroy(r);
            }
            finally
            {
                CleanupSpawned();
                Physics.simulationMode = previousMode;
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
        }

        // ==================================================================================== tests

        private static void Spawning()
        {
            Section("0. Spawn point and facing");
            Transform start = FindStart();
            LocationMarker garage = null;
            foreach (var m in UnityEngine.Object.FindObjectsByType<LocationMarker>()) if (m.Kind == LocationKind.GarageEntrance) garage = m;

            Rig r = Spawn(start.position + Vector3.up * 0.3f, start.eulerAngles.y);
            Vector3 toGarage = (garage.transform.position - r.Pos); toGarage.y = 0;
            float facing = Vector3.Dot(r.Go.transform.forward, toGarage.normalized);
            Line($"  player_start at {Fmt(start.position)}, car heading {r.Go.transform.eulerAngles.y:F0} deg, facing garage driveway: dot={facing:F2}");
            Check(facing > 0.95f, "vehicle faces the garage driveway");
            Destroy(r);
        }

        private static void Settle()
        {
            Section("1. Settling on the road");
            Rig r = Spawn(new Vector3(StartX, 0.35f, LaneZ), 90f);
            Step(r, default, 2f);
            Line($"  grounded wheels={r.C.GroundedWheels}/4, body height={r.Pos.y:F3} m, tilt={r.C.TiltAngle:F2} deg, speed={r.C.SpeedKmh:F2} km/h");
            Check(r.C.GroundedWheels == 4, "all four wheels touch the road");
            Check(r.C.SpeedKmh < 0.5f, "car does not creep when idle");
            Check(r.C.TiltAngle < 3f, "car sits level");
            Destroy(r);
        }

        private static void Acceleration()
        {
            Section("2. Acceleration (full throttle, flat road)");
            Rig r = Spawn(new Vector3(StartX, 0.35f, LaneZ), 90f);
            Step(r, default, 1f);
            float x0 = r.Pos.x, t30 = -1, t60 = -1, t100 = -1, maxKmh = 0, maxTilt = 0; int minGroundAccel = 4;
            float elapsed = 0;
            Step(r, _ => new VehicleInput { Throttle = 1f }, 14f, t =>
            {
                elapsed = t; minGroundAccel = Mathf.Min(minGroundAccel, r.C.GroundedWheels); float kmh = r.C.SpeedKmh; maxKmh = Mathf.Max(maxKmh, kmh); maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle);
                if (t30 < 0 && kmh >= 30) t30 = t; if (t60 < 0 && kmh >= 60) t60 = t; if (t100 < 0 && kmh >= 100) t100 = t;
            }, () => r.Pos.x > 76f);
            Line($"  0-30 km/h: {Fmt(t30)} s | 0-60: {Fmt(t60)} s | 0-100: {Fmt(t100)} s | speed at end of runway: {maxKmh:F0} km/h after {elapsed:F1} s, {r.Pos.x - x0:F0} m");
            Line($"  gear shown at end: {r.C.GearLabel} | lateral drift: {r.Pos.z - LaneZ:F2} m | max tilt: {maxTilt:F1} deg | wheelspin-free launch (no flip)");
            Check(t60 > 0 && t60 < 9f, "0-60 km/h reached in under 9 s");
            Check(Mathf.Abs(r.Pos.z - LaneZ) < 1.5f, "car tracks straight under power");
            Check(maxTilt < 8f, "no excessive pitch under acceleration");
            Check(minGroundAccel == 4, $"wheels never lose the road along 154 m incl. junction seams (min wheels on ground {minGroundAccel})");
            Destroy(r);
        }

        private static void Braking()
        {
            Section("3. Braking from 60 km/h");
            Rig r = Spawn(new Vector3(StartX, 0.35f, LaneZ), 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (60f / 3.6f);
            float x0 = r.Pos.x, maxTilt = 0, t = 0;
            Step(r, _ => new VehicleInput { Brake = 1f }, 8f, s => { t = s; maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle); }, () => r.C.SpeedKmh < 0.5f);
            float dist = r.Pos.x - x0;
            Line($"  stopped in {t:F2} s over {dist:F1} m (avg decel {(60f / 3.6f) / Mathf.Max(0.01f, t):F1} m/s2) | lateral drift {r.Pos.z - LaneZ:F2} m | max pitch {maxTilt:F1} deg");
            Check(dist > 12f && dist < 40f, "60 km/h stopping distance is between 12 and 40 m");
            Check(Mathf.Abs(r.Pos.z - LaneZ) < 1f, "car brakes in a straight line");
            Destroy(r);

            Section("3b. Handbrake from 60 km/h");
            r = Spawn(new Vector3(StartX, 0.35f, LaneZ), 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (60f / 3.6f);
            x0 = r.Pos.x; maxTilt = 0; t = 0;
            Step(r, _ => new VehicleInput { Handbrake = true }, 10f, s => { t = s; maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle); }, () => r.C.SpeedKmh < 0.5f);
            Line($"  stopped in {t:F2} s over {r.Pos.x - x0:F1} m | max tilt {maxTilt:F1} deg");
            Check(maxTilt < 15f, "handbrake does not flip the car");
            Destroy(r);
        }

        private static void ReverseTests()
        {
            Section("4. Reverse");
            Rig r = Spawn(new Vector3(0f, 0.35f, LaneZ), 90f);
            Step(r, default, 0.5f);
            float x0 = r.Pos.x, minFwd = 0;
            Step(r, _ => new VehicleInput { Reverse = 1f }, 6f, _ => minFwd = Mathf.Min(minFwd, r.C.ForwardSpeed));
            Line($"  from standstill, reverse pedal 6 s: max reverse speed {-minFwd * 3.6f:F1} km/h, moved {r.Pos.x - x0:F1} m, gear {r.C.GearLabel}, drift {r.Pos.z - LaneZ:F2} m");
            Check(-minFwd * 3.6f > 10f, "car reverses");
            Check(-minFwd * 3.6f < r.C.Stats.maxReverseSpeedKmh + 5f, "reverse speed is limited");
            Destroy(r);

            r = Spawn(new Vector3(StartX, 0.35f, LaneZ), 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (40f / 3.6f);
            float tStop = -1, tRev = -1;
            Step(r, _ => new VehicleInput { Reverse = 1f }, 8f, t =>
            {
                if (tStop < 0 && r.C.ForwardSpeed < 0.5f) tStop = t;
                if (tRev < 0 && r.C.ForwardSpeed < -2f) tRev = t;
            });
            Line($"  reverse pedal while rolling forward at 40 km/h: braked to a stop at {Fmt(tStop)} s, then reversed from {Fmt(tRev)} s");
            Check(tStop > 0 && tRev > tStop, "reverse pedal brakes first, then drives backwards");
            Destroy(r);
        }

        private static void Handling()
        {
            Section("15. Every car drives differently: grip, power slide and handbrake drift");
            var table = new Dictionary<string, float[]>();                       // id -> steady lateral g, power-slide slip deg, handbrake slip deg, power drift amount, handbrake drift amount
            foreach (var spec in CityDriver.Game.VehicleCatalog.All)
            {
                var m = new float[5]; float worstTilt = 0f, worstEnd = 0f;
                for (int test = 0; test < 3; test++)
                {
                    Rig r = Spawn(PadStart, 90f);
                    CityDriver.Game.VehicleTuning.Apply(r.C, new CityDriver.Save.OwnedVehicleData { vehicleId = spec.id });
                    Step(r, default, 0.6f);
                    float start = test == 0 ? 60f : 90f;
                    r.Rb.linearVelocity = r.Go.transform.forward * (start / 3.6f);
                    float slip = 0f, drift = 0f, yawSum = 0f, endSlip = 0f; int yawN = 0;
                    Step(r, t => test == 0 ? HoldSpeed(r, 60f, 0.55f)
                                : test == 1 ? new VehicleInput { Steer = 0.9f, Throttle = 1f }
                                : t < 0.9f ? new VehicleInput { Steer = 0.7f, Handbrake = true, Throttle = 0.3f } : new VehicleInput { Steer = 0f, Throttle = 0.4f }, 3f, t =>
                    {
                        worstTilt = Mathf.Max(worstTilt, r.C.TiltAngle);
                        slip = Mathf.Max(slip, Mathf.Abs(r.C.SlipAngle)); drift = Mathf.Max(drift, r.C.DriftAmount); endSlip = Mathf.Abs(r.C.SlipAngle);
                        if (test == 0 && t > 1.5f) { yawSum += Mathf.Abs(r.Rb.angularVelocity.y); yawN++; }
                    });
                    if (test == 0) m[0] = (r.C.SpeedKmh / 3.6f) * (yawSum / Mathf.Max(1, yawN)) / 9.81f;
                    else if (test == 1) { m[1] = slip; m[3] = drift; }
                    else { m[2] = slip; m[4] = drift; worstEnd = Mathf.Max(worstEnd, endSlip); }
                    Destroy(r);
                }
                table[spec.id] = m;
                Line($"  {spec.name,-14} {(spec.drive == DriveLayout.Rear ? "RWD" : spec.drive == DriveLayout.Front ? "FWD" : "AWD")} | steady turn {m[0]:F2} g | power slide: slip {m[1]:F0} deg, drift {m[3]:F2} | handbrake: slip {m[2]:F0} deg, drift {m[4]:F2} | after the slide, slip {worstEnd:F0} deg | worst tilt {worstTilt:F0}");
                Check(worstEnd < 25f, $"{spec.name} straightens out again after a handbrake turn (slip {worstEnd:F0} deg 2 s later)");
                Check(worstTilt < 40f, $"{spec.name} stays on its wheels in a power slide and a handbrake turn (tilt {worstTilt:F0} deg)");
            }
            var taxi = table["sedan_compact"]; var bulldog = table["muscle_coupe"]; var apex = table["sedan_sports"]; var vantage = table["supercar"]; var lux = table["suv_luxury"];
            Check(bulldog[1] > taxi[1] + 2f, $"the muscle car breaks loose under power much more than the taxi ({bulldog[1]:F0} vs {taxi[1]:F0} deg)");
            Check(apex[2] >= 8f && bulldog[2] >= 8f, $"the rear-drive sports cars drift with the handbrake (Apex {apex[2]:F0}, Bulldog {bulldog[2]:F0} deg)");
            Check(vantage[0] > taxi[0], $"the supercar corners harder than the taxi ({vantage[0]:F2} vs {taxi[0]:F2} g)");
            Check(lux[3] < bulldog[3], "the luxury cruiser is more stable than the muscle car");
        }

        private static void Steering()
        {
            Section("5. Steering (full lock) at low / medium / high speed");
            foreach (float kmh in new[] { 20f, 50f, 100f })
            {
                Rig r = Spawn(PadStart, 90f);
                Step(r, default, 0.5f);
                r.Rb.linearVelocity = r.Go.transform.forward * (kmh / 3.6f);
                float maxTilt = 0, maxSlip = 0, yawSum = 0; int yawCount = 0, minGround = 4; var airborneLog = new List<string>();
                Step(r, _ => HoldSpeed(r, kmh, 1f), 3.5f, t =>
                {
                    maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle);
                    minGround = Mathf.Min(minGround, r.C.GroundedWheels);
                    if (r.C.GroundedWheels < 3 && airborneLog.Count < 6) airborneLog.Add($"x={r.Pos.x:F1} wheels={r.C.GroundedWheels} tilt={r.C.TiltAngle:F1}");
                    Vector3 v = r.Rb.linearVelocity; v.y = 0;
                    if (v.magnitude > 2f) maxSlip = Mathf.Max(maxSlip, Vector3.Angle(r.Go.transform.forward, v));
                    if (t > 2f) { yawSum += Mathf.Abs(r.Rb.angularVelocity.y); yawCount++; }
                });
                float yaw = yawSum / Mathf.Max(1, yawCount);
                float speed = r.C.SpeedKmh / 3.6f;
                Line($"  {kmh:F0} km/h: wheel angle {r.C.SteerAngle:F1} deg | yaw {yaw * Mathf.Rad2Deg:F0} deg/s | turn radius {speed / Mathf.Max(0.01f, yaw):F1} m | lateral {speed * yaw / 9.81f:F2} g | max slip angle {maxSlip:F0} deg | max tilt {maxTilt:F1} deg | min wheels on ground {minGround}");
                if (airborneLog.Count > 0) Line("    frames with < 3 wheels on road: " + string.Join(" | ", airborneLog));
                Check(maxTilt < 25f, $"{kmh:F0} km/h full-lock turn stays stable (tilt < 25 deg)");
                Check(maxSlip < 30f, $"{kmh:F0} km/h full-lock turn does not spin out (max slip angle {maxSlip:F0} < 30 deg)");
                Check(speed * yaw / 9.81f < 1.25f, $"{kmh:F0} km/h full-lock turn stays within realistic grip (< 1.25 g)");
                if (kmh > 90f) Check(speed / Mathf.Max(0.01f, yaw) > 35f, "high-speed turn radius is wide (> 35 m): no twitchy steering");
                Destroy(r);
            }
        }

        private static void Stability()
        {
            Section("6. Stability stress tests");
            // Rapid lane-change style slalom at 120 km/h.
            Rig r = Spawn(PadStart, 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (120f / 3.6f);
            float maxTilt = 0, minUp = 1;
            Step(r, t => HoldSpeed(r, 120f, (Mathf.FloorToInt(t) % 2 == 0) ? 1f : -1f), 4f, _ => { maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle); minUp = Mathf.Min(minUp, r.Go.transform.up.y); });
            Line($"  120 km/h full-lock slalom: max tilt {maxTilt:F1} deg, min up.y {minUp:F2}");
            Check(minUp > 0.85f && maxTilt < 25f, "120 km/h slalom: body tilt stays under 25 deg");
            float slalomSlip = 0; { Vector3 v = r.Rb.linearVelocity; v.y = 0; if (v.magnitude > 2f) slalomSlip = Vector3.Angle(r.Go.transform.forward, v); }
            Check(slalomSlip < 30f, $"120 km/h slalom: car not spinning at the end (slip {slalomSlip:F0} deg)");
            Destroy(r);

            r = Spawn(PadStart, 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (100f / 3.6f);
            maxTilt = 0; float drift = 0;
            Step(r, _ => new VehicleInput { Brake = 1f, Steer = 0.6f }, 3.5f, _ => { maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle); drift = Mathf.Max(drift, Mathf.Abs(r.Pos.z - LaneZ)); });
            Line($"  100 km/h panic brake + steer 0.6: max tilt {maxTilt:F1} deg, lateral movement {drift:F1} m");
            Check(maxTilt < 35f, "panic brake while steering is controllable");
            Destroy(r);

            // Kerb strike: heading ~30 deg into the south kerb (z = -84.5, 0.15 m high) at 40 km/h.
            r = Spawn(new Vector3(-40f, 0.35f, -81.5f), 120f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (40f / 3.6f);
            maxTilt = 0;
            Step(r, default, 3f, _ => maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle));
            Line($"  40 km/h kerb strike at 30 deg: max tilt {maxTilt:F1} deg, final position {Fmt(r.Pos)}");
            Check(maxTilt < 30f, "kerb strike does not flip the car");
            Destroy(r);
        }

        private static void TopSpeed()
        {
            Section("7b. Top speed (flat test pad)");
            Rig r = Spawn(PadStart, 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (60f / 3.6f);
            float maxKmh = 0, maxTilt = 0, t100 = -1, t120 = -1;
            Step(r, _ => new VehicleInput { Throttle = 1f }, 70f, t =>
            {
                maxKmh = Mathf.Max(maxKmh, r.C.SpeedKmh); maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle);
                if (t100 < 0 && r.C.SpeedKmh >= 100) t100 = t; if (t120 < 0 && r.C.SpeedKmh >= 120) t120 = t;
            });
            Line($"  from 60 km/h at full throttle for 70 s: top speed {maxKmh:F0} km/h (limit {r.C.Stats.maxSpeedKmh:F0}), 100 km/h at {Fmt(t100)} s, 120 km/h at {Fmt(t120)} s, gear {r.C.GearLabel}, max tilt {maxTilt:F1} deg");
            Check(maxKmh > r.C.Stats.maxSpeedKmh * 0.9f && maxKmh < r.C.Stats.maxSpeedKmh * 1.05f, "top speed approaches but does not exceed the configured limit");
            Destroy(r);
        }

        private static void Suspension()
        {
            Section("7. Suspension (0.8 m drop)");
            Rig r = Spawn(new Vector3(-30f, 0.8f, LaneZ), 90f);
            var samples = new List<float>();
            Step(r, default, 3f, _ => samples.Add(r.Pos.y));
            float rest = samples[samples.Count - 1], minY = float.MaxValue; int bounces = 0; float prev = 0; bool wasDown = false; float settleT = -1;
            for (int i = 1; i < samples.Count; i++)
            {
                float vy = samples[i] - samples[i - 1];
                minY = Mathf.Min(minY, samples[i]);
                if (wasDown && vy > 0.0005f && prev < 0) bounces++;
                wasDown = vy < 0; prev = vy;
                if (settleT < 0 && i > 5 && Mathf.Abs(samples[i] - rest) < 0.01f && Mathf.Abs(vy) < 0.0006f) settleT = i * Dt;
            }
            Line($"  rest height {rest:F3} m, lowest {minY:F3} m (compression {(rest - minY) * 100f:F1} cm), bounces after landing {bounces}, settled by {Fmt(settleT)} s");
            Check(rest - minY < 0.25f, "suspension does not bottom out on a 0.8 m drop");
            Check(settleT > 0 && settleT < 2.5f, "suspension settles within 2.5 s");
            Check(bounces <= 3, "suspension is reasonably damped");
            Destroy(r);
        }

        private static void Collision()
        {
            Section("8. Collision with a solid wall at 50 km/h");
            var wall = new GameObject("TestWall");
            wall.transform.position = new Vector3(-36f, 2f, LaneZ);
            wall.transform.localScale = new Vector3(1f, 4f, 8f);
            wall.AddComponent<BoxCollider>();
            wall.layer = LayerMask.NameToLayer("Environment");
            Physics.SyncTransforms();

            Rig r = Spawn(new Vector3(-70f, 0.35f, LaneZ), 90f);
            Step(r, default, 0.5f);
            r.Rb.linearVelocity = r.Go.transform.forward * (50f / 3.6f);
            float maxTilt = 0, minCondition = 1, maxX = -999;
            bool damaged = false;
            r.C.Damage.ConditionChanged += _ => damaged = true;
            Step(r, _ => HoldSpeed(r, 50f, 0f), 4f, _ => { maxTilt = Mathf.Max(maxTilt, r.C.TiltAngle); minCondition = Mathf.Min(minCondition, r.C.Condition01); maxX = Mathf.Max(maxX, r.Pos.x); });
            Line($"  wall face at x=-35.5. Car furthest point x={maxX:F2} (front is ~2.25 m ahead of pivot), max tilt {maxTilt:F1} deg, speed afterwards {r.C.SpeedKmh:F1} km/h");
            Line($"  collision callback fired in edit-mode physics: {damaged}, condition {minCondition * 100f:F0}%");
            Check(maxX + 2.25f < -35.2f, "car cannot pass through the wall (no tunnelling)");
            Check(maxTilt < 35f, "frontal crash does not flip the car");

            // The damage model itself (callbacks may not be delivered by edit-mode Simulate).
            var damage = r.Go.GetComponent<VehicleDamage>();
            damage.Repair();
            damage.ApplyImpact(14f);
            Line($"  damage model: 14 m/s impact on a repaired car -> condition {damage.Condition01 * 100f:F0}%, performance factor {damage.PerformanceFactor:F2}");
            Check(damage.Condition01 < 0.9f && damage.Condition01 > 0.5f, "damage model reduces condition proportionally");
            Destroy(r);
            UnityEngine.Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
        }

        private static void FlipRecovery()
        {
            Section("9. Flip recovery");
            Rig r = Spawn(PadStart, 90f);                       // on the open test pad (the city road colliders are not always loaded in the editor)
            Step(r, default, 0.5f);
            r.Rb.rotation = Quaternion.Euler(0f, 90f, 180f);
            r.Rb.position += Vector3.up * 0.6f;
            r.Go.transform.SetPositionAndRotation(r.Rb.position, r.Rb.rotation);
            Physics.SyncTransforms();
            float tilt0 = Vector3.Angle(r.Go.transform.up, Vector3.up);
            float tRecover = -1;
            float nextTrace = 0.5f;
            Step(r, default, 8f, t =>
            {
                if (tRecover < 0 && r.C.TiltAngle < 15f) tRecover = t;
                if (t >= nextTrace) { nextTrace += 1f; Line($"    t={t:F1}s tilt={r.C.TiltAngle:F0} y={r.Pos.y:F2} wheels={r.C.GroundedWheels} speed={r.C.SpeedKmh:F1}"); }
            });
            Line($"  started upside down (tilt {tilt0:F0} deg) -> upright (<15 deg) after {Fmt(tRecover)} s, final tilt {r.C.TiltAngle:F1} deg, wheels on ground {r.C.GroundedWheels}");
            Check(tRecover > 0, "an upside-down car is put back on its wheels automatically");
            Destroy(r);
        }

        private static void WheelAlignment()
        {
            Section("10. Wheel visual alignment");
            Rig r = Spawn(new Vector3(-60f, 0.35f, LaneZ), 90f);
            r.Rb.linearVelocity = r.Go.transform.forward * (30f / 3.6f);
            Step(r, _ => HoldSpeed(r, 30f, 0.6f), 1.5f);
            var sync = r.Go.GetComponent<VehicleWheelVisuals>();
            sync.UpdateVisuals();

            var so = new SerializedObject(sync);
            var cols = so.FindProperty("colliders"); var vis = so.FindProperty("visuals");
            float worstPos = 0, worstTyreGap = 0;
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var wc = (WheelCollider)cols.GetArrayElementAtIndex(i).objectReferenceValue;
                var v = (Transform)vis.GetArrayElementAtIndex(i).objectReferenceValue;
                wc.GetWorldPose(out Vector3 p, out Quaternion q);
                worstPos = Mathf.Max(worstPos, Vector3.Distance(p, v.position));
                float gap = (v.position.y - wc.radius) - 0f; // tyre bottom height above the road surface (y=0)
                worstTyreGap = Mathf.Max(worstTyreGap, Mathf.Abs(gap));
                Vector3 local = r.Go.transform.InverseTransformPoint(v.position);
                Line($"  {names[i]}: local pos ({local.x:F2}, {local.y:F2}, {local.z:F2}), tyre bottom {gap * 100f:F1} cm above road, visual-vs-collider error {Vector3.Distance(p, v.position) * 1000f:F1} mm");
            }

            Vector3 fl = r.Go.transform.InverseTransformPoint(((Transform)vis.GetArrayElementAtIndex(0).objectReferenceValue).position);
            Vector3 fr = r.Go.transform.InverseTransformPoint(((Transform)vis.GetArrayElementAtIndex(1).objectReferenceValue).position);
            float steerVisual = Vector3.SignedAngle(r.Go.transform.forward, Vector3.ProjectOnPlane(((Transform)vis.GetArrayElementAtIndex(0).objectReferenceValue).forward, Vector3.up), Vector3.up);
            Line($"  controller steer angle {r.C.SteerAngle:F1} deg, front-left wheel visual yaw relative to body {steerVisual:F1} deg");
            Check(worstPos < 0.001f, "wheel visuals sit exactly on their WheelColliders");
            Check(Mathf.Abs(fl.x + fr.x) < 0.02f, "left/right wheels are symmetric");
            Check(Mathf.Abs(steerVisual - r.C.SteerAngle) < 3f, "front wheel visuals turn with the steering angle");
            Check(worstTyreGap < 0.06f, "tyres touch the road (no floating wheels)");
            Destroy(r);
        }

        private static void CameraFollow()
        {
            Section("11. Follow camera");
            var follow = UnityEngine.Object.FindAnyObjectByType<VehicleCameraController>();
            if (follow == null) { Line("  no VehicleCameraController in scene"); failures++; return; }
            var cam = follow.GetComponent<Camera>();
            Vector3 savedPos = cam.transform.position; Quaternion savedRot = cam.transform.rotation; float savedFov = cam.fieldOfView;
            int mask = LayerMask.GetMask("Road", "Environment");

            // Drive: accelerate, then a left-hand sweep, then brake. Camera is stepped with the car.
            Rig r = Spawn(PadStart, 90f);
            follow.SetTarget(r.Go.transform, r.Rb);
            string peakInfo = ""; float accSq = 0; int accN = 0; float minDist = 999, maxDist = 0, maxCamAccel = 0, maxAngleToCar = 0, minFov = 999, maxFov = 0; int clipped = 0;
            Vector3 prev = cam.transform.position - r.Pos, prevVel = Vector3.zero; bool first = true;
            Step(r, t => t < 4f ? new VehicleInput { Throttle = 1f } : t < 7f ? new VehicleInput { Throttle = 0.5f, Steer = 0.5f } : new VehicleInput { Brake = 1f }, 10f, t =>
            {
                follow.Tick(Dt);
                float d = Vector3.Distance(cam.transform.position, r.Pos);
                minDist = Mathf.Min(minDist, d); maxDist = Mathf.Max(maxDist, d);
                Vector3 rel = cam.transform.position - r.Pos;                    // camera relative to the car
                Vector3 vel = (rel - prev) / Dt;
                if (!first && t > 0.5f) { float acc = (vel - prevVel).magnitude / Dt; if (acc > maxCamAccel) { peakInfo = $"t={t:F2}s speed={r.C.SpeedKmh:F0}km/h steer={r.C.SteerAngle:F1} rel=({rel.x:F2},{rel.y:F2},{rel.z:F2}) wheels={r.C.GroundedWheels}"; } maxCamAccel = Mathf.Max(maxCamAccel, acc); accSq += acc * acc; accN++; }
                prevVel = vel; prev = rel; first = false;
                maxAngleToCar = Mathf.Max(maxAngleToCar, Vector3.Angle(cam.transform.forward, r.Pos + Vector3.up - cam.transform.position));
                minFov = Mathf.Min(minFov, cam.fieldOfView); maxFov = Mathf.Max(maxFov, cam.fieldOfView);
                if (Physics.CheckSphere(cam.transform.position, 0.25f, mask, QueryTriggerInteraction.Ignore)) clipped++;
            });
            Line($"  camera distance to car {minDist:F1}-{maxDist:F1} m | camera-vs-car acceleration: peak {maxCamAccel:F1}, rms {Mathf.Sqrt(accSq / Mathf.Max(1, accN)):F2} m/s2 | max angle car off view-centre {maxAngleToCar:F0} deg | FOV {minFov:F1}-{maxFov:F1} | frames inside geometry {clipped}");
            Line($"  camera peak at {peakInfo}");
            Check(maxDist < 10f && minDist > 3f, "camera keeps a sensible distance while driving");
            Check(maxAngleToCar < 35f, "car stays in frame");
            Check(clipped == 0, "camera never ends up inside geometry while driving");
            Check(maxCamAccel < 25f, "camera motion relative to the car is smooth (no shake spikes)");
            Destroy(r);

            // Obstacle avoidance: car nose-in against the garage's front wall, so the 'behind the car' point is inside the building.
            r = Spawn(new Vector3(40f, 0.35f, 49f), 180f);
            Step(r, default, 0.5f);
            follow.SetTarget(r.Go.transform, r.Rb);
            for (int i = 0; i < 60; i++) follow.Tick(Dt);
            bool inside = Physics.CheckSphere(cam.transform.position, 0.25f, mask, QueryTriggerInteraction.Ignore);
            Line($"  wall behind the car: camera {Vector3.Distance(cam.transform.position, r.Pos):F1} m from car (normal 6.5-7 m), inside geometry: {inside}");
            Check(!inside, "camera is pulled in front of a wall instead of clipping through it");

            follow.ResetCamera();
            follow.Tick(Dt);
            Vector3 behind = -r.Go.transform.forward;
            float resetDot = Vector3.Dot((cam.transform.position - r.Pos).normalized, new Vector3(behind.x, 0, behind.z).normalized);
            Line($"  reset camera: placed behind car, alignment dot={resetDot:F2}");
            Check(resetDot > 0.8f, "camera reset puts the camera behind the car");

            follow.SetTarget(null, null);
            cam.transform.SetPositionAndRotation(savedPos, savedRot); cam.fieldOfView = savedFov;
            Destroy(r);
        }

        // ==================================================================================== helpers

        private static Rig Spawn(Vector3 position, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var c = go.GetComponent<VehicleController>();
            c.ApplyStats(); // Awake does not run in Edit Mode
            Physics.SyncTransforms();
            spawned.Add(go);
            return new Rig { Go = go, C = c };
        }

        private static void CleanupSpawned()
        {
            foreach (var g in spawned) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            spawned.Clear();
            Physics.SyncTransforms();
        }

        private static void Destroy(Rig r)
        {
            spawned.Remove(r.Go);
            UnityEngine.Object.DestroyImmediate(r.Go);
            Physics.SyncTransforms();
        }

        private static void Step(Rig r, VehicleInput input, float seconds) => Step(r, _ => input, seconds);

        private static void Step(Rig r, VehicleInput input, float seconds, Action<float> after) => Step(r, _ => input, seconds, after);

        private static void Step(Rig r, Func<float, VehicleInput> inputAt, float seconds, Action<float> after = null, Func<bool> stopWhen = null)
        {
            int n = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < n; i++)
            {
                float t = i * Dt;
                r.C.Tick(Dt, inputAt(t));
                Physics.Simulate(Dt);
                after?.Invoke(t + Dt);
                if (stopWhen != null && stopWhen()) break;
            }
        }

        private static VehicleInput HoldSpeed(Rig r, float targetKmh, float steer)
        {
            float kmh = r.C.SpeedKmh;
            return new VehicleInput { Steer = steer, Throttle = kmh < targetKmh ? 1f : 0f, Brake = kmh > targetKmh + 6f ? 0.3f : 0f };
        }

        private static Transform FindStart()
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<LocationMarker>())
                if (m.Kind == LocationKind.PlayerStart) return m.transform;
            throw new InvalidOperationException("No PlayerStart marker in the scene.");
        }

        private static Quaternion LookAt(Vector3 from, Vector3 to) => Quaternion.LookRotation(to - from, Vector3.up);

        private static void ShotWithHud(string dir, string name, VehicleCameraController follow, Camera sceneCam, VehicleHUD hud)
        {
            var canvas = hud.GetComponent<Canvas>();
            var temp = new GameObject("HudCam") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            temp.transform.SetPositionAndRotation(sceneCam.transform.position, sceneCam.transform.rotation);
            temp.fieldOfView = sceneCam.fieldOfView; temp.farClipPlane = 900f;
            var rt = new RenderTexture(1280, 720, 24);
            temp.targetTexture = rt;
            var oldMode = canvas.renderMode; var oldCam = canvas.worldCamera; var oldPlane = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = temp; canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            temp.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, $"shot_{name}.png"), tex.EncodeToPNG());
            canvas.renderMode = oldMode; canvas.worldCamera = oldCam; canvas.planeDistance = oldPlane;
            temp.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(temp.gameObject);
        }

        private static void PadOn()
        {
            PadOff();
            pad = new GameObject("TestPad");
            pad.transform.position = new Vector3(3000f, -0.5f, 0f);
            pad.transform.localScale = new Vector3(4000f, 1f, 400f);
            pad.AddComponent<BoxCollider>();
            pad.layer = LayerMask.NameToLayer("Road");
            Physics.SyncTransforms();
        }

        private static void PadOff()
        {
            if (pad != null) UnityEngine.Object.DestroyImmediate(pad);
            pad = null;
            Physics.SyncTransforms();
        }

        private static void Section(string title) => Line("\n" + title);
        private static void Line(string text) => log.AppendLine(text);
        private static string Fmt(float v) => v < 0 ? "n/a" : v.ToString("F2");
        private static string Fmt(Vector3 v) => $"({v.x:F1}, {v.y:F2}, {v.z:F1})";

        private static void Check(bool ok, string description)
        {
            if (!ok) failures++;
            Line($"  [{(ok ? "PASS" : "FAIL")}] {description}");
        }
    }
}
