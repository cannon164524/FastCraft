using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FastCraft.Update;

namespace FastCraft
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _info;
        private readonly UpdateService _updates = new UpdateService();
        private CancellationTokenSource _cts;

        public UpdateWindow(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;
            VersionText.Text = UpdateService.DisplayTag(info.Tag) + " is ready. You have "
                              + UpdateService.ShortVersion(UpdateService.CurrentVersion) + ".";
            NotesText.Text = string.IsNullOrEmpty(info.Notes) ? "No notes were written for this build." : info.Notes;
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "Downloading…";

            try
            {
                var path = await _updates.DownloadAsync(_info, OnProgress, _cts.Token);
                StatusText.Text = "Update downloaded. FastCraft will restart to finish…";

                if (UpdateService.ApplyAndRestart(path))
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    StatusText.Text = "Could not start the updater. The new FastCraft is saved at " + path + ".";
                    UpdateButton.IsEnabled = true;
                    LaterButton.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Update cancelled.";
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Update failed: " + ex.Message;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
            }
            finally
            {
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
            Close();
        }

        private void OnProgress(double value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<double>(OnProgress), value);
                return;
            }
            Progress.Value = value;
            StatusText.Text = "Downloading… " + (int)(value * 100) + "%";
        }
    }
}
