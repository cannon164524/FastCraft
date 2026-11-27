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
        public string Problem;
        public readonly List<LibraryInfo> Libraries = new List<LibraryInfo>();
        public readonly List<ArgEntry> JvmArgs = new List<ArgEntry>();
    }

    public sealed class FabricInstaller
    {
        private const string LoaderListUrl = "https://meta.fabricmc.net/v2/versions/loader";
        private const string MavenBase = "https://maven.fabricmc.net/";

        /// <summary>
        /// Resolves the newest stable loader and its profile for this game version. Never
        /// returns null: a failure is reported in <see cref="FabricProfile.Problem"/> with the
        /// step that failed, so the message the player sees names the real step instead of
        /// claiming Fabric has no build for a version it supports.
        /// </summary>
        public async Task<FabricProfile> ResolveAsync(string gameVersion, CancellationToken cancellationToken)
        {
            var result = new FabricProfile();

            var loaderVersion = await LatestStableLoaderAsync(gameVersion, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(loaderVersion))
            {
                result.Problem = "Fabric has no build for " + gameVersion + " yet";
                return result;
            }

            result.LoaderVersion = loaderVersion;

            var url = LoaderListUrl + "/" + Uri.EscapeDataString(gameVersion) + "/"
                      + Uri.EscapeDataString(loaderVersion) + "/profile/json";

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
                result.Problem = "Fabric's profile for " + gameVersion + " (loader " + loaderVersion + ") could not be downloaded";
                return result;
            }

            VersionData parsed;
            try
            {
                parsed = VersionParser.Parse(json, MavenBase);
            }
            catch
            {
                result.Problem = "Fabric's profile for " + gameVersion + " could not be read";
                return result;
            }

            if (parsed.Libraries.Count == 0)
            {
                result.Problem = "Fabric's profile for " + gameVersion + " lists no libraries";
                return result;
            }

            result.MainClass = string.IsNullOrEmpty(parsed.MainClass)
                ? "net.fabricmc.loader.impl.launch.knot.KnotClient"
                : parsed.MainClass;
            result.Libraries.AddRange(parsed.Libraries);
            result.JvmArgs.AddRange(parsed.JvmArgs);
            return result;
        }

        /// <summary>
        /// The loaders that support this game version, from Fabric's game-specific list: its 404
        /// is the precise "Fabric does not support this version yet" answer. Newest stable wins;
        /// with no stable loader at all, the newest of any kind.
        /// </summary>
        private static async Task<string> LatestStableLoaderAsync(string gameVersion, CancellationToken cancellationToken)
        {
            try
            {
                using (var document = await Http.GetJsonAsync(LoaderListUrl + "/" + Uri.EscapeDataString(gameVersion), cancellationToken).ConfigureAwait(false))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Array) return null;

                    string fallback = null;
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;

                        // The game-specific list wraps each loader in "loader"; the global list
                        // carries the version at the top level. Accepting only the wrapped shape
                        // is what made this return null on every launch.
                        var loader = item;
                        JsonElement nested;
                        if (item.TryGetProperty("loader", out nested) && nested.ValueKind == JsonValueKind.Object)
                        {
                            loader = nested;
                        }

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
