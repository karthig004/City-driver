using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// First-time Google Play helper: create your upload keystore (using the JDK that ships with Unity, nothing to install), then build the
    /// signed .aab to upload. Passwords are typed here, passed to the build through process environment variables and never stored in the project.
    /// Open it from the menu: City Driver > Play Store > Release window.
    /// </summary>
    public sealed class PlayStoreWindow : EditorWindow
    {
        private string keystorePath = "", alias = "upload", pass = "", pass2 = "", name = "", org = "Karthi Lab", country = "IN";
        private string message = "";

        [MenuItem("City Driver/Play Store/Release window (keystore + signed AAB)", priority = 190)]
        public static void Open() { var w = GetWindow<PlayStoreWindow>("Play Store release"); w.minSize = new Vector2(520, 560); }

        private static string Keytool()
        {
            string editor = Path.GetDirectoryName(EditorApplication.applicationPath);
            string p = Path.Combine(editor, "Data", "PlaybackEngines", "AndroidPlayer", "OpenJDK", "bin", "keytool.exe");
            if (File.Exists(p)) return p;
            p = Path.Combine(editor, "Data", "PlaybackEngines", "AndroidPlayer", "OpenJDK", "bin", "keytool");
            return File.Exists(p) ? p : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Step 1: create your upload keystore ONCE and back it up (a USB stick AND cloud storage). If you lose the file or the passwords you cannot update your game on Google Play.\nStep 2: build the signed bundle (.aab) and upload it in Play Console.", MessageType.Info);
            GUILayout.Label("1. Keystore", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                keystorePath = EditorGUILayout.TextField("Keystore file (.jks)", keystorePath);
                if (GUILayout.Button("Browse", GUILayout.Width(70))) { string p = EditorUtility.OpenFilePanel("Choose keystore", "", "jks,keystore"); if (!string.IsNullOrEmpty(p)) keystorePath = p; }
            }
            alias = EditorGUILayout.TextField("Key alias", alias);
            pass = EditorGUILayout.PasswordField("Keystore password", pass);

            GUILayout.Space(6);
            GUILayout.Label("Create a NEW keystore (skip if you already have one)", EditorStyles.boldLabel);
            name = EditorGUILayout.TextField("Your name", name);
            org = EditorGUILayout.TextField("Organisation", org);
            country = EditorGUILayout.TextField("Country code (2 letters)", country);
            pass2 = EditorGUILayout.PasswordField("Repeat password", pass2);
            if (GUILayout.Button("Create keystore...")) CreateKeystore();

            GUILayout.Space(10);
            GUILayout.Label("2. Build", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(alias)))
                if (GUILayout.Button("Build signed AAB for Google Play", GUILayout.Height(36))) BuildSigned();
            if (GUILayout.Button("Open the output folder")) EditorUtility.RevealInFinder(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Android"));

            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, message.StartsWith("ERROR") ? MessageType.Error : MessageType.None);
        }

        private void CreateKeystore()
        {
            string kt = Keytool();
            if (kt == null) { message = "ERROR: keytool was not found inside this Unity install (Android Build Support / OpenJDK missing)."; return; }
            if (pass.Length < 6 || pass != pass2) { message = "ERROR: choose a password of at least 6 characters and type it twice the same."; return; }
            if (string.IsNullOrWhiteSpace(name) || country.Length != 2) { message = "ERROR: enter your name and a 2-letter country code."; return; }
            string target = EditorUtility.SaveFilePanel("Save your upload keystore", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "city-driver-upload-keystore", "jks");
            if (string.IsNullOrEmpty(target)) return;
            if (File.Exists(target)) { message = "ERROR: that file already exists; choose a new name (never overwrite an old keystore)."; return; }
            var psi = new ProcessStartInfo(kt, $"-genkeypair -v -keystore \"{target}\" -alias {alias} -keyalg RSA -keysize 2048 -validity 10000 -storepass \"{pass}\" -keypass \"{pass}\" -dname \"CN={name}, O={org}, C={country}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            try
            {
                using (var proc = Process.Start(psi)) { string o = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd(); proc.WaitForExit(); if (proc.ExitCode != 0) { message = "ERROR: " + o; return; } }
                keystorePath = target; pass2 = "";
                message = "Keystore created:\n" + target + "\n\nBACK IT UP NOW (two places) together with the password and the alias '" + alias + "'.";
            }
            catch (Exception e) { message = "ERROR: " + e.Message; }
        }

        private void BuildSigned()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { message = "ERROR: leave Play Mode first."; return; }
            Environment.SetEnvironmentVariable("CDU_KEYSTORE_PATH", keystorePath); Environment.SetEnvironmentVariable("CDU_KEYSTORE_PASS", pass);
            Environment.SetEnvironmentVariable("CDU_KEY_ALIAS", alias); Environment.SetEnvironmentVariable("CDU_KEY_PASS", pass);
            try
            {
                PlayStorePrep.BuildReleaseAab();
                string aab = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Android", "CITY_DRIVER_UNDERCOVER.aab");
                message = File.Exists(aab) ? "Done. Upload this file in Play Console:\n" + aab : "ERROR: the AAB was not produced; read Bridge/build_report_aab.txt.";
            }
            finally
            {
                Environment.SetEnvironmentVariable("CDU_KEYSTORE_PASS", null); Environment.SetEnvironmentVariable("CDU_KEY_PASS", null);   // never keep the passwords around
            }
        }
    }
}
