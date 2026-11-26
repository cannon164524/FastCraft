using System;
using System.IO;

namespace FastCraft.Core
{
    public static class Locations
    {
        static Locations()
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastCraft");
        }

        public static string Root { get; private set; }

        public static string Versions { get { return Path.Combine(Root, "versions"); } }
        public static string Libraries { get { return Path.Combine(Root, "libraries"); } }
        public static string Assets { get { return Path.Combine(Root, "assets"); } }
        public static string Runtime { get { return Path.Combine(Root, "runtime"); } }
        public static string Instances { get { return Path.Combine(Root, "instances"); } }
        public static string CacheDir { get { return Path.Combine(Root, "cache"); } }
        public static string UpdateDir { get { return Path.Combine(Root, "update"); } }

        public static string ManifestPath { get { return Path.Combine(CacheDir, "version_manifest_v2.json"); } }
        public static string HashCachePath { get { return Path.Combine(CacheDir, "verified.json"); } }
        public static string SettingsPath { get { return Path.Combine(Root, "settings.json"); } }
        public static string AccountPath { get { return Path.Combine(Root, "account.dat"); } }

        public static string VersionDir(string versionId)
        {
            return Path.Combine(Versions, versionId);
        }

        public static string InstanceDir(string versionId)
        {
            return Path.Combine(Instances, versionId);
        }

        public static void Ensure()
        {
            SafeCreate(Root);
            SafeCreate(Versions);
            SafeCreate(Libraries);
            SafeCreate(Assets);
            SafeCreate(Runtime);
            SafeCreate(Instances);
            SafeCreate(CacheDir);
        }

        private static void SafeCreate(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
            }
        }
    }
}
