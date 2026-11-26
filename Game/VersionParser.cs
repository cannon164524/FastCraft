using System;
using System.Collections.Generic;
using System.Text.Json;
using FastCraft.Core;

namespace FastCraft.Game
{
    /// <summary>
    /// Parses a Mojang version JSON. Rules are evaluated for Windows up front so the rest of
    /// the launcher only sees files and arguments that actually apply on this machine.
    /// </summary>
    public static class VersionParser
    {
        public static VersionData Parse(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                var version = new VersionData
                {
                    Id = JsonHelp.GetString(root, "id") ?? "unknown",
                    Type = JsonHelp.GetString(root, "type") ?? "release",
                    MainClass = JsonHelp.GetString(root, "mainClass") ?? "net.minecraft.client.main.Main",
                    AssetsId = JsonHelp.GetString(root, "assets") ?? "legacy",
                    JarId = JsonHelp.GetString(root, "jar")
                };

                JsonElement element;
                if (root.TryGetProperty("downloads", out element) && element.ValueKind == JsonValueKind.Object)
                {
                    JsonElement client;
                    if (element.TryGetProperty("client", out client) && client.ValueKind == JsonValueKind.Object)
                    {
                        version.ClientUrl = JsonHelp.GetString(client, "url");
                        version.ClientSha1 = JsonHelp.GetString(client, "sha1");
                        version.ClientSize = JsonHelp.GetLong(client, "size");
                    }
                }

                if (root.TryGetProperty("assetIndex", out element) && element.ValueKind == JsonValueKind.Object)
                {
                    version.AssetIndex = new AssetIndexInfo
                    {
                        Id = JsonHelp.GetString(element, "id"),
                        Url = JsonHelp.GetString(element, "url"),
                        Sha1 = JsonHelp.GetString(element, "sha1"),
                        Size = JsonHelp.GetLong(element, "size"),
                        TotalSize = JsonHelp.GetLong(element, "totalSize"),
                        Virtual = JsonHelp.GetBool(element, "virtual"),
                        MapToResources = JsonHelp.GetBool(element, "map_to_resources")
                    };
                }

                if (root.TryGetProperty("javaVersion", out element) && element.ValueKind == JsonValueKind.Object)
                {
                    version.JavaComponent = JsonHelp.GetString(element, "component") ?? "jre-legacy";
                    var major = (int)JsonHelp.GetLong(element, "majorVersion");
                    version.JavaMajor = major > 0 ? major : 8;
                }

                if (root.TryGetProperty("libraries", out element) && element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var library in element.EnumerateArray())
                    {
                        ParseLibrary(library, version.Libraries);
                    }
                }

                if (root.TryGetProperty("arguments", out element) && element.ValueKind == JsonValueKind.Object)
                {
                    JsonElement jvm;
                    if (element.TryGetProperty("jvm", out jvm) && jvm.ValueKind == JsonValueKind.Array)
                    {
                        ParseArguments(jvm, version.JvmArgs);
                    }
                    JsonElement game;
                    if (element.TryGetProperty("game", out game) && game.ValueKind == JsonValueKind.Array)
                    {
                        ParseArguments(game, version.GameArgs);
                    }
                }

                version.LegacyArguments = JsonHelp.GetString(root, "minecraftArguments");

                if (root.TryGetProperty("logging", out element) && element.ValueKind == JsonValueKind.Object)
                {
                    JsonElement client;
                    if (element.TryGetProperty("client", out client) && client.ValueKind == JsonValueKind.Object)
                    {
                        version.LoggingArgument = JsonHelp.GetString(client, "argument");
                        JsonElement file;
                        if (client.TryGetProperty("file", out file) && file.ValueKind == JsonValueKind.Object)
                        {
                            version.LoggingFileId = JsonHelp.GetString(file, "id");
                            version.LoggingFileUrl = JsonHelp.GetString(file, "url");
                            version.LoggingFileSha1 = JsonHelp.GetString(file, "sha1");
                            version.LoggingFileSize = JsonHelp.GetLong(file, "size");
                        }
                    }
                }

