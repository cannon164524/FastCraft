using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
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
        private const string ReleasesUrl = "https://api.github.com/repos/cannon164524/FastCraft/releases/latest";
        private const string AssetName = "FastCraft.exe";

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

        public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await Http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return Parse(body);
                }
            }
        }

        private static UpdateInfo Parse(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                if (JsonHelp.GetBool(root, "draft")) return null;

                var tag = JsonHelp.GetString(root, "tag_name");
                if (string.IsNullOrEmpty(tag)) return null;

                Version version;
                if (!TryParseVersion(tag, out version)) return null;

                var info = new UpdateInfo
                {
                    Tag = tag,
                    Version = version,
                    Notes = JsonHelp.GetString(root, "body") ?? ""
                };

                JsonElement assets;
                if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
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

                if (string.IsNullOrEmpty(info.Url)) return null;
                if (info.Version.CompareTo(CurrentVersion) <= 0) return null;
                return info;
            }
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
