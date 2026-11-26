using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastCraft.Auth
{
    public sealed class Account
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
        [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("xuid")] public string Xuid { get; set; } = "";
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// True for a local offline profile: a nickname with no Microsoft session behind it. Such
        /// a player can play single player and LAN, and online servers refuse it — that is the
        /// whole point of the distinction, so nothing here is signed in.
        /// </summary>
        [JsonPropertyName("offline")] public bool Offline { get; set; }

        public static Account CreateOffline(string name)
        {
            return new Account
            {
                Name = name,
                Uuid = OfflineUuid(name),
                AccessToken = "0",
                Xuid = "",
                Offline = true,
                ExpiresAt = DateTime.MaxValue
            };
        }

        /// <summary>
        /// The UUID the game gives an offline player: a version-3 (MD5) UUID of
        /// "OfflinePlayer:" + name, exactly as java.util.UUID.nameUUIDFromBytes builds it. Using
        /// the same name therefore always means the same player, so worlds keep their data.
        /// </summary>
        public static string OfflineUuid(string name)
        {
            byte[] hash;
            using (var md5 = MD5.Create())
            {
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + (name ?? "")));
            }
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            var hex = new StringBuilder(32);
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("x2"));
            }
            var text = hex.ToString();
            return text.Substring(0, 8) + "-" + text.Substring(8, 4) + "-" + text.Substring(12, 4)
                 + "-" + text.Substring(16, 4) + "-" + text.Substring(20, 12);
        }

        [JsonIgnore]
        public bool IsExpired
        {
            get { return DateTime.UtcNow > ExpiresAt.AddMinutes(-2); }
        }

        public string UnbatchedUuid
        {
            get { return (Uuid ?? "").Replace("-", ""); }
        }

        public static Account Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var protectedBytes = File.ReadAllBytes(path);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Account>(Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                return null;
            }
        }

        public void Save(string path)
        {
            try
            {
                var json = JsonSerializer.Serialize(this);
                var bytes = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, protectedBytes);
            }
            catch
            {
            }
        }
    }

    public sealed class AuthException : Exception
    {
        public AuthException(string message) : base(message)
        {
        }
    }
}
