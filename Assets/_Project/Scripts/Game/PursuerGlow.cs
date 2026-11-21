using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Makes a pursuit car impossible to miss: glowing halos on the roof light bar that flash red and blue (visible from far away, day and night),
    /// a coloured point light that washes the street, bright headlight halos and a headlight beam at night, and red tail halos. Gang cars
    /// get a dull red underglow. The look follows the flash type chosen by <see cref="PursuitManager"/> (0 gang, 1 / 2 police flash phases).
    /// </summary>
    public sealed class PursuerGlow : MonoBehaviour
    {
        private GlowBillboard roofL, roofR, under;
        private readonly GlowBillboard[] heads = new GlowBillboard[2], tails = new GlowBillboard[2];
        private Light beacon, headBeam;
        private int look = -1;
        private static readonly Color Red = new Color(1f, 0.12f, 0.1f, 0.95f), Blue = new Color(0.2f, 0.45f, 1f, 0.95f);

        private void Awake()
        {
            roofL = GlowKit.AddGlow(transform, new Vector3(-0.45f, 1.55f, -0.15f), 1.5f, Red); roofR = GlowKit.AddGlow(transform, new Vector3(0.45f, 1.55f, -0.15f), 1.5f, Blue);
            roofL.farBoost = roofR.farBoost = 4f;
            under = GlowKit.AddGlow(transform, new Vector3(0f, 0.25f, 0f), 3.2f, new Color(1f, 0.18f, 0.08f, 0.4f)); under.intensity = 0f;
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.62f : 0.62f;
                heads[i] = GlowKit.AddGlow(transform, new Vector3(x, 0.72f, 2.12f), 1.0f, new Color(1f, 0.96f, 0.85f, 0.9f));
                tails[i] = GlowKit.AddGlow(transform, new Vector3(x * 1.05f, 0.85f, -2.12f), 0.8f, new Color(1f, 0.1f, 0.06f, 0.9f));
            }
            var bg = new GameObject("Beacon"); bg.transform.SetParent(transform, false); bg.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            beacon = bg.AddComponent<Light>(); beacon.type = LightType.Point; beacon.range = 15f; beacon.intensity = 7f; beacon.shadows = LightShadows.None; beacon.enabled = false;
            var hg = new GameObject("HeadBeam"); hg.transform.SetParent(transform, false); hg.transform.localPosition = new Vector3(0f, 0.8f, 2.1f); hg.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            headBeam = hg.AddComponent<Light>(); headBeam.type = LightType.Spot; headBeam.spotAngle = 70f; headBeam.innerSpotAngle = 28f; headBeam.range = 48f; headBeam.intensity = 260f; headBeam.color = new Color(1f, 0.94f, 0.8f); headBeam.shadows = LightShadows.None; headBeam.enabled = false;
        }

        /// <summary>0 = gang car, 1 = police (red left / blue right), 2 = police (blue left / red right).</summary>
        public void SetLook(int type)
        {
            if (type == look || roofL == null) return;
            look = type;
            bool police = type != 0;
            roofL.colour = type == 1 ? Red : Blue; roofR.colour = type == 1 ? Blue : Red;
            roofL.intensity = roofR.intensity = police ? 1f : 0f;
            under.intensity = police ? 0f : 1f;
            beacon.enabled = police; beacon.color = type == 1 ? new Color(1f, 0.15f, 0.12f) : new Color(0.25f, 0.45f, 1f);
            beacon.transform.localPosition = new Vector3(type == 1 ? -0.7f : 0.7f, 2.2f, 0f);
        }

        private void Update()
        {
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.Night01 : 0f;
            float rain = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.RainIntensity : 0f;
            float dark = Mathf.Clamp01(Mathf.Max(night, rain * 0.7f));
            foreach (var h in heads) if (h != null) h.intensity = Mathf.Lerp(0.4f, 1f, dark);
            foreach (var t in tails) if (t != null) t.intensity = Mathf.Lerp(0.5f, 1f, dark);
            if (headBeam != null) headBeam.enabled = dark > 0.25f;
            if (beacon != null && beacon.enabled) beacon.intensity = Mathf.Lerp(5f, 10f, dark);
        }
    }
}
