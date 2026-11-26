using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Game
{
    public sealed class InstallResult
    {
        public string VersionDir;
        public string NativesDir;
        public string ClientJar;
        public string AssetsRoot;
        public string LogConfig;
        public readonly List<string> Classpath = new List<string>();
    }

    /// <summary>
    /// Downloads and prepares everything a version needs: client jar, libraries, natives,
    /// asset index, asset objects, legacy virtual assets and the log4j config.
    /// </summary>
    public sealed class GameInstaller
    {
        private readonly Downloader _downloader;

        public GameInstaller(Downloader downloader)
        {
            _downloader = downloader;
        }

        public async Task<InstallResult> InstallAsync(VersionData version, Action<string> log, CancellationToken cancellationToken)
        {
            var result = new InstallResult();
            var versionDir = Locations.VersionDir(version.Id);
            Directory.CreateDirectory(versionDir);

            result.VersionDir = versionDir;
            result.NativesDir = Path.Combine(versionDir, "natives");
            result.ClientJar = Path.Combine(versionDir, version.Id + ".jar");
            result.AssetsRoot = Locations.Assets;

            var batch = new List<DownloadItem>();
            var nativeLibs = new List<LibraryInfo>();
            var ordered = new List<LibraryInfo>();
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // De-duplicate by group:artifact, keeping the last occurrence (the newest version
            // in the list), but preserving its position in the classpath.
            foreach (var library in version.Libraries)
            {
                if (!library.NativeOnly && !string.IsNullOrEmpty(library.ArtifactPath) && !string.IsNullOrEmpty(library.ArtifactUrl))
                {
                    var key = library.Key ?? library.Name ?? "";
                    int index;
                    if (positions.TryGetValue(key, out index))
                    {
                        ordered[index] = library;
                    }
                    else
                    {
                        positions[key] = ordered.Count;
                        ordered.Add(library);
                    }
                }
                if (!string.IsNullOrEmpty(library.NativePath) && !string.IsNullOrEmpty(library.NativeUrl))
                {
                    nativeLibs.Add(library);
                }
            }

            foreach (var library in ordered)
            {
                var destination = Path.Combine(Locations.Libraries, library.ArtifactPath.Replace('/', Path.DirectorySeparatorChar));
                result.Classpath.Add(destination);
                batch.Add(new DownloadItem
                {
                    Url = library.ArtifactUrl,
                    Dest = destination,
                    Sha1 = library.ArtifactSha1,
                    Size = library.ArtifactSize
                });
            }

            if (!string.IsNullOrEmpty(version.ClientUrl))
            {
                batch.Add(new DownloadItem
                {
                    Url = version.ClientUrl,
                    Dest = result.ClientJar,
                    Sha1 = version.ClientSha1,
                    Size = version.ClientSize
                });
            }

            foreach (var library in nativeLibs)
            {
                var destination = Path.Combine(Locations.Libraries, library.NativePath.Replace('/', Path.DirectorySeparatorChar));
                batch.Add(new DownloadItem
                {
                    Url = library.NativeUrl,
                    Dest = destination,
                    Sha1 = library.NativeSha1,
                    Size = library.NativeSize
                });
            }

            string indexFile = null;
            if (version.AssetIndex != null && !string.IsNullOrEmpty(version.AssetIndex.Url))
            {
                var indexDir = Path.Combine(Locations.Assets, "indexes");
                Directory.CreateDirectory(indexDir);
                var assetIndexId = string.IsNullOrEmpty(version.AssetIndex.Id) ? version.AssetsId : version.AssetIndex.Id;
                indexFile = Path.Combine(indexDir, (assetIndexId ?? "legacy") + ".json");
                batch.Add(new DownloadItem
                {
                    Url = version.AssetIndex.Url,
                    Dest = indexFile,
                    Sha1 = version.AssetIndex.Sha1,
                    Size = version.AssetIndex.Size
                });
            }

            if (!string.IsNullOrEmpty(version.LoggingFileUrl) && !string.IsNullOrEmpty(version.LoggingArgument))
            {
                var logDir = Path.Combine(Locations.Assets, "log_configs");
                Directory.CreateDirectory(logDir);
                var logId = string.IsNullOrEmpty(version.LoggingFileId) ? "client.xml" : version.LoggingFileId;
                result.LogConfig = Path.Combine(logDir, logId);
                batch.Add(new DownloadItem
                {
                    Url = version.LoggingFileUrl,
                    Dest = result.LogConfig,
                    Sha1 = version.LoggingFileSha1,
                    Size = version.LoggingFileSize
                });
            }

            log("Checking " + batch.Count + " game files (client, libraries, natives)…");
            SeedFromVanilla(batch);
            await _downloader.RunAsync(batch, cancellationToken).ConfigureAwait(false);

            // Asset objects
            var assets = new List<DownloadItem>();
            bool virtualAssets = false;
            bool mapToResources = false;
            string indexId = null;
            if (indexFile != null && File.Exists(indexFile))
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(indexFile)))
                {
                    var root = document.RootElement;
                    virtualAssets = JsonHelp.GetBool(root, "virtual");
                    mapToResources = JsonHelp.GetBool(root, "map_to_resources");
                    indexId = JsonHelp.GetString(root, "id");
                    if (string.IsNullOrEmpty(indexId) && version.AssetIndex != null) indexId = version.AssetIndex.Id;
                    if (string.IsNullOrEmpty(indexId)) indexId = version.AssetsId;

                    JsonElement objects;
                    if (root.TryGetProperty("objects", out objects) && objects.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in objects.EnumerateObject())
                        {
                            var hash = JsonHelp.GetString(property.Value, "hash");
                            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
                            var size = JsonHelp.GetLong(property.Value, "size");
                            var sub = hash.Substring(0, 2);
                            assets.Add(new DownloadItem
                            {
                                Name = property.Name,
                                Url = "https://resources.download.minecraft.net/" + sub + "/" + hash,
                                Dest = Path.Combine(Locations.Assets, "objects", sub, hash),
                                Sha1 = hash,
                                Size = size
                            });
                        }
                    }
                }
            }

            if (assets.Count > 0)
            {
                log("Checking " + assets.Count + " asset files…");
                SeedFromVanilla(assets);
                await _downloader.RunAsync(assets, cancellationToken).ConfigureAwait(false);
            }

            // Legacy versions (1.6 and older) use a "virtual" asset folder and sometimes
            // copy resources into the game directory.
            if (virtualAssets || mapToResources)
            {
                var virtualDir = Path.Combine(Locations.Assets, "virtual", indexId ?? "legacy");
                Directory.CreateDirectory(virtualDir);
                foreach (var asset in assets)
                {
                    if (asset.Name == null) continue;
                    var target = Path.Combine(virtualDir, asset.Name.Replace('/', Path.DirectorySeparatorChar));
                    TryCopy(asset.Dest, target);
                }
                if (virtualAssets)
                {
                    result.AssetsRoot = virtualDir;
                }
            }

            log("Preparing native libraries…");
            PrepareNatives(nativeLibs, result.NativesDir);

            return result;
        }

        /// <summary>
        /// Reuses files the official launcher already downloaded into %APPDATA%\.minecraft,
        /// so the first FastCraft launch usually copies instead of downloading hundreds of MB.
        /// The downloader then verifies every copied file, so a stale file is replaced.
        /// </summary>
        private static void SeedFromVanilla(List<DownloadItem> items)
        {
            string vanillaRoot;
            try
            {
                vanillaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
                if (!Directory.Exists(vanillaRoot)) return;
            }
            catch
            {
                return;
            }

            foreach (var item in items)
            {
                try
                {
                    if (File.Exists(item.Dest)) continue;

                    var relative = item.Dest.Substring(Locations.Root.Length).TrimStart(Path.DirectorySeparatorChar);
                    var source = Path.Combine(vanillaRoot, relative);
                    if (!File.Exists(source)) continue;

                    var sourceInfo = new FileInfo(source);
                    if (sourceInfo.Length == 0) continue;
                    if (item.Size > 0 && sourceInfo.Length != item.Size) continue;

                    var directory = Path.GetDirectoryName(item.Dest);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.Copy(source, item.Dest, false);
                }
                catch
                {
                }
            }
        }

        private static void PrepareNatives(List<LibraryInfo> libraries, string nativesDir)
        {
            try
            {
                if (Directory.Exists(nativesDir)) Directory.Delete(nativesDir, true);
            }
            catch
            {
            }
            Directory.CreateDirectory(nativesDir);

            foreach (var library in libraries)
            {
                var jar = Path.Combine(Locations.Libraries, library.NativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(jar)) continue;
                try
                {
                    using (var archive = ZipFile.OpenRead(jar))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;

                            bool excluded = false;
                            foreach (var prefix in library.ExtractExclude)
                            {
                                if (string.IsNullOrEmpty(prefix)) continue;
                                if (entry.FullName.StartsWith(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                                {
                                    excluded = true;
                                    break;
                                }
                            }
                            if (excluded) continue;

                            var target = Path.Combine(nativesDir, Path.GetFileName(entry.FullName));
                            entry.ExtractToFile(target, true);
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static void TryCopy(string source, string destination)
        {
            try
            {
                if (!File.Exists(source)) return;
                if (File.Exists(destination))
                {
                    var from = new FileInfo(source);
                    var to = new FileInfo(destination);
                    if (from.Length == to.Length) return;
                }
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, destination, true);
            }
            catch
            {
            }
        }
    }
}
