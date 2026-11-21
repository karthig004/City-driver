using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Characters
{
    /// <summary>Applies a <see cref="CharacterLook"/> to a character prefab built by the editor kit: tints the shared materials per renderer, shows the chosen hair, beard and accessories.</summary>
    public sealed class CharacterCustomizer : MonoBehaviour
    {
        [SerializeField] private string lookJson = "";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public CharacterLook Look { get; private set; }

        public void SetDefault(CharacterLook look) { lookJson = look.ToJson(); }

        private void Awake() { if (Look == null) Apply(CharacterLook.FromJson(lookJson)); }

        public void Apply(CharacterLook look)
        {
            Look = look ?? new CharacterLook();
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string hex = HexFor(mats[i] != null ? mats[i].name : "");
                    if (hex == null || !ColorUtility.TryParseHtmlString(hex, out Color c)) continue;
                    r.GetPropertyBlock(block, i); block.SetColor(BaseColor, c); r.SetPropertyBlock(block, i);
                }
            }
            var head = FindDeep(transform, "Head");
            if (head != null)
            {
                for (int h = 0; h <= 8; h++) { var t = head.Find("Hair" + h); if (t != null) t.gameObject.SetActive(h == Look.hairStyle); }
                for (int f = 1; f <= 3; f++) { var t = head.Find("Facial" + f); if (t != null) t.gameObject.SetActive(f == Look.facial); }
            }
            foreach (var e in GetComponentsInChildren<Transform>(true)) if (e.name == "Extras") foreach (Transform t in e) t.gameObject.SetActive(Look.Has(t.name));
            var sl = FindDeep(transform, "LongSleeves"); if (sl != null) sl.gameObject.SetActive(Look.sleeves == 1);
            var sk = FindDeep(transform, "BareForearms"); if (sk != null) sk.gameObject.SetActive(Look.sleeves == 0);
            transform.localScale = new Vector3(Look.build, Look.height, Look.build);
        }

        private string HexFor(string matName)
        {
            switch (matName)
            {
                case "M_CharSkin": return Look.skin;
                case "M_CharHair": return Look.hair;
                case "M_CharTop": return Look.top;
                case "M_CharBottom": return Look.bottom;
                case "M_CharShoes": return Look.shoes;
                case "M_CharAccent": return Look.accent;
                case "M_CharAccent2": return Look.accent2;
                case "M_CharEye": return "#120D0C";
                case "M_CharIris": return Look.eyes;
                case "M_CharLips": return Look.lips;
                default: return null;
            }
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
