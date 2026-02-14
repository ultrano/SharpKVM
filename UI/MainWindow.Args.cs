using System;
using System.Threading.Tasks;

namespace SharpKVM
{
    public partial class MainWindow
    {
        private void ParseLaunchArguments()
        {
            var result = LaunchArgumentParser.Parse(Program.LaunchArgs ?? Array.Empty<string>());
            _autoStartClientMode = result.AutoStartClientMode;
            _autoServerIP = result.AutoServerIP;
        }

        private async void OnWindowOpened(object? s, EventArgs e)
        {
            UpdateScreenCache();

            if (!_autoStartClientMode) return;
            if (!string.IsNullOrWhiteSpace(_autoServerIP)) _txtServerIP.Text = _autoServerIP;
            _tabControl.SelectedIndex = 1;
            await AutoStartClientConnectionAsync();
        }

        private async Task AutoStartClientConnectionAsync()
        {
            if (_isClientRunning) return;
            _btnConnect.IsEnabled = false;
            try
            {
                await Task.Delay(200);
                _ = StartClientLoop();
            }
            finally
            {
                _btnConnect.IsEnabled = true;
            }
        }
    }
}
