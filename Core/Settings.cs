using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastCraft.Core
{
    public sealed class Settings
    {
        [JsonPropertyName("memoryMb")] public int MemoryMb { get; set; } = 4096;
        [JsonPropertyName("javaPath")] public string JavaPath { get; set; } = "";
        [JsonPropertyName("extraJvmArgs")] public string ExtraJvmArgs { get; set; } = "";
        [JsonPropertyName("lastVersion")] public string LastVersion { get; set; } = "";
        [JsonPropertyName("downloadJava")] public bool DownloadJava { get; set; } = true;
        [JsonPropertyName("downloadParallelism")] public int DownloadParallelism { get; set; } = 32;
        [JsonPropertyName("turboJvmFlags")] public bool TurboJvmFlags { get; set; } = true;
        [JsonPropertyName("keepLauncherOpen")] public bool KeepLauncherOpen { get; set; } = true;
        [JsonPropertyName("checkForUpdates")] public bool CheckForUpdates { get; set; } = true;
        [JsonPropertyName("showSnapshots")] public bool ShowSnapshots { get; set; } = false;

        public static Settings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                    if (settings != null) return settings;
                }
            }
            catch
            {
            }
            return new Settings();
        }

        public void Save(string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
