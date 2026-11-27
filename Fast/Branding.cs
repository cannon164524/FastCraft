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
        public const int Version = 1;

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

            if (state.Version >= Version && File.Exists(packPath) && File.Exists(menuOptions))
            {
                result.Summary = "already in place (v" + state.Version + ")";
                return result;
            }

            var pack = new ResourcePackBuilder().Build(gameDirectory, clientJar, assetIndexFile, log);
            var menu = new FancyMenuSetup().Apply(gameDirectory, clientJar, log);

            // Switch the pack on the first time only. If the player turns it off in Options,
            // that is their choice and a relaunch leaves it alone.
            var enabled = state.Enabled;
            if (!enabled)
            {
                enabled = EnablePack(gameDirectory, ResourcePackBuilder.PackFileName, log);
            }

            WriteState(markerPath, Version, enabled);

            result.Applied = true;
            result.Summary = "pack " + (pack.PackWritten ? "written" : "not written")
                             + ", pause menu " + (menu ? "set up" : "not set up")
                             + ", " + (enabled ? "pack enabled" : "pack left for the player to enable");
            return result;
        }

        /// <summary>
        /// Adds the pack to the resourcePacks list in options.txt, keeping every other pack and
        /// every other setting. A line that cannot be read is left untouched rather than risked.
        /// </summary>
        private static bool EnablePack(string gameDirectory, string packFileName, Action<string> log)
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

            var index = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("resourcePacks:", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            var packs = new List<string>();
            if (index >= 0)
            {
                var raw = lines[index].TrimStart().Substring("resourcePacks:".Length);
                try
                {
                    using (var document = JsonDocument.Parse(raw))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
                        foreach (var item in document.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String) packs.Add(item.GetString());
                        }
                    }
                }
                catch
                {
                    log("[brand] The resourcePacks line in options.txt is not readable, so it was left alone.");
                    return false;
                }
            }

            if (packs.Count == 0) packs.Add("vanilla");

            foreach (var pack in packs)
            {
                if (!string.Equals(pack, id, StringComparison.OrdinalIgnoreCase)) continue;
                log("[brand] The FastCraft pack was already enabled.");
                return true;
            }

            packs.Add(id);
            var json = JsonSerializer.Serialize(packs);

            if (index >= 0) lines[index] = "resourcePacks:" + json;
            else lines.Add("resourcePacks:" + json);

            try
            {
                File.WriteAllLines(path, lines.ToArray());
                log("[brand] The FastCraft pack is enabled in options.txt.");
                return true;
            }
            catch
            {
                return false;
            }
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
                }
            }
            catch
            {
            }
            return state;
        }

        private static void WriteState(string path, int version, bool enabled)
        {
            try
            {
                File.WriteAllLines(path, new[] { "version=" + version, "enabled=" + (enabled ? "1" : "0") });
            }
            catch
            {
            }
        }
    }
}
