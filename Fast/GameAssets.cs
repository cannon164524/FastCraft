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
        /// Text of a file inside the client jar, or false when this version does not ship it.
        /// The branding needs it for the vanilla language file, which must be merged rather than
        /// replaced: the game resolves a lang file per pack, so a pack that carries only its own
        /// key would blank every other string in the interface.
        /// </summary>
        public static bool TryReadJarText(string clientJar, string entryName, out string text)
        {
            text = null;
            try
            {
                if (string.IsNullOrEmpty(clientJar) || !File.Exists(clientJar)) return false;

                using (var archive = ZipFile.OpenRead(clientJar))
                {
                    var entry = archive.GetEntry(entryName);
                    if (entry == null) return false;

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        text = reader.ReadToEnd();
                        return !string.IsNullOrEmpty(text);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The resource pack format of this exact version as "major.minor", from the
        /// "version.json" the client jar carries. Since 25w31a that object is
        /// pack_version.resource = { major, minor } with string values; older jars carry a
        /// single number. Both are read: returning null here used to fall back to a range the
        /// game labels "broken or incompatible".
        /// </summary>
        public static string GetResourcePackFormat(string clientJar)
        {
            try
            {
                using (var document = OpenVersionJson(clientJar))
                {
                    if (document == null) return null;

                    JsonElement packVersion;
                    if (!document.RootElement.TryGetProperty("pack_version", out packVersion)) return null;
                    if (packVersion.ValueKind != JsonValueKind.Object) return null;

                    JsonElement resource;
                    if (!packVersion.TryGetProperty("resource", out resource)) return null;

                    if (resource.ValueKind == JsonValueKind.Number)
                    {
                        return resource.GetRawText() + ".0";
                    }
                    if (resource.ValueKind != JsonValueKind.Object) return null;

                    var major = ReadInt(resource, "major");
                    var minor = ReadInt(resource, "minor");
                    if (major <= 0) return null;
                    return major + "." + minor;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The data version of this exact version, which is the "version" line options.txt
        /// needs. The game replaces a file whose version line is missing with its defaults,
        /// taking the resource pack selection with it.
        /// </summary>
        public static int GetDataVersion(string clientJar)
        {
            try
            {
                using (var document = OpenVersionJson(clientJar))
                {
                    if (document == null) return 0;
                    return ReadInt(document.RootElement, "world_version");
                }
            }
            catch
            {
                return 0;
            }
        }

        private static JsonDocument OpenVersionJson(string clientJar)
        {
            if (string.IsNullOrEmpty(clientJar) || !File.Exists(clientJar)) return null;

            using (var archive = ZipFile.OpenRead(clientJar))
            {
                var entry = archive.GetEntry("version.json");
                if (entry == null) return null;

                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream))
                {
                    return JsonDocument.Parse(reader.ReadToEnd());
                }
            }
        }

        private static int ReadInt(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return 0;

            if (value.ValueKind == JsonValueKind.Number)
            {
                int number;
                return value.TryGetInt32(out number) ? number : 0;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                int number;
                return int.TryParse(value.GetString(), out number) ? number : 0;
            }
            return 0;
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
