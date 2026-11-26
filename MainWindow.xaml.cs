using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FastCraft.Auth;
using FastCraft.Core;
using FastCraft.Game;
using FastCraft.Update;

namespace FastCraft
{
    public partial class MainWindow : Window
    {
        private readonly Settings _settings;
        private readonly HashCache _hashCache;
        private readonly Downloader _downloader;
        private readonly AuthService _auth;
        private readonly JavaManager _java;
        private readonly Launcher _launcher;
        private readonly VersionService _versions;
        private readonly DispatcherTimer _timer;
        private readonly UpdateService _updates = new UpdateService();

        private VersionManifestData _manifest;
        private List<VersionSummary> _shownVersions = new List<VersionSummary>();
        private Account _account;
        private UpdateWindow _updateWindow;
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _ready;

        public MainWindow()
        {
            InitializeComponent();

            Locations.Ensure();
            _settings = Settings.Load(Locations.SettingsPath);
            _hashCache = HashCache.Load(Locations.HashCachePath);
            _downloader = new Downloader(_hashCache, Math.Max(4, _settings.DownloadParallelism));
            _auth = new AuthService();
            _java = new JavaManager();
            _launcher = new Launcher(_downloader, _java);
            _versions = new VersionService();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettingsToUi();

            try
            {
                _account = Account.Load(Locations.AccountPath);
            }
            catch
            {
                _account = null;
            }
            UpdateAccountUi();

            _ready = true;
            await ReloadVersionsAsync(false);
            _ = CheckForUpdatesAsync(false);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _settings.Save(Locations.SettingsPath);
                _hashCache.Save();
            }
            catch
            {
            }
            if (_timer != null) _timer.Stop();
            if (_launcher != null && _launcher.Current != null && !_launcher.Current.HasExited)
            {
                // Leave the game running when the launcher closes.
            }
        }

        private void ApplySettingsToUi()
        {
            MemorySlider.Value = Math.Min(32768, Math.Max(1024, _settings.MemoryMb));
            MemoryText.Text = _settings.MemoryMb + " MB";
            JavaPathBox.Text = _settings.JavaPath ?? "";
            ExtraJvmBox.Text = _settings.ExtraJvmArgs ?? "";
            DownloadJavaCheck.IsChecked = _settings.DownloadJava;
            TurboCheck.IsChecked = _settings.TurboJvmFlags;
            KeepOpenCheck.IsChecked = _settings.KeepLauncherOpen;
            SnapshotsCheck.IsChecked = _settings.ShowSnapshots;
            CheckUpdatesCheck.IsChecked = _settings.CheckForUpdates;
        }

