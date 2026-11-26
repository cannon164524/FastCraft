using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
            Title = "FastCraft " + UpdateService.CurrentVersion;
            TitleVersion.Text = UpdateService.CurrentVersion.ToString();
            Icon = BuildWindowIcon();

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

            _launcher.Crashed += OnGameCrashed;

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
            OfflineCheck.IsChecked = _settings.UseOffline;
            OfflineNameBox.Text = _settings.OfflineName ?? "";
            UpdateOfflineUi();
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
            VersionList.IsEnabled = !busy;
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
            var selected = VersionList.SelectedItem as string;
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
            VersionList.ItemsSource = ids;

            if (!string.IsNullOrEmpty(selected) && ids.Contains(selected))
            {
                VersionList.SelectedItem = selected;
            }
            else if (!string.IsNullOrEmpty(_manifest.LatestRelease) && ids.Contains(_manifest.LatestRelease))
            {
                VersionList.SelectedItem = _manifest.LatestRelease;
            }
            else if (ids.Count > 0)
            {
                VersionList.SelectedIndex = 0;
            }
            UpdateHero();
        }

        private VersionSummary SelectedVersion()
        {
            var id = VersionList.SelectedItem as string;
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

        // ------------------------------------------------------------- window chrome

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// The window and taskbar icon, drawn from the same shapes as the mark in App.xaml, so the
        /// launcher looks finished without shipping a bitmap. The exe's own icon comes from
        /// Assets\FastCraft.ico, which tools/make-icon.ps1 writes during the build.
        /// </summary>
        private static ImageSource BuildWindowIcon()
        {
            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                var tile = new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x2B));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x35, 0x42)), 1);
                context.DrawRoundedRectangle(tile, edge, new Rect(1, 1, 22, 22), 5.5, 5.5);

                var front = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xA5, 0x5B)), 2.1)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                context.DrawGeometry(null, front, Geometry.Parse("M 8,7 L 12.6,12 L 8,17"));

                var back = new Pen(new SolidColorBrush(Color.FromRgb(0x8C, 0x6E, 0x43)), 2.1)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                context.DrawGeometry(null, back, Geometry.Parse("M 13.2,7 L 17.8,12 L 13.2,17"));
            }
            group.Freeze();

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(64.0 / 24.0, 64.0 / 24.0));
                context.DrawDrawing(group);
            }
            var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ---------------------------------------------------------------- navigation

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageHost == null) return;
            var radio = sender as RadioButton;
            if (radio == null) return;
            ShowPage(radio.Tag as string ?? "play");
        }

        private void ShowPage(string tag)
        {
            var page = PlayPage;
            if (tag == "versions") page = VersionsPage;
            else if (tag == "settings") page = SettingsPage;
            else if (tag == "log") page = LogPage;
            if (page == null) return;

            var pages = new Grid[] { PlayPage, VersionsPage, SettingsPage, LogPage };
            foreach (var candidate in pages)
            {
                if (candidate == null || candidate == page) continue;
                candidate.Visibility = Visibility.Collapsed;
            }

            page.Visibility = Visibility.Visible;
            page.Opacity = 0;
            var slide = new TranslateTransform(0, 12);
            page.RenderTransform = slide;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromMilliseconds(240));
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateHero();
        }

        private void UpdateHero()
        {
            if (VersionHero == null) return;
            var summary = SelectedVersion();
            if (summary == null)
            {
                VersionHero.Text = "—";
                VersionMeta.Text = "No version selected";
                return;
            }
            VersionHero.Text = summary.Id;
            VersionMeta.Text = summary.Type == "snapshot" ? "Snapshot build" : "Release build";
        }

        /// <summary>
        /// The progress bar follows the download count in 200 ms steps; animating the value
        /// instead of assigning it turns those steps into a smooth sweep.
        /// </summary>
        private void SetProgress(double value)
        {
            var target = Math.Min(1.0, Math.Max(0.0, value));
            Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
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
                // A saved session that still works is checked first: reusing it costs one
                // request instead of a whole Microsoft sign-in, which Mojang throttles.
                if (_account != null && !string.IsNullOrEmpty(_account.AccessToken))
                {
                    SetBusy(true, "Checking your saved session…");
                    var reused = await _auth.ReuseAsync(_account, token);
                    if (reused != null)
                    {
                        _account = reused;
                        _account.Save(Locations.AccountPath);
                        UpdateAccountUi();
                        Log("Using the saved session for " + _account.Name + "; no new Microsoft sign-in needed.");
                        StatusText.Text = "Signed in as " + _account.Name + ".";
                        return;
                    }
                }

                StatusText.Text = "Starting Microsoft sign-in…";
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

        private void OfflineCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.UseOffline = OfflineCheck.IsChecked == true;
            _settings.Save(Locations.SettingsPath);
            UpdateOfflineUi();
            if (_settings.UseOffline)
            {
                Log("Offline mode is on: single player and LAN only. Online servers and Realms need a Microsoft sign-in.");
            }
        }

        private void OfflineNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_ready) return;
            _settings.OfflineName = (OfflineNameBox.Text ?? "").Trim();
            _settings.Save(Locations.SettingsPath);
        }

        private void UpdateOfflineUi()
        {
            if (OfflineCheck == null || OfflineNameBox == null) return;
            var offline = OfflineCheck.IsChecked == true;
            OfflineNameBox.IsEnabled = offline;
            OfflineHint.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Minecraft names are 3-16 characters of a-z, A-Z, 0-9 and underscore. The same rule is
        /// applied to an offline name, so a world made offline keeps working when its owner later
        /// signs in with the real account of that name.
        /// </summary>
        private void OnGameCrashed(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(OnGameCrashed), message);
                return;
            }
            Log(message);
            StatusText.Text = "Minecraft crashed.";
            MessageBox.Show(this,
                message + Environment.NewLine + Environment.NewLine
                + "Those two files hold the reason; the log above also has the game's output.",
                "Minecraft crashed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool IsValidPlayerName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 16) return false;
            foreach (var character in name)
            {
                var allowed = (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                           || (character >= '0' && character <= '9') || character == '_';
                if (!allowed) return false;
            }
            return true;
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

            // Which player gets launched: an offline profile when offline mode is ticked, the
            // signed-in Microsoft account otherwise.
            Account launchAccount;
            if (OfflineCheck.IsChecked == true)
            {
                var name = (OfflineNameBox.Text ?? "").Trim();
                if (!IsValidPlayerName(name))
                {
                    Log("Offline mode needs a player name of 3-16 letters, digits or underscores.");
                    MessageBox.Show(this, "Pick a player name for offline mode: 3-16 letters, digits or underscores.",
                        "Offline mode", MessageBoxButton.OK, MessageBoxImage.Information);
                    OfflineNameBox.Focus();
                    return;
                }
                launchAccount = Account.CreateOffline(name);
                Log("Offline mode: playing as " + name
                    + ". Single player and LAN work; online servers and Realms refuse an offline player.");
            }
            else if (_account != null)
            {
                launchAccount = _account;
            }
            else
            {
                await SignInAsync();
                if (_account == null) return;
                launchAccount = _account;
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
                if (!launchAccount.Offline && launchAccount.IsExpired)
                {
                    Log("Refreshing your Microsoft session…");
                    _account = await _auth.RefreshAsync(launchAccount, _cts.Token);
                    _account.Save(Locations.AccountPath);
                    UpdateAccountUi();
                    launchAccount = _account;
                }

                var versionPath = await _versions.EnsureVersionJsonAsync(summary, _downloader, _cts.Token);
                var version = VersionParser.Parse(File.ReadAllText(versionPath));

                Log("Launching " + version.Id + "…");
                var process = await _launcher.LaunchAsync(version, launchAccount, _settings, Log, _cts.Token);
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
                SetProgress(0);
                return;
            }
            var ratio = (double)done / total;
            SetProgress(ratio);

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
