using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Fast
{
    /// <summary>One downloadable mod jar, resolved for the supported game version.</summary>
    public sealed class ModFile
    {
        public string Project;
        public string Name;
        public string Version;
        public string FileName;
        public string Url;
        public string Sha1;
        public long Size;
        public readonly List<string> RequiredProjects = new List<string>();
    }

    /// <summary>
    /// Asks Modrinth what the performance mods look like for one Minecraft version. Modrinth
    /// is the publisher's own index: a project that has no build for that version answers with
    /// an empty list, which is reported and skipped instead of being downloaded anyway.
    /// </summary>
    public sealed class ModCatalog
    {
        private const string Api = "https://api.modrinth.com/v2/project/";

        public async Task<List<ModFile>> ResolveAsync(string gameVersion, Action<string> log, CancellationToken cancellationToken)
        {
            var files = new List<ModFile>();
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The curated list first; required dependencies of anything that resolves are
            // appended to the queue, one level deep.
            var queue = new List<KeyValuePair<string, string>>();
            foreach (var mod in FastEdition.Mods)
            {
                queue.Add(new KeyValuePair<string, string>(mod.Slug, mod.Name));
            }

            for (int i = 0; i < queue.Count && i < 40; i++)
            {
                var key = queue[i].Key;
                if (string.IsNullOrEmpty(key) || !handled.Add(key)) continue;

                var file = await ResolveProjectAsync(key, gameVersion, cancellationToken).ConfigureAwait(false);
                if (file == null)
                {
                    log("[fast] " + queue[i].Value + " has no " + gameVersion + " build yet; skipping it.");
                    continue;
                }

                file.Name = queue[i].Value;
                files.Add(file);

                foreach (var dependency in file.RequiredProjects)
                {
                    if (dependency == null || handled.Contains(dependency)) continue;
                    queue.Add(new KeyValuePair<string, string>(dependency, dependency));
                }
            }

            return files;
        }

        /// <summary>
        /// Newest release build for this project and game version. A project that only has beta
        /// or alpha builds for it is accepted as they come (newest first), because a beta is
        /// still a real build; nothing here invents a version that was not published.
        /// </summary>
        private static async Task<ModFile> ResolveProjectAsync(string project, string gameVersion, CancellationToken cancellationToken)
        {
            var url = Api + Uri.EscapeDataString(project) + "/version"
                      + "?loaders=" + Uri.EscapeDataString("[\"fabric\"]")
                      + "&game_versions=" + Uri.EscapeDataString("[\"" + gameVersion + "\"]");

            try
            {
                using (var document = await Http.GetJsonAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Array) return null;

                    bool found = false;
                    JsonElement chosen = default(JsonElement);
                    foreach (var entry in root.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object) continue;
                        if (!found)
                        {
                            chosen = entry;
                            found = true;
                        }
                        if (string.Equals(JsonHelp.GetString(entry, "version_type"), "release", StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = entry;
                            break;
                        }
                    }
                    if (!found) return null;

                    var file = new ModFile
                    {
                        Project = project,
                        Version = JsonHelp.GetString(chosen, "version_number") ?? JsonHelp.GetString(chosen, "name")
                    };

                    JsonElement files;
                    if (!chosen.TryGetProperty("files", out files) || files.ValueKind != JsonValueKind.Array) return null;

                    bool hasFile = false;
                    JsonElement fileElement = default(JsonElement);
                    foreach (var candidate in files.EnumerateArray())
                    {
                        if (candidate.ValueKind != JsonValueKind.Object) continue;
                        if (!hasFile)
                        {
                            fileElement = candidate;
                            hasFile = true;
                        }
                        if (JsonHelp.GetBool(candidate, "primary"))
                        {
                            fileElement = candidate;
                            break;
                        }
                    }
                    if (!hasFile) return null;

                    file.FileName = JsonHelp.GetString(fileElement, "filename");
                    file.Url = JsonHelp.GetString(fileElement, "url");
                    file.Size = JsonHelp.GetLong(fileElement, "size");

                    JsonElement hashes;
                    if (fileElement.TryGetProperty("hashes", out hashes) && hashes.ValueKind == JsonValueKind.Object)
                    {
                        file.Sha1 = JsonHelp.GetString(hashes, "sha1");
                    }

                    JsonElement dependencies;
                    if (chosen.TryGetProperty("dependencies", out dependencies) && dependencies.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dependency in dependencies.EnumerateArray())
                        {
                            if (dependency.ValueKind != JsonValueKind.Object) continue;
                            if (!string.Equals(JsonHelp.GetString(dependency, "dependency_type"), "required", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            var projectId = JsonHelp.GetString(dependency, "project_id");
                            if (!string.IsNullOrEmpty(projectId)) file.RequiredProjects.Add(projectId);
                        }
                    }

                    if (string.IsNullOrEmpty(file.FileName) || string.IsNullOrEmpty(file.Url)) return null;
                    return file;
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
