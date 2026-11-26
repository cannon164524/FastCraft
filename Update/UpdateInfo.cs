using System;

namespace FastCraft.Update
{
    /// <summary>
    /// A newer FastCraft published as a GitHub release.
    /// </summary>
    public sealed class UpdateInfo
    {
        public string Tag;
        public Version Version;
        public string Url;
        public long Size;
        public string Notes;
    }
}
