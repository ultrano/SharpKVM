using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SharpKVM
{
    public sealed class VirtualClientHost
    {
        private readonly object _sync = new object();
        private TcpClient? _client;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public bool IsRunning
        {
            get
            {
                lock (_sync) return _isRunning;
            }
        }

        public event Action<string>? Message;
        public event Action? Stopped;

        public static InputPacket[] CreateHandshakePackets(int width, int height, bool isMac)
        {
            return new[]
            {
                new InputPacket { Type = PacketType.Hello, X = width, Y = height },
                new InputPacket { Type = PacketType.PlatformInfo, KeyCode = isMac ? 1 : 0 }
            };
        }

        public bool TryStart(string host, int port, int width, int height, bool isMac)
        {
            lock (_sync)
            {
                if (_isRunning) return false;
                _isRunning = true;
                _cts = new CancellationTokenSource();
            }

            _ = Task.Run(() => RunAsync(host, port, width, height, isMac))
                .ContinueWith(t => Debug.WriteLine($"[SharpKVM] VirtualClient task failed: {t.Exception?.GetBaseException().Message}"), TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            TcpClient? client;
            lock (_sync)
            {
                if (!_isRunning) return;
                _isRunning = false;
                cts = _cts;
                _cts = null;
                client = _client;
                _client = null;
            }

            try { cts?.Cancel(); } catch { }
            try { client?.Close(); } catch { }
            Stopped?.Invoke();
        }

        private async Task RunAsync(string host, int port, int width, int height, bool isMac)
        {
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                Message?.Invoke($"Virtual client connecting to {host}:{port}...");
                _client = new TcpClient { NoDelay = true };
                await _client.ConnectAsync(host, port);
                Message?.Invoke("Virtual client connected.");

                using var stream = _client.GetStream();
                foreach (var packet in CreateHandshakePackets(width, height, isMac))
                {
                    var raw = InputPacketSerializer.Serialize(packet);
                    await stream.WriteAsync(raw, 0, raw.Length);
                }

                await DrainIncomingPacketsAsync(stream, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Message?.Invoke($"Virtual client error: {ex.Message}");
            }
            finally
            {
                bool shouldRaiseStopped = false;
                TcpClient? clientToClose;
                lock (_sync)
                {
                    if (_isRunning) shouldRaiseStopped = true;
                    clientToClose = _client;
                    _isRunning = false;
                    _cts = null;
                    _client = null;
                }

                try { clientToClose?.Close(); } catch { }
                if (shouldRaiseStopped) Stopped?.Invoke();
                Message?.Invoke("Virtual client stopped.");
            }
        }

        internal static async Task DrainIncomingPacketsAsync(Stream stream, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(stream);

            byte[] headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();
            while (!token.IsCancellationRequested)
            {
                var (status, packet) = await ProtocolStreamReader
                    .ReadInputPacketHeaderAsync(stream, headerBuffer, token)
                    .ConfigureAwait(false);

                if (status == InputPacketHeaderReadStatus.EndOfStream) break;
                if (status == InputPacketHeaderReadStatus.InvalidHeader) continue;

                if (!await SkipPayloadAsync(stream, packet, token).ConfigureAwait(false)) break;
            }
        }

        private static bool ShouldReadPayload(InputPacket packet)
        {
            return (packet.Type == PacketType.Clipboard
                || packet.Type == PacketType.ClipboardFile
                || packet.Type == PacketType.ClipboardImage)
                && packet.X > 0;
        }

        private static async Task<bool> SkipPayloadAsync(Stream stream, InputPacket packet, CancellationToken token)
        {
            if (!ShouldReadPayload(packet)) return true;
            var payload = await ProtocolStreamReader
                .ReadPayloadAsync(stream, packet.Type, packet.X, token)
                .ConfigureAwait(false);
            return payload != null;
        }
    }
}
