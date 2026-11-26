using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Game
{
    /// <summary>
    /// Finds a usable Java for a version and, when there is none, downloads Mojang's runtime
    /// (jre-legacy / java-runtime-gamma / …) exactly once.
    /// </summary>
    public sealed class JavaManager
    {
        private const string RuntimeManifestUrl = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

        private readonly Dictionary<string, int> _majorCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public async Task<string> EnsureJavaAsync(string component, int major, Settings settings, Downloader downloader,
            Action<string> log, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(component)) component = "jre-legacy";
            if (major <= 0) major = 8;

            if (!string.IsNullOrWhiteSpace(settings.JavaPath) && File.Exists(settings.JavaPath))
            {
                log("Using the Java you picked: " + settings.JavaPath);
                return settings.JavaPath;
            }

            var bundledDir = Path.Combine(Locations.Runtime, component);
            var bundled = FindJavaExe(bundledDir);
            if (bundled != null && JavaMajor(bundled) >= major)
            {
                log("Using the bundled runtime: " + bundled);
                return bundled;
            }

            foreach (var candidate in Discover())
            {
                if (JavaMajor(candidate) >= major)
                {
                    log("Using installed Java: " + candidate);
                    return candidate;
                }
            }

            if (!settings.DownloadJava)
            {
                throw new InvalidOperationException("No Java " + major + " or newer was found. Install one, pick javaw.exe in the settings, or allow FastCraft to download it.");
            }

            log("No Java " + major + " found — downloading Mojang's " + component + " runtime (one time)…");
            await DownloadRuntimeAsync(component, downloader, cancellationToken).ConfigureAwait(false);

            bundled = FindJavaExe(bundledDir);
            if (bundled == null)
            {
                throw new InvalidOperationException("The Java runtime was downloaded but javaw.exe is missing.");
            }
            log("Downloaded Java runtime: " + bundled);
            return bundled;
        }

        private static string FindJavaExe(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;
            var candidates = new[]
            {
                Path.Combine(directory, "bin", "javaw.exe"),
                Path.Combine(directory, "bin", "java.exe"),
                Path.Combine(directory, "jre", "bin", "javaw.exe"),
                Path.Combine(directory, "jre", "bin", "java.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static void AddIfExists(List<string> target, string candidate)
        {
            try
            {
                if (File.Exists(candidate)) target.Add(candidate);
            }
            catch
            {
            }
        }

        private static List<string> Discover()
        {
            var results = new List<string>();

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                AddIfExists(results, Path.Combine(javaHome, "bin", "javaw.exe"));
            }

            var directories = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium")
            };

            foreach (var directory in directories)
            {
                try
                {
                    if (!Directory.Exists(directory)) continue;
                    foreach (var sub in Directory.GetDirectories(directory))
                    {
                        AddIfExists(results, Path.Combine(sub, "bin", "javaw.exe"));
                    }
                }
                catch
                {
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    AddIfExists(results, Path.Combine(part.Trim(), "javaw.exe"));
                }
                catch
                {
                }
            }

            return results;
        }

        private int JavaMajor(string javawPath)
        {
            int cached;
            if (_majorCache.TryGetValue(javawPath, out cached)) return cached;

            int major = 0;
            try
            {
                var javaExe = javawPath;
                if (javaExe.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.GetDirectoryName(javaExe);
                    if (!string.IsNullOrEmpty(directory)) javaExe = Path.Combine(directory, "java.exe");
                }

                var info = new ProcessStartInfo(javaExe, "-version")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(info))
                {
                    if (process != null)
                    {
                        var output = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
                        process.WaitForExit(5000);
                        var match = Regex.Match(output, "version \"(?:1\\.)?(\\d+)");
                        if (match.Success)
                        {
                            int parsed;
                            if (int.TryParse(match.Groups[1].Value, out parsed)) major = parsed;
                        }
                    }
                }
            }
            catch
            {
            }

            _majorCache[javawPath] = major;
            return major;
        }

        private static async Task DownloadRuntimeAsync(string component, Downloader downloader, CancellationToken cancellationToken)
        {
            using (var document = await Http.GetJsonAsync(RuntimeManifestUrl, cancellationToken).ConfigureAwait(false))
            {
                var platformKey = Environment.Is64BitOperatingSystem ? "windows-x64" : "windows-x86";
                JsonElement platform;
                if (!document.RootElement.TryGetProperty(platformKey, out platform) || platform.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Mojang has no Java runtime for this platform (" + platformKey + ").");
                }

                JsonElement versions;
                if (!platform.TryGetProperty(component, out versions) || versions.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Mojang has no runtime named " + component + ".");
                }

                JsonElement entry = default(JsonElement);
                foreach (var item in versions.EnumerateArray())
                {
                    entry = item;
                    break;
                }
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Mojang's runtime list for " + component + " is empty.");
                }

                JsonElement manifestInfo;
                if (!entry.TryGetProperty("manifest", out manifestInfo) || manifestInfo.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Mojang's runtime entry has no manifest.");
                }

                var manifestUrl = JsonHelp.GetString(manifestInfo, "url");
                if (string.IsNullOrEmpty(manifestUrl))
                {
                    throw new InvalidOperationException("Mojang's runtime manifest has no URL.");
                }

                using (var manifest = await Http.GetJsonAsync(manifestUrl, cancellationToken).ConfigureAwait(false))
                {
                    var rootDirectory = Path.Combine(Locations.Runtime, component);
                    var items = new List<DownloadItem>();

                    JsonElement files;
                    if (manifest.RootElement.TryGetProperty("files", out files) && files.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var file in files.EnumerateObject())
                        {
                            var info = file.Value;
                            if (!string.Equals(JsonHelp.GetString(info, "type"), "file", StringComparison.OrdinalIgnoreCase)) continue;

                            JsonElement downloads;
                            if (!info.TryGetProperty("downloads", out downloads) || downloads.ValueKind != JsonValueKind.Object) continue;

                            JsonElement raw;
                            if (!downloads.TryGetProperty("raw", out raw) || raw.ValueKind != JsonValueKind.Object) continue;

                            var relative = file.Name.Replace('/', Path.DirectorySeparatorChar);
                            items.Add(new DownloadItem
                            {
                                Url = JsonHelp.GetString(raw, "url"),
                                Sha1 = JsonHelp.GetString(raw, "sha1"),
                                Size = JsonHelp.GetLong(raw, "size"),
                                Dest = Path.Combine(rootDirectory, relative)
                            });
                        }
                    }

                    await downloader.RunAsync(items, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
