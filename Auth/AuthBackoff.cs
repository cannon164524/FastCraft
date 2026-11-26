using System;
using System.IO;
using FastCraft.Core;

namespace FastCraft.Auth
{
    /// <summary>
    /// Mojang throttles the Minecraft sign-in endpoints: once one answer is 429, every further
    /// attempt makes the wait longer. The moment the throttle ends is kept on disk, so pressing
    /// Sign in again does not spend another request before the wait is over.
    /// </summary>
    public static class AuthBackoff
    {
        private static DateTime _until = DateTime.MinValue;

        static AuthBackoff()
        {
            Load();
        }

        private static string FilePath { get { return Path.Combine(Locations.CacheDir, "auth-throttle.txt"); } }

        public static bool IsActive { get { return DateTime.UtcNow < _until; } }

        public static int RemainingMinutes
        {
            get
            {
                var left = (_until - DateTime.UtcNow).TotalMinutes;
                if (left <= 0) return 0;
                return (int)Math.Ceiling(left);
            }
        }

        public static void Register(double seconds)
        {
            // Mojang often sends no Retry-After, and its throttling lasts minutes rather than
            // seconds, so an unstated wait is ten minutes.
            if (seconds <= 0) seconds = 600;
            if (seconds > 3600) seconds = 3600;
            var until = DateTime.UtcNow.AddSeconds(seconds);
            if (until > _until) _until = until;
            Save();
        }

        public static void Clear()
        {
            _until = DateTime.MinValue;
            Save();
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                long ticks;
                if (long.TryParse(File.ReadAllText(FilePath).Trim(), out ticks) && ticks > 0)
                {
                    _until = new DateTime(ticks, DateTimeKind.Utc);
                }
            }
            catch
            {
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Locations.CacheDir);
                if (_until > DateTime.UtcNow)
                {
                    File.WriteAllText(FilePath, _until.Ticks.ToString());
                }
                else if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
            }
        }
    }
}
