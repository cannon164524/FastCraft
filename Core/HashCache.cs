using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastCraft.Core
{
    /// <summary>
    /// Remembers which files were verified, keyed by size + last-write time, so the
    /// launcher only re-hashes files that actually changed. This is the single biggest
    /// reason repeat launches are quick: ~3.000 asset files are checked with two stat()
    /// calls instead of SHA-1 reads.
    /// </summary>
    public sealed class HashCache
    {
        public sealed class Entry
        {
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("ticks")] public long Ticks { get; set; }
            [JsonPropertyName("sha1")] public string Sha1 { get; set; }
        }

        private readonly string _path;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private bool _dirty;

        private HashCache(string path)
        {
            _path = path;
        }

        public static HashCache Load(string path)
        {
            var cache = new HashCache(path);
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json);
                    if (data != null)
                    {
                        foreach (var pair in data)
                        {
                            cache._entries[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            catch
            {
            }
            return cache;
        }

        public bool IsVerified(string path, long size, string sha1)
        {
            if (string.IsNullOrEmpty(sha1)) return false;
            var full = Path.GetFullPath(path);
            Entry entry;
            lock (_lock)
            {
                if (!_entries.TryGetValue(full, out entry)) return false;
            }
            if (entry.Size != size || entry.Sha1 == null) return false;
            if (!string.Equals(entry.Sha1, sha1, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (!File.Exists(full)) return false;
                return File.GetLastWriteTimeUtc(full).Ticks == entry.Ticks;
            }
            catch
            {
                return false;
            }
        }

        public void Remember(string path, string sha1)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var info = new FileInfo(full);
                if (!info.Exists) return;
                lock (_lock)
                {
                    _entries[full] = new Entry { Size = info.Length, Ticks = info.LastWriteTimeUtc.Ticks, Sha1 = sha1 };
                    _dirty = true;
                }
            }
            catch
            {
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                if (!_dirty) return;
                try
                {
                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    var json = JsonSerializer.Serialize(_entries);
                    File.WriteAllText(_path, json);
                    _dirty = false;
                }
                catch
                {
                }
            }
        }
    }
}
