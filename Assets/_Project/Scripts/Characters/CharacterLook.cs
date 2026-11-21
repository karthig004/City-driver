using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Characters
{
    /// <summary>Everything that can be customised on a character; stored as JSON (the player's driver look is saved in the save file).</summary>
    [Serializable]
    public sealed class CharacterLook
    {
        public string role = "Driver";
        public string skin = "#C8956A", hair = "#1B1410", top = "#2B3A55", bottom = "#1E2128", shoes = "#15161A", accent = "#C9A227", accent2 = "#2A2D34", eyes = "#1A1210";
        public bool female;
        public string lips = "#B5524F";
        public int hairStyle = 1;                 // 0 bald, 1 short, 2 side part, 3 long, 4 bun, 5 buzz cut, 6 long wavy, 7 ponytail, 8 bob
        public int facial = 0;                    // 0 none, 1 stubble, 2 moustache, 3 beard
        public int sleeves = 1;                   // 0 short sleeves (bare forearms), 1 long sleeves
        public float height = 0.95f, build = 1f;
        public List<string> extras = new List<string>();

        public static readonly string[] SkinTones = { "#F1C9A5", "#E0AC82", "#C8956A", "#A56E45", "#7A4A2B", "#5A3520" };
        public static readonly string[] HairColors = { "#0E0B09", "#2A1B12", "#5A3A22", "#8A6A3A", "#B7B7BA", "#8B1A1A" };
        public static readonly string[] OutfitColors = { "#2B3A55", "#1E2128", "#8B1A1A", "#2F6B45", "#C9A227", "#E9E9E6", "#5B3A29", "#3A2A5C" };
        public static readonly string[] HairNames = { "Bald", "Short", "Side part", "Long", "Bun", "Buzz cut", "Long wavy", "Ponytail", "Bob" };
        public static readonly string[] FacialNames = { "Clean", "Stubble", "Moustache", "Beard" };
        public static readonly string[] ExtraNames = { "Cap", "Glasses", "Sunglasses", "Chain", "Jacket", "Backpack", "Bandana" };

        public bool Has(string extra) => extras != null && extras.Contains(extra);
        public void Toggle(string extra) { if (extras == null) extras = new List<string>(); if (!extras.Remove(extra)) extras.Add(extra); }
        public CharacterLook Clone() => JsonUtility.FromJson<CharacterLook>(JsonUtility.ToJson(this));
        public static CharacterLook FromJson(string json, CharacterLook fallback = null)
        {
            if (string.IsNullOrEmpty(json)) return fallback ?? new CharacterLook();
            try { var l = JsonUtility.FromJson<CharacterLook>(json); return l ?? fallback ?? new CharacterLook(); } catch (Exception) { return fallback ?? new CharacterLook(); }
        }
        public string ToJson() => JsonUtility.ToJson(this);
    }
}
