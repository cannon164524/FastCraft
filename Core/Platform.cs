using System;

namespace FastCraft.Core
{
    public static class Platform
    {
        public static string ArchName
        {
            get { return Environment.Is64BitOperatingSystem ? "x86_64" : "x86"; }
        }

        public static string ArchBits
        {
            get { return Environment.Is64BitOperatingSystem ? "64" : "32"; }
        }
    }
}
