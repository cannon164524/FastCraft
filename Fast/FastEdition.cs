using System;

namespace FastCraft.Fast
{
    /// <summary>
    /// What "the FastCraft edition" is: the one game version it is built on, and the
    /// performance mods it installs on top of that version. Everything here is data, so
    /// supporting a new release means changing <see cref="GameVersion"/> and re-checking
    /// the list. A mod with no build for that version is skipped and logged; it is never
    /// guessed at, and it never stops the game from starting.
    /// </summary>
    public static class FastEdition
    {
        /// <summary>
        /// The only game version the FastCraft edition exists for. Anything older is started
        /// as plain vanilla Minecraft, and the launcher says so before it launches.
        /// </summary>
        public const string GameVersion = "26.3";

        /// <summary>The file that lists the mods this launcher put in the mods folder.</summary>
        public const string ManifestName = ".fastcraft-managed.txt";

        public static bool Supports(string versionId)
        {
            return string.Equals(versionId, GameVersion, StringComparison.OrdinalIgnoreCase);
        }

        public sealed class Mod
        {
            public string Slug;
            public string Name;
            public string Gain;

            public Mod(string slug, string name, string gain)
            {
                Slug = slug;
                Name = name;
                Gain = gain;
            }
        }

        /// <summary>
        /// The performance layer, in the order it is worth explaining: what draws the world,
        /// what ticks it, what travels over the network, what is hidden from the camera, and
        /// the library the rest is built on. FerriteCore is deliberately absent: its newest
        /// build was made for an older game version and only carries a 26.3 tag, so installing
        /// it risks a crash for a small memory gain.
        /// </summary>
        public static readonly Mod[] Mods =
        {
            new Mod("sodium", "Sodium", "rewritten chunk renderer"),
            new Mod("lithium", "Lithium", "game logic and ticking"),
            new Mod("krypton", "Krypton", "network stack"),
            new Mod("immediatelyfast", "ImmediatelyFast", "batched immediate rendering"),
            new Mod("entityculling", "EntityCulling", "skips entities you cannot see"),
            new Mod("moreculling", "MoreCulling", "skips hidden block faces"),
            new Mod("modernfix", "ModernFix", "load time and memory"),
            new Mod("fabric-api", "Fabric API", "the interfaces the others are built on"),
            new Mod("fancymenu", "FancyMenu", "the layout and text of the menus")
        };
    }
}
