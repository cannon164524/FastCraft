using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;
using FastCraft.Game;

namespace FastCraft.Fast
{
    /// <summary>
    /// The Fabric loader profile for a game version: its main class, its libraries and the
    /// few JVM arguments it wants. Fabric publishes this as a version JSON, which is exactly
    /// the format the rest of the launcher already understands.
    /// </summary>
    public sealed class FabricProfile
    {
        public string LoaderVersion;
        public string MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient";
        public readonly List<LibraryInfo> Libraries = new List<LibraryInfo>();
        public readonly List<ArgEntry> JvmArgs = new List<ArgEntry>();
    }

    public sealed class FabricInstaller
    {
        private const string LoaderListUrl = "https://meta.fabricmc.net/v2/versions/loader";

        /// <summary>
        /// Resolves the newest stable loader and its profile for this game version. Returns
        /// null when Fabric has no build for it, which is what a brand new Minecraft release
        /// looks like for a few days.
        /// </summary>
        public async Task<FabricProfile> ResolveAsync(string gameVersion, CancellationToken cancellationToken)
        {
            var loaderVersion = await LatestStableLoaderAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(loaderVersion)) return null;

            var url = LoaderListUrl + "/" + Uri.EscapeDataString(gameVersion)
                      + "/" + Uri.EscapeDataString(loaderVersion) + "/profile/json";

            string json;
            try
            {
                json = await Http.Client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 404 for an unsupported game version, or Fabric meta is down. Either way the
                // game still has to start, so this is a "no edition", not an error.
                return null;
            }

            VersionData parsed;
            try
            {
                parsed = VersionParser.Parse(json, "https://maven.fabricmc.net/");
            }
            catch
            {
                return null;
            }

            var profile = new FabricProfile
            {
                LoaderVersion = loaderVersion,
                MainClass = string.IsNullOrEmpty(parsed.MainClass) ? "net.fabricmc.loader.impl.launch.knot.KnotClient" : parsed.MainClass
            };
            profile.Libraries.AddRange(parsed.Libraries);
            profile.JvmArgs.AddRange(parsed.JvmArgs);
            return profile;
        }

        /// <summary>
        /// meta.fabricmc.net lists loaders newest first, each with a "stable" flag. Take the
        /// newest stable one; if Fabric has no stable build at all, the newest of any kind.
        /// </summary>
        private static async Task<string> LatestStableLoaderAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var document = await Http.GetJsonAsync(LoaderListUrl, cancellationToken).ConfigureAwait(false))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Array) return null;

                    string fallback = null;
                    foreach (var item in root.EnumerateArray())
                    {
                        JsonElement loader;
                        if (!item.TryGetProperty("loader", out loader) || loader.ValueKind != JsonValueKind.Object) continue;

                        var version = JsonHelp.GetString(loader, "version");
                        if (string.IsNullOrEmpty(version)) continue;
                        if (fallback == null) fallback = version;
                        if (JsonHelp.GetBool(loader, "stable")) return version;
                    }
                    return fallback;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
    }
}
