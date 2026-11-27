using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;
using FastCraft.Game;

namespace FastCraft.Fast
{
    /// <summary>
    /// What a launch of the FastCraft edition came to: the main class, the jars to put on the
    /// classpath, and a line for the log. When <see cref="Active"/> is false the launcher starts
    /// plain vanilla instead and explains why.
    /// </summary>
    public sealed class FastEditionResult
    {
        public bool Active;
        public string MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient";
        public string Summary = "";
        public string Problem = "";
        public readonly List<string> LibraryPaths = new List<string>();
        public readonly List<string> JvmArgs = new List<string>();
    }

    /// <summary>
    /// Builds the FastCraft edition on top of a vanilla installation: the Fabric loader, the
    /// performance mods from Modrinth, and a performance preset for a fresh game folder. The
    /// game files themselves are still Mojang's; what makes it FastCraft is what is added and
    /// how it is started.
    ///
    /// What to install is looked up once and then cached for <see cref="CacheLifetime"/>, so a
    /// launch behind a warm cache makes no catalogue requests at all: it only checks the jars
    /// that are already on disk, which is what keeps the launcher's part of a launch short.
    /// </summary>
    public sealed class FastEditionInstaller
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

        /// <summary>
        /// Part of the cache file name. Bump it when the mod list changes, so an edition that
        /// was resolved before the change is looked up again instead of being reused for the
        /// rest of the cache lifetime.
        /// </summary>
        private const int SetVersion = 2;

        private readonly Downloader _downloader;
        private readonly FabricInstaller _fabric = new FabricInstaller();
        private readonly ModCatalog _catalog = new ModCatalog();

        public FastEditionInstaller(Downloader downloader)
        {
            _downloader = downloader;
        }

        private sealed class EditionCache
        {
            public string LoaderVersion;
            public string MainClass;
            public DateTime ResolvedUtc;
            public readonly List<string> JvmArgs = new List<string>();
            public readonly List<DownloadItem> Libraries = new List<DownloadItem>();
            public readonly List<ModFile> Mods = new List<ModFile>();
        }

