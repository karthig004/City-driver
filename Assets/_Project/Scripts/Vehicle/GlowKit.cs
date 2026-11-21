using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Cheap light looks for night driving: soft glow halos that always face the camera (and grow with distance so a far-away police
    /// light bar or tail lamp still reads), and an additive headlight beam cone. Materials are clones of the rain particle material so the
    /// shader is always included in builds.
    /// </summary>
    public static class GlowKit
    {
        private static Material softMat, coneMat;
        private static Texture2D soft;
        private static Mesh quad, cone;

        private static Texture2D Soft()
        {
            if (soft != null) return soft;
            const int n = 96; soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.4f) + Mathf.Pow(Mathf.Clamp01(1f - d * 3.2f), 2f) * 0.9f;        // soft halo + hot core
                px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
            soft.SetPixels(px); soft.Apply();
            return soft;
        }

        private static Material Clone(Texture tex)
        {
            var src = Resources.Load<Material>("RainParticle");
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Cull")) m.SetInt("_Cull", 0);
            m.renderQueue = 3100;
            return m;
        }

        public static Material SoftMaterial => softMat != null ? softMat : softMat = Clone(Soft());
        public static Material ConeMaterial => coneMat != null ? coneMat : coneMat = Clone(Texture2D.whiteTexture);

        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "GlowQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.RecalculateBounds(); return quad;
            }
        }

        /// <summary>A beam of light along +Z: bright at the lamp, fading to nothing at the far end (vertex alpha).</summary>
        public static Mesh Cone
        {
            get
            {
                if (cone != null) return cone;
                const int seg = 14; const float length = 17f, radius = 3.0f;
                var v = new Vector3[seg + 2]; var c = new Color[seg + 2]; var t = new int[seg * 3];
                v[0] = Vector3.zero; c[0] = new Color(1, 1, 1, 0.12f);
                for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2f / seg; v[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.5f, length); c[i + 1] = new Color(1, 1, 1, 0f); }
                for (int i = 0; i < seg; i++) { t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % seg; t[i * 3 + 2] = 1 + i; }
                cone = new Mesh { name = "BeamCone", vertices = v, colors = c, triangles = t };
                cone.RecalculateBounds(); return cone;
            }
        }

        /// <summary>Adds a camera-facing halo under <paramref name="parent"/>.</summary>
        public static GlowBillboard AddGlow(Transform parent, Vector3 localPos, float size, Color colour)
        {
            var go = new GameObject("Glow"); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = SoftMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var g = go.AddComponent<GlowBillboard>(); g.baseSize = size; g.colour = colour; g.mr = mr;
            return g;
        }

        public static MeshRenderer AddCone(Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject("BeamCone"); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = Cone;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ConeMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(1f, 0.95f, 0.8f, 1f)); mr.SetPropertyBlock(block);
            return mr;
        }
    }

    /// <summary>A soft halo that faces the camera. <see cref="intensity"/> 0..1 fades it; far away it grows so a distant light still reads.</summary>
    public sealed class GlowBillboard : MonoBehaviour
    {
        public float baseSize = 1f, intensity = 1f, farBoost = 4f;
        public Color colour = Color.white;
        public MeshRenderer mr;
        private MaterialPropertyBlock block;
        private Color shown = new Color(-1, -1, -1, -1);

        private void LateUpdate()
        {
            if (mr == null) return;
            bool visible = intensity > 0.01f;
            if (mr.enabled != visible) mr.enabled = visible;
            if (!visible) return;
            var cam = Camera.main; if (cam == null) return;
            transform.rotation = cam.transform.rotation;
            float d = Vector3.Distance(cam.transform.position, transform.position);
            float s = baseSize * Mathf.Clamp(d / 22f, 1f, farBoost);
            var p = transform.parent; float ps = p != null ? Mathf.Max(0.01f, p.lossyScale.x) : 1f;
            transform.localScale = Vector3.one * (s / ps);
            Color c = colour; c.a = Mathf.Clamp01(colour.a * intensity);
            if (c != shown) { if (block == null) block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", c); mr.SetPropertyBlock(block); shown = c; }
        }
    }
}
