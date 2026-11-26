using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FastCraft.Core
{
    public static class Hashing
    {
        public static string Sha1File(string path)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var hash = sha1.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        public static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
