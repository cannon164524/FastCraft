using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FastCraft.Fast
{
    /// <summary>
    /// Puts FastCraft's look into the game on a 26.3 launch: the resource pack it draws itself,
    /// the FancyMenu set-up for the pause menu, and one entry in options.txt so the pack is on.
    ///
    /// Everything here is idempotent and forgiving. A version marker stops a relaunch from
    /// overwriting anything the player changed in game, the pack is only ever switched on
    /// once (turn it off in Options and it stays off), and any failure is reported as a log
    /// line instead of stopping the launch.
    /// </summary>
    public static class Branding
    {
        /// <summary>Bump this to rewrite the pack and the menu set-up on the next launch.</summary>
        public const int Version = 7;

        private const string MarkerName = ".fastcraft-brand.txt";

        public sealed class Result
        {
            public bool Applied;
            public string Summary = "";
            public string Problem = "";
        }

        private sealed class State
        {
            public int Version;
            public bool Enabled;
            public string Format = "";
        }

        public static Result Ensure(string gameVersion, string gameDirectory, string clientJar, string assetIndexFile,
            Action<string> log)
        {
            var result = new Result();

            if (!FastEdition.Supports(gameVersion))
            {
                result.Problem = "the FastCraft look is built for " + FastEdition.GameVersion + " only";
                return result;
            }

            var markerPath = Path.Combine(gameDirectory, MarkerName);
            var state = ReadState(markerPath);
            var packPath = ResourcePackBuilder.PackPath(gameDirectory);
            var menuOptions = Path.Combine(gameDirectory, "config", "fancymenu", "options.txt");

            // The pack's declared format comes from this game's own client jar. When it changes
            // (a new release), the pack has to be rebuilt or the game calls it incompatible.
            var format = GameAssets.GetResourcePackFormat(clientJar) ?? "";

            if (state.Version >= Version && string.Equals(state.Format, format, StringComparison.Ordinal)
                && File.Exists(packPath) && File.Exists(menuOptions))
            {
                // The files are current, so only the switch is confirmed: the pack is on by
                // default for the edition, and a launch must not leave it off.
                var keptOn = EnablePack(gameDirectory, ResourcePackBuilder.PackFileName, clientJar, log);
                WriteState(markerPath, Version, keptOn, format);
                result.Summary = "already in place (v" + state.Version + "), "
                                 + (keptOn ? "pack on" : "pack could not be switched on");
                return result;
            }

            var pack = new ResourcePackBuilder().Build(gameDirectory, clientJar, assetIndexFile, log);
            var menu = new FancyMenuSetup().Apply(gameDirectory, clientJar, log);

            // On by default: the pack is added to the game's list on every launch, so turning it
            // off by hand in Options lasts until the next launch of the edition.
            var enabled = EnablePack(gameDirectory, ResourcePackBuilder.PackFileName, clientJar, log);

            WriteState(markerPath, Version, enabled, format);

            result.Applied = true;
            result.Summary = "pack " + (pack.PackWritten ? "written" : "not written")
                             + ", pause menu " + (menu ? "set up" : "not set up")
                             + ", " + (enabled ? "pack on" : "pack could not be switched on");
            return result;
        }

        /// <summary>
        /// Puts the pack into the resourcePacks list in options.txt, keeping every other pack and
        /// every other setting, and clears it from incompatibleResourcePacks so a pack the game
        /// once refused does not stay refused. A line that cannot be read is left untouched.
        /// </summary>
        private static bool EnablePack(string gameDirectory, string packFileName, string clientJar, Action<string> log)
        {
            var id = "file/" + packFileName;
            var path = Path.Combine(gameDirectory, "options.txt");

            var lines = new List<string>();
            try
            {
                if (File.Exists(path)) lines = new List<string>(File.ReadAllLines(path));
            }
            catch
            {
                return false;
            }

            // The game replaces an options.txt whose "version" line is missing (or older than
            // its data version) with its defaults — taking the resource pack selection with it,
            // which is why the pack kept coming back switched off. The number comes from the
            // client jar of this exact version.
            var dataVersion = GameAssets.GetDataVersion(clientJar);
            if (dataVersion > 0 && !HasLine(lines, "version:"))
            {
                lines.Add("version:" + dataVersion);
                log("[brand] Added the options.txt version line (" + dataVersion + ") so the game keeps the pack selection.");
            }

            var packs = ReadListLine(lines, "resourcePacks:", log);
            if (packs == null) return false;

            var already = false;
            foreach (var pack in packs)
            {
                if (!string.Equals(pack, id, StringComparison.OrdinalIgnoreCase)) continue;
                already = true;
                break;
            }

            if (!already)
            {
                if (packs.Count == 0) packs.Add("vanilla");
                packs.Add(id);
                WriteListLine(lines, "resourcePacks:", packs);
            }

            var blocked = ReadListLine(lines, "incompatibleResourcePacks:", log);
            if (blocked != null)
            {
                var removed = blocked.RemoveAll(pack => string.Equals(pack, id, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) WriteListLine(lines, "incompatibleResourcePacks:", blocked);
            }

            try
            {
                File.WriteAllLines(path, lines.ToArray());
            }
            catch
            {
                return false;
            }

            log(already
                ? "[brand] The FastCraft pack is on in options.txt."
                : "[brand] The FastCraft pack was switched on in options.txt.");
            return true;
        }

        private static bool HasLine(List<string> lines, string key)
        {
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Reads a JSON array that sits on one line of options.txt, like resourcePacks.</summary>
        private static List<string> ReadListLine(List<string> lines, string key, Action<string> log)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!line.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

                var raw = line.TrimStart().Substring(key.Length);
                try
                {
                    using (var document = JsonDocument.Parse(raw))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

                        var list = new List<string>();
                        foreach (var item in document.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString());
                        }
                        return list;
                    }
                }
                catch
                {
                    log("[brand] The " + key.TrimEnd(':') + " line in options.txt is not readable, so it was left alone.");
                    return null;
                }
            }

            return new List<string>();
        }

        private static void WriteListLine(List<string> lines, string key, List<string> values)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = key + JsonSerializer.Serialize(values);
                return;
            }
            lines.Add(key + JsonSerializer.Serialize(values));
        }

        private static State ReadState(string path)
        {
            var state = new State();
            try
            {
                if (!File.Exists(path)) return state;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('=');
                    if (parts.Length < 2) continue;
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                    {
                        int version;
                        if (int.TryParse(value, out version)) state.Version = version;
                    }
                    if (string.Equals(key, "enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Enabled = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    if (string.Equals(key, "format", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Format = value;
                    }
                }
            }
            catch
            {
            }
            return state;
        }

        private static void WriteState(string path, int version, bool enabled, string format)
        {
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "version=" + version,
                    "enabled=" + (enabled ? "1" : "0"),
                    "format=" + (format ?? "")
                });
            }
            catch
            {
            }
        }
    }
}
