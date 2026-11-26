using System;
using System.IO;
using FastCraft.Core;

namespace FastCraft.Auth
{
    /// <summary>
    /// Every sign-in step is appended to %APPDATA%\FastCraft\auth.log — HTTP status and the
    /// server's error text, never a token — so a failure can be explained from that file.
    /// </summary>
    public static class AuthLog
    {
        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Locations.Root);
                File.AppendAllText(Path.Combine(Locations.Root, "auth.log"),
                    DateTime.Now.ToString("u") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
