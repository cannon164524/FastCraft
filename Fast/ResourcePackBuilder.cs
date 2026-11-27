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
        public string TitleSize = "";
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
            public readonly string Namespace;
            public readonly string Path;
            public readonly int DefaultWidth;
            public readonly int DefaultHeight;
            public readonly int Scale;
            public readonly bool InIndex;
            public readonly Func<int, int, byte[]> Draw;

            public TextureTarget(string path, int defaultWidth, int defaultHeight, Func<int, int, byte[]> draw)
                : this("minecraft", path, defaultWidth, defaultHeight, 2, true, draw)
            {
            }

            public TextureTarget(string path, int defaultWidth, int defaultHeight, int scale,
                Func<int, int, byte[]> draw)
                : this("minecraft", path, defaultWidth, defaultHeight, scale, true, draw)
            {
            }

            public TextureTarget(string @namespace, string path, int defaultWidth, int defaultHeight, int scale,
                bool inIndex, Func<int, int, byte[]> draw)
            {
                Namespace = @namespace;
                Path = path;
                DefaultWidth = defaultWidth;
                DefaultHeight = defaultHeight;
                Scale = Math.Max(1, scale);
                InIndex = inIndex;
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
                    + (result.Absent.Count > 0 ? " (" + string.Join(", ", result.Absent.ToArray()) + ")" : "")
                    + (string.IsNullOrEmpty(result.TitleSize) ? "" : "; title logo at " + result.TitleSize + ", the game's own size")
                    + ".");
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
                // The title's own two textures are drawn at exactly the size this game ships
                // (274×44 and 110×16). The title screen blits them at that size rather than
                // scaling, so a texture at twice the pixels was sampled from its top-left corner
                // and appeared cut off. The sprites below are nine-sliced and safe at twice.
                new TextureTarget("textures/gui/title/minecraft.png", 274, 44, 1,
                    (width, height) => BrandArt.Wordmark(width, height)),
                new TextureTarget("textures/gui/title/edition.png", 110, 16, 1,
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
                    (width, height) => BrandArt.Tile(width, height)),

                // The amber tier's own art. It lives in the fastcraft namespace, so the models the
                // mod ships for copper are replaced by FastCraft's own ore, ingot and tools.
                new TextureTarget("fastcraft", "textures/block/amber_ore.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberOre(width, height)),
                new TextureTarget("fastcraft", "textures/item/raw_amber.png", 16, 16, 2, false,
                    (width, height) => BrandArt.RawAmber(width, height)),
                new TextureTarget("fastcraft", "textures/item/amber_ingot.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberIngot(width, height)),
                new TextureTarget("fastcraft", "textures/item/amber_sword.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberTool(width, height, "sword")),
                new TextureTarget("fastcraft", "textures/item/amber_pickaxe.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberTool(width, height, "pickaxe")),
                new TextureTarget("fastcraft", "textures/item/amber_axe.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberTool(width, height, "axe")),
                new TextureTarget("fastcraft", "textures/item/amber_shovel.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberTool(width, height, "shovel")),
                new TextureTarget("fastcraft", "textures/item/amber_hoe.png", 16, 16, 2, false,
                    (width, height) => BrandArt.AmberTool(width, height, "hoe"))
            };

            foreach (var target in targets)
            {
                // Initialised because TryGetSize only runs when the texture is in the asset index;
                // with the short-circuit the compiler cannot prove the two are assigned.
                int width = 0;
                int height = 0;
                var present = target.InIndex
                              && assets.TryGetSize(target.Namespace + "/" + target.Path, out width, out height);
                if (!present)
                {
                    width = target.DefaultWidth;
                    height = target.DefaultHeight;
                    if (target.InIndex) result.Absent.Add(System.IO.Path.GetFileName(target.Path));
                }
                else
                {
                    result.Branded.Add(System.IO.Path.GetFileName(target.Path));
                }

                width = Math.Min(2048, Math.Max(1, width * target.Scale));
                height = Math.Min(2048, Math.Max(1, height * target.Scale));

                if (string.Equals(target.Path, "textures/gui/title/minecraft.png", StringComparison.Ordinal))
                {
                    result.TitleSize = width + "×" + height;
                }

                files["assets/" + target.Namespace + "/" + target.Path] = target.Draw(width, height);
            }

            // The mod's items point at copper's models; these files replace those pointers, so the
            // ore, the ingot and the five tools are drawn and named as amber instead.
            foreach (var pair in AmberJson())
            {
                files[pair.Key] = Encoding.UTF8.GetBytes(pair.Value);
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
        /// The mod's amber items are pointed at copper's models so they exist before this pack does.
        /// These files replace those pointers: the ore, the ingot and the five tools are drawn from
        /// FastCraft's own art and the block is named Amber Ore in the game.
        /// </summary>
        private static Dictionary<string, string> AmberJson()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);

            files["assets/fastcraft/blockstates/amber_ore.json"] =
                "{\"variants\":{\"\":{\"model\":\"fastcraft:block/amber_ore\"}}}";
            files["assets/fastcraft/models/block/amber_ore.json"] =
                "{\"parent\":\"minecraft:block/cube_all\",\"textures\":{\"all\":\"fastcraft:block/amber_ore\"}}";

            var generated = new[]
            {
                "raw_amber", "amber_ingot"
            };
            foreach (var item in generated)
            {
                files["assets/fastcraft/models/item/" + item + ".json"] =
                    "{\"parent\":\"minecraft:item/generated\",\"textures\":{\"layer0\":\"fastcraft:item/" + item + "\"}}";
            }

            var tools = new[]
            {
                "amber_sword", "amber_pickaxe", "amber_axe", "amber_shovel", "amber_hoe"
            };
            foreach (var tool in tools)
            {
                files["assets/fastcraft/models/item/" + tool + ".json"] =
                    "{\"parent\":\"minecraft:item/handheld\",\"textures\":{\"layer0\":\"fastcraft:item/" + tool + "\"}}";
            }

            files["assets/fastcraft/items/amber_ore.json"] =
                "{\"model\":{\"type\":\"minecraft:model\",\"model\":\"fastcraft:block/amber_ore\"}}";
            foreach (var item in generated)
            {
                files["assets/fastcraft/items/" + item + ".json"] =
                    "{\"model\":{\"type\":\"minecraft:model\",\"model\":\"fastcraft:item/" + item + "\"}}";
            }
            foreach (var tool in tools)
            {
                files["assets/fastcraft/items/" + tool + ".json"] =
                    "{\"model\":{\"type\":\"minecraft:model\",\"model\":\"fastcraft:item/" + tool + "\"}}";
            }

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
