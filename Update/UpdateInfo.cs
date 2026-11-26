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
        public DateTime? PublishedAt;
    }

    /// <summary>
    /// The outcome of an update check: what was found, and a message for the log — including
    /// the case where GitHub answers 404 because the repository is private.
    /// </summary>
    public sealed class UpdateCheck
    {
        public UpdateInfo Info;
        public string Message;
        public bool Failed;
    }
}
