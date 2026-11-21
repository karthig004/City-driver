using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityDriver.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Phase 3: builds the compact test district in the CityDriving scene from procedural geometry and reusable prefabs.
    /// Layout: 3x3 road grid (4-way centre, T-junctions on the ring edges, L-corners), four city blocks:
    ///   A (-,+) commercial + plaza | B (+,+) garage forecourt | C (-,-) parking lot | D (+,-) park.
    /// Traffic convention: LEFT-hand (India). All layout numbers are constants below.
    /// Menu: City Driver > Build.
    /// </summary>
    public static class CityBuilder
    {
        // ---- layout (meters) ---------------------------------------------------------------
        private const float Pitch = 80f;            // distance between road centre lines
        private const float RoadW = 9f;             // two 4.5 m lanes
        private const float BlockHalf = 35.5f;      // half of (Pitch - RoadW)
        private const float SidewalkW = 3f;
        private const float TopY = 0.15f;           // sidewalk / pavement height above road
        private const float SlabThick = 0.35f;      // slab goes from y=-0.2 to y=0.15
        private const float SlabCenterY = -0.025f;
        private const float PaintY = 0.04f;         // road paint lift (avoids z-fighting)
        private const float LotSize = 65f / 3f;     // 3x3 lots inside the sidewalk ring
        private static readonly float[] Lines = { -Pitch, 0f, Pitch };

        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        private enum Side { N, S, E, W }

        private struct Opening { public Side Side; public float Along; public float Width; }

        private sealed class Block
        {
            public string Name;
            public Vector2 Center;
            public string Interior;
            public readonly List<Opening> Openings = new List<Opening>();
        }

        private sealed class MeshSpec
        {
            public CityMeshBuilder Builder = new CityMeshBuilder(4f, 4f);
            public string Material;
            public string Layer;
            public bool Collider;
        }

        /// <summary>When false, the old ground skirt, outer sidewalk strips and backdrop buildings are skipped (the large city provides them).</summary>
        public static bool IncludeSurround = true;

        private static CityLibrary lib;
        private static Dictionary<string, MeshSpec> specs;
        private static System.Random rng;

        // =============================================================================================
        [MenuItem("City Driver/Build/Phase 3 - Build City", priority = 0)]
        [BridgeCommand]
        public static void BuildCity()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[City] Exit Play Mode first."); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != CityScenePath)
            {
                Debug.LogError($"[City] Scene '{active.name}' has unsaved changes. Save it (or discard) and run again.");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Open the city scene first: prefab creation spawns temporary objects in the active scene.
            var scene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            lib = CityAssetFactory.CreateAll();
            rng = new System.Random(20261114);
            specs = new Dictionary<string, MeshSpec>();

            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "City") UnityEngine.Object.DestroyImmediate(root);

            var city = new GameObject("City");
            Transform ground = Group(city.transform, "Ground"), props = Group(city.transform, "Props"),
                      buildings = Group(city.transform, "Buildings"), gameplay = Group(city.transform, "Gameplay");
            Transform lights = Group(props, "Streetlights"), trees = Group(props, "Trees"), signs = Group(props, "Signs"), benches = Group(props, "Benches");
            Transform geometry = Group(city.transform, "Geometry");

            if (IncludeSurround) AddGround();
            BuildRoadsAndPaint();
            var blocks = DefineBlocks();
            foreach (var b in blocks) BuildBlockSurface(b);
            if (IncludeSurround) BuildOuterStrips();

            foreach (var b in blocks) PopulateBlock(b, buildings, lights, trees, signs, benches);
            if (IncludeSurround) BuildBackdrop(buildings);
            BuildGameplayPoints(gameplay);
            MaterializeMeshes(geometry, ground);

            SetStatic(city.transform);
            SetupLightingAndCamera(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            LogStats(city, watch.ElapsedMilliseconds);
        }

        // ================================================================================== ground, roads, paint

        private static MeshSpec Spec(string key, string material, string layer, bool collider)
        {
            if (!specs.TryGetValue(key, out var s))
                specs[key] = s = new MeshSpec { Material = material, Layer = layer, Collider = collider };
            return s;
        }

        private static void AddGround()
        {
            // Big grass skirt below road level; the last thing you see past the backdrop.
            Spec("Ground", "Grass", "Environment", true).Builder.AddBox(new Vector3(0, -0.3f, 0), new Vector3(3000f, 0.5f, 3000f));
        }

        private static void BuildRoadsAndPaint()
        {
            MeshSpec roads = Spec("Roads", "Asphalt", "Road", true);
            MeshSpec white = Spec("PaintWhite", "PaintWhite", "Default", false);
            MeshSpec yellow = Spec("PaintYellow", "PaintYellow", "Default", false);
            const float edgeLen = Pitch - RoadW;

            foreach (float cx in Lines)
                foreach (float cz in Lines)
                    roads.Builder.AddBox(new Vector3(cx, -0.1f, cz), new Vector3(RoadW, 0.2f, RoadW));

            // Edges between neighbouring nodes, along X and along Z.
            for (int i = 0; i < Lines.Length - 1; i++)
            {
                float mid = (Lines[i] + Lines[i + 1]) * 0.5f;
                foreach (float fixedCoord in Lines)
                {
                    roads.Builder.AddBox(new Vector3(mid, -0.1f, fixedCoord), new Vector3(edgeLen, 0.2f, RoadW));
                    roads.Builder.AddBox(new Vector3(fixedCoord, -0.1f, mid), new Vector3(RoadW, 0.2f, edgeLen));
                    EdgePaint(white, yellow, new Vector2(mid, fixedCoord), true, edgeLen);
                    EdgePaint(white, yellow, new Vector2(fixedCoord, mid), false, edgeLen);
                }
            }

            // Crosswalks + stop lines on every arm of every node.
            foreach (float nx in Lines)
                foreach (float nz in Lines)
                    foreach (Vector3 arm in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        float ax = nx + arm.x * Pitch, az = nz + arm.z * Pitch;
                        if (Mathf.Abs(ax) > Pitch + 0.1f || Mathf.Abs(az) > Pitch + 0.1f) continue; // no neighbour that way
                        ArmPaint(white, new Vector3(nx, 0, nz), arm);
                    }
        }

        // Centre double-yellow + white edge lines, trimmed away from the crosswalk zone so paint never overlaps.
        private static void EdgePaint(MeshSpec white, MeshSpec yellow, Vector2 mid, bool alongX, float edgeLen)
        {
            float len = edgeLen - 10f;
            foreach (float off in new[] { -0.15f, 0.15f }) Quad(yellow, mid, alongX, len, 0.12f, off);
            foreach (float off in new[] { -(RoadW / 2f - 0.3f), RoadW / 2f - 0.3f }) Quad(white, mid, alongX, len, 0.12f, off);
        }

        private static void Quad(MeshSpec spec, Vector2 center, bool alongX, float length, float width, float perpendicularOffset)
        {
            float cx = center.x + (alongX ? 0f : perpendicularOffset);
            float cz = center.y + (alongX ? perpendicularOffset : 0f);
            spec.Builder.AddTopQuad(cx, cz, alongX ? length : width, alongX ? width : length, PaintY);
        }

        private static void ArmPaint(MeshSpec white, Vector3 node, Vector3 arm)
        {
            bool alongX = Mathf.Abs(arm.x) > 0.5f;
            Vector3 right = Vector3.Cross(Vector3.up, arm);     // perpendicular to the arm
            float crossDist = RoadW / 2f + 2f;                    // 6.5 m from node centre: crosswalk centre
            for (int i = -4; i <= 4; i++)                          // 9 zebra stripes across the 9 m road
            {
                Vector3 p = node + arm * crossDist + right * i;
                white.Builder.AddTopQuad(p.x, p.z, alongX ? 3f : 0.5f, alongX ? 0.5f : 3f, PaintY);
            }

            // Stop line in the lane that ENTERS the node (left-hand traffic: lane lies on the arm's +Cross(up,arm) side).
            Vector3 lane = node + arm * (RoadW / 2f + 4.2f) + right * (RoadW / 4f);
            white.Builder.AddTopQuad(lane.x, lane.z, alongX ? 0.4f : 3.6f, alongX ? 3.6f : 0.4f, PaintY);
        }

        // ================================================================================== blocks

        private static List<Block> DefineBlocks()
        {
            var a = new Block { Name = "Block_A_Commercial", Center = new Vector2(-40, 40), Interior = "Plaza" };
            var bGarage = new Block { Name = "Block_B_Garage", Center = new Vector2(40, 40), Interior = "Forecourt" };
            bGarage.Openings.Add(new Opening { Side = Side.S, Along = 0f, Width = 10f });
            var c = new Block { Name = "Block_C_Parking", Center = new Vector2(-40, -40), Interior = "ParkingLot" };
            c.Openings.Add(new Opening { Side = Side.E, Along = -8f, Width = 6f });
            c.Openings.Add(new Opening { Side = Side.S, Along = -10f, Width = 6f });
            var d = new Block { Name = "Block_D_Park", Center = new Vector2(40, -40), Interior = "Lawn" };
            return new List<Block> { a, bGarage, c, d };
        }

        private static Vector3 SideDir(Side s) { switch (s) { case Side.N: return Vector3.forward; case Side.S: return Vector3.back; case Side.E: return Vector3.right; default: return Vector3.left; } }
        private static Vector3 SideTangent(Side s) => (s == Side.N || s == Side.S) ? Vector3.right : Vector3.forward;
        private static Vector3 SidePoint(Vector2 c, Side s, float inset, float along) =>
            new Vector3(c.x, 0, c.y) + SideDir(s) * (BlockHalf - inset) + SideTangent(s) * along;

        private static bool Blocked(Block b, Side s, float along, float margin) =>
            b.Openings.Any(o => o.Side == s && Mathf.Abs(o.Along - along) < o.Width / 2f + margin);

        private static void BuildBlockSurface(Block b)
        {
            string interiorMat = b.Interior == "Plaza" ? "Concrete" : b.Interior == "Forecourt" ? "ConcreteDark" : b.Interior == "ParkingLot" ? "Asphalt" : "Grass";
            Spec(b.Interior, interiorMat, "Road", true).Builder.AddBox(new Vector3(b.Center.x, SlabCenterY, b.Center.y), new Vector3(65f, SlabThick, 65f));
            MeshSpec walk = Spec("Sidewalk", "Concrete", "Road", true);

            foreach (Side s in Enum.GetValues(typeof(Side)))
            {
                bool ns = s == Side.N || s == Side.S;
                float lo = ns ? -BlockHalf : -(BlockHalf - SidewalkW), hi = -lo;
                var cuts = b.Openings.Where(o => o.Side == s).OrderBy(o => o.Along).ToList();

                float cur = lo;
                var segments = new List<(float a, float b)>();
                foreach (var o in cuts)
                {
                    float start = o.Along - o.Width / 2f, end = o.Along + o.Width / 2f;
                    if (start > cur) segments.Add((cur, start));
                    cur = Mathf.Max(cur, end);
                }

                if (cur < hi) segments.Add((cur, hi));

                foreach (var seg in segments)
                {
                    Vector3 p = SidePoint(b.Center, s, SidewalkW / 2f, (seg.a + seg.b) / 2f);
                    float len = seg.b - seg.a;
                    walk.Builder.AddBox(new Vector3(p.x, SlabCenterY, p.z), ns ? new Vector3(len, SlabThick, SidewalkW) : new Vector3(SidewalkW, SlabThick, len));
                }
            }

            // Driveway ramps: a gently tilted slab filling each opening (road level -> pavement level over the sidewalk depth).
            float angle = Mathf.Atan2(TopY, SidewalkW) * Mathf.Rad2Deg;
            float rampLen = Mathf.Sqrt(SidewalkW * SidewalkW + TopY * TopY);
            const float rampThick = 0.3f;
            foreach (var o in b.Openings)
            {
                Vector3 p = SidePoint(b.Center, o.Side, SidewalkW / 2f, o.Along);
                Quaternion rot = Quaternion.LookRotation(-SideDir(o.Side)) * Quaternion.Euler(-angle, 0, 0);
                float y = TopY / 2f - rampThick / 2f * Mathf.Cos(angle * Mathf.Deg2Rad);
                walk.Builder.AddBox(new Vector3(p.x, y, p.z), new Vector3(o.Width, rampThick, rampLen), rot);
            }
        }

        private static void BuildOuterStrips()
        {
            MeshSpec walk = Spec("Sidewalk", "Concrete", "Road", true);
            const float outer = Pitch + RoadW / 2f;          // 84.5: outer road edge
            const float w = 6f;
            float c = outer + w / 2f;                        // 87.5
            walk.Builder.AddBox(new Vector3(0, SlabCenterY, c), new Vector3(2 * (outer + w), SlabThick, w));
            walk.Builder.AddBox(new Vector3(0, SlabCenterY, -c), new Vector3(2 * (outer + w), SlabThick, w));
            walk.Builder.AddBox(new Vector3(c, SlabCenterY, 0), new Vector3(w, SlabThick, 2 * outer));
            walk.Builder.AddBox(new Vector3(-c, SlabCenterY, 0), new Vector3(w, SlabThick, 2 * outer));
        }

        // ================================================================================== props and buildings

        private static void PopulateBlock(Block b, Transform buildings, Transform lights, Transform trees, Transform signs, Transform benches)
        {
            Vector3 c3 = new Vector3(b.Center.x, 0, b.Center.y);

            // Streetlights, trees on all four sidewalks (skipping driveway openings).
            foreach (Side s in Enum.GetValues(typeof(Side)))
            {
                foreach (float along in new[] { -22f, 22f })
                    if (!Blocked(b, s, along, 1.5f))
                        Place(lib.Streetlight, lights, SidePoint(b.Center, s, 1.2f, along) + Vector3.up * TopY, Quaternion.LookRotation(SideDir(s)));
                foreach (float along in new[] { -10f, 10f })
                    if (!Blocked(b, s, along, 1.5f))
                        Place(lib.Tree, trees, SidePoint(b.Center, s, 1.5f, along) + Vector3.up * TopY, Quaternion.Euler(0, rng.Next(0, 360), 0));
            }

            // Signs on the four block corners, alternating stop / speed, facing the junction.
            int k = 0;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    Vector3 p = c3 + new Vector3(sx * (BlockHalf - 1f), TopY, sz * (BlockHalf - 1f));
                    Place((k++ % 2 == 0) ? lib.SignStop : lib.SignSpeed, signs, p, Quaternion.LookRotation(new Vector3(sx, 0, sz)));
                }

            switch (b.Interior)
            {
                case "Plaza":
                    BuildingRing(b, buildings, null);
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sz in new[] { -1, 1 })
                            Place(lib.Tree, trees, c3 + new Vector3(sx * 8f, TopY, sz * 8f), Quaternion.Euler(0, rng.Next(0, 360), 0));
                    Place(lib.Bench, benches, c3 + new Vector3(0, TopY, 4f), Quaternion.Euler(0, 180, 0));
                    Place(lib.Bench, benches, c3 + new Vector3(0, TopY, -4f), Quaternion.identity);
                    break;

                case "Forecourt":
                    BuildingRing(b, buildings, new HashSet<Vector2Int> { new Vector2Int(0, 1), new Vector2Int(0, -1) });
                    Place(lib.GarageBuilding, buildings, c3 + new Vector3(0, TopY, LotSize), Quaternion.identity);
                    break;

                case "ParkingLot":
                    BuildingRing(b, buildings, new HashSet<Vector2Int> { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1) });
                    BuildParkingBays(b);
                    break;

                case "Lawn":
                    BuildPark(b, trees, benches);
                    break;
            }
        }

        // Fills the 8 perimeter lots of a block with randomly chosen building modules (centre lot stays open).
        private static void BuildingRing(Block b, Transform parent, HashSet<Vector2Int> skip)
        {
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0) continue;
                    if (skip != null && (skip.Contains(new Vector2Int(i, j)) || (b.Interior == "Forecourt" && i == 0))) continue;
                    if (b.Interior == "ParkingLot" && j != 1) continue; // parking: buildings only on the north row

                    var pos = new Vector3(b.Center.x + i * LotSize, TopY, b.Center.y + j * LotSize);
                    PlaceBuilding(parent, pos, rng.Next(0, 4) * 90f);
                }
        }

        private static GameObject PlaceBuilding(Transform parent, Vector3 pos, float yaw)
        {
            int kind = PickBuildingKind();
            var go = Place(lib.Buildings[kind], parent, pos, Quaternion.Euler(0, yaw, 0));
            var r = go.GetComponentInChildren<MeshRenderer>();
            if (r != null) r.sharedMaterial = lib.BuildingMaterials[rng.Next(0, lib.BuildingMaterials.Length)];
            return go;
        }

        private static int PickBuildingKind()
        {
            double r = rng.NextDouble();
            return r < 0.30 ? 0 : r < 0.55 ? 1 : r < 0.80 ? 2 : 3; // Low, Wide, Mid, Tall
        }

        private static void BuildParkingBays(Block b)
        {
            MeshSpec white = Spec("PaintWhite", "PaintWhite", "Default", false);
            float baseY = TopY + PaintY;
            foreach (float rowZ in new[] { -29.5f, -18.5f, -13.5f, -2.5f })     // row centres relative to block centre
            {
                for (int i = 0; i <= 22; i++)
                {
                    float x = -29.7f + i * 2.7f;
                    if (rowZ < -25f && Mathf.Abs(x - (-10f)) < 4f) continue;       // keep the south driveway clear
                    white.Builder.AddTopQuad(b.Center.x + x, b.Center.y + rowZ, 0.12f, 5f, baseY);
                }
            }
        }

        private static void BuildPark(Block b, Transform trees, Transform benches)
        {
            // Two concrete paths crossing at the centre, lifted slightly like paint decals.
            MeshSpec path = Spec("ParkPath", "Concrete", "Default", false);
            path.Builder.AddTopQuad(b.Center.x, b.Center.y, 3f, 65f, TopY + PaintY);
            path.Builder.AddTopQuad(b.Center.x, b.Center.y, 65f, 3f, TopY + PaintY);
            Vector3 c3 = new Vector3(b.Center.x, 0, b.Center.y);

            int placed = 0, attempts = 0;
            while (placed < 26 && attempts++ < 400)
            {
                float x = (float)(rng.NextDouble() * 56 - 28), z = (float)(rng.NextDouble() * 56 - 28);
                if (Mathf.Abs(x) < 4f || Mathf.Abs(z) < 4f) continue;           // keep paths clear
                Place(lib.Tree, trees, c3 + new Vector3(x, TopY, z), Quaternion.Euler(0, rng.Next(0, 360), 0));
                placed++;
            }

            Place(lib.Bench, benches, c3 + new Vector3(3.2f, TopY, 10f), Quaternion.Euler(0, -90, 0));
            Place(lib.Bench, benches, c3 + new Vector3(-3.2f, TopY, -10f), Quaternion.Euler(0, 90, 0));
            Place(lib.Bench, benches, c3 + new Vector3(10f, TopY, -3.2f), Quaternion.Euler(0, 0, 0));
            Place(lib.Bench, benches, c3 + new Vector3(-10f, TopY, 3.2f), Quaternion.Euler(0, 180, 0));
        }

        // Tall backdrop buildings outside the ring so the district doesn't end at an empty horizon.
        private static void BuildBackdrop(Transform parent)
        {
            var group = Group(parent, "Backdrop");
            for (float t = -132f; t <= 132f; t += 22f)
            {
                PlaceBuilding(group, new Vector3(t, 0, 112f), rng.Next(0, 2) * 90f);
                PlaceBuilding(group, new Vector3(t, 0, -112f), rng.Next(0, 2) * 90f);
            }

            for (float t = -88f; t <= 88f; t += 22f)
            {
                PlaceBuilding(group, new Vector3(112f, 0, t), rng.Next(0, 2) * 90f);
                PlaceBuilding(group, new Vector3(-112f, 0, t), rng.Next(0, 2) * 90f);
            }
        }

        // ================================================================================== gameplay anchors

        private static void BuildGameplayPoints(Transform parent)
        {
            Marker(parent, "PlayerStart", LocationKind.PlayerStart, "player_start", new Vector3(40, 0.6f, 22), Quaternion.Euler(0, 180, 0), null, false);
            Marker(parent, "GarageEntrance", LocationKind.GarageEntrance, "garage", new Vector3(40, 0.2f, 6), Quaternion.Euler(0, 180, 0), null, false);

            Marker(parent, "Pickup_A", LocationKind.Pickup, "pickup_a", new Vector3(-40, TopY + 0.02f, 6), Quaternion.identity, lib.PickupMarker, true);
            Marker(parent, "Pickup_B", LocationKind.Pickup, "pickup_b", new Vector3(6, TopY + 0.02f, -40), Quaternion.Euler(0, 90, 0), lib.PickupMarker, true);
            Marker(parent, "Pickup_C", LocationKind.Pickup, "pickup_c", new Vector3(74, TopY + 0.02f, 40), Quaternion.Euler(0, -90, 0), lib.PickupMarker, true);
            Marker(parent, "Dropoff_A", LocationKind.Dropoff, "dropoff_a", new Vector3(40, TopY + 0.02f, -6), Quaternion.identity, lib.DropoffMarker, false);
            Marker(parent, "Dropoff_B", LocationKind.Dropoff, "dropoff_b", new Vector3(-74, TopY + 0.02f, 40), Quaternion.Euler(0, 90, 0), lib.DropoffMarker, false);
            Marker(parent, "Dropoff_C", LocationKind.Dropoff, "dropoff_c", new Vector3(-40, TopY + 0.02f, -74), Quaternion.identity, lib.DropoffMarker, false);
        }

        private static void Marker(Transform parent, string name, LocationKind kind, string id, Vector3 pos, Quaternion rot, GameObject visual, bool withPassenger)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<LocationMarker>().Configure(kind, id);

            if (visual != null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(visual, go.transform);
                v.transform.localPosition = Vector3.zero;
                v.transform.localRotation = Quaternion.identity;
            }

            if (withPassenger)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(lib.Passenger, go.transform);
                p.transform.localPosition = new Vector3(0, 0, 1.4f);   // standing just off the curb, inside the marker ring
                p.transform.localRotation = Quaternion.identity;
            }
        }

        // ================================================================================== assembly helpers

        private static void MaterializeMeshes(Transform geometry, Transform ground)
        {
            foreach (var kv in specs)
            {
                MeshSpec s = kv.Value;
                Mesh mesh = CityAssetFactory.SaveMesh(s.Builder.ToMesh("City_" + kv.Key), $"{CityAssetFactory.MeshesDir}/City_{kv.Key}.asset");
                var go = new GameObject(kv.Key);
                go.transform.SetParent(kv.Key == "Ground" ? ground : geometry, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = lib.Materials[s.Material];
                if (s.Collider)
                {
                    if (kv.Key == "Ground") // huge flat triangles make unstable mesh colliders; a box is exact and cheaper
                    {
                        var box = go.AddComponent<BoxCollider>();
                        box.center = mesh.bounds.center;
                        box.size = mesh.bounds.size;
                    }
                    else go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }

                go.layer = LayerMask.NameToLayer(s.Layer);
            }
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            SetLayerRecursively(go, LayerMask.NameToLayer("Environment"));
            return go;
        }

        private static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
        }

        private static void SetStatic(Transform city)
        {
            foreach (Transform t in city.GetComponentsInChildren<Transform>(true))
            {
                bool gameplay = t.name == "Gameplay" || t.GetComponentInParent<LocationMarker>() != null;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, gameplay ? 0 : StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        private static void SetupLightingAndCamera(UnityEngine.SceneManagement.Scene scene)
        {
            Light sun = null; Camera cam = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (sun == null) sun = root.GetComponentInChildren<Light>();
                if (cam == null) cam = root.GetComponentInChildren<Camera>();
            }

            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.96f, 0.88f);
                sun.intensity = 1.15f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
                RenderSettings.sun = sun;
            }

            if (lib.Materials.TryGetValue("Skybox", out var sky) && sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.90f);
            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 720f;

            if (cam != null)
            {
                // Temporary overview camera until the follow camera exists (Phase 5).
                cam.transform.SetPositionAndRotation(new Vector3(0, 170, -170), Quaternion.Euler(45f, 0, 0));
                cam.fieldOfView = 60f;
                cam.farClipPlane = 800f;
            }
        }

        private static void LogStats(GameObject city, long ms)
        {
            var renderers = city.GetComponentsInChildren<MeshRenderer>(true);
            long tris = 0;
            foreach (var mf in city.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            int materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            int markers = city.GetComponentsInChildren<LocationMarker>(true).Length;
            Debug.Log($"[City] Built in {ms} ms. Renderers={renderers.Length}, triangles={tris}, distinct materials={materials}, location markers={markers}.");
        }

        // ================================================================================== physics verification

        [MenuItem("City Driver/Build/Verify City (ground + lane clearance)", priority = 1)]
        [BridgeCommand]
        public static void VerifyCity()
        {
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            int envMask = LayerMask.GetMask("Environment");
            int roadMask = LayerMask.GetMask("Road");
            var report = new System.Text.StringBuilder("[CityVerify]\n");

            // 1) Every location marker must have drivable/walkable ground right below it.
            int groundMisses = 0;
            foreach (var m in UnityEngine.Object.FindObjectsByType<LocationMarker>().OrderBy(x => x.LocationId))
            {
                bool hit = Physics.Raycast(m.transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit h, 10f, roadMask);
                if (!hit) groundMisses++;
                report.AppendLine($"  {m.LocationId,-14} {m.Kind,-15} ground={(hit ? $"{h.collider.name} y={h.point.y:F2}" : "MISSING")}");
            }

            // 2) A car-sized box (2.0 x 1.2 x 4.6 m, 0.4 m above the road) must fit along both lane centres of every road.
            int samples = 0, blocked = 0;
            var blockers = new HashSet<string>();
            foreach (float line in Lines)
                foreach (float lane in new[] { -RoadW / 4f, RoadW / 4f })
                    for (float t = -Pitch - 4.5f; t <= Pitch + 4.5f; t += 1.5f)
                        foreach (bool alongX in new[] { true, false })
                        {
                            Vector3 pos = alongX ? new Vector3(t, 1.0f, line + lane) : new Vector3(line + lane, 1.0f, t);
                            Quaternion rot = alongX ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
                            samples++;
                            var hits = Physics.OverlapBox(pos, new Vector3(1.0f, 0.6f, 2.3f), rot, envMask, QueryTriggerInteraction.Ignore);
                            if (hits.Length == 0) continue;
                            blocked++;
                            foreach (var c in hits) blockers.Add(c.transform.root == c.transform ? c.name : $"{c.transform.parent?.name}/{c.name}");
                        }

            report.AppendLine($"  Lane clearance: {samples - blocked}/{samples} samples free. Blockers: {(blockers.Count == 0 ? "none" : string.Join(", ", blockers.Take(10)))}");
            report.AppendLine($"  Markers without ground: {groundMisses}");
            Debug.Log(report.ToString());
        }

        // ================================================================================== screenshots (for verification)

        [BridgeCommand]
        public static void CaptureShots()
        {
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            Shot(dir, "overview", new Vector3(-130, 90, -130), Quaternion.Euler(30f, 45f, 0));
            Shot(dir, "garage_spawn", new Vector3(40, 2.6f, 14), Quaternion.Euler(10f, 180f, 0));
            Shot(dir, "centre_junction", new Vector3(0, 3.2f, -22), Quaternion.Euler(12f, 0f, 0));
            Shot(dir, "park_pickup", new Vector3(14, 3f, -28), Quaternion.Euler(14f, 20f, 0));
            Shot(dir, "parking", new Vector3(-12, 18, -78), Quaternion.Euler(50f, -50f, 0));
        }

        internal static void Shot(string dir, string name, Vector3 pos, Quaternion rot)
        {
            const int w = 1280, h = 720;
            var go = new GameObject("ShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 900f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, $"shot_{name}.png"), tex.EncodeToPNG());

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
