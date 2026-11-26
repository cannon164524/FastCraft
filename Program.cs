using System;
using System.IO;
using System.Windows;

namespace FastCraft
{
    /// <summary>
    /// Custom entry point: if FastCraft cannot start, the reason is written to
    /// %APPDATA%\FastCraft\startup.log and shown in a message box, instead of the
    /// process just disappearing.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            string logPath;
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastCraft");
                Directory.CreateDirectory(directory);
                logPath = Path.Combine(directory, "startup.log");
            }
            catch
            {
                logPath = Path.Combine(Path.GetTempPath(), "FastCraft-startup.log");
            }

            Write(logPath, "FastCraft " + typeof(Program).Assembly.GetName().Version + " starting " + DateTime.Now.ToString("u"));

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
            {
                Write(logPath, "Fatal (AppDomain): " + args.ExceptionObject);
                Show(logPath, args.ExceptionObject.ToString());
            };

            try
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                Write(logPath, "Fatal (Main): " + ex);
                Show(logPath, ex.ToString());
            }
        }

        private static void Write(string path, string text)
        {
            try
            {
                File.AppendAllText(path, text + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void Show(string logPath, string text)
        {
            try
            {
                MessageBox.Show(text + Environment.NewLine + Environment.NewLine + "Details were written to " + logPath,
                    "FastCraft could not start");
            }
            catch
            {
            }
        }
    }
}
