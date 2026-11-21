using System;
using UnityEngine;

namespace CityDriver.Game
{
    public enum SteeringMode { Buttons = 0, Wheel = 1, Tilt = 2 }

    /// <summary>Player preferences (PlayerPrefs). Everything else reads from here and listens to <see cref="Changed"/>.</summary>
    public static class GameSettings
    {
        public static event Action Changed;

        private static float GetF(string key, float fallback) { try { return PlayerPrefs.GetFloat(key, fallback); } catch (Exception) { return fallback; } }
        private static int GetI(string key, int fallback) { try { return PlayerPrefs.GetInt(key, fallback); } catch (Exception) { return fallback; } }
        private static void Set(string key, float v) { try { PlayerPrefs.SetFloat(key, v); } catch (Exception) { } Changed?.Invoke(); }
        private static void Set(string key, int v) { try { PlayerPrefs.SetInt(key, v); } catch (Exception) { } Changed?.Invoke(); }

        public static SteeringMode Steering { get => (SteeringMode)Mathf.Clamp(GetI("cd_steer_mode", 1), 0, 2); set => Set("cd_steer_mode", (int)value); }
        /// <summary>0.5 (gentle) .. 1.5 (sharp).</summary>
        public static float SteerSensitivity { get => Mathf.Clamp(GetF("cd_steer_sens", 1f), 0.5f, 1.5f); set => Set("cd_steer_sens", Mathf.Clamp(value, 0.5f, 1.5f)); }
        public static float Master { get => Mathf.Clamp01(GetF("cd_vol_master", 1f)); set => Set("cd_vol_master", Mathf.Clamp01(value)); }
        public static float Music { get => Mathf.Clamp01(GetF("cd_vol_music", 0.6f)); set => Set("cd_vol_music", Mathf.Clamp01(value)); }
        public static float Sfx { get => Mathf.Clamp01(GetF("cd_vol_sfx", 0.8f)); set => Set("cd_vol_sfx", Mathf.Clamp01(value)); }
        /// <summary>0 = radio off, 1..3 = station.</summary>
        public static int Radio { get => Mathf.Clamp(GetI("cd_radio", 1), 0, 3); set => Set("cd_radio", Mathf.Clamp(value, 0, 3)); }
        /// <summary>0 chase, 1 far chase, 2 bonnet, 3 top-down.</summary>
        public static int CameraView { get => Mathf.Clamp(GetI("cd_camera", 0), 0, 3); set => Set("cd_camera", Mathf.Clamp(value, 0, 3)); }
        public static bool UseMph { get => GetI("cd_mph", 0) == 1; set => Set("cd_mph", value ? 1 : 0); }
        /// <summary>-1 = leave the project default.</summary>
        public static int Quality { get => GetI("cd_quality", -1); set { Set("cd_quality", value); if (value >= 0) QualitySettings.SetQualityLevel(Mathf.Clamp(value, 0, QualitySettings.names.Length - 1), true); } }

        /// <summary>0 = automatic day cycle, 1 morning, 2 day, 3 sunset, 4 night.</summary>
        public static int TimeSetting { get => Mathf.Clamp(GetI("cd_time", 2), 0, 4); set => Set("cd_time", Mathf.Clamp(value, 0, 4)); }
        /// <summary>0 clear, 1 rain, 2 heavy rain, 3 fog.</summary>
        public static int WeatherSetting { get => Mathf.Clamp(GetI("cd_weather", 0), 0, 3); set => Set("cd_weather", Mathf.Clamp(value, 0, 3)); }
        public static bool Subtitles { get => GetI("cd_subtitles", 1) == 1; set => Set("cd_subtitles", value ? 1 : 0); }
        public static bool Voice { get => GetI("cd_voice", 1) == 1; set => Set("cd_voice", value ? 1 : 0); }
        public static float DialogueVolume { get => Mathf.Clamp01(GetF("cd_vol_voice", 0.9f)); set => Set("cd_vol_voice", Mathf.Clamp01(value)); }

        /// <summary>0 standard (x0.7), 1 long (x1.0, default), 2 epic (x1.5).</summary>
        public static int TripLength { get => Mathf.Clamp(GetI("cd_trip", 1), 0, 2); set => Set("cd_trip", Mathf.Clamp(value, 0, 2)); }
        public static float TripLengthScale => TripLength == 0 ? 0.7f : TripLength == 2 ? 1.5f : 1f;
        public static string[] TripNames = { "Standard", "Long", "Epic" };

        public static string[] TimeNames = { "Automatic", "Morning", "Day", "Sunset", "Night" };
        public static string[] WeatherNames = { "Clear", "Rain", "Heavy rain", "Fog" };
        public static string[] CameraNames = { "Chase", "Far chase", "Bonnet", "Top-down" };
        public static string[] RadioNames = { "Off", "Lo-Fi City", "Night Drive", "Metro Beats" };

        public static void ResetToDefaults()
        {
            foreach (string k in new[] { "cd_steer_mode", "cd_steer_sens", "cd_vol_master", "cd_vol_music", "cd_vol_sfx", "cd_radio", "cd_camera", "cd_mph", "cd_quality", "cd_time", "cd_weather", "cd_trip", "cd_subtitles", "cd_voice", "cd_vol_voice" })
            { try { PlayerPrefs.DeleteKey(k); } catch (Exception) { } }
            Changed?.Invoke();
        }
    }
}
