using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpKVM
{
    public partial class MainWindow
    {
#if DEBUG
        private void OnAddVirtualClientClicked(object? sender, RoutedEventArgs e)
        {
            if (!_isServerRunning)
            {
                Log("Start server first, then add virtual client.");
                return;
            }

            _virtualClientHost ??= CreateVirtualClientHost();
            if (!_virtualClientHost.TryStart("127.0.0.1", DEFAULT_PORT, 1920, 1080, false))
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
