using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityDriver.Missions;
using CityDriver.Player;
using CityDriver.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Generates the large city from the verified road graph: roads with lane markings, intersections, crosswalks, kerbed
    /// sidewalks, district blocks filled with modular buildings, street trees, lights, signals, bus stops and highway
    /// furniture. Everything is grouped into 240 m chunks that the ChunkStreamer activates by distance. The original
    /// 160 m core district (garage, parking, park, plaza and all mission markers) is kept exactly where it was.
    /// </summary>
    public static class WorldBuilder
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";
        private const string MeshDir = "Assets/_Project/Art/Environment/World";
        private const string ChunkSceneDir = "Assets/_Project/Scenes/CityChunks";
        private const float ChunkSize = 240f, ChunkOrigin = -1200f;
        private const float TopY = 0.15f, PaintY = 0.04f, SlabY = -0.025f, SlabThick = 0.35f;
        private const float DashLen = 3f, DashGap = 5f;

        private enum Side { S, N, W, E }

        // ---- build state
        private static RoadGraphData graph;
        private static WorldMaterials wm;
        private static System.Random rng;
        private static Dictionary<Vector2Int, Chunk> chunks;
        private static Transform worldRoot;

        private sealed class Chunk
        {
            public Vector2Int index; public GameObject root; public Transform buildings;
            public readonly Dictionary<string, CityMeshBuilder> meshes = new Dictionary<string, CityMeshBuilder>();
            public readonly Dictionary<string, List<CombineInstance>> combine = new Dictionary<string, List<CombineInstance>>();
            public readonly List<(Vector3 p, float r, float h)> capsules = new List<(Vector3, float, float)>();
            public readonly Dictionary<string, List<(Vector3 c, Vector3 s)>> boxes = new Dictionary<string, List<(Vector3, Vector3)>>();
            public Bounds bounds; public bool hasBounds; public int buildingCount; public long tris;

            public void Encapsulate(Vector3 p) { if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; } else bounds.Encapsulate(p); }
        }

        private static readonly Dictionary<string, (string mat, string layer, bool collider, float tile)> Surfaces = new Dictionary<string, (string, string, bool, float)>
        {
            ["Road"] = ("Road", "Road", true, 6f),
            ["Sidewalk"] = ("Sidewalk", "Road", true, 3f),
            ["Concrete"] = ("Concrete", "Road", true, 4f),
            ["ConcreteDark"] = ("ConcreteDark", "Road", true, 4f),
            ["Grass"] = ("Grass", "Road", true, 5f),
            ["Barrier"] = ("Barrier", "Environment", true, 3f),
        };

        // ================================================================================== entry point

        [MenuItem("City Driver/Build/Large City (from road graph)", priority = 40)]
        [BridgeCommand]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[World] Exit Play Mode first."); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != CityScenePath) { Debug.LogError("[World] Active scene has unsaved changes."); return; }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var asset = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(MapBaker.RoadGraphPath);
            if (asset == null) { Debug.LogError("[World] Road graph asset missing. Run WorldTests.GenerateAndTest first."); return; }
            graph = asset.data;
            rng = new System.Random(20261114);

            // 1) Materials, textures, kits.
            wm = WorldMaterials.Create();
            TreeKit.BuildAll();
            BuildingKit.BuildAll(wm);
            WorldMaterials.UpgradeCoreMaterials(wm);

            // 2) Rebuild the core district without the old surround (ground, outer strips, backdrop).
            CityBuilder.IncludeSurround = false;
            CityBuilder.BuildCity();
            CityBuilder.IncludeSurround = true;

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects()) if (root.name == "World") UnityEngine.Object.DestroyImmediate(root);
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            if (AssetDatabase.IsValidFolder(ChunkSceneDir)) AssetDatabase.DeleteAsset(ChunkSceneDir);
            CityAssetFactory.EnsureFolder(MeshDir);
            CityAssetFactory.EnsureFolder(ChunkSceneDir);

            worldRoot = new GameObject("World").transform;
            chunks = new Dictionary<Vector2Int, Chunk>();

            // 3) Geometry.
            AddGround();
            BuildRoads();
            BuildBlocks();
            BuildSignals();

            // 4) Materialise chunks, streaming component, lighting.
            ChunkInfo[] chunkInfos = Materialise(scene);
            SetupStreamer(scene, chunkInfos);
            UpdateBuildSettings(chunkInfos);
            RenderSettings.fogStartDistance = 200f; RenderSettings.fogEndDistance = 520f;
            RenderSettings.fog = true;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Report(chunkInfos, watch.ElapsedMilliseconds);
        }

        // ================================================================================== chunk plumbing

        private static Vector2Int IndexOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt((p.x - ChunkOrigin) / ChunkSize), Mathf.FloorToInt((p.z - ChunkOrigin) / ChunkSize));

        private static Chunk ChunkAt(Vector3 p)
        {
            Vector2Int i = IndexOf(p);
            if (!chunks.TryGetValue(i, out Chunk c))
            {
                c = new Chunk { index = i, root = new GameObject($"Chunk_{i.x}_{i.y}") };
                c.root.transform.SetParent(worldRoot, false);
                c.buildings = new GameObject("Buildings").transform; c.buildings.SetParent(c.root.transform, false);
                chunks[i] = c;
            }

            c.Encapsulate(p);
            return c;
        }

        private static CityMeshBuilder Mesh(Chunk c, string key)
        {
            if (!c.meshes.TryGetValue(key, out var b))
            {
                float tile = Surfaces.TryGetValue(key, out var s) ? s.tile : 4f;
                c.meshes[key] = b = new CityMeshBuilder(tile, tile);
            }

            return b;
        }

        private static void Box(Vector3 centre, Vector3 size, string key)
        {
            Chunk c = ChunkAt(centre);
            Mesh(c, key).AddBox(centre, size);
            c.Encapsulate(centre + size * 0.5f); c.Encapsulate(centre - size * 0.5f);
            if (Surfaces.TryGetValue(key, out var s) && s.collider)
            {
                if (!c.boxes.TryGetValue(key, out var list)) c.boxes[key] = list = new List<(Vector3, Vector3)>();
                list.Add((centre, size));      // plain box colliders: no collision-mesh cooking when a chunk is activated
            }
        }
        private static void Slab(float x, float z, float sx, float sz, string key) => Box(new Vector3(x, SlabY, z), new Vector3(sx, SlabThick, sz), key);
        private static void Paint(float x, float z, float sx, float sz, string key) { Chunk c = ChunkAt(new Vector3(x, 0, z)); Mesh(c, key).AddTopQuad(x, z, sx, sz, PaintY); }

        private static void Place(GameObject prefab, Vector3 pos, float yaw, Chunk chunk, BuildingInfo info)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, chunk.buildings);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            SetLayer(go, LayerMask.NameToLayer("Environment"));
            chunk.buildingCount++; chunk.tris += info.triangles;
            chunk.Encapsulate(pos + new Vector3(info.width, info.height, info.depth) * 0.5f); chunk.Encapsulate(pos - new Vector3(info.width, 0, info.depth) * 0.5f);
        }

        // Merges a prop mesh (all sub-meshes) into the chunk, one combined mesh per material.
        private static void Merge(Chunk c, Mesh mesh, Material[] mats, Vector3 pos, float yaw, float scale)
        {
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * scale);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string key = mats[Mathf.Min(s, mats.Length - 1)].name;
                if (!c.combine.TryGetValue(key, out var list)) c.combine[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
            }

            c.Encapsulate(pos);
        }

        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform t in go.transform) SetLayer(t.gameObject, layer); }

        // ================================================================================== ground, roads

        private static float Hw(float lineCoord) => CityLayout.WidthOf(CityLayout.ClassOfLine(lineCoord)) * 0.5f;
        private static bool IsCore(Vector3 p) => Mathf.Abs(p.x) <= 80.1f && Mathf.Abs(p.z) <= 80.1f;

        private static void AddGround()
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(worldRoot, false);
            var b = new CityMeshBuilder(6f, 6f);
            b.AddBox(new Vector3(0, -0.3f, 0), new Vector3(4000f, 0.5f, 4000f));
            Mesh mesh = CityAssetFactory.SaveMesh(b.ToMesh("World_Ground"), $"{MeshDir}/Ground.asset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = wm["Grass"];
            var col = go.AddComponent<BoxCollider>(); col.center = mesh.bounds.center; col.size = mesh.bounds.size;
            go.layer = LayerMask.NameToLayer("Environment");
        }

        private static void BuildRoads()
        {
            foreach (RoadNode n in graph.nodes)
            {
                if (IsCore(n.position)) continue;
                Slab2(n.position.x, n.position.z, Hw(n.position.x) * 2f, Hw(n.position.z) * 2f, "Road", -0.1f, 0.2f);
            }

            foreach (RoadEdge e in graph.edges)
            {
                Vector3 a = graph.nodes[e.from].position, b = graph.nodes[e.to].position;
                if (IsCore(a) && IsCore(b)) continue;                                       // the core district already has these roads
                bool alongX = Mathf.Abs(b.x - a.x) > 0.1f;
                float hwStart = alongX ? Hw(a.x) : Hw(a.z), hwEnd = alongX ? Hw(b.x) : Hw(b.z);
                Vector3 dir = alongX ? Vector3.right * Mathf.Sign(b.x - a.x) : Vector3.forward * Mathf.Sign(b.z - a.z);
                Vector3 p0 = a + dir * hwStart, p1 = b - dir * hwEnd;
                float len = Vector3.Distance(p0, p1);
                Vector3 mid = (p0 + p1) * 0.5f;
                Slab2(mid.x, mid.z, alongX ? len : e.width, alongX ? e.width : len, "Road", -0.1f, 0.2f);
                PaintEdge(e, p0, p1, dir, hwStart, hwEnd);

                if (e.roadClass == RoadClass.Highway) HighwayFurniture(p0, p1, dir, e);
            }
        }

        private static void Slab2(float x, float z, float sx, float sz, string key, float y, float thickness) => Box(new Vector3(x, y, z), new Vector3(sx, thickness, sz), key);

        private static void PaintEdge(RoadEdge e, Vector3 p0, Vector3 p1, Vector3 dir, float hwStart, float hwEnd)
        {
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            float length = Vector3.Distance(p0, p1);
            bool highway = e.roadClass == RoadClass.Highway;
            float trim = highway ? 4f : 10f;                                              // keep paint clear of the crosswalk zone
            if (length < trim * 2f + 5f) return;
            Vector3 s = p0 + dir * trim, t = p1 - dir * trim;
            Vector3 right = Vector3.Cross(Vector3.up, dir);

            void Solid(float offset, string key, float width = 0.14f) { Vector3 c = (s + t) * 0.5f + right * offset; float l = Vector3.Distance(s, t); Paint(c.x, c.z, alongX ? l : width, alongX ? width : l, key); }
            void Dashed(float offset, string key)
            {
                float total = Vector3.Distance(s, t);
                for (float d = 0f; d + DashLen <= total; d += DashLen + DashGap)
                {
                    Vector3 c = s + dir * (d + DashLen * 0.5f) + right * offset;
                    Paint(c.x, c.z, alongX ? DashLen : 0.14f, alongX ? 0.14f : DashLen, key);
                }
            }

            float hw = e.width * 0.5f;
            switch (e.roadClass)
            {
                case RoadClass.Highway:
                    Solid(-1.2f, "PaintYellow"); Solid(1.2f, "PaintYellow");
                    foreach (float o in new[] { -4.7f, -8.2f, 4.7f, 8.2f }) Dashed(o, "PaintWhite");
                    Solid(-(hw - 0.4f), "PaintWhite"); Solid(hw - 0.4f, "PaintWhite");
                    break;
                case RoadClass.Arterial:
                    Solid(-0.15f, "PaintYellow"); Solid(0.15f, "PaintYellow");
                    Dashed(-3.5f, "PaintWhite"); Dashed(3.5f, "PaintWhite");
                    Solid(-(hw - 0.3f), "PaintWhite"); Solid(hw - 0.3f, "PaintWhite");
                    break;
                case RoadClass.Collector:
                    Solid(-0.15f, "PaintYellow"); Solid(0.15f, "PaintYellow");
                    Solid(-(hw - 0.3f), "PaintWhite"); Solid(hw - 0.3f, "PaintWhite");
                    break;
                default:
                    Dashed(0f, "PaintWhite");
                    break;
            }

            if (highway) return;
            // Crosswalks and stop lines at both ends (left-hand traffic: the entering lane is on the arm's +right side).
            ArmPaint(p0, dir, e.width, hwStart);
            ArmPaint(p1, -dir, e.width, hwEnd);
        }

        private static void ArmPaint(Vector3 padEdge, Vector3 armDir, float roadWidth, float unused)
        {
            bool alongX = Mathf.Abs(armDir.x) > 0.5f;
            Vector3 right = Vector3.Cross(Vector3.up, armDir);
            Vector3 centre = padEdge + armDir * 2.0f;                                    // padEdge is the road/pad boundary; crossing 2 m out
            int stripes = Mathf.FloorToInt(roadWidth - 1f);
            for (int i = 0; i < stripes; i++)
            {
                float off = -(stripes - 1) * 0.5f + i;
                Vector3 p = centre + right * off;
                Paint(p.x, p.z, alongX ? 3f : 0.5f, alongX ? 0.5f : 3f, "PaintWhite");
            }

            Vector3 stop = padEdge + armDir * 5.4f + right * (roadWidth * 0.25f);
            float lane = roadWidth * 0.5f - 0.9f;
            Paint(stop.x, stop.z, alongX ? 0.4f : lane, alongX ? lane : 0.4f, "PaintWhite");
        }

        private static void HighwayFurniture(Vector3 p0, Vector3 p1, Vector3 dir, RoadEdge e)
        {
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            float len = Vector3.Distance(p0, p1);
            Vector3 mid = (p0 + p1) * 0.5f, right = Vector3.Cross(Vector3.up, dir);
            Box(new Vector3(mid.x, 0.25f, mid.z), new Vector3(alongX ? len : 1.4f, 0.5f, alongX ? 1.4f : len), "Barrier");       // concrete median divider
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 c = mid + right * side * (e.width * 0.5f + 0.4f);
                Box(new Vector3(c.x, 0.55f, c.z), new Vector3(alongX ? len : 0.12f, 0.35f, alongX ? 0.12f : len), "RailMetal");   // guard rail
            }

            for (float d = 20f; d < len - 10f; d += 55f)                                   // lamp posts on the median
            {
                Vector3 c = p0 + dir * d;
                Chunk ch = ChunkAt(c);
                Mesh(ch, "MetalDark").AddCylinder(new Vector3(c.x, 0.5f, c.z), 0.12f, 8f, 8);
                Mesh(ch, "MetalDark").AddBox(new Vector3(c.x, 8.4f, c.z), alongX ? new Vector3(0.2f, 0.2f, 9f) : new Vector3(9f, 0.2f, 0.2f));
                foreach (float s in new[] { -4f, 4f }) Mesh(ch, "LampGlow").AddBox(c + right * s + Vector3.up * 8.25f, new Vector3(0.7f, 0.12f, 0.7f));
                ch.capsules.Add((new Vector3(c.x, 0, c.z), 0.2f, 3f));
            }
        }

        // ================================================================================== blocks

        private struct Block { public float x0, x1, z0, z1; public DistrictType district; public float swS, swN, swW, swE; }

        private static float Sidewalk(float lineCoord) { RoadClass c = CityLayout.ClassOfLine(lineCoord); return c == RoadClass.Highway ? 2.5f : c == RoadClass.Arterial ? 4f : 3f; }

        private static void BuildBlocks()
        {
            IReadOnlyList<float> L = CityLayout.Lines;
            int n = L.Count;
            var edgeKeys = new HashSet<long>();
            long Key(int ax, int az, int bx, int bz) => ((long)ax * 100 + az) * 10000 + bx * 100 + bz;
            var lineList = L.ToList();
            int Ix(float x) => lineList.FindIndex(v => Mathf.Abs(v - x) < 0.1f);
            foreach (var e in graph.edges)
            {
                Vector3 a = graph.nodes[e.from].position, b = graph.nodes[e.to].position;
                int ax = Ix(a.x), az = Ix(a.z), bx = Ix(b.x), bz = Ix(b.z);
                edgeKeys.Add(Key(Mathf.Min(ax, bx), Mathf.Min(az, bz), Mathf.Max(ax, bx), Mathf.Max(az, bz)));
            }

            bool Has(int ax, int az, int bx, int bz) => edgeKeys.Contains(Key(Mathf.Min(ax, bx), Mathf.Min(az, bz), Mathf.Max(ax, bx), Mathf.Max(az, bz)));

            // Union cells that are separated by a missing (pruned) road.
            int cells = n - 1;
            var parent = new int[cells * cells];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (int ix = 0; ix < cells; ix++)
                for (int iz = 0; iz < cells; iz++)
                {
                    if (ix + 1 < cells && !Has(ix + 1, iz, ix + 1, iz + 1)) parent[Find(ix * cells + iz)] = Find((ix + 1) * cells + iz);
                    if (iz + 1 < cells && !Has(ix, iz + 1, ix + 1, iz + 1)) parent[Find(ix * cells + iz)] = Find(ix * cells + iz + 1);
                }

            var groups = new Dictionary<int, List<Vector2Int>>();
            for (int ix = 0; ix < cells; ix++) for (int iz = 0; iz < cells; iz++) { int r = Find(ix * cells + iz); if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<Vector2Int>(); g.Add(new Vector2Int(ix, iz)); }

            var rects = new List<(int ix0, int ix1, int iz0, int iz1)>();
            foreach (var g in groups.Values)
            {
                int x0 = g.Min(c => c.x), x1 = g.Max(c => c.x), z0 = g.Min(c => c.y), z1 = g.Max(c => c.y);
                if (g.Count == (x1 - x0 + 1) * (z1 - z0 + 1)) rects.Add((x0, x1, z0, z1));
                else foreach (var c in g) rects.Add((c.x, c.x, c.y, c.y));                 // non-rectangular union: keep cells separate
            }

            int blocks = 0, skippedCore = 0;
            foreach (var r in rects)
            {
                float lx0 = L[r.ix0], lx1 = L[r.ix1 + 1], lz0 = L[r.iz0], lz1 = L[r.iz1 + 1];
                if (lx0 >= -80f && lx1 <= 80f && lz0 >= -80f && lz1 <= 80f) { skippedCore++; continue; }   // the four core blocks exist already
                var blk = new Block { x0 = lx0 + Hw(lx0), x1 = lx1 - Hw(lx1), z0 = lz0 + Hw(lz0), z1 = lz1 - Hw(lz1), swW = Sidewalk(lx0), swE = Sidewalk(lx1), swS = Sidewalk(lz0), swN = Sidewalk(lz1) };
                blk.district = CityLayout.DistrictAt(graph.districts, (blk.x0 + blk.x1) * 0.5f, (blk.z0 + blk.z1) * 0.5f);
                if (blk.x1 - blk.x0 < 20f || blk.z1 - blk.z0 < 20f) continue;
                PopulateBlock(blk);
                blocks++;
            }

            Debug.Log($"[World] blocks built: {blocks} (core blocks skipped: {skippedCore})");
        }

        private static void PopulateBlock(Block b)
        {
            float cx = (b.x0 + b.x1) * 0.5f, cz = (b.z0 + b.z1) * 0.5f, w = b.x1 - b.x0, d = b.z1 - b.z0;

            // Sidewalk ring + interior surface.
            Slab(cx, b.z0 + b.swS * 0.5f, w, b.swS, "Sidewalk"); Slab(cx, b.z1 - b.swN * 0.5f, w, b.swN, "Sidewalk");
            Slab(b.x0 + b.swW * 0.5f, cz, b.swW, d - b.swS - b.swN, "Sidewalk"); Slab(b.x1 - b.swE * 0.5f, cz, b.swE, d - b.swS - b.swN, "Sidewalk");
            string interior = b.district == DistrictType.Residential ? "Grass" : b.district == DistrictType.Industrial ? "ConcreteDark" : "Concrete";
            Slab(cx, cz, w - b.swW - b.swE, d - b.swS - b.swN, interior);

            var taken = new List<Rect>();

            // Landmarks first: a point of interest inside this block gets its special building.
            foreach (PointOfInterest poi in graph.pois)
                if (poi.position.x > b.x0 && poi.position.x < b.x1 && poi.position.z > b.z0 && poi.position.z < b.z1) PlacePoi(b, poi, taken);

            // Perimeter rows: buildings face the street they stand on.
            PlaceRow(b, Side.S, taken); PlaceRow(b, Side.N, taken); PlaceRow(b, Side.W, taken); PlaceRow(b, Side.E, taken);
            FillInterior(b, taken);
            StreetProps(b, taken);
        }

        // ------------------------------------------------------------------------------ buildings

        private static readonly Dictionary<DistrictType, (string arch, float weight)[]> Mix = new Dictionary<DistrictType, (string, float)[]>
        {
            [DistrictType.Residential] = new[] { ("Apartment", 0.35f), ("Shophouse", 0.25f), ("House", 0.40f) },
            [DistrictType.Commercial] = new[] { ("Office", 0.35f), ("Shophouse", 0.25f), ("Mall", 0.08f), ("Apartment", 0.32f) },
            [DistrictType.Downtown] = new[] { ("Tower", 0.40f), ("Office", 0.40f), ("Hotel", 0.10f), ("Apartment", 0.10f) },
            [DistrictType.Industrial] = new[] { ("Warehouse", 0.50f), ("Factory", 0.25f), ("ContainerYard", 0.25f) },
            [DistrictType.Entertainment] = new[] { ("Nightclub", 0.25f), ("Cinema", 0.15f), ("Hotel", 0.20f), ("Shophouse", 0.20f), ("Office", 0.20f) },
            [DistrictType.Special] = new[] { ("Apartment", 0.30f), ("Luxury", 0.20f), ("Office", 0.30f), ("Shophouse", 0.20f) },
            [DistrictType.Highway] = new[] { ("Warehouse", 1f) },
        };

        private static BuildingInfo Pick(DistrictType district, float maxWidth, float maxDepth)
        {
            var mix = Mix[district];
            for (int attempt = 0; attempt < 8; attempt++)
            {
                double roll = rng.NextDouble() * mix.Sum(m => m.weight), acc = 0; string arch = mix[0].arch;
                foreach (var m in mix) { acc += m.weight; if (roll <= acc) { arch = m.arch; break; } }
                var fit = BuildingKit.Of(arch).Where(x => x.width <= maxWidth && x.depth <= maxDepth).ToList();
                if (fit.Count > 0) return fit[rng.Next(fit.Count)];
            }

            return null;
        }

        private static void PlaceRow(Block b, Side side, List<Rect> taken)
        {
            bool alongX = side == Side.S || side == Side.N;
            float start = alongX ? b.x0 + 2f : b.z0 + 2f, end = alongX ? b.x1 - 2f : b.z1 - 2f;
            float sw = side == Side.S ? b.swS : side == Side.N ? b.swN : side == Side.W ? b.swW : b.swE;
            float maxDepth = Mathf.Min(alongX ? b.z1 - b.z0 : b.x1 - b.x0, 60f) * 0.5f - sw - 2f;
            float cursor = start;
            int guard = 0;

            while (cursor < end - 6f && guard++ < 60)
            {
                BuildingInfo info = Pick(b.district, end - cursor, maxDepth);
                if (info == null) break;
                float gap = 0.8f + (float)rng.NextDouble() * 2f;
                float along = cursor + info.width * 0.5f;
                float off = sw + 1.0f + info.depth * 0.5f;
                Vector3 pos; float yaw;
                switch (side)
                {
                    case Side.S: pos = new Vector3(along, TopY, b.z0 + off); yaw = 0f; break;
                    case Side.N: pos = new Vector3(along, TopY, b.z1 - off); yaw = 180f; break;
                    case Side.W: pos = new Vector3(b.x0 + off, TopY, along); yaw = 90f; break;
                    default: pos = new Vector3(b.x1 - off, TopY, along); yaw = 270f; break;
                }

                if (TryReserve(taken, pos, info, yaw)) Place(info.prefab, pos, yaw, ChunkAt(pos), info);
                cursor += info.width + gap;
            }
        }

        private static bool TryReserve(List<Rect> taken, Vector3 pos, BuildingInfo info, float yaw)
        {
            bool swap = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
            float w = swap ? info.depth : info.width, d = swap ? info.width : info.depth;
            var r = new Rect(pos.x - w * 0.5f - 0.5f, pos.z - d * 0.5f - 0.5f, w + 1f, d + 1f);
            foreach (var t in taken) if (t.Overlaps(r)) return false;
            taken.Add(r);
            return true;
        }

        private static void FillInterior(Block b, List<Rect> taken)
        {
            float sw = 3f;
            float x0 = b.x0 + sw + 28f, x1 = b.x1 - sw - 28f, z0 = b.z0 + sw + 28f, z1 = b.z1 - sw - 28f;
            if (x1 - x0 < 30f || z1 - z0 < 30f) return;

            if (b.district == DistrictType.Industrial)
            {
                for (float z = z0 + 15f; z < z1 - 10f; z += 45f)
                    for (float x = x0 + 22f; x < x1 - 15f; x += 60f)
                    {
                        BuildingInfo info = Pick(DistrictType.Industrial, 50f, 32f);
                        if (info == null) continue;
                        var pos = new Vector3(x, TopY, z);
                        if (TryReserve(taken, pos, info, 0f)) Place(info.prefab, pos, 0f, ChunkAt(pos), info);
                    }
            }
            else if (b.district == DistrictType.Residential)
            {
                // Pocket park in the middle of larger residential blocks: trees on the grass, no buildings.
                var pr = new Rect(x0, z0, x1 - x0, z1 - z0);
                taken.Add(pr);
                TreeKit_Scatter(b, x0 + 4f, x1 - 4f, z0 + 4f, z1 - 4f, Mathf.RoundToInt((x1 - x0) * (z1 - z0) / 260f));
            }
        }

        private static void PlacePoi(Block b, PointOfInterest poi, List<Rect> taken)
        {
            string arch;
            switch (poi.type)
            {
                case PoiType.Hospital: arch = "Hospital"; break;
                case PoiType.PoliceStation: arch = "Police"; break;
                case PoiType.SafeHouse: arch = "House"; break;
                case PoiType.RepairWorkshop: arch = "RepairShop"; break;
                case PoiType.LuxuryBuilding: arch = "Luxury"; break;
                case PoiType.Nightclub: arch = "Nightclub"; break;
                case PoiType.Cinema: arch = "Cinema"; break;
                case PoiType.Hotel: arch = "Hotel"; break;
                case PoiType.Warehouse: arch = poi.id.Contains("2") ? "ContainerYard" : "Warehouse"; break;
                case PoiType.PetrolStation: PetrolStation(poi.position); return;
                default: return;
            }

            float maxW = b.x1 - b.x0 - 12f, maxD = b.z1 - b.z0 - 12f;
            BuildingInfo info = BuildingKit.Of(arch).Where(x => x.width <= maxW && x.depth <= maxD).OrderByDescending(x => x.width * x.depth).FirstOrDefault();
            if (info == null) return;

            // Face the nearest street.
            float dS = poi.position.z - b.z0, dN = b.z1 - poi.position.z, dW = poi.position.x - b.x0, dE = b.x1 - poi.position.x;
            float m = Mathf.Min(Mathf.Min(dS, dN), Mathf.Min(dW, dE));
            float yaw = m == dS ? 0f : m == dN ? 180f : m == dW ? 90f : 270f;
            bool swap = yaw == 90f || yaw == 270f;
            float hw = (swap ? info.depth : info.width) * 0.5f + 5f, hd = (swap ? info.width : info.depth) * 0.5f + 5f;
            var pos = new Vector3(Mathf.Clamp(poi.position.x, b.x0 + hw, b.x1 - hw), TopY, Mathf.Clamp(poi.position.z, b.z0 + hd, b.z1 - hd));
            if (TryReserve(taken, pos, info, yaw)) Place(info.prefab, pos, yaw, ChunkAt(pos), info);
        }

        private static void PetrolStation(Vector3 p)
        {
            Chunk c = ChunkAt(p);
            CityMeshBuilder red = Mesh(c, "PetrolRed"), white = Mesh(c, "White"), dark = Mesh(c, "MetalDark");
            red.AddBox(new Vector3(p.x, 5.4f, p.z), new Vector3(18f, 0.7f, 10f));                       // canopy
            foreach (float dx in new[] { -7f, 7f }) foreach (float dz in new[] { -3.5f, 3.5f }) white.AddCylinder(new Vector3(p.x + dx, TopY, p.z + dz), 0.3f, 5.1f, 8);
            foreach (float dx in new[] { -4f, 4f }) dark.AddBox(new Vector3(p.x + dx, TopY + 0.6f, p.z), new Vector3(0.6f, 1.2f, 1.6f));   // pumps
            white.AddBox(new Vector3(p.x, TopY + 2.2f, p.z - 12f), new Vector3(10f, 4.4f, 6f));          // shop
            c.capsules.Add((new Vector3(p.x, TopY, p.z - 12f), 4f, 4f));
            c.Encapsulate(p + new Vector3(10, 6, 14)); c.Encapsulate(p - new Vector3(10, 0, 14));
        }

        // ------------------------------------------------------------------------------ props

        private static void StreetProps(Block b, List<Rect> taken)
        {
            string[] speciesByDistrict = b.district == DistrictType.Entertainment || b.district == DistrictType.Special ? new[] { "Palm", "Gulmohar", "Palm" }
                : b.district == DistrictType.Residential ? new[] { "Neem", "Gulmohar", "Ashoka", "Neem" }
                : b.district == DistrictType.Industrial ? new[] { "Neem", "Ashoka" } : new[] { "Neem", "Ashoka", "Gulmohar" };

            foreach (Side side in new[] { Side.S, Side.N, Side.W, Side.E })
            {
                bool alongX = side == Side.S || side == Side.N;
                float sw = side == Side.S ? b.swS : side == Side.N ? b.swN : side == Side.W ? b.swW : b.swE;
                float start = (alongX ? b.x0 : b.z0) + 9f, end = (alongX ? b.x1 : b.z1) - 9f;
                float curbOffset = sw * 0.5f;
                int i = 0;
                for (float a = start; a < end; a += 15f + (float)rng.NextDouble() * 4f, i++)
                {
                    Vector3 pos = SidePoint(b, side, curbOffset, a);
                    if (b.district == DistrictType.Industrial && i % 2 == 1) continue;
                    AddTree(pos, speciesByDistrict[rng.Next(speciesByDistrict.Length)]);
                }

                for (float a = start + 20f; a < end; a += 42f)                              // streetlights
                {
                    Vector3 pos = SidePoint(b, side, 0.7f, a);
                    Vector3 toRoad = OutDir(side);
                    AddStreetlight(pos, toRoad);
                }

                if ((side == Side.S || side == Side.W) && sw >= 4f && (b.x1 - b.x0) > 100f) BusStop(SidePoint(b, side, 1.8f, (start + end) * 0.5f), side);
            }
        }

        private static Vector3 OutDir(Side s) => s == Side.S ? Vector3.back : s == Side.N ? Vector3.forward : s == Side.W ? Vector3.left : Vector3.right;

        private static Vector3 SidePoint(Block b, Side side, float inset, float along)
        {
            switch (side)
            {
                case Side.S: return new Vector3(along, TopY, b.z0 + inset);
                case Side.N: return new Vector3(along, TopY, b.z1 - inset);
                case Side.W: return new Vector3(b.x0 + inset, TopY, along);
                default: return new Vector3(b.x1 - inset, TopY, along);
            }
        }

        private static void AddTree(Vector3 pos, string species)
        {
            TreeSpecies s = TreeKit.Get(species);
            Chunk c = ChunkAt(pos);
            float scale = 0.8f + (float)rng.NextDouble() * 0.55f;
            Merge(c, s.mesh, new[] { s.bark, s.leaf }, pos, (float)rng.NextDouble() * 360f, scale);
            c.capsules.Add((pos, s.trunkRadius * scale, 3.2f));
        }

        private static void TreeKit_Scatter(Block b, float x0, float x1, float z0, float z1, int count)
        {
            string[] sp = { "Neem", "Gulmohar", "Ashoka", "Neem" };
            for (int i = 0; i < count; i++)
                AddTree(new Vector3(Mathf.Lerp(x0, x1, (float)rng.NextDouble()), TopY, Mathf.Lerp(z0, z1, (float)rng.NextDouble())), sp[rng.Next(sp.Length)]);
        }

        private static void AddStreetlight(Vector3 pos, Vector3 toRoad)
        {
            Chunk c = ChunkAt(pos);
            bool alongX = Mathf.Abs(toRoad.x) > 0.5f;
            Mesh(c, "MetalDark").AddCylinder(pos, 0.09f, 7f, 8);
            Vector3 arm = pos + Vector3.up * 6.9f + toRoad * 1.2f;
            Mesh(c, "MetalDark").AddBox(arm - toRoad * 0.0f, alongX ? new Vector3(2.5f, 0.1f, 0.1f) : new Vector3(0.1f, 0.1f, 2.5f));
            Mesh(c, "LampGlow").AddBox(pos + Vector3.up * 6.82f + toRoad * 2.3f, alongX ? new Vector3(0.8f, 0.1f, 0.4f) : new Vector3(0.4f, 0.1f, 0.8f));
            c.capsules.Add((pos, 0.2f, 3f));
        }

        private static void BusStop(Vector3 pos, Side side)
        {
            Chunk c = ChunkAt(pos);
            bool alongX = side == Side.S || side == Side.N;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            CityMeshBuilder dark = Mesh(c, "MetalDark"), light = Mesh(c, "TrimLight");
            foreach (float s in new[] { -2f, 2f }) dark.AddBox(pos + along * s + Vector3.up * 1.2f, new Vector3(0.1f, 2.4f, 0.1f));
            light.AddBox(pos + Vector3.up * 2.5f, alongX ? new Vector3(4.6f, 0.12f, 1.8f) : new Vector3(1.8f, 0.12f, 4.6f));
            dark.AddBox(pos - OutDir(side) * 0.7f + Vector3.up * 1.2f, alongX ? new Vector3(4f, 2.2f, 0.05f) : new Vector3(0.05f, 2.2f, 4f));
            dark.AddBox(pos - OutDir(side) * 0.4f + Vector3.up * 0.45f, alongX ? new Vector3(2.4f, 0.1f, 0.5f) : new Vector3(0.5f, 0.1f, 2.4f));
            c.capsules.Add((pos, 0.8f, 2.5f));
        }

        private static void BuildSignals()
        {
            foreach (RoadNode n in graph.nodes)
            {
                if (!n.signalised || IsCore(n.position)) continue;
                float hx = Hw(n.position.x) + 1.2f, hz = Hw(n.position.z) + 1.2f;
                foreach (var corner in new[] { new Vector2(hx, hz), new Vector2(-hx, -hz) })
                {
                    Vector3 p = new Vector3(n.position.x + corner.x, TopY, n.position.z + corner.y);
                    Chunk c = ChunkAt(p);
                    Mesh(c, "MetalDark").AddCylinder(p, 0.1f, 5.2f, 8);
                    Mesh(c, "SignalBox").AddBox(p + Vector3.up * 5.0f, new Vector3(0.5f, 1.5f, 0.5f));
                    Mesh(c, "SignalRed").AddBox(p + Vector3.up * 5.5f + new Vector3(0, 0, -0.26f), new Vector3(0.3f, 0.3f, 0.05f));
                    Mesh(c, "SignalAmber").AddBox(p + Vector3.up * 5.0f + new Vector3(0, 0, -0.26f), new Vector3(0.3f, 0.3f, 0.05f));
                    Mesh(c, "SignalGreen").AddBox(p + Vector3.up * 4.5f + new Vector3(0, 0, -0.26f), new Vector3(0.3f, 0.3f, 0.05f));
                    c.capsules.Add((p, 0.2f, 3f));
                }
            }
        }

        // ================================================================================== finish

        private static ChunkInfo[] Materialise(UnityEngine.SceneManagement.Scene mainScene)
        {
            var result = new List<ChunkInfo>();
            int layerRoad = LayerMask.NameToLayer("Road"), layerEnv = LayerMask.NameToLayer("Environment");

            foreach (Chunk c in chunks.Values.OrderBy(k => k.index.x).ThenBy(k => k.index.y))
            {
                var geo = new GameObject("Geometry").transform; geo.SetParent(c.root.transform, false);
                foreach (var kv in c.meshes)
                {
                    bool known = Surfaces.TryGetValue(kv.Key, out var s);
                    string matKey = known ? s.mat : kv.Key;
                    Mesh mesh = CityAssetFactory.SaveMesh(kv.Value.ToMesh($"W_{c.index.x}_{c.index.y}_{kv.Key}"), $"{MeshDir}/C{c.index.x}_{c.index.y}_{kv.Key}.asset");
                    var go = new GameObject(kv.Key); go.transform.SetParent(geo, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = TryMat(matKey);
                    if (kv.Key.StartsWith("Paint")) mr.shadowCastingMode = ShadowCastingMode.Off;
                    go.layer = known ? LayerMask.NameToLayer(s.layer) : layerEnv;
                    c.tris += mesh.triangles.Length / 3;
                }

                foreach (var kv in c.boxes)
                {
                    var colGo = new GameObject("Colliders_" + kv.Key); colGo.transform.SetParent(geo, false);
                    colGo.layer = LayerMask.NameToLayer(Surfaces[kv.Key].layer);
                    foreach (var (centre, size) in kv.Value) { var bc = colGo.AddComponent<BoxCollider>(); bc.center = centre; bc.size = size; }
                }

                foreach (var kv in c.combine)
                {
                    var combined = new Mesh { name = $"P_{c.index.x}_{c.index.y}_{kv.Key}", indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(kv.Value.ToArray(), true, true);
                    combined = CityAssetFactory.SaveMesh(combined, $"{MeshDir}/C{c.index.x}_{c.index.y}_P_{kv.Key}.asset");
                    var go = new GameObject("Trees_" + kv.Key); go.transform.SetParent(geo, false);
                    go.AddComponent<MeshFilter>().sharedMesh = combined;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = TryMat(kv.Key);
                    go.layer = layerEnv;
                    c.tris += combined.triangles.Length / 3;
                }

                if (c.capsules.Count > 0)
                {
                    var cg = new GameObject("PropColliders"); cg.transform.SetParent(c.root.transform, false); cg.layer = layerEnv;
                    foreach (var (p, r, h) in c.capsules) { var cc = cg.AddComponent<CapsuleCollider>(); cc.center = new Vector3(p.x, p.y + h * 0.5f, p.z); cc.radius = r; cc.height = h; }
                }

                var comp = c.root.AddComponent<CityChunk>();
                comp.index = c.index; comp.center = c.bounds.center; comp.radius = Mathf.Max(60f, new Vector2(c.bounds.extents.x, c.bounds.extents.z).magnitude);
                comp.triangleCount = (int)Mathf.Min(int.MaxValue, c.tris); comp.buildingCount = c.buildingCount;
                comp.rendererCount = c.root.GetComponentsInChildren<Renderer>(true).Length;
                foreach (Transform t in c.root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);

                // Each chunk becomes its own scene so it can be streamed in and out at runtime.
                string path = $"{ChunkSceneDir}/Chunk_{c.index.x}_{c.index.y}.unity";
                c.root.transform.SetParent(null, true);
                var chunkScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.MoveGameObjectToScene(c.root, chunkScene);
                EditorSceneManager.SaveScene(chunkScene, path);
                EditorSceneManager.CloseScene(chunkScene, true);
                EditorSceneManager.SetActiveScene(mainScene);
                result.Add(new ChunkInfo { scenePath = path, index = c.index, center = comp.center, radius = comp.radius, triangleCount = comp.triangleCount, buildingCount = comp.buildingCount });
            }

            return result.ToArray();
        }

        private static Material TryMat(string key)
        {
            try { return wm[key]; }
            catch (KeyNotFoundException)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{CityAssetFactory.MaterialsDir}/{key}.mat");
                if (m == null) throw;
                return m;
            }
        }

        private static void SetupStreamer(UnityEngine.SceneManagement.Scene scene, ChunkInfo[] list)
        {
            var systems = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Systems") ?? new GameObject("Systems");
            Transform t = systems.transform.Find("Streaming");
            if (t == null) { t = new GameObject("Streaming").transform; t.SetParent(systems.transform, false); }
            var streamer = t.GetComponent<ChunkStreamer>() ?? t.gameObject.AddComponent<ChunkStreamer>();
            streamer.SetChunks(list);
            ApplyStreamerSettings(streamer);
            EditorUtility.SetDirty(streamer);

            var spawner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerSpawner>(true)).FirstOrDefault();
            if (spawner != null)
            {
                var so = new SerializedObject(spawner);
                var p = so.FindProperty("chunkStreamer");
                if (p != null) { p.objectReferenceValue = streamer; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        private static void ApplyStreamerSettings(ChunkStreamer streamer)
        {
            var so = new SerializedObject(streamer);
            so.FindProperty("loadDistance").floatValue = 230f;
            so.FindProperty("hysteresis").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Re-applies the streaming distances without rebuilding the city.</summary>
        [BridgeCommand]
        public static void ConfigureStreamer()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var s = UnityEngine.Object.FindAnyObjectByType<ChunkStreamer>();
            if (s == null) { Debug.LogError("[World] No ChunkStreamer in the scene."); return; }
            ApplyStreamerSettings(s);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        private static void UpdateBuildSettings(ChunkInfo[] infos)
        {
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string p in new[] { "Assets/_Project/Scenes/MainMenu/MainMenu.unity", CityScenePath, "Assets/_Project/Scenes/Garage/Garage.unity" })
                scenes.Add(new EditorBuildSettingsScene(p, true));
            foreach (var i in infos) scenes.Add(new EditorBuildSettingsScene(i.scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void Report(ChunkInfo[] list, long ms)
        {
            var sb = new StringBuilder("[World] BUILD REPORT" + Environment.NewLine);
            sb.AppendLine($"  time {ms / 1000.0:F1} s | chunk scenes {list.Length} | buildings {list.Sum(c => c.buildingCount)} | triangles (all chunks) {list.Sum(c => (long)c.triangleCount):N0}");
            sb.AppendLine($"  busiest chunk: {list.Max(c => c.triangleCount):N0} tris, {list.Max(c => c.buildingCount)} buildings");
            sb.AppendLine($"  prefab variants: {BuildingKit.All.Count} buildings ({BuildingKit.All.Select(b => b.archetype).Distinct().Count()} archetypes), {TreeKit.Species.Count} tree species");
            Debug.Log(sb.ToString());
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "world_build.txt"), sb.ToString());
        }
    }
}
