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
