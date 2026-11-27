using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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
                    Add(archive, "pack.mcmeta", Encoding.UTF8.GetBytes(PackMeta(format)));
                    Add(archive, "assets/minecraft/texts/splashes.txt", Encoding.UTF8.GetBytes(Splashes()));
                    Add(archive, "assets/minecraft/lang/en_us.json", Encoding.UTF8.GetBytes(Language()));
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

        private static string PackMeta(string format)
        {
            if (!string.IsNullOrEmpty(format))
            {
                return "{\n  \"pack\": {\n    \"description\": \"" + Description + "\",\n"
                       + "    \"min_format\": " + format + ",\n    \"max_format\": " + format + "\n  }\n}\n";
            }

            // No version.json to read: a range wide enough to be accepted, so the pack still
            // loads instead of being refused as incompatible.
            return "{\n  \"pack\": {\n    \"description\": \"" + Description + "\",\n"
                   + "    \"pack_format\": 1,\n    \"supported_formats\": {\n"
                   + "      \"min_inclusive\": 1,\n      \"max_inclusive\": 999\n    }\n  }\n}\n";
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

        private static string Language()
        {
            return "{\n  \"menu.game\": \"FastCraft \u00b7 Game Menu\"\n}\n";
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
