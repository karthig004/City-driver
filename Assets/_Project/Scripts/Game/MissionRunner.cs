using System;
using System.IO;
using CityDriver.Career;
using CityDriver.Missions;
using CityDriver.Navigation;
using CityDriver.Population;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Game
{
    public enum MissionState { Idle, ToPickup, Boarding, ToDropoff, Exiting, Finished }

    /// <summary>What the result screen shows after a mission.</summary>
    public sealed class MissionResult
    {
        public MissionDefinition Mission;
        public bool Success;
        public string FailReason = "";
        public float TripLength; public long DistanceBonus; public long BaseReward, TimeBonus, SafeBonus, EscapeBonus, DamageDeduction, PenaltyApplied, WalletChange, XpGained;
        public bool Pursued, Busted;
        public float ElapsedSeconds, ConditionLost01;
        public bool Replay;
        public string LevelNote = "";
        public bool LeveledUp;
        public bool LevelDown; public int LevelFrom, LevelTo; public long XpLost;
    }

    /// <summary>
    /// Runs one passenger mission at a time: go to the pickup, stop to let the passenger board, drive to the drop-off, stop to let
    /// them out, then pay out through the career/wallet/XP services (which prevent duplicate rewards) and save. Timed missions
    /// fail when the clock runs out. Subtitles come from short passenger lines.
    /// </summary>
    public sealed class MissionRunner : MonoBehaviour
    {
        public const float ArriveRadius = 7f;
        public const float StopSpeedKmh = 6f;
        public const float BoardSeconds = 2.0f;

        public static MissionRunner Instance { get; private set; }

        public MissionState State { get; private set; } = MissionState.Idle;
        public MissionDefinition Active { get; private set; }
        public MissionResult LastResult { get; private set; }
        public float Elapsed { get; private set; }
        public float TimeRemaining => Active != null && timeLimit > 0 ? Mathf.Max(0f, timeLimit - Elapsed) : -1f;

        // ---- trips are planned on the real road network so they are LONG (not the old 150 m hops)
        public const string PickupId = "trip_pickup", DropoffId = "trip_dropoff";
        public Vector3 PickupPosition { get; private set; }
        public Vector3 DropoffPosition { get; private set; }
        public float TripLength { get; private set; }
        public string DestinationLabel { get; private set; } = "";
        public int TimeLimit => timeLimit;
        private int timeLimit;
        private GameObject tripPickupObj, tripDropoffObj;
        public string PassengerName { get; private set; } = "";
        public float DistanceToTarget { get; private set; }
        public event Action<MissionResult> Finished;
        public event Action<string, string> Subtitle;            // speaker, line
        public event Action<string> Toast;

        private VehicleController car;
        private Transform passenger;
        private float stoppedFor, startCondition, stateTime;
        private int attempt, dialogueIndex;
        private bool pursuitStarted, pursuitBusted;

        private static readonly string[] Names = { "Aarav", "Meera", "Karthik", "Divya", "Imran", "Lakshmi", "Rohan", "Priya" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
#if UNITY_EDITOR
            if (File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "disable_game_layer.txt"))) return;   // bisecting aid
#endif
            if (FindFirstObjectByType<MissionRunner>() != null) return;
            new GameObject("MissionRunner").AddComponent<MissionRunner>();
        }

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; if (passenger != null) Destroy(passenger.gameObject); }

        private VehicleController Car { get { if (car == null) car = FindFirstObjectByType<VehicleController>(); return car; } }

        public bool CanStart(MissionDefinition m) => State == MissionState.Idle || State == MissionState.Finished
            ? GameSession.Instance.Career.IsAvailable(m)
            : false;

        /// <summary>Begins a mission. Shadow Run missions switch the city to night + rain and start a pursuit once the passenger is aboard.</summary>
        public bool StartMission(MissionDefinition mission)
        {
            if (mission == null || !CanStart(mission) || Car == null) return false;
            if (!PlanTrip(mission)) { Toast?.Invoke("Could not plan a trip from here. Try again."); return false; }
            var pickup = LocationMarker.Find(LocationKind.Pickup, PickupId);
            var dropoff = LocationMarker.Find(LocationKind.Dropoff, DropoffId);
            if (pickup == null || dropoff == null) { Toast?.Invoke("This mission's locations are not in the city yet."); return false; }

            if (NavigationService.Instance != null) NavigationService.Instance.KeepRouteOnArrival = true;
            Active = mission; attempt++; Elapsed = 0f; stoppedFor = 0f; stateTime = 0f;
            startCondition = Car.Condition01;
            PassengerName = Names[Mathf.Abs((mission.Id ?? "").GetHashCode()) % Names.Length];
            LastResult = null;
            GameSession.Instance.Career.StartMission(mission);
            dialogueIndex = 0; pursuitStarted = false; pursuitBusted = false;
            if (!mission.ShadowRun) WorldEnvironment.Instance?.OnMissionStarted(mission.NightOnly);
            if (mission.ShadowRun) { WorldEnvironment.Instance?.BeginShadowMode(5f); Say("Dispatch", "Secret job. Pick up " + PassengerName + ". Keep your head down."); }
            SpawnPassenger(pickup.transform.position);
            Enter(MissionState.ToPickup);
            Toast?.Invoke($"{mission.Title}: pick up {PassengerName}");
            return true;
        }

        public void Abandon()
        {
            if (Active == null || State == MissionState.Finished || State == MissionState.Idle) return;
            Fail("Mission abandoned.");
        }

        private void Say(string speaker, string line)
        {
            Subtitle?.Invoke(speaker, line);
            VoiceService.Speak(speaker, line);
        }

        // The Level 3 story mission: the mysterious passenger talks during the ride and hands the driver the Shadow Pass.
        private static readonly (float t, string speaker, string line)[] MysteryLines =
        {
            (2f, "Mysterious Passenger", "Driver, take me somewhere safe. Don't ask questions."),
            (9f, "Mysterious Passenger", "They are watching this city. You drive well. Better than most."),
            (17f, "Mysterious Passenger", "Stop near the nightclub. I will leave you something."),
        };

        private bool IsMystery => Active != null && Active.Id.ToLowerInvariant().Contains("mysterious");

        private void Enter(MissionState next)
        {
            State = next; stateTime = 0f; stoppedFor = 0f;
            string target = next == MissionState.ToPickup ? PickupId : next == MissionState.ToDropoff ? DropoffId : null;
            if (MarkerDirector.Instance != null) { if (target != null) MarkerDirector.Instance.ShowOnly(target); else MarkerDirector.Instance.HideAll(); }
            var nav = NavigationService.Instance;
            if (nav != null) { if (target != null) nav.SetDestinationToLocation(target); else nav.ClearDestination(); }
        }

        private void Update()
        {
            if (Active == null || State == MissionState.Idle || State == MissionState.Finished) return;
            if (Car == null) return;
            float dt = Time.deltaTime;
            Elapsed += dt; stateTime += dt;

            // the GPS route can be lost (e.g. a failed re-route near buildings): put it back so the player never loses the track
            if ((State == MissionState.ToPickup || State == MissionState.ToDropoff) && NavigationService.Instance != null && !NavigationService.Instance.HasDestination && Time.frameCount % 20 == 0)
                NavigationService.Instance.SetDestinationToLocation(State == MissionState.ToPickup ? PickupId : DropoffId);
            if (timeLimit > 0 && Elapsed >= timeLimit && State != MissionState.Exiting) { Fail("Time ran out."); return; }

            if (IsMystery && State == MissionState.ToDropoff)
                while (dialogueIndex < MysteryLines.Length && stateTime >= MysteryLines[dialogueIndex].t) { Say(MysteryLines[dialogueIndex].speaker, MysteryLines[dialogueIndex].line); dialogueIndex++; }
            if (Active.ShadowRun && Car.Condition01 <= 0.02f && State != MissionState.Exiting) { Fail("The car was wrecked."); return; }

            switch (State)
            {
                case MissionState.ToPickup: TickTravel(LocationKind.Pickup, PickupId, MissionState.Boarding, "boarding"); break;
                case MissionState.Boarding: TickBoarding(dt); break;
                case MissionState.ToDropoff: TickTravel(LocationKind.Dropoff, DropoffId, MissionState.Exiting, "exiting"); break;
                case MissionState.Exiting: TickExiting(); break;
            }
        }

        private bool Near(LocationKind kind, string id, out float distance)
        {
            distance = float.MaxValue;
            var marker = LocationMarker.Find(kind, id);
            if (marker == null) return false;
            Vector3 d = marker.transform.position - Car.transform.position; d.y = 0f;
            distance = d.magnitude;
            return distance <= ArriveRadius;
        }

        private void TickTravel(LocationKind kind, string id, MissionState next, string _)
        {
            bool near = Near(kind, id, out float distance);
            DistanceToTarget = distance;
            if (near && Car.SpeedKmh <= StopSpeedKmh) stoppedFor += Time.deltaTime; else stoppedFor = 0f;
            if (stoppedFor < 0.4f) return;

            if (next == MissionState.Boarding)
            {
                if (!IsMystery) Say(PassengerName, "Thanks for stopping. I'm in a bit of a hurry.");
                Enter(MissionState.Boarding);
            }
            else
            {
                if (IsMystery) { Say("Mysterious Passenger", "Keep this card. When you see the shadow, answer."); Toast?.Invoke("You received the SHADOW PASS"); }
                else Say(PassengerName, Active.ShadowRun ? "You got me out alive. I won't forget it." : "This is perfect. Thank you, driver.");
                Enter(MissionState.Exiting);
                if (passenger != null) PlacePassengerBeside(Car.transform);
            }
        }

        private void TickBoarding(float dt)
        {
            if (Car.SpeedKmh > StopSpeedKmh * 2f) { Toast?.Invoke("Stay still while the passenger boards."); stateTime = 0f; return; }
            if (passenger != null)
            {
                float k = Mathf.Clamp01(stateTime / BoardSeconds);
                passenger.position = Vector3.Lerp(passenger.position, Car.transform.position + Car.transform.right * -1.0f, k * 0.25f);
            }
            if (stateTime >= BoardSeconds)
            {
                if (passenger != null) passenger.gameObject.SetActive(false);
                if (!IsMystery) Say(PassengerName, "Take me to " + DestinationLabel + ", please. It is a long way.");
                Enter(MissionState.ToDropoff);
                if (Active.ShadowRun) StartPursuit();
            }
        }

        private void TickExiting()
        {
            if (passenger != null) passenger.position += Car.transform.right * -1.4f * Time.deltaTime;
            if (stateTime >= 1.6f) Complete();
        }


        // ------------------------------------------------------------------------------------------- trip planning
        private static float TripBase(MissionDefinition m)
        {
            string id = (m.Id ?? "").ToLowerInvariant();
            float b = m.Level <= 1 ? 1300f : m.Level == 2 ? 1700f : m.Level == 3 ? 2100f : 2500f;
            if (id.Contains("long_journey")) b = 3400f;
            else if (id.Contains("time_challenge")) b = 1900f;
            if (m.ShadowRun) b = Mathf.Max(b, 2600f);
            return b * GameSettings.TripLengthScale;
        }

        private Vector3 KerbPoint(RoadGraphData data, int edge, float t)
        {
            RoadEdge e = data.edges[edge];
            Vector3 a = data.nodes[e.from].position, b = data.nodes[e.to].position;
            Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.Cross(dir, Vector3.up) * Mathf.Max(1.5f, e.width * 0.5f - 1.5f);
            if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 16f, LayerMask.GetMask("Road"))) p.y = hit.point.y; else p.y = 0f;
            return p;
        }

        private bool PlanTrip(MissionDefinition m)
        {
            var nav = NavigationService.Instance;
            if (nav == null || nav.Graph == null || nav.Data == null || Car == null) return false;
            var data = nav.Data; var graph = nav.Graph;
            Vector3 me = Car.transform.position;
            float minLen = TripBase(m);

            // pickup: a street 60-320 m from the car
            int pe = -1; float pt = 0.5f; Vector3 pickup = Vector3.zero;
            for (int i = 0; i < 120 && pe < 0; i++)
            {
                int e = UnityEngine.Random.Range(0, data.edges.Count);
                if (data.edges[e].roadClass == RoadClass.Highway || data.edges[e].length < 40f) continue;
                float t = UnityEngine.Random.Range(0.3f, 0.7f);
                Vector3 pos = KerbPoint(data, e, t);
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(me.x, me.z));
                if (d < 60f || d > 320f) continue;
                pe = e; pt = t; pickup = pos;
            }
            if (pe < 0) return false;

            // drop-off: scan streets and keep the route whose length is closest to 1.2x the target. If the city has no route that long from
            // this pickup, relax the target (75%, then 55%) rather than refusing the mission.
            var tmp = new Route(); int bestEdge = -1; float bestLen = 0f; Vector3 bestPos = Vector3.zero;
            foreach (float factor in new[] { 1f, 0.75f, 0.55f })
            {
                float need = Mathf.Max(minLen * factor, 700f), aim = need * 1.2f, bestScore = float.MaxValue;
                for (int i = 0; i < 350; i++)
                {
                    int e = UnityEngine.Random.Range(0, data.edges.Count);
                    if (e == pe || data.edges[e].roadClass == RoadClass.Highway || data.edges[e].length < 40f) continue;
                    float t = UnityEngine.Random.Range(0.3f, 0.7f);
                    Vector3 pos = KerbPoint(data, e, t);
                    if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(pickup.x, pickup.z)) < need * 0.4f) continue;
                    if (!graph.TryBuildRoute(pickup, pos, tmp)) continue;
                    float len = tmp.Length;
                    if (len < need * 0.8f) continue;
                    float score = Mathf.Abs(len - aim);
                    if (score < bestScore) { bestScore = score; bestEdge = e; bestLen = len; bestPos = pos; if (score < need * 0.1f) break; }
                }
                if (bestEdge >= 0) break;
            }
            if (bestEdge < 0) return false;

            PickupPosition = pickup; DropoffPosition = bestPos; TripLength = bestLen;
            DestinationLabel = Describe(data, bestPos);
            timeLimit = m.TimeLimitSeconds > 0 ? Mathf.RoundToInt(TripLength / 8.5f * 1.2f + 45f) : 0;
            ClearTripMarkers();
            tripPickupObj = SpawnMarker(LocationKind.Pickup, PickupId, pickup);
            tripDropoffObj = SpawnMarker(LocationKind.Dropoff, DropoffId, bestPos);
            if (tripPickupObj == null || tripDropoffObj == null) return false;
            if (MarkerDirector.Instance != null && Car != null) MarkerDirector.Instance.SetViewer(Car.transform);
            return true;
        }

        private GameObject SpawnMarker(LocationKind kind, string id, Vector3 pos)
        {
            LocationMarker template = null;
            foreach (var mk in LocationMarker.All) if (mk.Kind == kind && mk.LocationId != id) { template = mk; break; }
            if (template == null) return null;
            var go = Instantiate(template.gameObject, pos, Quaternion.identity);
            go.name = "Trip_" + id;
            go.GetComponent<LocationMarker>().Configure(kind, id);
            return go;
        }

        private void ClearTripMarkers()
        {
            if (tripPickupObj != null) Destroy(tripPickupObj);
            if (tripDropoffObj != null) Destroy(tripDropoffObj);
            tripPickupObj = tripDropoffObj = null;
        }

        private static string Describe(RoadGraphData data, Vector3 p)
        {
            float best = 220f; string name = null;
            foreach (var poi in data.pois) { float d = Vector2.Distance(new Vector2(poi.position.x, poi.position.z), new Vector2(p.x, p.z)); if (d < best) { best = d; name = poi.displayName; } }
            if (name != null) return name;
            foreach (var dist in data.districts) if (dist.Contains(p.x, p.z)) return dist.name;
            return "the destination";
        }

        // ------------------------------------------------------------------------------------------- pursuit
        private void StartPursuit()
        {
            var pm = PursuitManager.Instance;
            if (pm == null || pursuitStarted) return;
            pursuitStarted = true;
            pm.BustedEvent -= OnBusted; pm.BustedEvent += OnBusted;
            pm.Begin(PursuitPlan.ForLevel(Active.Level));
            Say("Dispatch", "They found us. Gang cars are coming. Get to the safe house!");
        }

        private void OnBusted()
        {
            if (Active == null || State == MissionState.Finished || State == MissionState.Idle) return;
            pursuitBusted = true;
            GameSession.Instance.Career.SetCaptured(true);
            Fail("Busted by the police.");
        }

        // ------------------------------------------------------------------------------------------- results
        private void Complete()
        {
            var session = GameSession.Instance;
            var m = Active;
            var r = new MissionResult { Mission = m, Success = true, ElapsedSeconds = Elapsed };
            r.Replay = session.Career.IsCompleted(m.Id);
            long walletBefore = session.Wallet.Balance, xpBefore = session.Xp.Xp;

            float lost = Mathf.Max(0f, startCondition - Car.Condition01);
            r.ConditionLost01 = lost;
            r.BaseReward = r.Replay ? (long)(m.Reward * 0.25) : m.Reward;

            if (timeLimit > 0 && Elapsed < timeLimit)
                r.TimeBonus = (long)(m.Reward * 0.25 * (1.0 - Elapsed / timeLimit));
            r.TripLength = TripLength;
            r.DistanceBonus = (long)(m.Reward * Mathf.Clamp(TripLength / 1000f - 0.6f, 0f, 4f));      // longer trips pay more
            if (lost < 0.03f) r.SafeBonus = (long)(m.Reward * 0.10);

            var pm = PursuitManager.Instance;
            r.Pursued = pm != null && pm.Engaged;
            if (m.ShadowRun && r.Pursued && !pursuitBusted) r.EscapeBonus = 2000;                         // successful escape bonus
            long bonus = r.TimeBonus + r.SafeBonus + r.EscapeBonus + r.DistanceBonus;
            session.Career.CompleteMission(m, bonus);

            if (lost >= 0.02f)
            {
                r.DamageDeduction = lost < 0.15f ? 300 : 1500;                     // minor / major collision repair cost
                session.Wallet.Apply($"mission:{m.Id}:attempt{attempt}:damage", -r.DamageDeduction, "Collision repair");
            }

            r.WalletChange = session.Wallet.Balance - walletBefore;
            r.XpGained = session.Xp.Xp - xpBefore;

            if (session.Career.TryAdvanceLevel(session.SelectedVehicle, out string reason)) { r.LeveledUp = true; r.LevelNote = $"Level {session.Career.Level} reached!"; }
            else if (reason != null) r.LevelNote = reason;

            session.Persist();
            Finish(r);
        }

        private void Fail(string reason)
        {
            var session = GameSession.Instance;
            var m = Active;
            var r = new MissionResult { Mission = m, Success = false, FailReason = reason, ElapsedSeconds = Elapsed, Busted = pursuitBusted, Pursued = PursuitManager.Instance != null && PursuitManager.Instance.Engaged };
            long before = session.Wallet.Balance;
            session.Career.FailMission(m, "attempt" + attempt);
            r.PenaltyApplied = before - session.Wallet.Balance;
            r.WalletChange = session.Wallet.Balance - before;
            if (m != null && m.ShadowRun && session.Career.DemoteAfterShadowFailure(out int fromLv, out int toLv, out long lost))
            {
                r.LevelDown = true; r.LevelFrom = fromLv; r.LevelTo = toLv; r.XpLost = lost;
                r.LevelNote = $"Level {fromLv} -> Level {toLv}. Replay the Level {toLv} assignments to climb back.";
            }
            session.Persist();
            Finish(r);
        }

        private void Finish(MissionResult r)
        {
            LastResult = r; State = MissionState.Finished;
            if (NavigationService.Instance != null) NavigationService.Instance.KeepRouteOnArrival = false;
            ClearTripMarkers();
            if (PursuitManager.Instance != null) { PursuitManager.Instance.BustedEvent -= OnBusted; PursuitManager.Instance.End(); }
            if (r.Mission != null && r.Mission.ShadowRun) WorldEnvironment.Instance?.EndShadowMode(); else WorldEnvironment.Instance?.OnMissionEnded();
            if (MarkerDirector.Instance != null) MarkerDirector.Instance.HideAll();
            if (NavigationService.Instance != null) NavigationService.Instance.ClearDestination();
            if (passenger != null) passenger.gameObject.SetActive(false);
            Active = r.Mission;
            Finished?.Invoke(r);
        }

        /// <summary>Back to idle after the result screen is dismissed.</summary>
        public void Dismiss() { if (State == MissionState.Finished) { State = MissionState.Idle; Active = null; } }

        // ------------------------------------------------------------------------------------------- passenger visual
        private static readonly string[] PassengerIds =
        {
            "office_commuter_1", "office_commuter_2", "office_commuter_3", "college_student_1", "college_student_2", "college_student_3", "tourist_1", "tourist_2", "tourist_3",
            "businessman_1", "businessman_2", "businessman_3", "hospital_visitor_1", "hospital_visitor_2", "hospital_visitor_3", "club_guest_1", "club_guest_2", "club_guest_3",
        };
        private string passengerPrefabId = "";

        private void SpawnPassenger(Vector3 near)
        {
            string id = IsMystery ? "mysterious_passenger" : PassengerIds[UnityEngine.Random.Range(0, PassengerIds.Length)];
            var prefab = Resources.Load<GameObject>("Characters/Char_" + id);
            if (prefab != null)
            {
                if (passenger != null && passengerPrefabId != id) { Destroy(passenger.gameObject); passenger = null; }
                if (passenger == null) { passenger = Instantiate(prefab).transform; passenger.name = "Passenger"; passengerPrefabId = id; }
                var anim = passenger.GetComponent<CityDriver.Characters.CharacterAnimator>(); if (anim != null) anim.pose = CityDriver.Characters.CharPose.Wave;
            }
            else if (passenger == null)
            {
                var go = new GameObject("Passenger");
                go.AddComponent<MeshFilter>().sharedMesh = PopulationMeshes.Pedestrian(7, 0);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { PopulationMeshes.Material, PopulationMeshes.FaceMaterial };
                passenger = go.transform;
            }
            passenger.gameObject.SetActive(true);
            var mf = passenger.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = PopulationMeshes.Pedestrian(UnityEngine.Random.Range(0, PopulationMeshes.PedVariants), 0);
            Vector3 p = near + new Vector3(2.5f, 0f, 0f);
            if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 10f, LayerMask.GetMask("Road"))) p.y = hit.point.y;
            passenger.position = p;
            if (Car != null) { Vector3 toCar = Car.transform.position - p; toCar.y = 0f; if (toCar.sqrMagnitude > 0.1f) passenger.rotation = Quaternion.LookRotation(toCar); }
        }

        private void PlacePassengerBeside(Transform carTransform)
        {
            passenger.gameObject.SetActive(true);
            Vector3 p = carTransform.position - carTransform.right * 1.5f; p.y = carTransform.position.y - 0.2f;
            passenger.position = p;
            passenger.rotation = Quaternion.LookRotation(-carTransform.right);
            var anim = passenger.GetComponent<CityDriver.Characters.CharacterAnimator>(); if (anim != null) anim.pose = CityDriver.Characters.CharPose.Talk;
        }

        private static string Pretty(string id) => string.IsNullOrEmpty(id) ? "the destination" : id.Replace("dropoff_", "drop-off ").Replace("pickup_", "pickup ").ToUpperInvariant();
    }
}
