using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Update
{
    /// <summary>
    /// Checks the GitHub releases of this repository for a newer FastCraft and can apply it:
    /// the new exe is downloaded next to the account files, and a small batch script
    /// replaces the running exe after FastCraft exits and starts it again.
    /// </summary>
    public sealed class UpdateService
    {
        // The releases list, not just the latest one: a player who is several builds behind sees
        // the changes of every release between the one they run and the newest.
        private const string ReleasesUrl = "https://api.github.com/repos/cannon164524/FastCraft/releases?per_page=20";
        private const string AssetName = "FastCraft.exe";
        private const string PlaceholderNotes = "Automatic build of commit";

        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var version = typeof(UpdateService).Assembly.GetName().Version;
                    return version ?? new Version(1, 0, 0, 0);
                }
                catch
                {
                    return new Version(1, 0, 0, 0);
                }
            }
        }

        public static string FailedReportPath
        {
            get { return Path.Combine(Locations.UpdateDir, "update-failed.txt"); }
        }

        public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
        {
            var result = new UpdateCheck();
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl))
                {
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using (var response = await Http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            // GitHub also answers 404 when the repository is private: releases
                            // are only served anonymously for public repositories.
                            result.Message = "GitHub has no release data for FastCraft — the repository is private, or no release was published yet.";
                            return result;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            result.Failed = true;
                            result.Message = "GitHub returned HTTP " + (int)response.StatusCode + " when checking for updates. " + DescribeGitHubError(error);
                            return result;
                        }

                        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        var releases = ParseReleases(body);
                        if (releases.Count == 0)
                        {
                            result.Failed = true;
                            result.Message = "GitHub's releases have no version tag or no FastCraft.exe asset.";
                            return result;
                        }

                        var newer = new List<UpdateInfo>();
                        foreach (var release in releases)
                        {
                            if (release.Version.CompareTo(CurrentVersion) > 0) newer.Add(release);
                        }

                        if (newer.Count == 0)
                        {
                            result.Message = "FastCraft " + ShortVersion(CurrentVersion) + " is up to date (latest release: " + releases[0].Tag + ").";
                            return result;
                        }

                        var latest = newer[0];
                        latest.Notes = BuildNotes(newer);
                        result.Info = latest;
                        result.Message = "FastCraft " + latest.Tag + " is available (you have " + ShortVersion(CurrentVersion) + ").";
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.Message = "Could not check for updates: " + ex.Message;
                return result;
            }
        }

        private static string DescribeGitHubError(string body)
        {
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var message = JsonHelp.GetString(document.RootElement, "message");
                    if (!string.IsNullOrEmpty(message)) return message;
                }
            }
            catch (JsonException)
            {
            }
            return "";
        }

        private static List<UpdateInfo> ParseReleases(string json)
        {
            var releases = new List<UpdateInfo>();
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array) return releases;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    var release = ParseRelease(element);
                    if (release != null) releases.Add(release);
                }
            }

            releases.Sort(delegate(UpdateInfo left, UpdateInfo right)
            {
                return right.Version.CompareTo(left.Version);
            });
            return releases;
        }

        private static UpdateInfo ParseRelease(JsonElement release)
        {
            if (release.ValueKind != JsonValueKind.Object) return null;
            if (JsonHelp.GetBool(release, "draft")) return null;
            if (JsonHelp.GetBool(release, "prerelease")) return null;

            var tag = JsonHelp.GetString(release, "tag_name");
            Version version;
            if (!TryParseVersion(tag, out version)) return null;

            var info = new UpdateInfo
            {
                Tag = tag,
                Version = version,
                Notes = CleanNotes(JsonHelp.GetString(release, "body"))
            };

            var published = JsonHelp.GetString(release, "published_at");
            DateTime publishedAt;
            if (published != null && DateTime.TryParse(published, out publishedAt))
            {
                info.PublishedAt = publishedAt;
            }

            JsonElement assets;
            if (release.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = JsonHelp.GetString(asset, "name");
                    if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    info.Url = JsonHelp.GetString(asset, "browser_download_url");
                    info.Size = JsonHelp.GetLong(asset, "size");
                    break;
                }
            }

            return string.IsNullOrEmpty(info.Url) ? null : info;
        }

        /// <summary>
        /// The text of a release. Older builds carry "Automatic build of commit …" instead of
        /// notes; that is no note at all, so it is dropped.
        /// </summary>
        private static string CleanNotes(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            var text = body.Replace("\r\n", "\n").Trim();
            if (text.StartsWith(PlaceholderNotes, StringComparison.OrdinalIgnoreCase)) return "";
            return text;
        }

        /// <summary>
        /// What's new, release by release, newest first, so a player several builds behind sees
        /// everything that changed instead of only the newest build.
        /// </summary>
        private static string BuildNotes(List<UpdateInfo> newer)
        {
            var builder = new StringBuilder();
            var single = newer.Count == 1;

            foreach (var release in newer)
            {
                if (builder.Length > 0) builder.Append("\n\n");

                // With more than one build waiting, each release gets its own heading.
                if (!single)
                {
                    builder.Append(DisplayTag(release.Tag));
                    if (release.PublishedAt.HasValue)
                    {
                        builder.Append("   ·   ").Append(release.PublishedAt.Value.ToLocalTime().ToString("d MMM yyyy"));
                    }
                    builder.Append('\n');
                }

                if (string.IsNullOrEmpty(release.Notes))
                {
                    builder.Append("- No notes were written for this build.");
                }
                else
                {
                    builder.Append(release.Notes);
                }
            }
            return builder.ToString();
        }

        public static string DisplayTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            return tag.TrimStart('v', 'V');
        }

        public static string ShortVersion(Version version)
        {
            if (version == null) return "";
            return version.Major + "." + version.Minor + "." + version.Build;
        }

        public static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(tag)) return false;
            var text = tag.Trim();
            if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V')) text = text.Substring(1);
            return Version.TryParse(text, out version);
        }

        public async Task<string> DownloadAsync(UpdateInfo info, Action<double> progress, CancellationToken cancellationToken)
        {
            var directory = Locations.UpdateDir;
            Directory.CreateDirectory(directory);

            var temp = Path.Combine(directory, AssetName + ".part");
            var destination = Path.Combine(directory, AssetName);

            using (var response = await Http.Client.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength ?? info.Size;
                using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        written += read;
                        if (length > 0 && progress != null) progress((double)written / length);
                    }
                }
            }

            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temp, destination);
            return destination;
        }

        /// <summary>
        /// Starts a small batch script that waits for this process to exit, copies the new
        /// exe over the old one and starts FastCraft again. If the exe cannot be replaced
        /// (for example because it sits in Program Files), it leaves a marker so the next
        /// start explains where the downloaded update is.
        /// </summary>
        public static bool ApplyAndRestart(string downloadedExe)
        {
            try
            {
                var current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current))
                {
                    current = Process.GetCurrentProcess().MainModule.FileName;
                }
                if (string.IsNullOrEmpty(current)) return false;

                var script = Path.Combine(Path.GetTempPath(), "fastcraft-update.cmd");
                var failed = FailedReportPath;

                var lines = new[]
                {
                    "@echo off",
                    "ping -n 4 127.0.0.1 >nul",
                    "move /y \"" + current + "\" \"" + current + ".old\" >nul 2>nul",
                    "copy /y \"" + downloadedExe + "\" \"" + current + "\" >nul 2>nul",
                    "if errorlevel 1 (",
                    "  if exist \"" + current + ".old\" move /y \"" + current + ".old\" \"" + current + "\" >nul 2>nul",
                    "  echo update could not be applied > \"" + failed + "\"",
                    "  start \"\" explorer \"" + Locations.UpdateDir + "\"",
                    ") else (",
                    "  if exist \"" + current + ".old\" del \"" + current + ".old\" >nul 2>nul",
                    "  del \"" + failed + "\" >nul 2>nul",
                    ")",
                    "start \"\" \"" + current + "\"",
                    "del \"%~f0\""
                };

                File.WriteAllLines(script, lines);
                Process.Start(new ProcessStartInfo(script)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
