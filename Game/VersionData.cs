using System;
using System.Collections.Generic;

namespace FastCraft.Game
{
    public sealed class Rule
    {
        public string Action = "allow";
        public string OsName;
        public string OsArch;
        public string OsVersion;
        public Dictionary<string, bool> Features;
    }

    public sealed class ArgEntry
    {
        public readonly List<string> Values = new List<string>();
        public List<Rule> Rules;
    }

    public sealed class LibraryInfo
    {
        public string Name;
        public string Key;
        public bool NativeOnly;
        public string ArtifactPath;
        public string ArtifactUrl;
        public string ArtifactSha1;
        public long ArtifactSize;
        public string NativePath;
        public string NativeUrl;
        public string NativeSha1;
        public long NativeSize;
        public readonly List<string> ExtractExclude = new List<string>();
    }

    public sealed class AssetIndexInfo
    {
        public string Id;
        public string Url;
        public string Sha1;
        public long Size;
        public long TotalSize;
        public bool Virtual;
        public bool MapToResources;
    }

    public sealed class VersionData
    {
        public string Id = "";
        public string Type = "release";
        public string MainClass = "net.minecraft.client.main.Main";
        public string AssetsId = "legacy";
        public string JarId;
        public AssetIndexInfo AssetIndex;
        public string ClientUrl;
        public string ClientSha1;
        public long ClientSize;
        public string JavaComponent = "jre-legacy";
        public int JavaMajor = 8;
        public readonly List<LibraryInfo> Libraries = new List<LibraryInfo>();
        public readonly List<ArgEntry> JvmArgs = new List<ArgEntry>();
        public readonly List<ArgEntry> GameArgs = new List<ArgEntry>();
        public string LegacyArguments;
        public string LoggingArgument;
        public string LoggingFileUrl;
        public string LoggingFileSha1;
        public long LoggingFileSize;
        public string LoggingFileId;
    }
}
