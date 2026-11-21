using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityDriver.Save
{
    /// <summary>Where save text lives. Swappable per platform (files on Android/desktop, PlayerPrefs on Web, memory in tests).</summary>
    public interface ISaveStorage
    {
        bool TryRead(string key, out string text);
        void Write(string key, string text);
        void Delete(string key);
    }

    /// <summary>File storage under persistentDataPath with atomic replace and a rolling .bak copy.</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string directory;

        public FileSaveStorage(string directory = null)
        {
            this.directory = string.IsNullOrEmpty(directory) ? Application.persistentDataPath : directory;
            Directory.CreateDirectory(this.directory);
        }

        private string PathFor(string key) => Path.Combine(directory, key);

        public bool TryRead(string key, out string text)
        {
            text = null;
            string path = PathFor(key);
            if (!File.Exists(path)) return false;
            try { text = File.ReadAllText(path); return true; }
            catch (IOException) { return false; }
        }

        public void Write(string key, string text)
        {
            string path = PathFor(key), temp = path + ".tmp", backup = path + ".bak";
            File.WriteAllText(temp, text);                       // write fully first: a crash can never leave a half-written save
            if (File.Exists(path)) File.Copy(path, backup, true);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public void Delete(string key)
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
            {
                string path = PathFor(key) + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>In-memory storage for tests and as the base for a Web (PlayerPrefs / IndexedDB) implementation.</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool TryRead(string key, out string text) => Files.TryGetValue(key, out text);
        public void Write(string key, string text) { if (Files.TryGetValue(key, out var old)) Files[key + ".bak"] = old; Files[key] = text; }
        public void Delete(string key) { Files.Remove(key); Files.Remove(key + ".bak"); }
    }
}
