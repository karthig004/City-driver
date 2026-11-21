using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Builds the 8 car body prefabs (Resources/CarBodies/Body_{id}.prefab; the game swaps the right one onto the player's physics frame at
    /// runtime) and renders a showroom picture of each so the result can be checked without playing.
    /// </summary>
    internal static class CarModels
    {
        public const string BodyDir = "Assets/_Project/Resources/CarBodies";
        private const float WheelRadius = 0.33f, WheelWidth = 0.22f, TrackHalf = 0.84f, WheelBaseHalf = 1.36f;

        private static CarKit.Mats MakeMats()
        {
            return new CarKit.Mats
            {
                paint = CityAssetFactory.Lit("M_CarPaint", new Color(0.96f, 0.74f, 0.05f), null, 0.88f, 0.35f),
                glass = CityAssetFactory.Lit("M_CarGlass", new Color(0.05f, 0.07f, 0.10f), null, 0.9f, 0.1f),
                trim = CityAssetFactory.Lit("M_CarTrim", new Color(0.07f, 0.07f, 0.08f), null, 0.3f),
                tyre = CityAssetFactory.Lit("M_CarTyre", new Color(0.11f, 0.11f, 0.12f), null, 0.35f),
                rim = CityAssetFactory.Lit("M_CarRim", new Color(0.72f, 0.72f, 0.75f), null, 0.8f, 0.8f),
                rimDark = CityAssetFactory.Lit("M_CarRimDark", new Color(0.30f, 0.31f, 0.36f), null, 0.65f, 0.45f),
                disc = CityAssetFactory.Lit("M_CarDisc", new Color(0.52f, 0.52f, 0.55f), null, 0.5f, 0.5f),
                chrome = CityAssetFactory.Lit("M_CarChrome", new Color(0.86f, 0.87f, 0.91f), null, 0.8f, 0.6f),
                plate = CityAssetFactory.Lit("M_CarPlate", new Color(0.93f, 0.93f, 0.9f), null, 0.3f),
                head = CityAssetFactory.Lit("M_CarHeadlight", new Color(1f, 1f, 0.95f), null, 0.5f, 0f, new Color(1.4f, 1.4f, 1.3f)),
                tail = CityAssetFactory.Lit("M_CarTaillight", new Color(0.8f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(0.35f, 0.02f, 0.02f)),
                accent = CityAssetFactory.Lit("M_CarAccent", new Color(0.05f, 0.16f, 0.52f), null, 0.7f, 0.3f),
                barA = CityAssetFactory.Lit("M_CarBarRedOn", new Color(0.9f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(4f, 0.1f, 0.1f)),
                barB = CityAssetFactory.Lit("M_CarBarBlueOff", new Color(0.05f, 0.1f, 0.45f), null, 0.5f, 0f, new Color(0.02f, 0.04f, 0.2f)),
            };
        }

        private static IEnumerable<CarShape> AllShapes() { foreach (var s in CarShapes.All) yield return s; foreach (var s in CarShapes.Extra) yield return s; }

        [BridgeCommand]
        public static void BuildBodies()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Cars] Exit Play Mode first."); return; }
            CityAssetFactory.EnsureFolder(BodyDir);
            var mats = MakeMats();
            var roofSign = CityAssetFactory.Lit("M_CarRoofSign", new Color(1f, 0.95f, 0.7f), null, 0.4f, 0f, new Color(1.2f, 1.1f, 0.5f));
            foreach (var shape in AllShapes())
            {
                var root = new GameObject("Body");
                CarKit.AddBody(root.transform, mats, shape);
                var sign = root.transform.Find("RoofSign"); if (sign != null) sign.GetComponent<Renderer>().sharedMaterial = roofSign;
                PrefabUtility.SaveAsPrefabAsset(root, $"{BodyDir}/Body_{shape.id}.prefab");
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[Cars] Built {CarShapes.All.Count} body prefabs in {BodyDir}.");
        }

        /// <summary>Renders each car (3/4 front and side) to Bridge/showroom_{id}_a.png / _b.png using a throw-away camera and lights.</summary>
        [BridgeCommand]
        public static void RenderShowroom()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Cars] Exit Play Mode first."); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            var mats = MakeMats();
            var tmp = new GameObject("ShowroomTmp");
            tmp.transform.position = new Vector3(0, 1000, 0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.SetParent(tmp.transform, false); ground.transform.localScale = new Vector3(3, 1, 3);
            ground.GetComponent<Renderer>().sharedMaterial = CityAssetFactory.Lit("M_ShowroomFloor", new Color(0.55f, 0.56f, 0.6f), null, 0.5f);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(tmp.transform, false); sun.type = LightType.Directional; sun.intensity = 2.2f; sun.color = new Color(1f, 0.95f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(tmp.transform, false); fill.type = LightType.Directional; fill.intensity = 0.7f; fill.color = new Color(0.7f, 0.8f, 1f);
            fill.transform.rotation = Quaternion.Euler(30f, 140f, 0f);
            var camGo = new GameObject("Cam"); camGo.transform.SetParent(tmp.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.78f, 0.82f, 0.9f); cam.fieldOfView = 34f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var oldAmbient = RenderSettings.ambientMode; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.64f);
            foreach (var shape in AllShapes())
            {
                var car = new GameObject("Car"); car.transform.SetParent(tmp.transform, false);
                var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BodyDir}/Body_{shape.id}.prefab");
                if (bodyPrefab == null) { Debug.LogError("[Cars] Missing body prefab for " + shape.id + " (run BuildBodies first)."); continue; }
                var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, car.transform);
                foreach (float z in new[] { WheelBaseHalf, -WheelBaseHalf })
                    foreach (float x in new[] { -TrackHalf, TrackHalf })
                    {
                        var pivot = new GameObject("W").transform; pivot.SetParent(car.transform, false); pivot.localPosition = new Vector3(x, WheelRadius, z);
                        CarKit.AddWheel(pivot, WheelRadius, WheelWidth, Mathf.Sign(x), mats);
                    }
                if (ColorUtility.TryParseHtmlString(shape.paintHex, out Color c))
                {
                    var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", c);
                    foreach (var r in car.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].name == "M_CarPaint") r.SetPropertyBlock(block, i); }
                }
                Vector3 centre = tmp.transform.position + Vector3.up * 0.7f;
                foreach (var view in new[] { ("a", new Vector3(5.6f, 1.7f, 6.0f)), ("b", new Vector3(8.2f, 1.1f, 0.3f)), ("c", new Vector3(-5.2f, 1.6f, -6.2f)) })
                {
                    cam.transform.position = tmp.transform.position + view.Item2; cam.transform.LookAt(centre);
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0, false); tex.Apply(false); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, $"showroom_{shape.id}_{view.Item1}.png"), tex.EncodeToPNG());
                }
                Object.DestroyImmediate(car);
            }
            RenderSettings.ambientMode = oldAmbient;
            cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(tmp);
            Debug.Log("[Cars] Showroom pictures written to Bridge/showroom_*.png");
        }
    }
}