                return version;
            }
        }

        private static void ParseLibrary(JsonElement library, List<LibraryInfo> target)
        {
            var rules = ParseRules(library);
            if (!RulesAllow(rules, null)) return;

            var name = JsonHelp.GetString(library, "name");
            if (string.IsNullOrEmpty(name)) return;

            var info = new LibraryInfo { Name = name, Key = LibraryKey(name) };
            var mavenBase = JsonHelp.GetString(library, "url") ?? "https://libraries.minecraft.net/";

            JsonElement downloads;
            bool hasDownloads = library.TryGetProperty("downloads", out downloads) && downloads.ValueKind == JsonValueKind.Object;

            JsonElement artifact = default(JsonElement);
            bool hasArtifact = hasDownloads && downloads.TryGetProperty("artifact", out artifact) && artifact.ValueKind == JsonValueKind.Object;
            if (hasArtifact)
            {
                info.ArtifactPath = JsonHelp.GetString(artifact, "path");
                info.ArtifactUrl = JsonHelp.GetString(artifact, "url");
                info.ArtifactSha1 = JsonHelp.GetString(artifact, "sha1");
                info.ArtifactSize = JsonHelp.GetLong(artifact, "size");
            }
            else
            {
                // Old version JSONs have no "downloads" block; build the Maven path instead.
                string path;
                string url;
                if (TryMaven(name, mavenBase, null, out path, out url))
                {
                    info.ArtifactPath = path;
                    info.ArtifactUrl = url;
                }
            }

            JsonElement natives;
            if (library.TryGetProperty("natives", out natives) && natives.ValueKind == JsonValueKind.Object)
            {
                var classifier = JsonHelp.GetString(natives, "windows");
                if (!string.IsNullOrEmpty(classifier))
                {
                    classifier = classifier.Replace("${arch}", Platform.ArchBits);

                    JsonElement classifiers = default(JsonElement);
                    bool hasClassifiers = hasDownloads && downloads.TryGetProperty("classifiers", out classifiers) && classifiers.ValueKind == JsonValueKind.Object;
                    JsonElement native = default(JsonElement);
                    bool hasNative = hasClassifiers && classifiers.TryGetProperty(classifier, out native) && native.ValueKind == JsonValueKind.Object;

                    if (hasNative)
                    {
                        info.NativePath = JsonHelp.GetString(native, "path");
                        info.NativeUrl = JsonHelp.GetString(native, "url");
                        info.NativeSha1 = JsonHelp.GetString(native, "sha1");
                        info.NativeSize = JsonHelp.GetLong(native, "size");
                    }
                    else
                    {
                        string path;
                        string url;
                        if (TryMaven(name, mavenBase, classifier, out path, out url))
                        {
                            info.NativePath = path;
                            info.NativeUrl = url;
                        }
                    }
                }
            }

            JsonElement extract;
            if (library.TryGetProperty("extract", out extract) && extract.ValueKind == JsonValueKind.Object)
            {
                JsonElement exclude;
                if (extract.TryGetProperty("exclude", out exclude) && exclude.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in exclude.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            info.ExtractExclude.Add(item.GetString());
                        }
                    }
                }
            }

            // From 1.19 on, the natives are separate library entries whose name carries the
            // "natives-windows" classifier instead of the old "natives" map. Those jars are
            // extracted and must not end up on the classpath.
            if (string.IsNullOrEmpty(info.NativePath) &&
                name.IndexOf(":natives-windows", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isArm64 = name.IndexOf("natives-windows-arm64", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isArm64 && Platform.ArchName != "arm64") return;
                if (!isArm64 && Platform.ArchName == "arm64") return;

                info.NativeOnly = true;
                info.NativePath = info.ArtifactPath;
                info.NativeUrl = info.ArtifactUrl;
                info.NativeSha1 = info.ArtifactSha1;
                info.NativeSize = info.ArtifactSize;
                info.ArtifactPath = null;
                info.ArtifactUrl = null;
                info.ArtifactSha1 = null;
                info.ArtifactSize = 0;
            }

            if (string.IsNullOrEmpty(info.ArtifactPath) && string.IsNullOrEmpty(info.NativePath)) return;
            target.Add(info);
        }

        private static string LibraryKey(string name)
        {
            var trimmed = name;
            var at = trimmed.IndexOf('@');
            if (at > 0) trimmed = trimmed.Substring(0, at);
            var parts = trimmed.Split(':');
            if (parts.Length >= 2) return parts[0] + ":" + parts[1];
            return trimmed;
        }

        private static bool TryMaven(string name, string baseUrl, string forcedClassifier, out string path, out string url)
        {
            path = null;
            url = null;

            var trimmed = name;
            var extension = "jar";
            var at = trimmed.IndexOf('@');
            if (at > 0)
            {
                extension = trimmed.Substring(at + 1);
                trimmed = trimmed.Substring(0, at);
            }

            var parts = trimmed.Split(':');
            if (parts.Length < 3) return false;

            var group = parts[0];
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length >= 4 ? parts[3] : forcedClassifier;

            var relative = group.Replace('.', '/') + "/" + artifact + "/" + version + "/"
                         + artifact + "-" + version
                         + (string.IsNullOrEmpty(classifier) ? "" : "-" + classifier)
                         + "." + extension;

            var prefix = string.IsNullOrEmpty(baseUrl) ? "https://libraries.minecraft.net/" : baseUrl;
            if (!prefix.EndsWith("/")) prefix += "/";

            path = relative;
            url = prefix + relative;
            return true;
        }

        private static void ParseArguments(JsonElement array, List<ArgEntry> target)
        {
            foreach (var item in array.EnumerateArray())
            {
                var entry = new ArgEntry();
                if (item.ValueKind == JsonValueKind.String)
                {
                    entry.Values.Add(item.GetString());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    entry.Rules = ParseRules(item);
                    JsonElement value;
                    if (item.TryGetProperty("value", out value))
                    {
                        if (value.ValueKind == JsonValueKind.String)
                        {
                            entry.Values.Add(value.GetString());
                        }
                        else if (value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var single in value.EnumerateArray())
                            {
                                if (single.ValueKind == JsonValueKind.String) entry.Values.Add(single.GetString());
                            }
                        }
                    }
                }
                if (entry.Values.Count > 0) target.Add(entry);
            }
        }

        private static List<Rule> ParseRules(JsonElement owner)
        {
            JsonElement rules;
            if (!owner.TryGetProperty("rules", out rules) || rules.ValueKind != JsonValueKind.Array) return null;

            var list = new List<Rule>();
            foreach (var item in rules.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var rule = new Rule { Action = JsonHelp.GetString(item, "action") ?? "allow" };

                JsonElement os;
                if (item.TryGetProperty("os", out os) && os.ValueKind == JsonValueKind.Object)
                {
                    rule.OsName = JsonHelp.GetString(os, "name");
                    rule.OsArch = JsonHelp.GetString(os, "arch");
                    rule.OsVersion = JsonHelp.GetString(os, "version");
                }

                JsonElement features;
                if (item.TryGetProperty("features", out features) && features.ValueKind == JsonValueKind.Object)
                {
                    rule.Features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (var feature in features.EnumerateObject())
                    {
                        if (feature.Value.ValueKind == JsonValueKind.True) rule.Features[feature.Name] = true;
                        else if (feature.Value.ValueKind == JsonValueKind.False) rule.Features[feature.Name] = false;
                    }
                }

                list.Add(rule);
            }
            return list;
        }

        public static bool RulesAllow(List<Rule> rules, Dictionary<string, bool> features)
        {
            if (rules == null || rules.Count == 0) return true;

            var allowed = false;
            foreach (var rule in rules)
            {
                if (!Applies(rule, features)) continue;
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
            return allowed;
        }

        private static bool Applies(Rule rule, Dictionary<string, bool> features)
        {
            if (!string.IsNullOrEmpty(rule.OsName) && !string.Equals(rule.OsName, "windows", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(rule.OsArch) && !string.Equals(rule.OsArch, Platform.ArchName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(rule.OsVersion))
            {
                // Mojang uses this for Windows 10+ checks ("^10\.").
                try
                {
                    var current = Environment.OSVersion.Version.ToString();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(current, rule.OsVersion)) return false;
                }
                catch
                {
                    return false;
                }
            }
            if (rule.Features != null)
            {
                foreach (var pair in rule.Features)
                {
                    var actual = false;
                    if (features != null) features.TryGetValue(pair.Key, out actual);
                    if (actual != pair.Value) return false;
                }
            }
            return true;
        }
    }
}