        public async Task<FastEditionResult> EnsureAsync(string gameVersion, string gameDirectory, string clientJar,
            string assetIndexFile, Action<string> log, CancellationToken cancellationToken)
        {
            var result = new FastEditionResult();

            if (!FastEdition.Supports(gameVersion))
            {
                result.Problem = "the FastCraft edition is built for " + FastEdition.GameVersion + " only";
                return result;
            }

            var cachePath = Path.Combine(Locations.CacheDir, "fast-edition-" + gameVersion + "-v" + SetVersion + ".txt");
            var cache = ReadCache(cachePath);

            string loaderVersion;
            string mainClass;
            List<DownloadItem> libraryItems;
            List<ModFile> mods;

            if (cache != null)
            {
                loaderVersion = cache.LoaderVersion;
                mainClass = cache.MainClass;
                libraryItems = cache.Libraries;
                mods = cache.Mods;
                result.JvmArgs.AddRange(cache.JvmArgs);
                log("[fast] Edition list from " + cache.ResolvedUtc.ToLocalTime().ToString("g")
                    + ": loader " + loaderVersion + ", " + mods.Count + " mods. No catalogue lookups needed.");
            }
            else
            {
                var profile = await _fabric.ResolveAsync(gameVersion, cancellationToken).ConfigureAwait(false);
                if (profile == null)
                {
                    result.Problem = "Fabric could not be reached";
                    return result;
                }
                if (!string.IsNullOrEmpty(profile.Problem))
                {
                    result.Problem = profile.Problem;
                    return result;
                }

                loaderVersion = profile.LoaderVersion;
                mainClass = profile.MainClass;
                log("[fast] Fabric loader " + loaderVersion + " for " + gameVersion + ".");

                // The loader's own jars live in the shared libraries folder, like every other
                // library, so a second launch downloads nothing.
                libraryItems = new List<DownloadItem>();
                foreach (var library in profile.Libraries)
                {
                    if (string.IsNullOrEmpty(library.ArtifactPath) || string.IsNullOrEmpty(library.ArtifactUrl)) continue;
                    libraryItems.Add(new DownloadItem
                    {
                        Url = library.ArtifactUrl,
                        Dest = Path.Combine(Locations.Libraries, library.ArtifactPath.Replace('/', Path.DirectorySeparatorChar)),
                        Sha1 = library.ArtifactSha1,
                        Size = library.ArtifactSize
                    });
                }

                foreach (var entry in profile.JvmArgs)
                {
                    if (!VersionParser.RulesAllow(entry.Rules, null)) continue;
                    foreach (var value in entry.Values)
                    {
                        if (string.IsNullOrEmpty(value)) continue;
                        result.JvmArgs.Add(value
                            .Replace("${launcher_name}", "FastCraft")
                            .Replace("${launcher_version}", "1.0.0"));
                    }
                }

                mods = await _catalog.ResolveAsync(gameVersion, log, cancellationToken).ConfigureAwait(false);

                // A lookup that found no mod at all is not worth remembering: it usually means
                // Modrinth could not be reached, and the next launch should try again.
                if (mods.Count > 0)
                {
                    WriteCache(cachePath, loaderVersion, mainClass, result.JvmArgs, libraryItems, mods);
                }
            }

            foreach (var library in libraryItems) result.LibraryPaths.Add(library.Dest);
            if (libraryItems.Count > 0)
            {
                log("[fast] Checking " + libraryItems.Count + " loader jars…");
                await _downloader.RunAsync(libraryItems, cancellationToken).ConfigureAwait(false);
            }

            var modsDirectory = Path.Combine(gameDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            log("[fast] Mods folder: " + modsDirectory + " (" + CountJars(modsDirectory) + " jar(s) before this launch).");

            var installed = await InstallModsAsync(modsDirectory, mods, log, cancellationToken).ConfigureAwait(false);
            installed += await InstallEmbeddedModAsync(modsDirectory, log, cancellationToken).ConfigureAwait(false);
            log("[fast] Mods folder now has " + CountJars(modsDirectory) + " jar(s), " + installed + " from the FastCraft set.");

            // A performance preset, written only into a game folder that has none: options the
            // player changed are never overwritten.
            WriteOptionsPreset(gameDirectory, log);

            // 3. The look inside the game: the resource pack FastCraft draws itself, and the
            //    FancyMenu set-up that adds what a pack cannot. A failure here is a log line,
            //    never a stopped launch.
            try
            {
                var branding = Branding.Ensure(gameVersion, gameDirectory, clientJar, assetIndexFile, log);
                if (!string.IsNullOrEmpty(branding.Problem))
                {
                    log("[fast] Branding skipped: " + branding.Problem + ".");
                }
                else
                {
                    log("[fast] Branding: " + branding.Summary + ".");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log("[fast] Branding could not be applied: " + ex.Message);
            }

            result.Active = true;
            result.MainClass = mainClass;
            result.Summary = "Fabric loader " + loaderVersion + ", " + installed + " mods, performance preset";
            return result;
        }

        private const string EmbeddedModName = "fastcraft-amber.jar";

        /// <summary>
        /// Writes FastCraft's own mod — the amber tier between copper and iron — out of the
        /// launcher's resources into the mods folder. It has no download of its own: the jar is
        /// built with the launcher and carried inside it, so the content works even while the
        /// repository is private. A launcher built without Gradle simply has no resource here.
        /// </summary>
        private static async Task<int> InstallEmbeddedModAsync(string modsDirectory, Action<string> log,
            CancellationToken cancellationToken)
        {
            try
            {
                var assembly = typeof(FastEditionInstaller).Assembly;

                Stream stream = null;
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith(EmbeddedModName, StringComparison.OrdinalIgnoreCase)) continue;
                    stream = assembly.GetManifestResourceStream(name);
                    break;
                }

                if (stream == null)
                {
                    log("[fast] This build carries no amber mod; the edition works without it.");
                    return 0;
                }

                var destination = Path.Combine(modsDirectory, EmbeddedModName);
                var temp = destination + ".tmp";

                using (stream)
                using (var file = File.Create(temp))
                {
                    await stream.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temp, destination);
                log("[fast] Installed FastCraft's own mod: " + EmbeddedModName + ".");
                return 1;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log("[fast] The amber mod could not be installed: " + ex.Message);
                return 0;
            }
        }

        private static int CountJars(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.jar").Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Downloads the resolved jars and removes the ones an earlier FastCraft put there that
        /// are no longer part of the set (a mod update changes the file name). Files the player
        /// added himself are never touched: only names in the manifest are ever deleted.
        /// </summary>
        private async Task<int> InstallModsAsync(string modsDirectory, List<ModFile> files, Action<string> log,
            CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(modsDirectory, FastEdition.ManifestName);
            var previous = ReadManifest(manifestPath);

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files) keep.Add(file.FileName);
            keep.Add(EmbeddedModName);

            foreach (var name in previous)
            {
                if (string.IsNullOrEmpty(name) || keep.Contains(name)) continue;
                try
                {
                    var path = Path.Combine(modsDirectory, name);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        log("[fast] Removed an older " + name + ".");
                    }
                }
                catch
                {
                }
            }

            if (files.Count == 0)
            {
                log("[fast] No performance mod has a " + FastEdition.GameVersion
                    + " build yet; the edition runs on the loader and the preset alone.");
                return 0;
            }

