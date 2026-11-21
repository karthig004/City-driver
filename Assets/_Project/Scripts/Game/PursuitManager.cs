using System;
using System.Collections.Generic;
using CityDriver.Navigation;
using CityDriver.Population;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Game
{
    public enum PursuerKind { Gang, Police }

    /// <summary>What chases the player at each career level (matches the brief's difficulty table).</summary>
    public sealed class PursuitPlan
    {
        public int gang, police, roadblocks;
        public float speedScale = 1f;
        public bool interceptors;
        public string Describe() => $"{gang} gang car(s), {police} police car(s), {roadblocks} roadblock(s), speed x{speedScale:F2}{(interceptors ? ", interceptors" : "")}";

        public static PursuitPlan ForLevel(int level)
        {
            switch (level)
            {
                case 4: return new PursuitPlan { gang = 1, speedScale = 0.9f };                                       // one gang vehicle follows
                case 5: return new PursuitPlan { gang = 1, police = 1, speedScale = 1.0f };                           // police pursuit introduced
                case 6: return new PursuitPlan { gang = 3, police = 1, speedScale = 1.0f };                           // gang vehicles attack together
                case 7: return new PursuitPlan { gang = 2, police = 2, speedScale = 1.05f };                          // police and gang
                case 8: return new PursuitPlan { gang = 2, police = 2, roadblocks = 1, speedScale = 1.1f };           // roadblocks, multiple routes
                case 9: return new PursuitPlan { gang = 3, police = 3, roadblocks = 1, speedScale = 1.15f, interceptors = true };
                case 10: return new PursuitPlan { gang = 4, police = 3, roadblocks = 2, speedScale = 1.2f, interceptors = true };
                default: return new PursuitPlan { gang = level >= 3 ? 1 : 0, speedScale = 0.85f };
            }
        }
    }

    public sealed class WeaponInfo { public string id, name, blurb; public long price; }

    /// <summary>
    /// Police and gang pursuit during Shadow Run missions. Pursuers are kinematic cars that route to the player over the real road
    /// graph (A*), always spawn BEHIND the player and out of view, ram (gang) or box the player in (police), and can be shaken off by
    /// distance. Heat (0-5) follows the number of active pursuers; roadblocks are placed ahead on the player's route. The weapon shop's
    /// smoke launcher and EMP pulse act on nearby pursuers.
    /// </summary>
    public sealed class PursuitManager : MonoBehaviour
    {
        public static readonly WeaponInfo[] Weapons =
        {
            new WeaponInfo { id = "smoke", name = "Smoke Launcher", price = 6000, blurb = "Blinds pursuers within 45 m: they slow to 40% for 5 s." },
            new WeaponInfo { id = "emp", name = "EMP Pulse", price = 10000, blurb = "Stops the nearest pursuer within 40 m for 5 s." },
        };

        public const float EscapeDistance = 230f, EscapeSeconds = 8f, BustedSeconds = 3.5f, WeaponCooldownSeconds = 12f;

        private sealed class Pursuer
        {
            public Transform tf; public MeshFilter filter; public PursuerKind kind; public bool roadblock;
            public float speed, baseSpeed, repath, farTime, contact, ramCooldown, stunUntil, slowUntil, flashClock;
            public readonly Route route = new Route(); public int routeIndex; public AudioSource siren;
        }

        public static PursuitManager Instance { get; private set; }

        private readonly List<Pursuer> active = new List<Pursuer>();
        private readonly List<Transform> pool = new List<Transform>();
        private PursuitPlan plan;
        private bool running;
        private float spawnTimer, bustedTime, escapedHold, weaponReadyAt, startedAt;
        private int spawnedGang, spawnedPolice, spawnedBlocks;
        private VehicleController player;
        private Camera cam;

        public bool Running => running;
        public int Heat { get; private set; }
        public int ActivePursuers => active.Count;
        public int ActivePolice { get { int n = 0; foreach (var p in active) if (p.kind == PursuerKind.Police) n++; return n; } }
        public int ActiveGang { get { int n = 0; foreach (var p in active) if (p.kind == PursuerKind.Gang && !p.roadblock) n++; return n; } }
        public int ActiveRoadblockCars { get { int n = 0; foreach (var p in active) if (p.roadblock) n++; return n; } }
        public bool Busted { get; private set; }
        public bool Escaped { get; private set; }
        public bool Engaged { get; private set; }                         // a pursuit actually started during this mission
        public int EscapedCount { get; private set; }
        public float BustedProgress01 => Mathf.Clamp01(bustedTime / BustedSeconds);
        public float WeaponCooldown01 => Mathf.Clamp01((weaponReadyAt - Time.time) / WeaponCooldownSeconds);
        public PursuitPlan Plan => plan;
        public float MinSpawnDistance { get; private set; } = float.MaxValue;
        public bool AnySpawnedInFront { get; private set; }
        public event Action BustedEvent, EscapedEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<PursuitManager>() != null) return;
            new GameObject("PursuitManager").AddComponent<PursuitManager>();
        }

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------------------------------------------------------------------------------------- control
        public void Begin(PursuitPlan newPlan)
        {
            End();
            plan = newPlan; running = true; Busted = false; Escaped = false; Engaged = false; EscapedCount = 0;
            spawnedGang = spawnedPolice = spawnedBlocks = 0; bustedTime = 0f; spawnTimer = 4f; startedAt = Time.time;
            MinSpawnDistance = float.MaxValue; AnySpawnedInFront = false; escapedHold = 0f;
        }

        public void End()
        {
            running = false;
            foreach (var p in active) { if (p.tf != null) p.tf.gameObject.SetActive(false); if (p.siren != null) p.siren.Stop(); }
            active.Clear(); Heat = 0; bustedTime = 0f;
        }

        // ---------------------------------------------------------------------------------------------- weapons
        public bool UseWeapon(string weaponId)
        {
            if (!running || Time.time < weaponReadyAt || active.Count == 0 || player == null) return false;
            weaponReadyAt = Time.time + WeaponCooldownSeconds;
            if (weaponId == "smoke")
            {
                foreach (var p in active) if (!p.roadblock && Vector3.Distance(p.tf.position, player.transform.position) < 45f) p.slowUntil = Time.time + 5f;
                SpawnSmoke(player.transform.position - player.transform.forward * 3f);
                return true;
            }
            if (weaponId == "emp")
            {
                Pursuer best = null; float bd = 40f;
                foreach (var p in active) { if (p.roadblock) continue; float d = Vector3.Distance(p.tf.position, player.transform.position); if (d < bd) { bd = d; best = p; } }
                if (best == null) { weaponReadyAt = Time.time; return false; }
                best.stunUntil = Time.time + 5f;
                return true;
            }
            return false;
        }

        private void SpawnSmoke(Vector3 at)
        {
            var go = new GameObject("SmokePuff"); go.transform.position = at + Vector3.up * 0.8f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);          // a new system auto-plays; its duration can only be changed while stopped
            var main = ps.main; main.duration = 1f; main.loop = false; main.startLifetime = 4.5f; main.startSpeed = 2f; main.startSize = 4f; main.startColor = new Color(0.8f, 0.8f, 0.82f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            Material m = Resources.Load<Material>("RainParticle"); if (m == null) { Shader sh = Shader.Find("Sprites/Default"); if (sh != null) m = new Material(sh); }
            if (m != null) r.sharedMaterial = m;
            ps.Play(); Destroy(go, 6f);
        }

        // ---------------------------------------------------------------------------------------------- update
        private void Update()
        {
            if (!running) return;
            if (player == null) player = FindFirstObjectByType<VehicleController>();
            if (cam == null) cam = Camera.main;
            if (player == null || NavigationService.Instance == null || NavigationService.Instance.Graph == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            SpawnLogic(dt);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Pursuer p = active[i];
                if (p.roadblock) TickRoadblock(p, i); else TickPursuer(p, dt, i);
            }

            Heat = Mathf.Clamp(ActiveGang + ActivePolice + (ActiveRoadblockCars > 0 ? 1 : 0), 0, 5);

            // police box-in: a police car close while the player is almost stopped
            bool boxed = false;
            foreach (var p in active)
            {
                if (p.kind != PursuerKind.Police) continue;
                Vector3 d = p.tf.position - player.transform.position; d.y = 0f;
                if (d.magnitude < 7.5f && player.SpeedKmh < 20f) boxed = true;
            }
            bustedTime = boxed ? bustedTime + dt : Mathf.Max(0f, bustedTime - dt * 1.5f);
            if (bustedTime >= BustedSeconds && !Busted) { Busted = true; BustedEvent?.Invoke(); }

            // escape: every pursuer gone for a few seconds after the chase had begun
            if (Engaged && active.Count == 0 && spawnedGang + spawnedPolice >= plan.gang + plan.police)
            {
                escapedHold += dt;
                if (escapedHold > 4f && !Escaped) { Escaped = true; EscapedEvent?.Invoke(); }
            }
            else escapedHold = 0f;
        }

        // ---------------------------------------------------------------------------------------------- spawning
        private void SpawnLogic(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = 4f;

            if (spawnedGang < plan.gang) { if (SpawnChaser(PursuerKind.Gang)) spawnedGang++; return; }
            if (spawnedPolice < plan.police) { if (SpawnChaser(PursuerKind.Police)) spawnedPolice++; return; }
            if (spawnedBlocks < plan.roadblocks) { if (SpawnRoadblock()) spawnedBlocks++; }
        }

        /// <summary>Sets a pursuer's model: 0 = gang SUV, 1 = police car (red lit), 2 = police car (blue lit). Uses the baked realistic models when present.</summary>
        private static void SetLook(Pursuer p, int type)
        {
            var glow = p.filter != null ? p.filter.GetComponent<PursuerGlow>() : null; if (glow != null) glow.SetLook(type);
            var set = CityDriver.Population.TrafficCarSet.Get(); var mr = p.filter.GetComponent<MeshRenderer>();
            if (set != null && set.police != null && set.gang != null)
            {
                if (type == 0) { if (p.filter.sharedMesh != set.gang) { p.filter.sharedMesh = set.gang; mr.sharedMaterials = set.GangMaterials(); } }
                else { if (p.filter.sharedMesh != set.police) p.filter.sharedMesh = set.police; mr.sharedMaterials = set.PoliceMaterials(type == 1); }
            }
            else { p.filter.sharedMesh = PopulationMeshes.Pursuit(type); mr.sharedMaterial = PopulationMeshes.Material; }
        }

        private Pursuer NewPursuer(PursuerKind kind)
        {
            Transform t = null;
            foreach (var tr in pool) if (!tr.gameObject.activeSelf) { t = tr; break; }
            if (t == null)
            {
                var go = new GameObject("Pursuer");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = PopulationMeshes.Material;
                var box = go.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0.75f, 0); box.size = new Vector3(1.9f, 1.4f, 4.3f);
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                go.AddComponent<PursuerGlow>();
                var src = go.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.loop = true; src.maxDistance = 140f; src.rolloffMode = AudioRolloffMode.Linear; src.playOnAwake = false;
                t = go.transform; pool.Add(t);
            }
            t.gameObject.SetActive(true);
            var p = new Pursuer { tf = t, filter = t.GetComponent<MeshFilter>(), kind = kind, siren = t.GetComponent<AudioSource>() };
            SetLook(p, kind == PursuerKind.Gang ? 0 : 1);
            p.baseSpeed = (kind == PursuerKind.Gang ? 21f : 23f) * plan.speedScale;
            return p;
        }

        // Pick a road point 140-260 m away, BEHIND the player's direction of travel and outside the camera view.
        private bool FindBehindPoint(out Vector3 pos, out Vector3 dir)
        {
            pos = Vector3.zero; dir = Vector3.forward;
            var graph = NavigationService.Instance.Graph; var data = NavigationService.Instance.Data;
            Vector3 me = player.transform.position; Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int e = UnityEngine.Random.Range(0, data.edges.Count);
                RoadEdge ed = data.edges[e];
                Vector3 a = data.nodes[ed.from].position, b = data.nodes[ed.to].position;
                Vector3 point = Vector3.Lerp(a, b, UnityEngine.Random.Range(0.3f, 0.7f)); point.y = me.y;
                Vector3 to = point - me; to.y = 0f; float dist = to.magnitude;
                if (dist < 140f || dist > 260f) continue;
                if (Vector3.Dot(fwd, to / dist) > -0.1f) continue;                                  // must be behind (or beside), never ahead
                if (cam != null) { Vector3 vp = cam.WorldToViewportPoint(point + Vector3.up); if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f) continue; }
                pos = point; dir = (b - a).normalized; return true;
            }
            return false;
        }

        private bool SpawnChaser(PursuerKind kind)
        {
            if (!FindBehindPoint(out Vector3 pos, out Vector3 dir)) return false;
            Pursuer p = NewPursuer(kind);
            if (Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 14f, LayerMask.GetMask("Road"))) pos.y = hit.point.y;
            p.tf.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            p.speed = 0f;
            active.Add(p); Engaged = true;
            float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(player.transform.position.x, player.transform.position.z));
            MinSpawnDistance = Mathf.Min(MinSpawnDistance, dist);
            Vector3 f = player.transform.forward; f.y = 0f; Vector3 to = pos - player.transform.position; to.y = 0f;
            if (Vector3.Dot(f.normalized, to.normalized) > 0.1f) AnySpawnedInFront = true;
            if (kind == PursuerKind.Police && GameAudio.Instance != null && GameAudio.Instance.SirenClip != null) { p.siren.clip = GameAudio.Instance.SirenClip; p.siren.volume = 0.55f * GameSettings.Sfx * GameSettings.Master; p.siren.Play(); }
            return true;
        }

        private bool SpawnRoadblock()
        {
            var nav = NavigationService.Instance;
            if (!nav.HasDestination || nav.Route.points.Count < 2) return false;
            Route r = nav.Route;
            // a point 200-260 m of route length ahead of the car
            float myDist = 0f; float best = float.MaxValue;
            for (int i = 0; i + 1 < r.points.Count; i++)                                    // distance to the route LINE (points are sparse junctions)
            {
                Vector3 a0 = r.points[i], b0 = r.points[i + 1]; Vector3 ab = b0 - a0; ab.y = 0f;
                float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
                Vector3 ap = player.transform.position - a0; ap.y = 0f;
                float tt = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
                float d = (ap - ab * tt).magnitude;
                if (d < best) { best = d; myDist = r.cumulative[i] + tt * Mathf.Sqrt(len2); }
            }
            if (best > 25f) return false;                                                // the route is stale (player just teleported / left it): wait for a fresh one
            float ahead = Mathf.Min(220f, (r.Length - myDist) * 0.6f);                     // shorter routes get the block closer, never beyond the destination
            if (ahead < 60f) return false;
            float want = myDist + ahead;
            int seg = 0; while (seg < r.cumulative.Count - 2 && r.cumulative[seg + 1] < want) seg++;                  // the route segment that contains 'want'
            float segLen = Mathf.Max(0.01f, r.cumulative[seg + 1] - r.cumulative[seg]);
            Vector3 pos = Vector3.Lerp(r.points[seg], r.points[seg + 1], Mathf.Clamp01((want - r.cumulative[seg]) / segLen));
            Vector3 dir = r.points[seg + 1] - r.points[seg]; dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return false; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int k = -1; k <= 1; k += 2)
            {
                Pursuer p = NewPursuer(PursuerKind.Police); p.roadblock = true; p.speed = 0f;
                SetLook(p, k < 0 ? 1 : 2);
                Vector3 pp = pos + side * (3.1f * k);
                if (Physics.Raycast(pp + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 14f, LayerMask.GetMask("Road"))) pp.y = hit.point.y;
                p.tf.SetPositionAndRotation(pp, Quaternion.LookRotation(side) * Quaternion.Euler(0f, k < 0 ? 12f : -12f, 0f));      // parked across the road, angled in
                if (GameAudio.Instance != null && GameAudio.Instance.SirenClip != null) { p.siren.clip = GameAudio.Instance.SirenClip; p.siren.volume = 0.4f * GameSettings.Sfx * GameSettings.Master; p.siren.Play(); }
                active.Add(p);
            }
            Engaged = true;
            return true;
        }

        // ---------------------------------------------------------------------------------------------- behaviour
        private void TickRoadblock(Pursuer p, int index)
        {
            p.flashClock += Time.deltaTime;
            SetLook(p, ((int)(p.flashClock * 3f) % 2 == 0) ? 1 : 2);
            Vector3 d = p.tf.position - player.transform.position; d.y = 0f;
            Vector3 vel = player.Body != null ? player.Body.linearVelocity : Vector3.zero; vel.y = 0f;
            bool passed = d.magnitude > 35f && vel.magnitude > 3f && Vector3.Dot(vel, d) < 0f;     // the player has driven past it and is moving away
            if (passed || d.magnitude > 420f) { Retire(p, index, false); }
        }

        private void TickPursuer(Pursuer p, float dt, int index)
        {
            Vector3 me = player.transform.position, pos = p.tf.position;
            Vector3 toPlayer = me - pos; toPlayer.y = 0f; float dist = toPlayer.magnitude;

            // escape by distance
            if (dist > EscapeDistance) { p.farTime += dt; if (p.farTime > EscapeSeconds) { EscapedCount++; Retire(p, index, true); return; } } else p.farTime = 0f;

            if (p.kind == PursuerKind.Police) { p.flashClock += dt; SetLook(p, ((int)(p.flashClock * 4f) % 2 == 0) ? 1 : 2); }

            // route to the player over the road graph, refreshed a few times a second
            p.repath -= dt;
            if (p.repath <= 0f)
            {
                p.repath = 1.2f;
                if (NavigationService.Instance.Graph.TryBuildRoute(pos, me, p.route)) p.routeIndex = Mathf.Min(1, p.route.points.Count - 1);
            }

            Vector3 target = me;
            if (p.route.points.Count > 1)
            {
                while (p.routeIndex < p.route.points.Count - 1 && Vector3.Distance(pos, p.route.points[p.routeIndex]) < 9f) p.routeIndex++;
                target = p.route.points[p.routeIndex];
                if (dist < 22f) target = me;                                              // close in: aim straight at the player
            }

            Vector3 want = target - pos; want.y = 0f;
            float desired = p.baseSpeed;
            float playerSpeed = player.SpeedKmh / 3.6f;
            if (dist < 30f) desired = Mathf.Min(desired, playerSpeed + 3.5f);
            if (dist > 90f) desired = p.baseSpeed * 1.12f;
            Vector3 heading = p.tf.forward; heading.y = 0f;
            float turnAngle = want.sqrMagnitude > 0.01f ? Vector3.Angle(heading, want) : 0f;
            if (turnAngle > 50f) desired = Mathf.Min(desired, 9f); else if (turnAngle > 25f) desired = Mathf.Min(desired, 15f);
            if (Time.time < p.slowUntil) desired *= 0.4f;
            if (Time.time < p.stunUntil) desired = 0f;
            p.speed = p.speed < desired ? Mathf.Min(desired, p.speed + 6f * dt) : Mathf.Max(desired, p.speed - 11f * dt);

            if (want.sqrMagnitude > 0.01f)
            {
                Quaternion goal = Quaternion.LookRotation(want.normalized);
                p.tf.rotation = Quaternion.RotateTowards(p.tf.rotation, goal, 110f * dt);
            }
            Vector3 np = pos + p.tf.forward * (p.speed * dt);
            if (Physics.Raycast(np + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, LayerMask.GetMask("Road"))) np.y = Mathf.Lerp(pos.y, hit.point.y, 0.3f);
            p.tf.position = np;

            // gang cars ram the player: damage and a shove, at most every 1.6 s
            if (p.kind == PursuerKind.Gang && dist < 3.8f && Time.time >= p.ramCooldown && Time.time >= p.stunUntil)
            {
                p.ramCooldown = Time.time + 1.6f;
                var damage = player.GetComponent<VehicleDamage>();
                if (damage != null) damage.ApplyImpact(6f);
                if (player.Body != null) player.Body.AddForce((toPlayer.normalized + Vector3.up * 0.1f) * 3200f, ForceMode.Impulse);
            }
        }

        private void Retire(Pursuer p, int index, bool escaped)
        {
            if (p.siren != null) p.siren.Stop();
            p.tf.gameObject.SetActive(false);
            active.RemoveAt(index);
        }

        /// <summary>What the radar and city map need to draw one chaser.</summary>
        public struct ThreatInfo { public Vector3 position; public PursuerKind kind; public bool roadblock, disabled; }

        /// <summary>Fills <paramref name="into"/> with every active chaser (police, gang cars and roadblock cars) for the map views.</summary>
        public void GetThreats(List<ThreatInfo> into)
        {
            into.Clear();
            foreach (var p in active)
                if (p.tf != null && p.tf.gameObject.activeSelf)
                    into.Add(new ThreatInfo { position = p.tf.position, kind = p.kind, roadblock = p.roadblock, disabled = Time.time < p.stunUntil });
        }

        public Transform NearestPursuer()
        {
            Transform best = null; float bd = float.MaxValue;
            foreach (var p in active) { if (player == null) break; float d = Vector3.Distance(p.tf.position, player.transform.position); if (d < bd) { bd = d; best = p.tf; } }
            return best;
        }

        /// <summary>Test seam: places an active pursuer at the given distance behind the player so tests can check capture/escape logic quickly.</summary>
        public void DebugTeleportPursuers(float distanceBehind, float stunSeconds = 0f)
        {
            if (player == null) return;
            Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
            foreach (var p in active) if (!p.roadblock) { p.tf.position = player.transform.position - fwd * distanceBehind; p.tf.rotation = Quaternion.LookRotation(fwd); p.speed = 0f; if (stunSeconds > 0f) p.stunUntil = Time.time + stunSeconds; }
        }
    }
}