        private void Log(string message)
        {
            if (LogBox == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(Log), message);
                return;
            }
            try
            {
                if (LogBox.Text.Length > 200000)
                {
                    LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - 100000);
                }
                LogBox.AppendText(message + Environment.NewLine);
                LogBox.ScrollToEnd();
            }
            catch
            {
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            LaunchButton.IsEnabled = !busy;
            SignInButton.IsEnabled = !busy;
            RefreshVersionsButton.IsEnabled = !busy;
            VersionCombo.IsEnabled = !busy;
            SnapshotsCheck.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            if (!string.IsNullOrEmpty(status)) StatusText.Text = status;
        }

        private async Task ReloadVersionsAsync(bool force)
        {
            SetBusy(true, "Loading the version list…");
            try
            {
                _manifest = await _versions.GetManifestAsync(force, CancellationToken.None);
                PopulateVersions();
            }
            catch (Exception ex)
            {
                Log("Could not load the version list: " + ex.Message);
                StatusText.Text = "Could not load the version list.";
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void PopulateVersions()
        {
            if (_manifest == null) return;

            var showSnapshots = SnapshotsCheck.IsChecked == true;
            var selected = VersionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) selected = _settings.LastVersion;

            var versions = new List<VersionSummary>();
            foreach (var version in _manifest.Versions)
            {
                if (version.Type == "release") versions.Add(version);
                else if (showSnapshots && version.Type == "snapshot") versions.Add(version);
            }
            _shownVersions = versions;

            var ids = new List<string>();
            foreach (var version in versions) ids.Add(version.Id);
            VersionCombo.ItemsSource = ids;

            if (!string.IsNullOrEmpty(selected) && ids.Contains(selected))
            {
                VersionCombo.SelectedItem = selected;
            }
            else if (!string.IsNullOrEmpty(_manifest.LatestRelease) && ids.Contains(_manifest.LatestRelease))
            {
                VersionCombo.SelectedItem = _manifest.LatestRelease;
            }
            else if (ids.Count > 0)
            {
                VersionCombo.SelectedIndex = 0;
            }
        }

        private VersionSummary SelectedVersion()
        {
            var id = VersionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var version in _shownVersions)
            {
                if (version.Id == id) return version;
            }
            return null;
        }

        private void UpdateAccountUi()
        {
            if (_account == null)
            {
                AccountText.Text = "Not signed in";
                SignOutButton.IsEnabled = false;
            }
            else
            {
                AccountText.Text = "Signed in as " + _account.Name;
                SignOutButton.IsEnabled = true;
            }
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (!_settings.CheckForUpdates && !manual) return;

            try
            {
                if (File.Exists(UpdateService.FailedReportPath))
                {
                    try
                    {
                        File.Delete(UpdateService.FailedReportPath);
                    }
                    catch
                    {
                    }
                    MessageBox.Show(this,
                        "The last update could not be applied automatically. The new FastCraft is saved in "
                        + Locations.UpdateDir + " — replace FastCraft.exe with it by hand, or move FastCraft.exe somewhere you can write to.",
                        "FastCraft update", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch
            {
            }

            var check = await _updates.CheckAsync(CancellationToken.None);
            Log("Update check: " + check.Message);

            if (check.Info != null)
            {
                if (_updateWindow == null)
                {
                    var window = new UpdateWindow(check.Info) { Owner = this };
                    _updateWindow = window;
                    window.Closed += delegate { _updateWindow = null; };
                    window.Show();
                }
                return;
            }

            if (manual)
            {
                MessageBox.Show(this, check.Message, "FastCraft updates", MessageBoxButton.OK,
                    check.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            await CheckForUpdatesAsync(true);
        }

        private static void WriteAuthLog(string message)
        {
            try
            {
                var path = Path.Combine(Locations.Root, "auth.log");
                File.AppendAllText(path, DateTime.Now.ToString("u") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private async Task SignInAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetBusy(true, "Starting Microsoft sign-in…");
            DeviceCodeText.Text = "";
            try
            {
                var code = await _auth.StartDeviceCodeAsync(token);
                DeviceCodeText.Text = "Enter code " + code.UserCode + " at " + code.VerificationUri;
                Log("A browser will open. Enter the code " + code.UserCode + " at " + code.VerificationUri + " and sign in with the Microsoft account that owns Minecraft.");
                try
                {
                    Process.Start(new ProcessStartInfo(code.VerificationUri) { UseShellExecute = true });
                }
                catch
                {
                    Log("Could not open the browser. Open " + code.VerificationUri + " yourself.");
                }

                var polled = await _auth.PollAsync(code, token);
                _account = await _auth.CompleteLoginAsync(polled, token);
                _account.Save(Locations.AccountPath);
                UpdateAccountUi();
                DeviceCodeText.Text = "";
                Log("Signed in as " + _account.Name + ".");
            }
            catch (OperationCanceledException)
            {
                Log("Sign-in cancelled.");
                DeviceCodeText.Text = "";
            }
            catch (AuthException ex)
            {
                Log("Sign-in failed: " + ex.Message);
                WriteAuthLog(ex.Message);
                DeviceCodeText.Text = "";
            }
            catch (Exception ex)
            {
                Log("Sign-in failed: " + ex.Message);
                WriteAuthLog(ex.Message);
                DeviceCodeText.Text = "";
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            await SignInAsync();
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            _account = null;
            try
            {
                if (File.Exists(Locations.AccountPath)) File.Delete(Locations.AccountPath);
            }
            catch
            {
            }
            UpdateAccountUi();
            Log("Signed out.");
        }

        private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            await ReloadVersionsAsync(true);
        }

        private void SnapshotsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.ShowSnapshots = SnapshotsCheck.IsChecked == true;
            _settings.Save(Locations.SettingsPath);
            PopulateVersions();
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            if (_account == null)
            {
                await SignInAsync();
                if (_account == null) return;
            }

            var summary = SelectedVersion();
            if (summary == null)
            {
                Log("Pick a version first.");
                return;
            }

            _settings.LastVersion = summary.Id;
            _settings.Save(Locations.SettingsPath);

            _downloader.ResetCounters();
            _cts = new CancellationTokenSource();
            SetBusy(true, "Preparing " + summary.Id + "…");
            _timer.Start();

            try
            {
                if (_account.IsExpired)
                {
                    Log("Refreshing your Microsoft session…");
                    _account = await _auth.RefreshAsync(_account, _cts.Token);
                    _account.Save(Locations.AccountPath);
                    UpdateAccountUi();
                }

                var versionPath = await _versions.EnsureVersionJsonAsync(summary, _downloader, _cts.Token);
                var version = VersionParser.Parse(File.ReadAllText(versionPath));

                Log("Launching " + version.Id + "…");
                var process = await _launcher.LaunchAsync(version, _account, _settings, Log, _cts.Token);
                Log("Minecraft is running (process " + process.Id + ").");
                StatusText.Text = "Minecraft is running.";
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled.");
                StatusText.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                Log("Launch failed: " + ex.Message);
                StatusText.Text = "Launch failed.";
                MessageBox.Show(this, ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _timer.Stop();
                _hashCache.Save();
                SetBusy(false, null);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            var summary = SelectedVersion();
            var directory = summary != null ? Locations.InstanceDir(summary.Id) : Locations.Root;
            try
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("Could not open the folder: " + ex.Message);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            int total = _downloader.Total;
            int done = _downloader.Done;
            if (total <= 0)
            {
                Progress.Value = 0;
                return;
            }
            var ratio = (double)done / total;
            Progress.Value = Math.Min(1.0, Math.Max(0.0, ratio));

            var megabytes = _downloader.Bytes / (1024.0 * 1024.0);
            StatusText.Text = done + "/" + total + " files, " + megabytes.ToString("0.0") + " MB downloaded, "
                             + _downloader.Skipped + " already up to date — " + _downloader.Current;
        }

        private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready || MemoryText == null) return;
            var megabytes = (int)e.NewValue;
            MemoryText.Text = megabytes + " MB";
            _settings.MemoryMb = megabytes;
            _settings.Save(Locations.SettingsPath);
        }

        private void JavaPathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_ready) return;
            _settings.JavaPath = JavaPathBox.Text.Trim().Trim('"');
            _settings.Save(Locations.SettingsPath);
        }

        private void ExtraJvmBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_ready) return;
            _settings.ExtraJvmArgs = ExtraJvmBox.Text.Trim();
            _settings.Save(Locations.SettingsPath);
        }

        private void BrowseJavaButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick javaw.exe or java.exe",
                Filter = "Java executables|java*.exe|All files|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                JavaPathBox.Text = dialog.FileName;
            }
        }

        private void SettingsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.DownloadJava = DownloadJavaCheck.IsChecked == true;
            _settings.TurboJvmFlags = TurboCheck.IsChecked == true;
            _settings.KeepLauncherOpen = KeepOpenCheck.IsChecked == true;
            _settings.CheckForUpdates = CheckUpdatesCheck.IsChecked == true;
            _settings.Save(Locations.SettingsPath);
        }
    }
}