            var batch = new List<DownloadItem>();
            foreach (var file in files)
            {
                batch.Add(new DownloadItem
                {
                    Url = file.Url,
                    Dest = Path.Combine(modsDirectory, file.FileName),
                    Sha1 = file.Sha1,
                    Size = file.Size
                });
            }
            log("[fast] Checking " + batch.Count + " mod jars…");
            await _downloader.RunAsync(batch, cancellationToken).ConfigureAwait(false);

            WriteManifest(manifestPath, keep);
            foreach (var file in files)
            {
                log("[fast] " + file.Name + " " + (file.Version ?? "") + " → " + file.FileName);
            }
            return files.Count;
        }

        /// <summary>
        /// A brand new game folder gets options that suit a client built for speed. A folder
        /// that already has options.txt is left exactly as the player left it.
        /// </summary>
        private static void WriteOptionsPreset(string gameDirectory, Action<string> log)
        {
            try
            {
                var path = Path.Combine(gameDirectory, "options.txt");
                if (File.Exists(path)) return;

                var preset = new[]
                {
                    "maxFps:260",
                    "enableVsync:false",
                    "renderDistance:12",
                    "simulationDistance:10",
                    "graphicsMode:1",
                    "particles:2",
                    "entityShadows:false",
                    "mipmapLevels:4",
                    "useNativeTransport:true",
                    "ao:true"
                };
                File.WriteAllLines(path, preset);
                log("[fast] Wrote a performance options.txt for the new game folder: 12 chunks of render distance, fewer particles, no entity shadows, vsync off.");
            }
            catch
            {
            }
        }

        private static EditionCache ReadCache(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var cache = new EditionCache();
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;
                    switch (parts[0])
                    {
                        case "loader":
                            cache.LoaderVersion = parts[1];
                            break;
                        case "main":
                            cache.MainClass = parts[1];
                            break;
                        case "resolved":
                            cache.ResolvedUtc = new DateTime(ParseLong(parts[1]), DateTimeKind.Utc);
                            break;
                        case "jvm":
                            cache.JvmArgs.Add(parts[1]);
                            break;
                        case "lib":
                            if (parts.Length < 5) break;
                            cache.Libraries.Add(new DownloadItem
                            {
                                Dest = parts[1],
                                Url = parts[2],
                                Sha1 = string.IsNullOrEmpty(parts[3]) ? null : parts[3],
                                Size = ParseLong(parts[4])
                            });
                            break;
                        case "mod":
                            if (parts.Length < 7) break;
                            cache.Mods.Add(new ModFile
                            {
                                Name = parts[1],
                                FileName = parts[2],
                                Url = parts[3],
                                Sha1 = string.IsNullOrEmpty(parts[4]) ? null : parts[4],
                                Size = ParseLong(parts[5]),
                                Version = parts[6]
                            });
                            break;
                    }
                }

                if (string.IsNullOrEmpty(cache.LoaderVersion) || string.IsNullOrEmpty(cache.MainClass)) return null;

                var age = DateTime.UtcNow - cache.ResolvedUtc;
                if (age < TimeSpan.Zero || age > CacheLifetime) return null;
                return cache;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCache(string path, string loaderVersion, string mainClass, List<string> jvmArgs,
            List<DownloadItem> libraries, List<ModFile> mods)
        {
            try
            {
                Directory.CreateDirectory(Locations.CacheDir);

                var lines = new List<string>
                {
                    "loader|" + loaderVersion,
                    "main|" + mainClass,
                    "resolved|" + DateTime.UtcNow.Ticks
                };
                foreach (var argument in jvmArgs) lines.Add("jvm|" + argument);
                foreach (var library in libraries)
                {
                    lines.Add("lib|" + library.Dest + "|" + library.Url + "|" + (library.Sha1 ?? "") + "|" + library.Size);
                }
                foreach (var mod in mods)
                {
                    lines.Add("mod|" + mod.Name + "|" + mod.FileName + "|" + mod.Url + "|" + (mod.Sha1 ?? "")
                              + "|" + mod.Size + "|" + (mod.Version ?? ""));
                }
                File.WriteAllLines(path, lines);
            }
            catch
            {
            }
        }

        private static long ParseLong(string value)
        {
            long result;
            return long.TryParse(value, out result) ? result : 0;
        }

        private static List<string> ReadManifest(string path)
        {
            var names = new List<string>();
            try
            {
                if (!File.Exists(path)) return names;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Length > 0) names.Add(line);
                }
            }
            catch
            {
            }
            return names;
        }

        private static void WriteManifest(string path, IEnumerable<string> names)
        {
            try
            {
                File.WriteAllLines(path, new List<string>(names));
            }
            catch
            {
            }
        }
    }
}
