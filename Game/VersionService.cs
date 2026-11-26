using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Game
{
    public sealed class VersionSummary
    {
        public string Id;
        public string Type;
        public string Url;
        public string Sha1;
        public DateTime ReleaseTime;
    }

    public sealed class VersionManifestData
    {
        public string LatestRelease;
        public string LatestSnapshot;
        public readonly List<VersionSummary> Versions = new List<VersionSummary>();
    }

    public sealed class VersionService
    {
        public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public async Task<VersionManifestData> GetManifestAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            var path = Locations.ManifestPath;
            bool fresh = false;
            if (!forceRefresh && File.Exists(path))
            {
                try
                {
                    fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(15);
                }
                catch
                {
                }
            }

            if (!fresh)
            {
                try
                {
                    var temp = path + ".part";
                    using (var response = await Http.Client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await stream.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                }
                catch when (File.Exists(path))
                {
                    // Offline: fall back to the cached list.
                }
            }

            return Parse(File.ReadAllText(path));
        }

        public async Task<string> EnsureVersionJsonAsync(VersionSummary summary, Downloader downloader, CancellationToken cancellationToken)
        {
            var directory = Locations.VersionDir(summary.Id);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, summary.Id + ".json");
            await downloader.EnsureAsync(new DownloadItem { Url = summary.Url, Dest = path, Sha1 = summary.Sha1 }, cancellationToken).ConfigureAwait(false);
            return path;
        }

        private static VersionManifestData Parse(string json)
        {
            var data = new VersionManifestData();
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                JsonElement latest;
                if (root.TryGetProperty("latest", out latest) && latest.ValueKind == JsonValueKind.Object)
                {
                    data.LatestRelease = JsonHelp.GetString(latest, "release");
                    data.LatestSnapshot = JsonHelp.GetString(latest, "snapshot");
                }

                JsonElement versions;
                if (root.TryGetProperty("versions", out versions) && versions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in versions.EnumerateArray())
                    {
                        var summary = new VersionSummary
                        {
                            Id = JsonHelp.GetString(item, "id"),
                            Type = JsonHelp.GetString(item, "type"),
                            Url = JsonHelp.GetString(item, "url"),
                            Sha1 = JsonHelp.GetString(item, "sha1")
                        };
                        var releaseTime = JsonHelp.GetString(item, "releaseTime");
                        DateTime parsed;
                        if (releaseTime != null && DateTime.TryParse(releaseTime, out parsed))
                        {
                            summary.ReleaseTime = parsed;
                        }
                        if (!string.IsNullOrEmpty(summary.Id) && !string.IsNullOrEmpty(summary.Url))
                        {
                            data.Versions.Add(summary);
                        }
                    }
                }
            }
            return data;
        }
    }
}
