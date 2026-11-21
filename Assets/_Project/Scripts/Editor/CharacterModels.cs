using System.Collections.Generic;
using System.IO;
using CityDriver.Characters;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>The roster: 6 story characters, 7 passenger roles x 3, 4 police roles x 3, 4 gang roles x 3, 3 race rivals (54 looks) as prefabs in Resources/Characters.</summary>
    internal static class CharacterModels
    {
        public const string PrefabDir = "Assets/_Project/Resources/Characters";
        internal sealed class Preset { public string id, group; public CharacterLook look; }
        public static readonly List<Preset> All = new List<Preset>();

        private static CharacterLook L(string role, int skin, string hair, int hairStyle, int facial, string top, string bottom, string accent, int sleeves, string shoes, params string[] extras)
        {
            var l = new CharacterLook { role = role, skin = CharacterLook.SkinTones[skin], hair = hair, hairStyle = hairStyle, facial = facial, top = top, bottom = bottom, accent = accent, sleeves = sleeves, shoes = shoes };
            l.extras.AddRange(extras); return l;
        }

        private static void P(string id, string group, CharacterLook look) { All.Add(new Preset { id = id, group = group, look = look }); }

        private static void Roster()
        {
            All.Clear();
            // ---- story / hero characters
            P("driver", "story", L("The Driver", 2, "#0E0B09", 1, 1, "#E9E9E6", "#2B3A55", "#5B3A29", 1, "#15161A", "Jacket", "Chain"));
            P("mysterious_passenger", "story", L("The Mysterious Passenger", 1, "#0E0B09", 3, 0, "#2A1B3C", "#1E2128", "#3A2A5C", 1, "#15161A", "Hood"));
            P("police_commissioner", "story", L("Police Commissioner", 3, "#B7B7BA", 1, 2, "#C3A97A", "#7A6A48", "#1B2A4A", 1, "#15161A", "Cap", "Sunglasses", "Badge", "Belt", "Pistol"));
            P("gang_lord", "story", L("The Gang Lord", 3, "#0E0B09", 2, 3, "#111111", "#1E2128", "#6B1A2B", 1, "#15161A", "Jacket", "Chain"));
            P("rival_racer", "story", L("The Rival Racer", 2, "#0E0B09", 2, 0, "#B3141A", "#1E2128", "#B3141A", 0, "#E9E9E6", "Bandana"));
            P("city_tycoon", "story", L("The City Tycoon", 1, "#B7B7BA", 2, 3, "#E9E9E6", "#E9E9E6", "#E9E9E6", 1, "#E9E9E6", "Jacket", "Sunglasses"));

            // ---- passengers (3 looks per role)
            string[] roles = { "office_commuter", "college_student", "tourist", "businessman", "hospital_visitor", "club_guest", "emergency_patient" };
            for (int i = 0; i < 3; i++)
            {
                int[] sk = { 2 - i % 2, 1 + i, 3 - i, 0 + i * 2 % 5, 2 + i % 3, 1 + i, 3 - i };
                P(roles[0] + "_" + (i + 1), "passenger", L("Office Commuter", sk[0], i == 1 ? "#5A3A22" : "#0E0B09", i == 1 ? 3 : 2, i == 2 ? 1 : 0, new[] { "#8FB4D9", "#E9E9E6", "#C9A8E6" }[i], i == 1 ? "#5B3A29" : "#2B2F3A", "#2B3A55", 1, "#15161A", i == 1 ? new[] { "Dress" } : new[] { "Tie", "Glasses" }));
                P(roles[1] + "_" + (i + 1), "passenger", L("College Student", sk[1], new[] { "#0E0B09", "#2A1B12", "#8B1A1A" }[i], new[] { 5, 3, 4 }[i], 0, new[] { "#2F6B45", "#3A2A5C", "#C9A227" }[i], "#2B3A55", "#E9E9E6", 1, "#E9E9E6", "Backpack"));
                P(roles[2] + "_" + (i + 1), "passenger", L("Tourist", sk[2], new[] { "#8A6A3A", "#0E0B09", "#B7B7BA" }[i], new[] { 1, 2, 4 }[i], i == 2 ? 2 : 0, new[] { "#F0D060", "#E07A1F", "#4DA6C8" }[i], "#C3A97A", "#C9A227", 0, "#5B3A29", "Cap", "Sunglasses"));
                P(roles[3] + "_" + (i + 1), "passenger", L("Businessman", sk[3], new[] { "#0E0B09", "#B7B7BA", "#2A1B12" }[i], 2, new[] { 0, 3, 2 }[i], "#E9E9E6", new[] { "#1E2128", "#2B3A55", "#3A3A3A" }[i], new[] { "#1E2128", "#2B3A55", "#3A3A3A" }[i], 1, "#15161A", "Jacket", "Tie", "Glasses"));
                P(roles[4] + "_" + (i + 1), "passenger", L("Hospital Visitor", sk[4], new[] { "#2A1B12", "#0E0B09", "#8A6A3A" }[i], new[] { 3, 1, 4 }[i], 0, new[] { "#E9E9E6", "#8FB4D9", "#C9E2C0" }[i], "#2B3A55", "#8B1A1A", 1, "#E9E9E6"));
                P(roles[5] + "_" + (i + 1), "passenger", L("Club Guest", sk[5], new[] { "#0E0B09", "#5A3A22", "#8B1A1A" }[i], new[] { 3, 4, 2 }[i], 0, "#111111", "#111111", new[] { "#B3141A", "#3A2A5C", "#C9A227" }[i], 0, "#15161A", i == 2 ? new[] { "Jacket", "Chain" } : new[] { "Dress", "Chain" }));
                P(roles[6] + "_" + (i + 1), "passenger", L("Emergency Patient", sk[6], new[] { "#0E0B09", "#B7B7BA", "#2A1B12" }[i], new[] { 5, 1, 2 }[i], i == 1 ? 1 : 0, "#CFE3EE", "#CFE3EE", "#E9E9E6", 0, "#E9E9E6", "Bandage"));
            }

            // ---- police (3 looks per role)
            for (int i = 0; i < 3; i++)
            {
                int s = 1 + i * 1 % 4;
                P("police_patrol_" + (i + 1), "police", L("Patrol Officer", s, "#0E0B09", 5, new[] { 0, 2, 1 }[i], "#1B2A4A", "#141B2B", "#1B2A4A", 1, "#0A0A0A", "Cap", "Badge", "Belt", "Pistol"));
                P("police_chase_" + (i + 1), "police", L("Chase Unit", s, "#0E0B09", 5, new[] { 1, 0, 2 }[i], "#1B2A4A", "#141B2B", "#2A2D34", 1, "#0A0A0A", "Helmet", "Visor", "Vest", "Badge", "Belt", "Pistol"));
                P("police_swat_" + (i + 1), "police", L("SWAT Team", 2 + i % 2, "#0E0B09", 5, 0, "#15161A", "#15161A", "#222428", 1, "#0A0A0A", "Helmet", "Visor", "Vest", "Belt", "Pistol"));
                P("police_roadblock_" + (i + 1), "police", L("Roadblock Unit", s, "#0E0B09", 5, new[] { 0, 1, 0 }[i], "#1B2A4A", "#141B2B", "#C8E21E", 1, "#0A0A0A", "Cap", "Vest", "Belt"));
            }

            // ---- gang crew (3 looks per role)
            for (int i = 0; i < 3; i++)
            {
                int s = 2 + i % 3;
                P("gang_enforcer_" + (i + 1), "gang", L("Gang Enforcer", s, "#0E0B09", new[] { 5, 0, 5 }[i], new[] { 3, 1, 2 }[i], "#111111", "#2B2F3A", "#6B1A2B", 0, "#15161A", "Bandana", "Chain"));
                P("gang_biker_" + (i + 1), "gang", L("Biker Gang", s, "#2A1B12", new[] { 3, 1, 4 }[i], new[] { 3, 3, 1 }[i], "#15161A", "#2B3A55", "#15161A", 1, "#0A0A0A", "Jacket", "Sunglasses"));
                P("gang_armed_" + (i + 1), "gang", L("Armed Crew", s, "#0E0B09", 5, new[] { 1, 0, 2 }[i], "#2F3A2F", "#1E2128", "#2F3A2F", 1, "#15161A", "Hood", "Pistol"));
                P("gang_driver_" + (i + 1), "gang", L("Gang Driver", s, "#0E0B09", 2, new[] { 0, 2, 1 }[i], "#E9E9E6", "#1E2128", "#4A2A2A", 1, "#15161A", "Jacket", "Sunglasses", "Chain"));
            }

            // ---- race rivals
            P("street_racer", "racer", L("Street Racer", 2, "#0E0B09", 2, 0, "#F0D060", "#1E2128", "#F0D060", 1, "#E9E9E6", "Jacket"));
            P("track_king", "racer", L("Track King", 3, "#0E0B09", 5, 2, "#15161A", "#15161A", "#15161A", 1, "#15161A", "Jacket", "Sunglasses"));
            P("speed_queen", "racer", L("Speed Queen", 1, "#2A1B12", 3, 0, "#B3141A", "#B3141A", "#B3141A", 1, "#15161A", "Jacket"));

            // ---- women: same roles, female body (hair 6 long wavy, 7 ponytail, 8 bob)
            P("driver_f", "story", L("The Driver", 2, "#0E0B09", 7, 0, "#E9E9E6", "#2B3A55", "#5B3A29", 1, "#15161A", "Jacket", "Chain"));
            P("police_commissioner_f", "story", L("Police Commissioner", 2, "#2A1B12", 8, 0, "#C3A97A", "#7A6A48", "#1B2A4A", 1, "#15161A", "Cap", "Badge", "Belt", "Pistol", "Epaulettes", "Collar", "Pockets"));
            foreach (var (id, hs, lips) in new[] { ("mysterious_passenger", 6, "#7A2A3A"), ("office_commuter_2", 8, "#A8454B"), ("college_student_2", 7, "#B5524F"), ("hospital_visitor_1", 6, "#B5524F"), ("hospital_visitor_3", 7, "#C06060"),
                                                  ("club_guest_1", 6, "#9A1F2E"), ("club_guest_2", 6, "#B3141A"), ("emergency_patient_2", 8, "#C06060"), ("tourist_2", 7, "#B5524F"),
                                                  ("police_patrol_2", 7, "#A8454B"), ("police_chase_3", 7, "#A8454B"), ("police_swat_2", 7, "#A8454B"), ("police_roadblock_2", 7, "#A8454B"),
                                                  ("gang_biker_2", 6, "#6B1A2B"), ("gang_enforcer_3", 7, "#6B1A2B"), ("speed_queen", 6, "#B3141A") })
            {
                var p = All.Find(x => x.id == id); if (p == null) continue;
                p.look.female = true; p.look.hairStyle = hs; p.look.lips = lips; p.look.facial = 0; p.look.height = 0.93f;
            }
            foreach (var p in All)                                  // uniforms get collars, pockets and shoulder boards
                if (p.group == "police") { p.look.extras.Add("Collar"); p.look.extras.Add("Pockets"); if (!p.look.extras.Contains("Helmet")) p.look.extras.Add("Epaulettes"); }
        }

        [BridgeCommand]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Characters] Exit Play Mode first."); return; }
            Roster();
            CityAssetFactory.EnsureFolder(PrefabDir);
            foreach (var p in All)
            {
                var go = CharacterKit.Build(p.id, p.look);
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Char_{p.id}.prefab");
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[Characters] Built {All.Count} character prefabs in {PrefabDir}.");
        }

        [BridgeCommand]
        public static void RenderDriver()
        {
            Roster();
            var one = new List<Preset> { All[0] };
            RenderRow("driver", one);
        }

        /// <summary>Renders contact sheets of the roster (front view, rest pose) to Bridge/showroom_char_*.png.</summary>
        [BridgeCommand]
        public static void RenderSheets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Characters] Exit Play Mode first."); return; }
            Roster();
            var groups = new Dictionary<string, List<Preset>>();
            foreach (var p in All) { if (!groups.TryGetValue(p.group, out var l)) groups[p.group] = l = new List<Preset>(); l.Add(p); }
            RenderRow("story", groups["story"]);
            for (int r = 0; r < 3; r++) { var row = new List<Preset>(); foreach (var p in groups["passenger"]) if (p.id.EndsWith("_" + (r + 1))) row.Add(p); RenderRow("passenger" + (r + 1), row); }
            RenderRow("police", groups["police"]); RenderRow("gang", groups["gang"]); RenderRow("racer", groups["racer"]);
            Debug.Log("[Characters] Sheets written to Bridge/showroom_char_*.png");
        }

        private static void RenderRow(string name, List<Preset> items)
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            const int W = 2000, H = 560; const float spacing = 1.05f;
            foreach (var stale in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include)) if (stale != null && stale.name == "CharSheetTmp") Object.DestroyImmediate(stale);
            var tmp = new GameObject("CharSheetTmp"); tmp.transform.position = new Vector3(0f, 2000f, 0f);
            float width = items.Count * spacing;
            for (int i = 0; i < items.Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Char_{items[i].id}.prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tmp.transform);
                go.transform.localPosition = new Vector3(-width * 0.5f + spacing * (i + 0.5f), 0f, 0f);
                go.GetComponent<CharacterCustomizer>().Apply(items[i].look);
            }
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(tmp.transform, false); sun.type = LightType.Directional; sun.intensity = 2.4f; sun.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(tmp.transform, false); fill.type = LightType.Directional; fill.intensity = 0.8f; fill.color = new Color(0.7f, 0.8f, 1f); fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.transform.SetParent(tmp.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.18f, 0.24f); cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f;
            float aspect = W / (float)H; float dist = Mathf.Max(5f, (width * 0.5f + 0.4f) / (Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect));
            cam.transform.localPosition = new Vector3(0f, 0.95f, dist); cam.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; rt.Create(); cam.targetTexture = rt;
            var old = RenderSettings.ambientMode; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.64f);
            cam.Render();
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, $"showroom_char_{name}.png"), tex.EncodeToPNG());
            RenderSettings.ambientMode = old; cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(tmp);
        }
    }
}
