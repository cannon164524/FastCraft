using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FastCraft.Core;

namespace FastCraft.Fast
{
    /// <summary>
    /// What the installed game actually ships, read from the files FastCraft already has:
    /// the texture paths of its asset index (with their pixel sizes) and the class names of
    /// its screens, taken from the client jar. The branding is built from this, so it
    /// targets what exists in this version instead of what a wiki said about another one.
    /// </summary>
    public sealed class GameAssets
    {
        private readonly Dictionary<string, string> _objects =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public GameAssets(string assetIndexFile)
        {
            try
            {
                if (string.IsNullOrEmpty(assetIndexFile) || !File.Exists(assetIndexFile)) return;

                using (var document = JsonDocument.Parse(File.ReadAllText(assetIndexFile)))
                {
                    JsonElement objects;
                    if (!document.RootElement.TryGetProperty("objects", out objects)) return;
                    if (objects.ValueKind != JsonValueKind.Object) return;

                    foreach (var asset in objects.EnumerateObject())
                    {
                        var hash = JsonHelp.GetString(asset.Value, "hash");
                        if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
                        _objects[asset.Name] = Path.Combine(Locations.Assets, "objects", hash.Substring(0, 2), hash);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>Pixel size of a texture in the game's own resources, if it is there.</summary>
        public bool TryGetSize(string assetPath, out int width, out int height)
        {
            width = 0;
            height = 0;

            string file;
            if (!_objects.TryGetValue(assetPath, out file) || !File.Exists(file)) return false;

            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[24];
                    if (stream.Read(header, 0, header.Length) < header.Length) return false;

                    // PNG: 8 byte signature, 4 byte length, "IHDR", then width and height.
                    if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return false;
                    if (header[12] != 0x49 || header[13] != 0x48 || header[14] != 0x44 || header[15] != 0x52) return false;

                    width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    return width > 0 && height > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The resource pack format of this exact version, from the "version.json" the client
        /// jar carries. Returned as raw text so a format like 63.1 stays intact.
        /// </summary>
        public static string GetResourcePackFormat(string clientJar)
        {
            try
            {
                if (string.IsNullOrEmpty(clientJar) || !File.Exists(clientJar)) return null;

                using (var archive = ZipFile.OpenRead(clientJar))
                {
                    var entry = archive.GetEntry("version.json");
                    if (entry == null) return null;

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        using (var document = JsonDocument.Parse(reader.ReadToEnd()))
                        {
                            JsonElement packVersion;
                            if (!document.RootElement.TryGetProperty("pack_version", out packVersion)) return null;
                            if (packVersion.ValueKind != JsonValueKind.Object) return null;

                            JsonElement resource;
                            if (!packVersion.TryGetProperty("resource", out resource)) return null;
                            if (resource.ValueKind != JsonValueKind.Number) return null;

                            var raw = resource.GetRawText();
                            return string.IsNullOrEmpty(raw) ? null : raw;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Class names of the game's screens that the menu set-up needs, looked up by simple
        /// name so a package rename between versions does not matter.
        /// </summary>
        public static Dictionary<string, string> FindScreenClasses(string clientJar, params string[] simpleNames)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                if (string.IsNullOrEmpty(clientJar) || !File.Exists(clientJar)) return found;

                using (var archive = ZipFile.OpenRead(clientJar))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var name = entry.FullName;
                        if (!name.EndsWith(".class", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.IndexOf("/client/gui/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (name.IndexOf('$') >= 0) continue;

                        foreach (var simple in simpleNames)
                        {
                            if (!name.EndsWith("/" + simple + ".class", StringComparison.Ordinal)) continue;
                            if (found.ContainsKey(simple)) continue;
                            found[simple] = name.Substring(0, name.Length - ".class".Length).Replace('/', '.');
                        }
                    }
                }
            }
            catch
            {
            }

            return found;
        }
    }
}
