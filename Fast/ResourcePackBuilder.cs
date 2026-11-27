using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace FastCraft.Fast
{
    /// <summary>What a branding run did, for the log and for the caller's summary.</summary>
    public sealed class BrandingResult
    {
        public bool PackWritten;
        public bool MenuWritten;
        public bool PackEnabled;
        public string PackPath = "";
        public readonly List<string> Branded = new List<string>();
        public readonly List<string> Absent = new List<string>();
        public string Note = "";
    }

    /// <summary>
    /// Draws and writes FastCraft's resource pack into the game's own resourcepacks folder.
    /// The textures are generated at twice the size the game ships and only for paths the
    /// game's asset index actually has, so the pack fits this version instead of a guess at it.
    /// </summary>
    public sealed class ResourcePackBuilder
    {
        public const string PackFileName = "FastCraft.zip";
        private const string Description = "FastCraft: the launcher's look";

        private sealed class TextureTarget
        {
            public readonly string Path;
            public readonly int DefaultWidth;
            public readonly int DefaultHeight;
            public readonly Func<int, int, byte[]> Draw;

            public TextureTarget(string path, int defaultWidth, int defaultHeight, Func<int, int, byte[]> draw)
            {
                Path = path;
                DefaultWidth = defaultWidth;
                DefaultHeight = defaultHeight;
                Draw = draw;
            }
        }

        public static string PackPath(string gameDirectory)
        {
            return System.IO.Path.Combine(gameDirectory, "resourcepacks", PackFileName);
        }

        public BrandingResult Build(string gameDirectory, string clientJar, string assetIndexFile, Action<string> log)
        {
            var result = new BrandingResult();
            var assets = new GameAssets(assetIndexFile);

            // Everything that needs WPF lives in this one call, so the drawing always happens
            // on the dispatcher's thread no matter which thread the install runs on.
            var files = OnUiThread(() => DrawAll(assets, result));

            var directory = System.IO.Path.Combine(gameDirectory, "resourcepacks");
            Directory.CreateDirectory(directory);

            var path = PackPath(gameDirectory);
            var temp = path + ".tmp";

            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    foreach (var pair in files)
                    {
                        Add(archive, pair.Key, pair.Value);
                    }

                    var format = GameAssets.GetResourcePackFormat(clientJar);
                    Add(archive, "pack.mcmeta", Encoding.UTF8.GetBytes(PackMeta(format, log)));
                    Add(archive, "assets/minecraft/texts/splashes.txt", Encoding.UTF8.GetBytes(Splashes()));

                    // A lang file is resolved per pack, not merged: writing one that holds only
                    // our key would replace every other string with its translation key. The key
                    // is merged into the version's own en_us.json instead, and the file is left
                    // out when that cannot be read, rather than an interface of raw keys.
                    var language = Language(clientJar, log);
                    if (!string.IsNullOrEmpty(language))
                    {
                        Add(archive, "assets/minecraft/lang/en_us.json", Encoding.UTF8.GetBytes(language));
                    }
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                result.PackWritten = true;
                result.PackPath = path;

                log("[brand] Pack written: " + result.Branded.Count + " texture(s) branded, "
                    + result.Absent.Count + " path(s) this version does not ship"
                    + (result.Absent.Count > 0 ? " (" + string.Join(", ", result.Absent.ToArray()) + ")" : "") + ".");
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                }
                throw new IOException("the resource pack could not be written: " + ex.Message, ex);
            }

            return result;
        }

        private static Dictionary<string, byte[]> DrawAll(GameAssets assets, BrandingResult result)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            var targets = new List<TextureTarget>
            {
                new TextureTarget("textures/gui/title/minecraft.png", 274, 44,
                    (width, height) => BrandArt.Wordmark(width, height)),
                new TextureTarget("textures/gui/title/edition.png", 110, 16,
                    (width, height) => BrandArt.EditionLabel(width, height)),
                new TextureTarget("textures/gui/sprites/widget/button.png", 200, 20,
                    (width, height) => BrandArt.Button(width, height, BrandArt.ButtonKind.Normal)),
                new TextureTarget("textures/gui/sprites/widget/button_highlighted.png", 200, 20,
                    (width, height) => BrandArt.Button(width, height, BrandArt.ButtonKind.Highlighted)),
                new TextureTarget("textures/gui/sprites/widget/button_disabled.png", 200, 20,
                    (width, height) => BrandArt.Button(width, height, BrandArt.ButtonKind.Disabled)),
                new TextureTarget("textures/gui/menu_background.png", 32, 32,
                    (width, height) => BrandArt.Tile(width, height)),
                new TextureTarget("textures/gui/options_background.png", 16, 16,
                    (width, height) => BrandArt.Tile(width, height))
            };

            foreach (var target in targets)
            {
                int width;
                int height;
                var present = assets.TryGetSize("minecraft/" + target.Path, out width, out height);
                if (!present)
                {
                    width = target.DefaultWidth;
                    height = target.DefaultHeight;
                    result.Absent.Add(System.IO.Path.GetFileName(target.Path));
                }
                else
                {
                    result.Branded.Add(System.IO.Path.GetFileName(target.Path));
                }

                width = Math.Min(2048, Math.Max(1, width * 2));
                height = Math.Min(2048, Math.Max(1, height * 2));

                files["assets/minecraft/" + target.Path] = target.Draw(width, height);
            }

            // The title screen's panorama is a cubemap that spins; dark gradients with a little
            // light read as the launcher's background rather than someone else's scenery.
            for (int face = 0; face < 6; face++)
            {
                files["assets/minecraft/textures/gui/title/background/panorama_" + face + ".png"] =
                    BrandArt.PanoramaFace(512, face == 0 || face == 4);
            }

            // The pack thumbnail, so it is recognisable in the resource pack list.
            files["pack.png"] = BrandArt.PackIcon(128);

            return files;
        }

        /// <summary>
        /// pack.mcmeta in the shape Minecraft has wanted since 25w31a. min_format and
        /// max_format take an integer or a two-integer array [major, minor] — never a decimal,
        /// and never a value below 65 without the old pack_format. Both mistakes are what made
        /// the game label this pack "broken or incompatible": "97.1" is not a valid value, and a
        /// fallback of 1 is below the allowed floor.
        /// </summary>
        private static string PackMeta(string format, Action<string> log)
        {
            string min = "65";
            string max = "9999";

            int major;
            int minor;
            if (TrySplitFormat(format, out major, out minor))
            {
                // 97.1 becomes [97, 0] .. [97, 1]: the whole major, up to the exact minor.
                min = "[" + major + ", 0]";
                max = "[" + major + ", " + minor + "]";
                log("[brand] Pack format " + major + "." + minor + " read from the client jar.");
            }
            else
            {
                log("[brand] The client jar did not give a pack format; the pack declares 65 and up, which the game accepts.");
            }

            return "{\n  \"pack\": {\n    \"description\": \"" + Description + "\",\n"
                   + "    \"min_format\": " + min + ",\n    \"max_format\": " + max + "\n  }\n}\n";
        }

        private static bool TrySplitFormat(string format, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrEmpty(format)) return false;

            var parts = format.Split('.');
            if (!int.TryParse(parts[0], out major) || major <= 0) return false;
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            return true;
        }

        private static string Splashes()
        {
            var lines = new[]
            {
                "FastCraft edition!",
                "Powered by Fabric!",
                "Faster than vanilla!",
                "Rendered by Sodium!",
                "Now with fewer stutters!",
                "Mojang made it, FastCraft made it quick!",
                "Also plays offline!",
                "Sodium, Lithium and a little Krypton!",
                "Chunk loading, but with a plan!",
                "Your frame rate called. It is fine now.",
                "Made of amber and patience!",
                "Does this menu look faster?",
                "Ticks optimised, blocks culled!",
                "One launcher, one world, no waiting!",
                "Also try the pause menu!"
            };

            var builder = new StringBuilder();
            foreach (var line in lines) builder.AppendLine(line);
            return builder.ToString();
        }

        /// <summary>
        /// The version's own en_us.json with menu.game replaced, so the pause screen header says
        /// FastCraft while every other string still comes from the game.
        /// </summary>
        private static string Language(string clientJar, Action<string> log)
        {
            string vanilla;
            if (!GameAssets.TryReadJarText(clientJar, "assets/minecraft/lang/en_us.json", out vanilla))
            {
                log("[brand] The game's en_us.json was not readable, so only FancyMenu names the pause screen.");
                return null;
            }

            try
            {
                var node = JsonNode.Parse(vanilla) as JsonObject;
                if (node == null)
                {
                    log("[brand] The game's en_us.json is not a JSON object, so only FancyMenu names the pause screen.");
                    return null;
                }
                node["menu.game"] = "FastCraft \u00b7 Game Menu";
                return node.ToJsonString();
            }
            catch (JsonException)
            {
                log("[brand] The game's en_us.json could not be parsed, so only FancyMenu names the pause screen.");
                return null;
            }
        }

        private static void Add(ZipArchive archive, string name, byte[] content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(content, 0, content.Length);
            }
        }

        /// <summary>
        /// WPF drawing is dispatcher-bound. When the install runs on a worker thread (it does,
        /// after the first await) the work is handed to the UI thread, which is free because
        /// the launcher is waiting on this task.
        /// </summary>
        private static T OnUiThread<T>(Func<T> work)
        {
            var application = Application.Current;
            if (application == null || application.Dispatcher.CheckAccess()) return work();
            return application.Dispatcher.Invoke(work);
        }
    }
}
