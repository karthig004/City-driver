using System;
using System.Collections.Generic;
using CityDriver.Characters;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Builds the customisable human character: a jointed rigid-part rig (hips, torso, head, shoulders, elbows, hips, knees) made of lofted
    /// tubes and ellipsoids with a face, hair styles, facial hair and accessories. All characters share the same meshes; the look (skin, hair,
    /// outfit colours, extras) is applied at runtime by <see cref="CharacterCustomizer"/>. Procedural, stylised, no third-party assets.
    /// </summary>
    internal static class CharacterKit
    {
        public const string MeshDir = "Assets/_Project/Art/Characters/Meshes";
        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        private static Mesh cube;

        internal struct Ring { public float y, rx, rz, cx, cz; public Ring(float y, float rx, float rz, float cx = 0f, float cz = 0f) { this.y = y; this.rx = rx; this.rz = rz; this.cx = cx; this.cz = cz; } }

        internal sealed class CharMats { public Material skin, hair, top, bottom, shoes, accent, accent2, eye, gold, metal, glass, white, lips, iris; }

        public static CharMats MakeMats() => new CharMats
        {
            skin = CityAssetFactory.Lit("M_CharSkin", new Color(0.78f, 0.58f, 0.42f), null, 0.28f),
            hair = CityAssetFactory.Lit("M_CharHair", new Color(0.1f, 0.08f, 0.06f), null, 0.4f),
            top = CityAssetFactory.Lit("M_CharTop", new Color(0.17f, 0.23f, 0.33f), null, 0.15f),
            bottom = CityAssetFactory.Lit("M_CharBottom", new Color(0.12f, 0.13f, 0.16f), null, 0.15f),
            shoes = CityAssetFactory.Lit("M_CharShoes", new Color(0.08f, 0.08f, 0.09f), null, 0.3f),
            accent = CityAssetFactory.Lit("M_CharAccent", new Color(0.79f, 0.64f, 0.15f), null, 0.2f),
            accent2 = CityAssetFactory.Lit("M_CharAccent2", new Color(0.16f, 0.17f, 0.2f), null, 0.3f),
            eye = CityAssetFactory.Lit("M_CharEye", new Color(0.1f, 0.07f, 0.06f), null, 0.5f),
            gold = CityAssetFactory.Lit("M_CharGold", new Color(0.9f, 0.7f, 0.18f), null, 0.85f, 0.9f),
            metal = CityAssetFactory.Lit("M_CharMetal", new Color(0.1f, 0.1f, 0.12f), null, 0.6f, 0.7f),
            glass = CityAssetFactory.Lit("M_CharGlass", new Color(0.02f, 0.03f, 0.05f), null, 0.9f, 0.2f),
            white = CityAssetFactory.Lit("M_CharWhite", new Color(0.92f, 0.93f, 0.95f), null, 0.2f),
            lips = CityAssetFactory.Lit("M_CharLips", new Color(0.71f, 0.32f, 0.31f), null, 0.55f),
            iris = CityAssetFactory.Lit("M_CharIris", new Color(0.2f, 0.12f, 0.08f), null, 0.6f),
        };

        // ------------------------------------------------------------------------------------------ mesh helpers
        private static Mesh Cube() { if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"); return cube; }

        private static Mesh Tube(string name, Ring[] rings, int seg = 12, bool caps = true)
        {
            var v = new List<Vector3>(); var tris = new List<int>();
            foreach (var r in rings) for (int k = 0; k < seg; k++) { float a = Mathf.PI * 2f * k / seg; v.Add(new Vector3(r.cx + r.rx * Mathf.Cos(a), r.y, r.cz + r.rz * Mathf.Sin(a))); }
            void Tri(int a, int b, int c, Vector3 inside)
            {
                Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (n.sqrMagnitude < 1e-14f) return;
                if (Vector3.Dot(n, (v[a] + v[b] + v[c]) / 3f - inside) < 0f) { int t = b; b = c; c = t; }
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            for (int i = 0; i < rings.Length - 1; i++)
            {
                Vector3 inside = new Vector3((rings[i].cx + rings[i + 1].cx) * 0.5f, (rings[i].y + rings[i + 1].y) * 0.5f, (rings[i].cz + rings[i + 1].cz) * 0.5f);
                for (int k = 0; k < seg; k++)
                {
                    int a = i * seg + k, b = i * seg + (k + 1) % seg, c = (i + 1) * seg + k, d = (i + 1) * seg + (k + 1) % seg;
                    Tri(a, b, c, inside); Tri(b, d, c, inside);
                }
            }
            if (caps)
                foreach (int end in new[] { 0, rings.Length - 1 })
                {
                    var r = rings[end]; int centre = v.Count; v.Add(new Vector3(r.cx, r.y, r.cz));
                    Vector3 inside = end == 0 ? centre > 0 ? new Vector3(r.cx, r.y + 0.1f, r.cz) : Vector3.zero : new Vector3(r.cx, r.y - 0.1f, r.cz);
                    for (int k = 0; k < seg; k++) Tri(end * seg + k, end * seg + (k + 1) % seg, centre, inside);
                }
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh Ellipsoid(string name, float rx, float ry, float rz, Vector3 c, int rings = 8, int seg = 12)
        {
            var list = new List<Ring>();
            for (int i = 0; i <= rings; i++) { float th = Mathf.PI * i / rings; list.Add(new Ring(c.y - ry * Mathf.Cos(th), rx * Mathf.Sin(th), rz * Mathf.Sin(th), c.x, c.z)); }
            return Tube(name, list.ToArray(), seg, false);
        }

        private struct Part { public Mesh mesh; public Matrix4x4 m; }
        private static Part Box(Vector3 centre, Vector3 size, Vector3? euler = null) => new Part { mesh = Cube(), m = Matrix4x4.TRS(centre, Quaternion.Euler(euler ?? Vector3.zero), size) };
        private static Part Of(Mesh mesh) => new Part { mesh = mesh, m = Matrix4x4.identity };

        private static Mesh Combine(string name, params Part[] parts)
        {
            var ci = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].m };
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            m.CombineMeshes(ci, true, true); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh Shared(string key, Func<Mesh> build)
        {
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            CityAssetFactory.EnsureFolder(MeshDir);
            var made = build(); made.name = "Char_" + key;
            m = CityAssetFactory.SaveMesh(made, $"{MeshDir}/Char_{key}.asset");
            meshCache[key] = m; return m;
        }

        private static GameObject Joint(string name, Transform parent, Vector3 pos) { var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos; return g; }

        private static GameObject Add(string name, Transform parent, Mesh mesh, Material mat, Vector3? pos = null)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); if (pos.HasValue) g.transform.localPosition = pos.Value;
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat; return g;
        }

        // ------------------------------------------------------------------------------------------ the rig
        public static GameObject Build(string id, CharacterLook look)
        {
            var m = MakeMats(); bool f = look.female; string k = f ? "F_" : ""; float af = f ? 0.88f : 1f;
            var root = new GameObject("Char_" + id);
            var hips = Joint("Hips", root.transform, new Vector3(0f, 0.95f, 0f));

            Add("Pelvis", hips.transform, Shared(k + "Pelvis", () => f ? Ellipsoid("Pelvis", 0.185f, 0.125f, 0.125f, Vector3.zero, 7, 16) : Ellipsoid("Pelvis", 0.17f, 0.12f, 0.115f, Vector3.zero, 7, 12)), m.bottom);

            // torso (waist pivot)
            var torso = Joint("Torso", hips.transform, new Vector3(0f, 0.05f, 0f));
            Add("Chest", torso.transform, Shared(k + "Chest", () => f ? Tube("Chest", new[] { new Ring(-0.02f, 0.165f, 0.105f), new Ring(0.07f, 0.14f, 0.095f), new Ring(0.19f, 0.165f, 0.112f), new Ring(0.29f, 0.186f, 0.128f), new Ring(0.39f, 0.196f, 0.118f), new Ring(0.48f, 0.172f, 0.1f), new Ring(0.53f, 0.08f, 0.062f) }, 16) : Tube("Chest", new[] { new Ring(-0.02f, 0.165f, 0.105f), new Ring(0.10f, 0.172f, 0.108f), new Ring(0.26f, 0.205f, 0.122f), new Ring(0.40f, 0.222f, 0.125f), new Ring(0.49f, 0.19f, 0.108f), new Ring(0.53f, 0.09f, 0.07f) }, 14)), m.top);
            Add("Neck", torso.transform, Shared(k + "Neck", () => Tube("Neck", f ? new[] { new Ring(0.50f, 0.046f, 0.046f), new Ring(0.60f, 0.041f, 0.041f) } : new[] { new Ring(0.50f, 0.052f, 0.052f), new Ring(0.60f, 0.047f, 0.047f) }, 12)), m.skin);

            // head
            var head = Joint("Head", torso.transform, new Vector3(0f, f ? 0.625f : 0.635f, 0f)); head.transform.localScale = Vector3.one * (f ? 1.17f : 1.24f);
            Add("Skull", head.transform, Shared(k + "Skull", () => f ? Ellipsoid("Skull", 0.086f, 0.116f, 0.1f, new Vector3(0f, 0.09f, 0f), 10, 18) : Ellipsoid("Skull", 0.092f, 0.118f, 0.104f, new Vector3(0f, 0.09f, 0f), 10, 18)), m.skin);
            Add("Ears", head.transform, Shared("Ears", () => Combine("Ears", Box(new Vector3(0.094f, 0.085f, 0f), new Vector3(0.014f, 0.04f, 0.026f)), Box(new Vector3(-0.094f, 0.085f, 0f), new Vector3(0.014f, 0.04f, 0.026f)))), m.skin);
            Add("Nose", head.transform, Shared("Nose", () => Combine("Nose", Box(new Vector3(0f, 0.078f, 0.108f), new Vector3(0.022f, 0.036f, 0.028f), new Vector3(-14f, 0f, 0f)))), m.skin);
            Add("EyeWhites", head.transform, Shared("EyeWhites", () => Combine("EyeWhites", Of(Ellipsoid("EWL", 0.0155f, 0.0125f, 0.009f, new Vector3(-0.036f, 0.104f, 0.0925f), 5, 10)), Of(Ellipsoid("EWR", 0.0155f, 0.0125f, 0.009f, new Vector3(0.036f, 0.104f, 0.0925f), 5, 10)))), m.white);
            Add("Iris", head.transform, Shared("Iris", () => Combine("Iris", Of(Ellipsoid("IL", 0.0085f, 0.0085f, 0.004f, new Vector3(-0.036f, 0.104f, 0.1005f), 4, 10)), Of(Ellipsoid("IR", 0.0085f, 0.0085f, 0.004f, new Vector3(0.036f, 0.104f, 0.1005f), 4, 10)))), m.iris);
            Add("Pupils", head.transform, Shared("Pupils", () => Combine("Pupils", Of(Ellipsoid("PL", 0.0045f, 0.0045f, 0.003f, new Vector3(-0.036f, 0.104f, 0.1035f), 3, 8)), Of(Ellipsoid("PR", 0.0045f, 0.0045f, 0.003f, new Vector3(0.036f, 0.104f, 0.1035f), 3, 8)))), m.eye);
            Add("Lashes", head.transform, Shared(k + "Lashes", () => f
                ? Combine("Lashes", Box(new Vector3(-0.036f, 0.1175f, 0.097f), new Vector3(0.038f, 0.006f, 0.012f), new Vector3(0f, 0f, 5f)), Box(new Vector3(0.036f, 0.1175f, 0.097f), new Vector3(0.038f, 0.006f, 0.012f), new Vector3(0f, 0f, -5f)))
                : Combine("Lashes", Box(new Vector3(-0.036f, 0.1165f, 0.097f), new Vector3(0.032f, 0.003f, 0.01f)), Box(new Vector3(0.036f, 0.1165f, 0.097f), new Vector3(0.032f, 0.003f, 0.01f)))), m.eye);
            Add("Lips", head.transform, Shared(k + "Lips", () => Combine("Lips", Of(Ellipsoid("UL", f ? 0.021f : 0.019f, 0.005f, 0.008f, new Vector3(0f, 0.049f, 0.1f), 3, 10)), Of(Ellipsoid("LL", f ? 0.019f : 0.017f, f ? 0.0075f : 0.006f, 0.009f, new Vector3(0f, 0.039f, 0.0995f), 3, 10)))), m.lips);
            Add("MouthLine", head.transform, Shared("MouthLine", () => Combine("MouthLine", Box(new Vector3(0f, 0.0445f, 0.1035f), new Vector3(0.036f, 0.0025f, 0.004f)))), m.eye);
            Add("Brows", head.transform, Shared(k + "Brows", () => f
                ? Combine("Brows", Box(new Vector3(-0.036f, 0.128f, 0.095f), new Vector3(0.034f, 0.004f, 0.008f), new Vector3(0f, 0f, 10f)), Box(new Vector3(0.036f, 0.128f, 0.095f), new Vector3(0.034f, 0.004f, 0.008f), new Vector3(0f, 0f, -10f)))
                : Combine("Brows", Box(new Vector3(-0.036f, 0.125f, 0.096f), new Vector3(0.036f, 0.007f, 0.01f), new Vector3(0f, 0f, 6f)), Box(new Vector3(0.036f, 0.125f, 0.096f), new Vector3(0.036f, 0.007f, 0.01f), new Vector3(0f, 0f, -6f)))), m.hair);

            // hair styles (exactly one is shown)
            Add("Hair0", head.transform, Shared("Hair0", () => Combine("Hair0", Box(new Vector3(0f, -5f, 0f), Vector3.zero))), m.hair);
            Add("Hair1", head.transform, Shared("Hair1", () => HairCap(0.102f, 0.122f, 0.114f, 0.10f)), m.hair);
            Add("Hair2", head.transform, Shared("Hair2", () => Combine("Hair2", Of(HairCap(0.102f, 0.122f, 0.114f, 0.10f)), Box(new Vector3(-0.045f, 0.19f, 0.07f), new Vector3(0.12f, 0.03f, 0.06f), new Vector3(0f, 0f, 14f)))), m.hair);
            Add("Hair3", head.transform, Shared("Hair3", () => Combine("Hair3", Of(HairCap(0.104f, 0.124f, 0.116f, 0.10f)), Box(new Vector3(0f, 0.03f, -0.085f), new Vector3(0.2f, 0.30f, 0.05f)), Box(new Vector3(0.1f, 0.06f, -0.02f), new Vector3(0.025f, 0.22f, 0.12f)), Box(new Vector3(-0.1f, 0.06f, -0.02f), new Vector3(0.025f, 0.22f, 0.12f)))), m.hair);
            Add("Hair4", head.transform, Shared("Hair4", () => Combine("Hair4", Of(HairCap(0.102f, 0.122f, 0.114f, 0.10f)), Of(Ellipsoid("Bun", 0.05f, 0.05f, 0.05f, new Vector3(0f, 0.235f, -0.06f), 5, 10)))), m.hair);
            Add("Hair5", head.transform, Shared("Hair5", () => HairCap(0.096f, 0.119f, 0.108f, 0.11f)), m.hair);

            Add("Hair6", head.transform, Shared("Hair6", () => Combine("Hair6", Of(HairCap(0.106f, 0.126f, 0.118f, 0.095f)), Box(new Vector3(0f, -0.02f, -0.092f), new Vector3(0.22f, 0.40f, 0.07f)), Box(new Vector3(0.105f, -0.01f, -0.03f), new Vector3(0.03f, 0.34f, 0.13f)), Box(new Vector3(-0.105f, -0.01f, -0.03f), new Vector3(0.03f, 0.34f, 0.13f)), Of(Ellipsoid("Waves", 0.115f, 0.1f, 0.06f, new Vector3(0f, -0.19f, -0.09f), 5, 12)))), m.hair);
            Add("Hair7", head.transform, Shared("Hair7", () => Combine("Hair7", Of(HairCap(0.104f, 0.124f, 0.116f, 0.095f)), Of(Tube("Ponytail", new[] { new Ring(0.14f, 0.04f, 0.04f, 0f, -0.115f), new Ring(0.04f, 0.048f, 0.048f, 0f, -0.15f), new Ring(-0.1f, 0.05f, 0.05f, 0f, -0.16f), new Ring(-0.24f, 0.034f, 0.034f, 0f, -0.15f), new Ring(-0.3f, 0.008f, 0.008f, 0f, -0.145f) }, 10)))), m.hair);
            Add("Hair8", head.transform, Shared("Hair8", () => Combine("Hair8", Of(HairCap(0.106f, 0.126f, 0.118f, 0.095f)), Of(Tube("BobShell", new[] { new Ring(0.15f, 0.114f, 0.124f, 0f, -0.012f), new Ring(0.07f, 0.126f, 0.134f, 0f, -0.012f), new Ring(-0.005f, 0.122f, 0.128f, 0f, -0.02f), new Ring(-0.03f, 0.1f, 0.105f, 0f, -0.03f) }, 16, false)))), m.hair);

            // facial hair
            Add("Facial1", head.transform, Shared("Facial1", () => Tube("Stubble", new[] { new Ring(0.015f, 0.058f, 0.07f, 0f, 0.02f), new Ring(0.05f, 0.092f, 0.102f, 0f, 0.003f), new Ring(0.085f, 0.094f, 0.104f), }, 14, false)), m.hair);
            Add("Facial2", head.transform, Shared("Facial2", () => Combine("Moustache", Box(new Vector3(0f, 0.062f, 0.104f), new Vector3(0.07f, 0.014f, 0.022f)))), m.hair);
            Add("Facial3", head.transform, Shared("Facial3", () => Combine("Beard", Of(Tube("BeardShell", new[] { new Ring(-0.015f, 0.052f, 0.07f, 0f, 0.025f), new Ring(0.03f, 0.1f, 0.112f, 0f, 0.0f), new Ring(0.08f, 0.097f, 0.108f) }, 14, false)), Box(new Vector3(0f, 0.062f, 0.108f), new Vector3(0.075f, 0.014f, 0.022f)))), m.hair);

            // head accessories
            var hx = Joint("Extras", head.transform, Vector3.zero);
            Add("Cap", hx.transform, Shared("Cap", () => Combine("Cap", Of(HairCap(0.108f, 0.128f, 0.12f, 0.115f)), Box(new Vector3(0f, 0.12f, 0.12f), new Vector3(0.18f, 0.012f, 0.09f), new Vector3(8f, 0f, 0f)))), m.accent);
            Add("Helmet", hx.transform, Shared("Helmet", () => Combine("Helmet", Of(HairCap(0.116f, 0.14f, 0.128f, 0.06f)), Box(new Vector3(0f, 0.04f, -0.01f), new Vector3(0.234f, 0.03f, 0.25f)))), m.accent2);
            Add("Visor", hx.transform, Shared("Visor", () => Combine("Visor", Box(new Vector3(0f, 0.105f, 0.108f), new Vector3(0.2f, 0.05f, 0.03f)))), m.glass);
            Add("Glasses", hx.transform, Shared("Glasses", () => Combine("Glasses", Box(new Vector3(-0.036f, 0.104f, 0.108f), new Vector3(0.05f, 0.032f, 0.008f)), Box(new Vector3(0.036f, 0.104f, 0.108f), new Vector3(0.05f, 0.032f, 0.008f)), Box(new Vector3(0f, 0.108f, 0.108f), new Vector3(0.02f, 0.006f, 0.008f)))), m.metal);
            Add("Sunglasses", hx.transform, Shared("Sunglasses", () => Combine("Sunglasses", Box(new Vector3(-0.04f, 0.103f, 0.108f), new Vector3(0.062f, 0.04f, 0.012f)), Box(new Vector3(0.04f, 0.103f, 0.108f), new Vector3(0.062f, 0.04f, 0.012f)), Box(new Vector3(0f, 0.108f, 0.108f), new Vector3(0.02f, 0.008f, 0.01f)))), m.glass);
            Add("Bandana", hx.transform, Shared("Bandana", () => Tube("Bandana", new[] { new Ring(0.14f, 0.104f, 0.116f, 0f, 0f), new Ring(0.17f, 0.106f, 0.118f) }, 14, false)), m.accent);
            Add("Bandage", hx.transform, Shared("Bandage", () => Tube("Bandage", new[] { new Ring(0.115f, 0.1f, 0.113f), new Ring(0.185f, 0.104f, 0.117f) }, 14, false)), m.white);
            Add("Hood", hx.transform, Shared("Hood", () => Combine("Hood", Of(HairCap(0.12f, 0.145f, 0.135f, 0.03f)), Box(new Vector3(0f, 0.0f, -0.11f), new Vector3(0.25f, 0.2f, 0.06f)), Box(new Vector3(0.12f, 0.0f, 0f), new Vector3(0.025f, 0.12f, 0.2f)), Box(new Vector3(-0.12f, 0.0f, 0f), new Vector3(0.025f, 0.12f, 0.2f)))), m.accent);

            // torso accessories
            var tx = Joint("Extras", torso.transform, Vector3.zero);
            Add("Jacket", tx.transform, Shared(k + "Jacket", () => f ? Tube("Jacket", new[] { new Ring(-0.12f, 0.175f, 0.12f), new Ring(0.08f, 0.15f, 0.108f), new Ring(0.20f, 0.18f, 0.125f), new Ring(0.30f, 0.205f, 0.14f), new Ring(0.40f, 0.214f, 0.135f), new Ring(0.49f, 0.182f, 0.11f), new Ring(0.53f, 0.09f, 0.075f) }, 16, true) : Tube("Jacket", new[] { new Ring(-0.12f, 0.19f, 0.125f), new Ring(0.10f, 0.19f, 0.122f), new Ring(0.26f, 0.222f, 0.14f), new Ring(0.40f, 0.238f, 0.142f), new Ring(0.49f, 0.2f, 0.12f), new Ring(0.53f, 0.1f, 0.08f) }, 14, true)), m.accent);
            Add("Vest", tx.transform, Shared(k + "Vest", () => f ? Tube("Vest", new[] { new Ring(0.0f, 0.16f, 0.11f), new Ring(0.16f, 0.17f, 0.12f), new Ring(0.30f, 0.2f, 0.138f), new Ring(0.44f, 0.208f, 0.128f), new Ring(0.48f, 0.145f, 0.1f) }, 16, false) : Tube("Vest", new[] { new Ring(0.0f, 0.18f, 0.12f), new Ring(0.26f, 0.218f, 0.138f), new Ring(0.44f, 0.222f, 0.132f), new Ring(0.48f, 0.15f, 0.1f) }, 14, false)), m.accent);
            Add("Backpack", tx.transform, Shared("Backpack", () => Combine("Backpack", Box(new Vector3(0f, 0.26f, -0.17f), new Vector3(0.30f, 0.36f, 0.14f)), Box(new Vector3(0.1f, 0.3f, 0.0f), new Vector3(0.04f, 0.3f, 0.26f)), Box(new Vector3(-0.1f, 0.3f, 0.0f), new Vector3(0.04f, 0.3f, 0.26f)))), m.accent2);
            Add("Belt", tx.transform, Shared("Belt", () => Combine("Belt", Of(Tube("BeltBand", new[] { new Ring(0.0f, 0.172f, 0.112f), new Ring(0.05f, 0.172f, 0.112f) }, 14, false)), Box(new Vector3(0.14f, -0.02f, 0.02f), new Vector3(0.06f, 0.12f, 0.09f)), Box(new Vector3(0f, 0.025f, 0.113f), new Vector3(0.05f, 0.04f, 0.01f)))), m.metal);
            Add("Tie", tx.transform, Shared("Tie", () => Combine("Tie", Box(new Vector3(0f, 0.37f, 0.126f), new Vector3(0.04f, 0.2f, 0.01f)), Box(new Vector3(0f, 0.47f, 0.12f), new Vector3(0.06f, 0.04f, 0.015f)))), m.accent);
            Add("Badge", tx.transform, Shared("Badge", () => Combine("Badge", Box(new Vector3(0.09f, 0.34f, 0.128f), new Vector3(0.05f, 0.06f, 0.008f)))), m.gold);
            Add("Chain", tx.transform, Shared("Chain", () => Tube("Chain", new[] { new Ring(0.46f, 0.085f, 0.095f, 0f, 0.015f), new Ring(0.475f, 0.085f, 0.095f, 0f, 0.015f) }, 14, false)), m.gold);
            Add("Dress", tx.transform, Shared("Dress", () => Tube("Dress", new[] { new Ring(-0.02f, 0.18f, 0.12f), new Ring(-0.2f, 0.25f, 0.18f), new Ring(-0.42f, 0.29f, 0.21f) }, 14, false)), m.accent);

            Add("Epaulettes", tx.transform, Shared("Epaulettes", () => Combine("Epaulettes", Box(new Vector3(0.215f, 0.505f, 0f), new Vector3(0.085f, 0.014f, 0.07f), new Vector3(0f, 0f, -12f)), Box(new Vector3(-0.215f, 0.505f, 0f), new Vector3(0.085f, 0.014f, 0.07f), new Vector3(0f, 0f, 12f)))), m.gold);
            Add("Collar", tx.transform, Shared("Collar", () => Tube("Collar", new[] { new Ring(0.47f, 0.088f, 0.082f, 0f, 0.005f), new Ring(0.535f, 0.07f, 0.066f) }, 14, false)), m.white);
            Add("Pockets", tx.transform, Shared("Pockets", () => Combine("Pockets", Box(new Vector3(0.085f, 0.30f, 0.128f), new Vector3(0.07f, 0.075f, 0.008f)), Box(new Vector3(-0.085f, 0.30f, 0.128f), new Vector3(0.07f, 0.075f, 0.008f)))), m.accent2);
            Add("Skirt", tx.transform, Shared("Skirt", () => Tube("Skirt", new[] { new Ring(0.07f, 0.145f, 0.10f), new Ring(-0.02f, 0.19f, 0.135f), new Ring(-0.22f, 0.215f, 0.155f), new Ring(-0.36f, 0.225f, 0.165f) }, 16, false)), m.bottom);

            // arms
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "L" : "R";
                var sh = Joint("Shoulder" + side, torso.transform, new Vector3(s * (f ? 0.232f : 0.248f), f ? 0.44f : 0.455f, 0f));
                Add("Shoulder", sh.transform, Shared("Shoulder", () => Ellipsoid("Shoulder", 0.066f, 0.066f, 0.066f, Vector3.zero, 5, 10)), m.top);
                Add("UpperArm", sh.transform, Shared(k + "UpperArm", () => Tube("UpperArm", new[] { new Ring(0f, 0.062f * af, 0.062f * af), new Ring(-0.05f, 0.059f * af, 0.059f * af), new Ring(-0.17f, 0.05f * af, 0.05f * af), new Ring(-0.29f, 0.044f * af, 0.044f * af) }, 12)), m.top);
                var el = Joint("Elbow" + side, sh.transform, new Vector3(0f, -0.29f, 0f));
                Add("ElbowCap", el.transform, Shared("ElbowCap", () => Ellipsoid("ElbowCap", 0.045f, 0.045f, 0.045f, Vector3.zero, 4, 8)), m.top);
                var longS = Joint("LongSleeves", el.transform, Vector3.zero);
                Add("Forearm", longS.transform, Shared(k + "ForearmSleeve", () => Tube("ForearmSleeve", new[] { new Ring(0f, 0.043f * af, 0.043f * af), new Ring(-0.12f, 0.038f * af, 0.038f * af), new Ring(-0.26f, 0.034f * af, 0.034f * af) }, 12)), m.top);
                var bare = Joint("BareForearms", el.transform, Vector3.zero);
                Add("ForearmBare", bare.transform, Shared(k + "ForearmBare", () => Tube("ForearmBare", new[] { new Ring(0f, 0.04f * af, 0.04f * af), new Ring(-0.12f, 0.036f * af, 0.036f * af), new Ring(-0.26f, 0.032f * af, 0.032f * af) }, 12)), m.skin);
                var hand = Joint("Hand" + side, el.transform, new Vector3(0f, -0.27f, 0f));
                Add("HandMesh", hand.transform, Shared("Hand", () => Ellipsoid("Hand", 0.038f, 0.052f, 0.026f, new Vector3(0f, -0.02f, 0f), 5, 10)), m.skin);
                if (s > 0)
                {
                    var gun = Joint("Pistol", hand.transform, new Vector3(0f, -0.04f, 0.02f));
                    Add("PistolMesh", gun.transform, Shared("Pistol", () => Combine("Pistol", Box(new Vector3(0f, 0.01f, 0.07f), new Vector3(0.03f, 0.04f, 0.17f)), Box(new Vector3(0f, -0.03f, 0.015f), new Vector3(0.028f, 0.075f, 0.04f), new Vector3(12f, 0f, 0f)))), m.metal);
                    gun.SetActive(false);
                }
            }

            // legs
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "L" : "R";
                var hp = Joint("Hip" + side, hips.transform, new Vector3(s * (f ? 0.092f : 0.098f), -0.03f, 0f));
                Add("Thigh", hp.transform, Shared(k + "Thigh", () => Tube("Thigh", f ? new[] { new Ring(0.02f, 0.088f, 0.09f), new Ring(-0.06f, 0.088f, 0.09f), new Ring(-0.25f, 0.066f, 0.068f), new Ring(-0.46f, 0.052f, 0.052f) } : new[] { new Ring(0.02f, 0.082f, 0.085f), new Ring(-0.06f, 0.086f, 0.088f), new Ring(-0.25f, 0.072f, 0.072f), new Ring(-0.46f, 0.058f, 0.058f) }, 12)), m.bottom);
                var kn = Joint("Knee" + side, hp.transform, new Vector3(0f, -0.46f, 0f));
                Add("Shin", kn.transform, Shared(k + "Shin", () => Tube("Shin", new[] { new Ring(0f, 0.057f * (f ? 0.93f : 1f), 0.057f * (f ? 0.93f : 1f)), new Ring(-0.1f, 0.059f * (f ? 0.93f : 1f), 0.06f * (f ? 0.93f : 1f)), new Ring(-0.3f, 0.046f * (f ? 0.9f : 1f), 0.046f * (f ? 0.9f : 1f)), new Ring(-0.43f, 0.04f * (f ? 0.9f : 1f), 0.04f * (f ? 0.9f : 1f)) }, 12)), m.bottom);
                Add("Shoe", kn.transform, Shared(k + "Shoe", () => f ? Combine("Shoe", Box(new Vector3(0f, -0.455f, 0.04f), new Vector3(0.082f, 0.065f, 0.25f)), Box(new Vector3(0f, -0.49f, 0.04f), new Vector3(0.086f, 0.016f, 0.26f))) : Combine("Shoe", Box(new Vector3(0f, -0.455f, 0.045f), new Vector3(0.095f, 0.07f, 0.27f)), Box(new Vector3(0f, -0.49f, 0.045f), new Vector3(0.1f, 0.016f, 0.28f)))), m.shoes);
            }

            var cust = root.AddComponent<CharacterCustomizer>(); cust.SetDefault(look);
            root.AddComponent<CharacterAnimator>();
            return root;
        }

        /// <summary>Dome that covers the top of the head (hair, cap, helmet): a partial ellipsoid from a start height up to the crown.</summary>
        private static Mesh HairCap(float rx, float ry, float rz, float startY)
        {
            var list = new List<Ring>();
            const int n = 7; float cy = 0.09f;
            float th0 = Mathf.Acos(Mathf.Clamp((startY - cy) / -ry, -1f, 1f));      // polar angle at the start height
            for (int i = 0; i <= n; i++)
            {
                float th = Mathf.Lerp(th0, Mathf.PI, i / (float)n);
                list.Add(new Ring(cy - ry * Mathf.Cos(th), rx * Mathf.Sin(th), rz * Mathf.Sin(th)));
            }
            return Tube("HairCap", list.ToArray(), 14, false);
        }
    }
}
