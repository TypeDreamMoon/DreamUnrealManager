using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DreamUnrealManager.Services
{
    public static class Settings
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DreamUnrealManager");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");
        private static readonly object _lock = new();
        private static JsonObject _json = new();

        static Settings()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath))
                {
                    var txt = File.ReadAllText(FilePath);
                    _json = JsonNode.Parse(txt)?.AsObject() ?? new JsonObject();
                }
            }
            catch { _json = new JsonObject(); }
        }

        public static T Get<T>(string key, T defaultValue = default!)
        {
            lock (_lock)
            {
                if (_json.TryGetPropertyValue(key, out var node) && node is not null)
                {
                    try { return node.Deserialize<T>()!; } catch { }
                }
                return defaultValue;
            }
        }

        public static void Set<T>(string key, T value)
        {
            lock (_lock)
            {
                _json[key] = JsonSerializer.SerializeToNode(value);
                File.WriteAllText(FilePath, _json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}