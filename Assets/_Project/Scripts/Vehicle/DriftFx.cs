using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Tyre smoke and skid marks at the rear wheels while the car slides, spins its wheels or brakes hard. It reads the real wheel slip from
    /// the WheelColliders and the controller's drift amount; the materials are clones of the rain particle material (so the shader is always
    /// included in builds) with a soft round texture. Added automatically by <see cref="VehicleController"/>.
    /// </summary>
    public sealed class DriftFx : MonoBehaviour
    {
        private VehicleController car;
        private readonly ParticleSystem[] smoke = new ParticleSystem[2];
        private readonly TrailRenderer[] marks = new TrailRenderer[2];
        private WheelCollider[] rear;
        private static Texture2D soft;
        private static Material smokeMat, markMat;

        private static Texture2D Soft()
        {
            if (soft != null) return soft;
            const int n = 64; soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; float d = Mathf.Sqrt(u * u + v * v); px[y * n + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f)); }
            soft.SetPixels(px); soft.Apply();
            return soft;
        }

        private static Material Clone(bool solid)
        {
            var src = Resources.Load<Material>("RainParticle");
            Material m;
            if (src != null) m = new Material(src);
            else { var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"); m = new Material(sh); }
            Texture t = solid ? Texture2D.whiteTexture : Soft();
            m.mainTexture = t; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            return m;
        }

        private void Start()
        {
            car = GetComponent<VehicleController>();
            if (car == null || car.RearLeft == null) { enabled = false; return; }
            rear = new[] { car.RearLeft, car.RearRight };
            if (smokeMat == null) smokeMat = Clone(false);
            if (markMat == null) markMat = Clone(true);
            for (int i = 0; i < 2; i++)
            {
                var g = new GameObject("DriftSmoke" + i); g.transform.SetParent(transform, false);
                var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
                var main = ps.main; main.loop = true; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.4f); main.startColor = new Color(0.9f, 0.9f, 0.93f, 0.34f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 160; main.gravityModifier = -0.04f;
                var em = ps.emission; em.rateOverTime = 0f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.12f;
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2.4f)));
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.82f), 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
                var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                smoke[i] = ps;

                var tg = new GameObject("SkidMark" + i); tg.transform.SetParent(null, false);       // trails must not move with the car
                tg.transform.SetParent(transform.parent, true);
                var tr = tg.AddComponent<TrailRenderer>(); tr.time = 6f; tr.minVertexDistance = 0.35f; tr.startWidth = 0.24f; tr.endWidth = 0.2f; tr.emitting = false; tr.alignment = LineAlignment.TransformZ;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false; tr.sharedMaterial = markMat; tr.numCapVertices = 2;
                tr.startColor = new Color(0.03f, 0.03f, 0.04f, 0.62f); tr.endColor = new Color(0.03f, 0.03f, 0.04f, 0f);
                marks[i] = tr;
            }
        }

        private void OnDestroy() { for (int i = 0; i < 2; i++) if (marks[i] != null) Destroy(marks[i].gameObject); }

        private void LateUpdate()
        {
            if (car == null || rear == null) return;
            for (int i = 0; i < 2; i++)
            {
                var w = rear[i]; if (w == null || smoke[i] == null) continue;
                float amount = 0f; bool grounded = w.GetGroundHit(out WheelHit hit);
                if (grounded)
                {
                    float side = Mathf.Abs(hit.sidewaysSlip), fwd = Mathf.Abs(hit.forwardSlip);
                    amount = Mathf.Max(Mathf.Clamp01((side - 0.35f) * 2.2f), Mathf.Clamp01((fwd - 0.7f) * 1.8f) * Mathf.Clamp01(1f - car.SpeedKmh / 60f + 0.3f), car.DriftAmount * 0.9f);
                    if (car.SpeedKmh < 6f && fwd < 0.8f) amount = 0f;
                }
                w.GetWorldPose(out Vector3 pos, out _);
                Vector3 ground = new Vector3(pos.x, hit.point.y + 0.03f, pos.z); if (!grounded) ground = pos - Vector3.up * w.radius;
                smoke[i].transform.position = ground + Vector3.up * 0.1f;
                var em = smoke[i].emission; em.rateOverTime = amount > 0.2f ? Mathf.Lerp(8f, 70f, amount) : 0f;
                if (!smoke[i].isPlaying && amount > 0.2f) smoke[i].Play();
                if (marks[i] != null)
                {
                    marks[i].transform.position = ground; marks[i].transform.rotation = Quaternion.Euler(90f, car.transform.eulerAngles.y, 0f);
                    bool want = grounded && amount > 0.38f;
                    if (marks[i].emitting != want) { if (want) marks[i].Clear(); marks[i].emitting = want; }
                }
            }
        }
    }
}
