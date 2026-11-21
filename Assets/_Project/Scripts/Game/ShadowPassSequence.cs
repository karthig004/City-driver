using System.Collections;
using CityDriver.CameraSystem;
using CityDriver.Navigation;
using CityDriver.Vehicle;
using CityDriver.World;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// The Shadow Pass moment: the card appears and is pulled towards the camera, daylight fades to night, rain starts, the music turns
    /// to suspense, the car's headlights come on, the camera shows the nightclub entrance, and a secret mission appears.
    /// It runs on unscaled time so it also works while the game is paused by a menu; the car must be (nearly) stopped.
    /// </summary>
    public sealed class ShadowPassSequence : MonoBehaviour
    {
        public static ShadowPassSequence Instance { get; private set; }
        public bool Playing { get; private set; }
        public string Stage { get; private set; } = "";
        public int StagesCompleted { get; private set; }

        private Font font;
        private Sprite rounded;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public static ShadowPassSequence Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("ShadowPassSequence").AddComponent<ShadowPassSequence>();
        }

        public bool Play(Transform canvasParent, Font uiFont, Sprite roundedSprite)
        {
            if (Playing) return false;
            var car = FindFirstObjectByType<VehicleController>();
            if (car != null && car.SpeedKmh > 8f) { GameUI.Instance?.ShowToast("Stop the car to use the Shadow Pass"); return false; }
            font = uiFont; rounded = roundedSprite;
            StartCoroutine(Run(canvasParent));
            return true;
        }

        private IEnumerator Run(Transform canvasParent)
        {
            Playing = true; StagesCompleted = 0;
            var session = GameSession.Instance;
            var env = WorldEnvironment.Instance;
            var cam = FindFirstObjectByType<VehicleCameraController>();

            // 1-2. the card: a magical card appears face-down in a violet aura, flips to reveal itself, floats with a rune ring and sparkles,
            //      then is pulled into the camera while the world turns to night
            Stage = "card";
            var root = new GameObject("ShadowPassCinematic", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            root.transform.SetParent(canvasParent, false);
            var rootRt = (RectTransform)root.transform; rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one; rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            var rootGroup = root.GetComponent<CanvasGroup>(); rootGroup.alpha = 1f; rootGroup.blocksRaycasts = true;
            var dim = root.GetComponent<Image>(); dim.color = new Color(0.02f, 0f, 0.05f, 0f);

            Image Mk(Transform parent, string name, Sprite sp, Vector2 size, Color c, Vector2 pos)
            {
                var g = new GameObject(name, typeof(RectTransform), typeof(Image)); g.transform.SetParent(parent, false);
                var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = size; r.anchoredPosition = pos;
                var im = g.GetComponent<Image>(); im.sprite = sp; im.color = c; im.raycastTarget = false; return im;
            }
            Text Tx(Transform parent, string text, int size, Font f, Vector2 pos, float spacing)
            {
                var g = new GameObject("Text", typeof(RectTransform), typeof(Text)); g.transform.SetParent(parent, false);
                var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(420, size * 1.3f); r.anchoredPosition = pos;
                var tt = g.GetComponent<Text>(); tt.font = f; tt.fontSize = size; tt.alignment = TextAnchor.MiddleCenter; tt.text = text; tt.raycastTarget = false; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.color = new Color(1, 1, 1, 0);
                if (spacing > 0f) g.AddComponent<LetterSpacing>().spacing = spacing;
                return tt;
            }

            var rays = Mk(root.transform, "Rays", MagicCardArt.Rays, new Vector2(1900, 1900), new Color(0.85f, 0.55f, 1f, 0f), Vector2.zero);
            var aura = Mk(root.transform, "Aura", MagicCardArt.Aura, new Vector2(1500, 1500), new Color(0.6f, 0.18f, 0.95f, 0f), Vector2.zero);
            var aura2 = Mk(root.transform, "AuraWarm", MagicCardArt.Aura, new Vector2(760, 980), new Color(1f, 0.25f, 0.35f, 0f), Vector2.zero);
            var ring = Mk(root.transform, "Runes", MagicCardArt.Runes, new Vector2(900, 900), new Color(0.85f, 0.68f, 1f, 0f), Vector2.zero);
            var cardGo = new GameObject("Card", typeof(RectTransform)); cardGo.transform.SetParent(root.transform, false);
            var cardRt = (RectTransform)cardGo.transform; cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f); cardRt.sizeDelta = new Vector2(440, 660);
            var backImg = Mk(cardGo.transform, "Back", MagicCardArt.Back, new Vector2(440, 660), Color.white, Vector2.zero);
            var frontImg = Mk(cardGo.transform, "Front", MagicCardArt.Face, new Vector2(440, 660), Color.white, Vector2.zero);
            frontImg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var sigil = Mk(frontImg.transform, "Sigil", MagicCardArt.Sigil, new Vector2(320, 320), new Color(1f, 1f, 1f, 0f), new Vector2(0, 6));
            var title = Tx(frontImg.transform, "SHADOW PASS", 84, UiTheme.Display, new Vector2(0, 236), 3f);
            var tg = title.gameObject.AddComponent<TextGradient>(); tg.top = Color.white; tg.bottom = new Color(0.72f, 0.58f, 1f); tg.shineWidth = 0.18f;
            var tol = title.gameObject.AddComponent<Outline>(); tol.effectColor = new Color(0.16f, 0.03f, 0.3f, 0.95f); tol.effectDistance = new Vector2(3.5f, -3.5f);
            var sub = Tx(frontImg.transform, "PRIVATE  -  DRIVER ACCESS", 22, UiTheme.Bold, new Vector2(0, -226), 5f); sub.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            var foot = Tx(frontImg.transform, "NIGHT JOBS UNLOCKED", 25, UiTheme.Bold, new Vector2(0, -268), 6f); foot.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            var beam = Mk(frontImg.transform, "Shimmer", MagicCardArt.Beam, new Vector2(150, 900), new Color(1f, 0.95f, 1f, 0f), new Vector2(-600, 0)); beam.rectTransform.localRotation = Quaternion.Euler(0, 0, 24f);
            const int SparkN = 34; var sparks = new Image[SparkN]; var sparkAng = new float[SparkN]; var sparkRad = new float[SparkN]; var sparkSpd = new float[SparkN]; var sparkPh = new float[SparkN]; var sparkSz = new float[SparkN];
            var srng = new System.Random(11);
            for (int i = 0; i < SparkN; i++)
            {
                sparkSz[i] = 26f + (float)srng.NextDouble() * 50f; sparks[i] = Mk(root.transform, "Spark" + i, MagicCardArt.Spark, new Vector2(sparkSz[i], sparkSz[i]), new Color(1f, 0.92f, 1f, 0f), Vector2.zero);
                sparkAng[i] = (float)srng.NextDouble() * 6.283f; sparkRad[i] = 0.55f + (float)srng.NextDouble() * 0.6f; sparkSpd[i] = (0.15f + (float)srng.NextDouble() * 0.35f) * (i % 2 == 0 ? 1f : -1f); sparkPh[i] = (float)srng.NextDouble() * 6.283f;
            }
            var edge = Mk(root.transform, "EdgeGlow", MagicCardArt.Aura, new Vector2(700, 980), new Color(1f, 0.75f, 0.35f, 0f), Vector2.zero); edge.transform.SetSiblingIndex(cardGo.transform.GetSiblingIndex());
            var shock = new Image[3]; for (int i = 0; i < shock.Length; i++) shock[i] = Mk(root.transform, "Shock" + i, CityDriver.UI.ThreatMarkers.Ring, new Vector2(400, 400), new Color(1f, 0.88f, 0.6f, 0f), Vector2.zero);
            const int EmberN = 46; var embers = new Image[EmberN]; var emberX = new float[EmberN]; var emberSpd = new float[EmberN]; var emberPh = new float[EmberN]; var emberSz = new float[EmberN];
            for (int i = 0; i < EmberN; i++)
            {
                emberX[i] = ((float)srng.NextDouble() * 2f - 1f) * 520f; emberSpd[i] = 0.08f + (float)srng.NextDouble() * 0.14f; emberPh[i] = (float)srng.NextDouble(); emberSz[i] = 8f + (float)srng.NextDouble() * 22f;
                embers[i] = Mk(root.transform, "Ember" + i, MagicCardArt.Aura, new Vector2(emberSz[i], emberSz[i]), new Color(i % 3 == 0 ? 1f : 0.85f, i % 3 == 0 ? 0.8f : 0.55f, i % 3 == 0 ? 0.4f : 1f, 0f), Vector2.zero);
            }
            var flash = Mk(root.transform, "Flash", null, new Vector2(4000, 3000), new Color(1f, 0.95f, 1f, 0f), Vector2.zero);
            float zoom = 1f, fade = 1f, clock = 0f;

            void Tick(float t)
            {
                dim.color = new Color(0.02f, 0f, 0.05f, 0.9f * Mathf.Clamp01(t / 0.5f));
                SetAlpha(rays, 0.4f * Mathf.Clamp01((t - 0.2f) / 0.9f)); rays.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 5f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
                SetAlpha(aura, Mathf.Clamp01(t / 0.7f) * (0.62f + 0.2f * pulse)); SetAlpha(aura2, Mathf.Clamp01((t - 0.9f) / 0.8f) * (0.22f + 0.1f * pulse));
                SetAlpha(ring, 0.55f * Mathf.Clamp01((t - 0.3f) / 0.7f)); ring.rectTransform.localRotation = Quaternion.Euler(0, 0, -t * 9f); ring.rectTransform.localScale = Vector3.one * (1f + 0.02f * Mathf.Sin(t * 1.3f));
                float ke = Mathf.Clamp01(t / 0.9f), scale = Mathf.LerpUnclamped(0.3f, 1f, UiTheme.EaseOutBack(ke)) * zoom * 1.15f;
                float kf = Mathf.Clamp01((t - 1.3f) / 0.55f), sx = Mathf.Abs(Mathf.Cos(kf * Mathf.PI));
                bool frontUp = kf >= 0.5f;
                backImg.gameObject.SetActive(!frontUp); frontImg.gameObject.SetActive(frontUp);
                float bob = t > 1.2f ? Mathf.Sin(t * 1.7f) * 9f : 0f, tilt = t > 1.2f ? Mathf.Sin(t * 1.1f) * 1.8f : 0f;
                cardRt.localScale = new Vector3(Mathf.Max(0.02f, sx) * scale, scale, 1f);
                cardRt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-14f, 0f, UiTheme.EaseOutCubic(ke)) + tilt);
                cardRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-90f, 0f, UiTheme.EaseOutCubic(ke)) + bob);
                float ta = Mathf.Clamp01((t - 1.85f) / 0.5f), sa = Mathf.Clamp01((t - 2.15f) / 0.5f), fa = Mathf.Clamp01((t - 2.45f) / 0.5f);
                SetAlpha(title, ta); title.rectTransform.anchoredPosition = new Vector2(0f, 236f + 18f * (1f - UiTheme.EaseOutCubic(ta)));
                SetAlpha(sub, sa); SetAlpha(foot, fa * (0.8f + 0.2f * pulse)); SetAlpha(sigil, Mathf.Clamp01((t - 1.7f) / 0.6f) * (0.82f + 0.18f * pulse));
                sigil.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.8f) * 3f);
                float sh = Mathf.Repeat(t - 2.0f, 2.8f); tg.shine = t > 2.0f && sh < 1.1f ? Mathf.Lerp(-0.25f, 1.25f, sh / 1.1f) : -1f; title.SetVerticesDirty();
                bool sweep = t > 2.0f && sh < 1.0f; SetAlpha(beam, sweep ? 0.5f * Mathf.Sin(sh * Mathf.PI) : 0f); beam.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-420f, 420f, sh), 0f);
                float fl = t > 1.85f ? Mathf.Clamp01(1f - (t - 1.85f) / 0.5f) : 0f; SetAlpha(flash, 0.55f * fl);
                float sp = Mathf.Clamp01((t - 1.8f) / 0.8f);
                for (int i = 0; i < SparkN; i++)
                {
                    float a = sparkAng[i] + t * sparkSpd[i], rr = sparkRad[i] + 0.04f * Mathf.Sin(t * 0.9f + sparkPh[i]);
                    sparks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * 340f * rr * zoom, Mathf.Sin(a) * 440f * rr * zoom + 12f * Mathf.Sin(t + sparkPh[i]));
                    float tw = Mathf.Max(0f, Mathf.Sin(t * (1.8f + i * 0.13f) + sparkPh[i])); tw *= tw;
                    sparks[i].rectTransform.localScale = Vector3.one * (0.35f + 0.9f * tw); sparks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 20f * i + t * 40f);
                    SetAlpha(sparks[i], sp * (0.25f + 0.75f * tw));
                }
                SetAlpha(edge, Mathf.Clamp01((t - 1.7f) / 0.6f) * (0.42f + 0.2f * pulse)); edge.rectTransform.sizeDelta = new Vector2(700f, 980f) * (zoom * 1.15f); edge.rectTransform.anchoredPosition = cardRt.anchoredPosition; edge.rectTransform.localRotation = cardRt.localRotation;
                for (int i = 0; i < shock.Length; i++)
                {
                    float ks = (t - 1.85f - i * 0.28f) / 1.2f; bool on = ks > 0f && ks < 1f;
                    shock[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.35f, 3.4f, UiTheme.EaseOutCubic(Mathf.Clamp01(ks))); SetAlpha(shock[i], on ? 0.85f * (1f - ks) : 0f);
                }
                float er = Mathf.Clamp01((t - 1.0f) / 1.0f);
                for (int i = 0; i < EmberN; i++)
                {
                    float f = Mathf.Repeat(t * emberSpd[i] + emberPh[i], 1f);
                    embers[i].rectTransform.anchoredPosition = new Vector2(emberX[i] * zoom + Mathf.Sin(t * 1.1f + emberPh[i] * 6.28f) * 36f, Mathf.Lerp(-560f, 560f, f));
                    SetAlpha(embers[i], er * Mathf.Sin(f * Mathf.PI) * (0.5f + 0.5f * Mathf.Sin(t * 3f + i)));
                }
                cardRt.localScale = new Vector3(cardRt.localScale.x * (1f + (t > 1.9f ? 0.025f * Mathf.Sin(t * 1.3f) : 0f)), cardRt.localScale.y, 1f);
                rootGroup.alpha = fade;
            }

            GameAudio.PlayCard();
            session.Data.shadowCinematicSeen = true; session.Persist();

            while (clock < 3.3f) { clock += Time.unscaledDeltaTime; Tick(clock); yield return null; }
            StagesCompleted = 1;
            for (float w0 = 0f; w0 < 0.9f; w0 += Time.unscaledDeltaTime) { clock += Time.unscaledDeltaTime; Tick(clock); yield return null; }

            // 3. the card is pulled into the camera while the world turns to night
            Stage = "zoom + night";
            env?.BeginShadowMode(7f);                                    // night, rain, suspense music, headlights (WorldEnvironment turns the beams on)
            StagesCompleted = 2;
            for (float t3 = 0f; t3 < 2.4f; t3 += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t3 / 2.4f);
                zoom = Mathf.Lerp(1f, 2.8f, k); fade = 1f - Mathf.Clamp01((k - 0.55f) / 0.45f);
                clock += Time.unscaledDeltaTime; Tick(clock);
                SetAlpha(flash, 0.7f * Mathf.Clamp01((k - 0.35f) / 0.3f) * (1f - Mathf.Clamp01((k - 0.65f) / 0.35f)));
                yield return null;
            }
            Destroy(root);
            StagesCompleted = 3;

            // 4-8. nightclub entrance
            Stage = "nightclub";
            var nav = NavigationService.Instance;
            Vector3 club = Vector3.zero; bool haveClub = false;
            if (nav != null && nav.Data != null) foreach (var poi in nav.Data.pois) if (poi.type == PoiType.Nightclub) { club = poi.position; haveClub = true; break; }
            var carObj = FindFirstObjectByType<VehicleController>();
            if (cam != null && carObj != null)
            {
                Vector3 focus = haveClub ? club : carObj.transform.position + carObj.transform.forward * 20f;
                Vector3 camPos = focus + new Vector3(10f, 5.5f, -14f);
                cam.SetCinematic(camPos, Quaternion.LookRotation((focus + Vector3.up * 3f) - camPos));
                StagesCompleted = 4;
                yield return WaitUnscaled(4.2f);
                cam.EndCinematic();
            }
            StagesCompleted = 5;

            // 9. the secret mission appears
            Stage = "mission";
            string next = "a secret rescue";
            foreach (var m in CareerCatalogShadow()) { next = m; break; }
            GameUI.Instance?.ShowToast("SECRET MISSION: " + next);
            GameUI.Instance?.ShowSubtitle("Unknown caller", "Driver... we have a job for you. Check your missions.");
            VoiceService.Speak("Unknown caller", "Driver. We have a job for you. Check your missions.");
            StagesCompleted = 6;
            Stage = "done"; Playing = false;
        }

        private static System.Collections.Generic.IEnumerable<string> CareerCatalogShadow()
        {
            int level = GameSession.Instance.Career.Level;
            foreach (var m in Career.CareerCatalog.ForLevel(level)) if (m.ShadowRun) yield return m.Title;
            foreach (var m in Career.CareerCatalog.Missions) if (m.ShadowRun && m.Level <= level) yield return m.Title;
        }

        private static IEnumerator WaitUnscaled(float seconds) { for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null; }

        private Text MakeText(Transform parent, string text, int size, Color color, Vector2 pos)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(600, 80); r.anchoredPosition = pos;
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.color = color; t.alignment = TextAnchor.MiddleCenter; t.text = text; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
            return t;
        }

        private static void SetAlpha(Graphic g, float a) { var c = g.color; c.a = a; g.color = c; }
    }
}
