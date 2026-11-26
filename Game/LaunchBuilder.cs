using System;
using System.Collections.Generic;
using System.Text;
using FastCraft.Auth;
using FastCraft.Core;

namespace FastCraft.Game
{
    public sealed class LaunchPlan
    {
        public string MainClass;
        public readonly List<string> JvmArgs = new List<string>();
        public readonly List<string> GameArgs = new List<string>();
    }

    /// <summary>
    /// Turns a parsed version JSON plus an account into a literal command line.
    /// This is pure string work: no network, no disk.
    /// </summary>
    public static class LaunchBuilder
    {
        public static LaunchPlan Build(VersionData version, Account account, Settings settings, string classpath,
            string nativesDir, string assetsRoot, string gameDir, string logConfig, string librariesDir)
        {
            var plan = new LaunchPlan { MainClass = version.MainClass };
            var features = DefaultFeatures();

            // 1. Memory and tuning. Only -Xmx: the JVM starts with its default heap and
            // grows as needed, which keeps launch faster than committing the whole heap.
            int memory = Math.Max(1024, settings.MemoryMb);
            plan.JvmArgs.Add("-Xmx" + memory + "M");

            if (settings.TurboJvmFlags && version.JavaMajor >= 17)
            {
                // Aikar-style G1 flags: fewer GC pauses in game. They don't raise FPS above
                // what the game itself gives with the same Java and memory.
                plan.JvmArgs.Add("-XX:+UnlockExperimentalVMOptions");
                plan.JvmArgs.Add("-XX:+UseG1GC");
                plan.JvmArgs.Add("-XX:+ParallelRefProcEnabled");
                plan.JvmArgs.Add("-XX:MaxGCPauseMillis=200");
                plan.JvmArgs.Add("-XX:+DisableExplicitGC");
                plan.JvmArgs.Add("-XX:G1NewSizePercent=20");
                plan.JvmArgs.Add("-XX:G1MaxNewSizePercent=40");
                plan.JvmArgs.Add("-XX:G1ReservePercent=20");
                plan.JvmArgs.Add("-XX:G1HeapWastePercent=5");
                plan.JvmArgs.Add("-XX:G1MixedGCCountTarget=4");
                plan.JvmArgs.Add("-XX:InitiatingHeapOccupancyPercent=15");
                plan.JvmArgs.Add("-XX:G1MixedGCLiveThresholdPercent=90");
                plan.JvmArgs.Add("-XX:G1RSetUpdatingPauseTimePercent=5");
                plan.JvmArgs.Add("-XX:SurvivorRatio=32");
            }

            if (!string.IsNullOrWhiteSpace(settings.ExtraJvmArgs))
            {
                foreach (var argument in SplitArgs(settings.ExtraJvmArgs))
                {
                    plan.JvmArgs.Add(argument);
                }
            }

            // 2. Log4j configuration (1.7+).
            if (!string.IsNullOrEmpty(logConfig) && !string.IsNullOrEmpty(version.LoggingArgument))
            {
                plan.JvmArgs.Add(version.LoggingArgument.Replace("${path}", logConfig));
            }

            // 3. Version JVM arguments.
            if (version.JvmArgs.Count > 0)
            {
                foreach (var entry in version.JvmArgs)
                {
                    if (!VersionParser.RulesAllow(entry.Rules, features)) continue;
                    foreach (var value in entry.Values)
                    {
                        plan.JvmArgs.Add(SubstituteJvm(value, version.Id, nativesDir, classpath, librariesDir));
                    }
                }
            }
            else
            {
                plan.JvmArgs.Add("-Djava.library.path=" + nativesDir);
                plan.JvmArgs.Add("-cp");
                plan.JvmArgs.Add(classpath);
            }

            // 4. Game arguments.
            var uuid = account.UnbatchedUuid ?? "";
            var token = account.AccessToken ?? "";

            if (version.GameArgs.Count > 0)
            {
                foreach (var entry in version.GameArgs)
                {
                    if (!VersionParser.RulesAllow(entry.Rules, features)) continue;
                    foreach (var value in entry.Values)
                    {
                        plan.GameArgs.Add(SubstituteGame(value, version, account, uuid, token, assetsRoot, gameDir));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(version.LegacyArguments))
            {
                foreach (var value in SplitArgs(version.LegacyArguments))
                {
                    plan.GameArgs.Add(SubstituteGame(value, version, account, uuid, token, assetsRoot, gameDir));
                }
            }

            return plan;
        }

        private static Dictionary<string, bool> DefaultFeatures()
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "is_demo_user", false },
                { "has_custom_resolution", false },
                { "has_quick_plays_support", false },
                { "is_quick_play_singleplayer", false },
                { "is_quick_play_multiplayer", false },
                { "is_quick_play_realms", false }
            };
        }

        private static string SubstituteJvm(string value, string versionId, string nativesDir, string classpath, string librariesDir)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace("${natives_directory}", nativesDir)
                .Replace("${launcher_name}", "FastCraft")
                .Replace("${launcher_version}", "1.0.0")
                .Replace("${classpath_separator}", ";")
                .Replace("${classpath}", classpath)
                .Replace("${library_directory}", librariesDir)
                .Replace("${version_name}", versionId ?? "");
        }

        private static string SubstituteGame(string value, VersionData version, Account account, string uuid, string token,
            string assetsRoot, string gameDir)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // ${auth_session} expands to "token:<token>:<uuid>", so it must be replaced
            // before ${auth_access_token} and ${auth_uuid}.
            return value
                .Replace("${auth_session}", "token:" + token + ":" + uuid)
                .Replace("${auth_player_name}", account.Name ?? "")
                .Replace("${version_name}", version.Id ?? "")
                .Replace("${game_directory}", gameDir)
                .Replace("${assets_root}", assetsRoot)
                .Replace("${game_assets}", assetsRoot)
                .Replace("${assets_index_name}", version.AssetsId ?? "legacy")
                .Replace("${auth_uuid}", uuid)
                .Replace("${auth_access_token}", token)
                .Replace("${clientid}", AuthService.ClientId)
                .Replace("${auth_xuid}", account.Xuid ?? "")
                .Replace("${user_type}", "msa")
                .Replace("${version_type}", version.Type ?? "release")
                .Replace("${user_properties}", "{}")
                .Replace("${resolution_width}", "854")
                .Replace("${resolution_height}", "480");
        }

        /// <summary>
        /// Splits an arguments string, honouring double quotes.
        /// </summary>
        public static List<string> SplitArgs(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input)) return result;

            var builder = new StringBuilder();
            bool inQuotes = false;
            foreach (char character in input)
            {
                if (character == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (!inQuotes && char.IsWhiteSpace(character))
                {
                    if (builder.Length > 0)
                    {
                        result.Add(builder.ToString());
                        builder.Length = 0;
                    }
                    continue;
                }
                builder.Append(character);
            }
            if (builder.Length > 0) result.Add(builder.ToString());
            return result;
        }
    }
}
