using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpKVM
{
    public partial class MainWindow
    {
#if DEBUG
        private sealed class VirtualResolutionPreset
        {
            public string Label { get; init; } = string.Empty;
            public int Width { get; init; }
            public int Height { get; init; }

            public override string ToString() => Label;
        }

        private void InitializeVirtualResolutionPresets()
        {
            if (_cmbVirtualResolution == null) return;

            var presets = new[]
            {
                new VirtualResolutionPreset { Label = "1280x720", Width = 1280, Height = 720 },
                new VirtualResolutionPreset { Label = "1600x900", Width = 1600, Height = 900 },
                new VirtualResolutionPreset { Label = "1920x1080", Width = 1920, Height = 1080 },
                new VirtualResolutionPreset { Label = "2560x1440", Width = 2560, Height = 1440 },
                new VirtualResolutionPreset { Label = "3840x2160", Width = 3840, Height = 2160 }
            };

            _cmbVirtualResolution.ItemsSource = presets;
            _cmbVirtualResolution.SelectedIndex = 2;
        }

        private void OnVirtualResolutionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_cmbVirtualResolution?.SelectedItem is not VirtualResolutionPreset preset) return;
            _selectedVirtualWidth = preset.Width;
            _selectedVirtualHeight = preset.Height;
        }

        private void OnAddVirtualClientClicked(object? sender, RoutedEventArgs e)
        {
            if (!_isServerRunning)
            {
                Log("Start server first, then add virtual client.");
                return;
            }

            _virtualClientHost ??= CreateVirtualClientHost();
            Log($"Starting virtual client with {_selectedVirtualWidth}x{_selectedVirtualHeight}.");
            if (!_virtualClientHost.TryStart("127.0.0.1", DEFAULT_PORT, _selectedVirtualWidth, _selectedVirtualHeight, false))
            {
                Log("Virtual client already running.");
                return;
            }

            if (_btnAddVirtualClient != null) _btnAddVirtualClient.IsEnabled = false;
        }

        private VirtualClientHost CreateVirtualClientHost()
        {
            var host = new VirtualClientHost();
            host.Message += msg => Dispatcher.UIThread.Post(() => Log(msg));
            host.Stopped += () => Dispatcher.UIThread.Post(() =>
            {
                if (_btnAddVirtualClient != null) _btnAddVirtualClient.IsEnabled = true;
            });
            return host;
        }

        private void StopVirtualClientForDebug()
        {
            _virtualClientHost?.Stop();
            _virtualClientHost = null;
            if (_btnAddVirtualClient != null) _btnAddVirtualClient.IsEnabled = true;
        }
#else
        private void StopVirtualClientForDebug()
        {
        }
#endif
    }
}
