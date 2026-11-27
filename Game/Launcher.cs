using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Auth;
using FastCraft.Core;
using FastCraft.Fast;

namespace FastCraft.Game
{
    /// <summary>
    /// Installs the version, resolves Java, builds the command line and starts the game.
    /// </summary>
    public sealed class Launcher
    {
        private readonly Downloader _downloader;
        private readonly GameInstaller _installer;
        private readonly JavaManager _java;
        private readonly FastEditionInstaller _fast;

        public Launcher(Downloader downloader, JavaManager java)
        {
            _downloader = downloader;
            _java = java;
            _installer = new GameInstaller(downloader);
            _fast = new FastEditionInstaller(downloader);
        }

        public Process Current { get; private set; }

        /// <summary>
        /// Raised with an explanation (exit code and where the crash report and game log
        /// are) when Minecraft exits with a non-zero code. May be raised on any thread.
        /// </summary>
        public event Action<string> Crashed;

        /// <summary>
        /// Raised with the reason when the performance layer could not be added and the game
        /// starts as vanilla instead. May be raised on any thread.
        /// </summary>
        public event Action<string> EditionFailed;

        private void RaiseEditionFailed(string message)
        {
            var handler = EditionFailed;
            if (handler == null) return;
            try
            {
                handler(message);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Starts Minecraft. With <paramref name="fastEdition"/> the game is started through the
        /// FastCraft edition: the Fabric loader, the performance mods and the preset are put in
        /// place first, and the game runs on Fabric's main class. If any of that cannot be
        /// installed the game still starts, as plain vanilla, and the log says why.
        /// </summary>
        public async Task<Process> LaunchAsync(VersionData version, Account account, Settings settings, bool fastEdition,
            Action<string> log, CancellationToken cancellationToken)
        {
            log("Preparing " + version.Id + "…");
            var install = await _installer.InstallAsync(version, log, cancellationToken).ConfigureAwait(false);

            var javaPath = await _java.EnsureJavaAsync(version.JavaComponent, version.JavaMajor, settings, _downloader, log, cancellationToken).ConfigureAwait(false);

            var gameDirectory = Locations.InstanceDir(version.Id);
            Directory.CreateDirectory(gameDirectory);

            var fastLibraries = new List<string>();
            var mainClass = version.MainClass;
            var editionJvmArgs = new List<string>();

            if (fastEdition)
            {
                try
                {
                    var edition = await _fast.EnsureAsync(version.Id, gameDirectory, log, cancellationToken).ConfigureAwait(false);
                    if (edition.Active)
                    {
                        fastLibraries.AddRange(edition.LibraryPaths);
                        mainClass = edition.MainClass;
                        editionJvmArgs.AddRange(edition.JvmArgs);
                        log("[fast] FastCraft edition ready: " + edition.Summary + ".");
                    }
                    else
                    {
                        log("[fast] FastCraft edition not available (" + edition.Problem + "). Starting vanilla Minecraft.");
                        RaiseEditionFailed("the performance mods could not be added: " + edition.Problem + ".");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log("[fast] FastCraft edition could not be installed: " + ex.Message + " Starting vanilla Minecraft.");
                    RaiseEditionFailed("the performance mods could not be added: " + ex.Message + ".");
                }
            }

            // Fabric's jars come first: its ASM and Mixin builds have to win over the copies the
            // game ships, because the loader is compiled against exactly those. The client jar
            // always comes last.
            var classpathEntries = new List<string>();
            classpathEntries.AddRange(fastLibraries);
            classpathEntries.AddRange(install.Classpath);
            classpathEntries.Add(install.ClientJar);
            var classpath = string.Join(";", classpathEntries);

            var plan = LaunchBuilder.Build(version, account, settings, classpath, install.NativesDir, install.AssetsRoot, gameDirectory, install.LogConfig, Locations.Libraries);

            // Fabric's own main class boots the same game and loads the mods; its few JVM
            // arguments are plain system properties, so they stay valid after the classpath.
            plan.MainClass = mainClass;
            if (editionJvmArgs.Count > 0) plan.JvmArgs.AddRange(editionJvmArgs);

            var info = new ProcessStartInfo(javaPath)
            {
                UseShellExecute = false,
                WorkingDirectory = gameDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in plan.JvmArgs)
            {
                info.ArgumentList.Add(argument);
            }
            info.ArgumentList.Add(plan.MainClass);
            foreach (var argument in plan.GameArgs)
            {
                info.ArgumentList.Add(argument);
            }

            log("Starting Java " + version.JavaMajor + " with " + settings.MemoryMb + " MB…");

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null) log("[mc] " + args.Data);
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null) log("[mc] " + args.Data);
            };
            process.Exited += (sender, args) =>
            {
                try
                {
                    var code = process.ExitCode;
                    if (code == 0)
                    {
                        log("[mc] Minecraft exited normally.");
                        return;
                    }

                    var message = DescribeCrash(gameDirectory, code);
                    log(message);

                    var handler = Crashed;
                    if (handler != null)
                    {
                        try
                        {
                            handler(message);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                // High priority for the JVM: fewer stutters while other Windows apps work.
                process.PriorityClass = ProcessPriorityClass.High;
            }
            catch
            {
            }

            Current = process;
            return process;
        }

        /// <summary>
        /// Finds the newest crash report and the game log for a crashed run, so the reason
        /// can be read without hunting through folders.
        /// </summary>
        private static string DescribeCrash(string gameDirectory, int code)
        {
            var message = "Minecraft exited with code " + code + ".";
            try
            {
                var reports = Path.Combine(gameDirectory, "crash-reports");
                if (Directory.Exists(reports))
                {
                    var latest = "";
                    var latestTime = DateTime.MinValue;
                    foreach (var file in Directory.GetFiles(reports, "*.txt"))
                    {
                        var time = File.GetLastWriteTimeUtc(file);
                        if (time > latestTime)
                        {
                            latestTime = time;
                            latest = file;
                        }
                    }
                    if (!string.IsNullOrEmpty(latest))
                    {
                        message += Environment.NewLine + "Crash report: " + latest;
                    }
                }

                var log = Path.Combine(gameDirectory, "logs", "latest.log");
                if (File.Exists(log))
                {
                    message += Environment.NewLine + "Game log: " + log;
                }
            }
            catch
            {
            }
            return message;
        }
    }
}
