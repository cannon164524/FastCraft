using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FastCraft.Fast
{
    /// <summary>
    /// Writes the FancyMenu set-up that a resource pack cannot: a custom menu title and a line
    /// of text in the pause menu. The file formats are FancyMenu's own:
    ///
    ///   layouts        config/fancymenu/customization/*.txt   ("type = fancymenu_layout",
    ///                                                          "container { key = value }")
    ///   settings       config/fancymenu/options.txt            (key = value)
    ///   customised     config/fancymenu/customizablemenus.txt  (one screen class per line)
    ///
    /// The screen classes are read from the client jar, so a package rename in a later
    /// version does not silently point the layouts at nothing.
    /// </summary>
    public sealed class FancyMenuSetup
    {
        private const string FallbackTitle = "net.minecraft.client.gui.screens.TitleScreen";
        private const string FallbackPause = "net.minecraft.client.gui.screens.PauseScreen";

        public bool Apply(string gameDirectory, string clientJar, Action<string> log)
        {
            var directory = Path.Combine(gameDirectory, "config", "fancymenu");
            var customization = Path.Combine(directory, "customization");
            Directory.CreateDirectory(customization);

            var classes = GameAssets.FindScreenClasses(clientJar, "TitleScreen", "PauseScreen");
            var title = Pick(classes, "TitleScreen", FallbackTitle);
            var pause = Pick(classes, "PauseScreen", FallbackPause);

            WriteOptions(directory);
            AddCustomizableScreens(directory, pause);
            WriteLayout(Path.Combine(customization, "fastcraft_pause.txt"), pause,
                "FastCraft \u00b7 Game Menu", "FASTCRAFT");

            log("[brand] FancyMenu set-up written for " + pause + ".");
            return true;
        }

        private static string Pick(Dictionary<string, string> classes, string key, string fallback)
        {
            string value;
            if (classes != null && classes.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            return fallback;
        }

        /// <summary>
        /// FancyMenu's own settings. "modpack_mode" hides its menu bar and hotkeys, which is what
        /// makes a branded menu look like a client instead of a mod; existing keys are kept.
        /// </summary>
        private static void WriteOptions(string directory)
        {
            var path = Path.Combine(directory, "options.txt");
            var lines = ReadLines(path);

            SetOption(lines, "modpack_mode", "true");
            SetOption(lines, "show_welcome_screen", "false");

            WriteLines(path, lines);
        }

        private static void SetOption(List<string> lines, string key, string value)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.Length <= key.Length) continue;
                if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

                var rest = trimmed.Substring(key.Length).TrimStart();
                if (rest.Length == 0) continue;
                if (rest[0] != '=' && rest[0] != ':') continue;

                lines[i] = key + " = " + value;
                return;
            }
            lines.Add(key + " = " + value);
        }

        /// <summary>FancyMenu only touches screens listed here, by class name.</summary>
        private static void AddCustomizableScreens(string directory, params string[] screens)
        {
            var path = Path.Combine(directory, "customizablemenus.txt");
            var lines = ReadLines(path);

            foreach (var screen in screens)
            {
                if (string.IsNullOrEmpty(screen)) continue;
                var present = false;
                foreach (var line in lines)
                {
                    if (string.Equals(line.Trim(), screen, StringComparison.OrdinalIgnoreCase))
                    {
                        present = true;
                        break;
                    }
                }
                if (!present) lines.Add(screen);
            }

            WriteLines(path, lines);
        }

        /// <summary>
        /// One layout file in FancyMenu's property format. Only keys its own serializers write
        /// are used: layout-meta with the screen identifier, and a text_v2 element.
        /// </summary>
        private static void WriteLayout(string path, string screen, string menuTitle, string cornerText)
        {
            var builder = new StringBuilder();
            builder.AppendLine("type = fancymenu_layout");
            builder.AppendLine();
            builder.AppendLine("layout-meta {");
            builder.AppendLine("  identifier = " + screen);
            builder.AppendLine("  render_custom_elements_behind_vanilla = false");
            builder.AppendLine("  last_edited_time = " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            builder.AppendLine("  is_enabled = true");
            if (!string.IsNullOrEmpty(menuTitle)) builder.AppendLine("  custom_menu_title = " + menuTitle);
            builder.AppendLine("}");
            builder.AppendLine();

            if (!string.IsNullOrEmpty(cornerText))
            {
                builder.AppendLine("element {");
                builder.AppendLine("  element_type = text_v2");
                builder.AppendLine("  instance_identifier = fastcraft_corner_mark");
                builder.AppendLine("  source = " + cornerText);
                builder.AppendLine("  source_mode = DIRECT");
                builder.AppendLine("  x = 6");
                builder.AppendLine("  y = 6");
                builder.AppendLine("  width = 200");
                builder.AppendLine("  height = 20");
                builder.AppendLine("  stay_on_screen = true");
                builder.AppendLine("}");
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static List<string> ReadLines(string path)
        {
            try
            {
                if (File.Exists(path)) return new List<string>(File.ReadAllLines(path));
            }
            catch
            {
            }
            return new List<string>();
        }

        private static void WriteLines(string path, List<string> lines)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch
            {
            }
        }
    }
}
