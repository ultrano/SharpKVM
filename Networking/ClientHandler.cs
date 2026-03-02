using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpKVM
{
    public class ClientHandler : IDisposable
    {
        public TcpClient Socket;
        public string Name;
        private readonly NetworkStream _stream;
        public int Width { get; private set; } = 1920;
        public int Height { get; private set; } = 1080;
        public bool IsMac { get; private set; } = false;
        public event Action<object>? Disconnected;

        public double Sensitivity { get; set; } = 3.0;
        public double WheelSensitivity { get; set; } = 1.0;

        private readonly BlockingCollection<byte[]> _sendQueue = new BlockingCollection<byte[]>();
        private readonly IClientHandlerMessageSink _messageSink;
        private int _isClosed;

        // SECURITY NOTE: Network stream is used without TLS. All packets (keystrokes, mouse,
        // clipboard text/files/images) are transmitted in plaintext. Wrap with SslStream for encryption.
        internal ClientHandler(TcpClient s, IClientHandlerMessageSink messageSink)
        {
            Socket = s ?? throw new ArgumentNullException(nameof(s));
            _stream = s.GetStream();
            _messageSink = messageSink ?? throw new ArgumentNullException(nameof(messageSink));
            Name = "Client-" + ((IPEndPoint)s.Client.RemoteEndPoint!).Address;
            Task.Run(SendingLoop)
                .ContinueWith(t => Debug.WriteLine($"[SharpKVM] SendingLoop task failed for {Name}: {t.Exception?.GetBaseException().Message}"), TaskContinuationOptions.OnlyOnFaulted);
        }

        private void SendingLoop()
        {
            try
            {
                foreach (var data in _sendQueue.GetConsumingEnumerable())
                {
                    if (Volatile.Read(ref _isClosed) == 1) break;
                    if (!_stream.CanWrite) break;
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharpKVM] SendingLoop error for {Name}: {ex.Message}");
                Disconnected?.Invoke(this);
            }
        }

        public async Task<bool> HandshakeAsync()
        {
            try
            {
                byte[] headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();
                var (status, p) = await ProtocolStreamReader
                    .ReadInputPacketHeaderAsync(_stream, headerBuffer)
                    .ConfigureAwait(false);
                if (status != InputPacketHeaderReadStatus.Success) return false;

                if (p.Type == PacketType.Hello)
                {
                    if (p.X <= 0 || p.Y <= 0) return false;
                    Width = p.X;
                    Height = p.Y;
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[SharpKVM] Handshake failed for {Name}: {ex.Message}"); }

            return false;
        }

        public void StartReading()
        {
            var sink = _messageSink;

            _ = Task.Run(async () =>
            {
                byte[] headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();
                try
                {
                    while (true)
                    {
                        var (status, packet) = await ProtocolStreamReader
                            .ReadInputPacketHeaderAsync(_stream, headerBuffer)
                            .ConfigureAwait(false);
                        if (status == InputPacketHeaderReadStatus.EndOfStream) break;
                        if (status == InputPacketHeaderReadStatus.InvalidHeader) continue;
                        if (!await TryHandleIncomingPacketAsync(packet, sink).ConfigureAwait(false)) break;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[SharpKVM] ReadLoop error for {Name}: {ex.Message}"); }

                Disconnected?.Invoke(this);
            }).ContinueWith(t => Debug.WriteLine($"[SharpKVM] ReadLoop task failed for {Name}: {t.Exception?.GetBaseException().Message}"), TaskContinuationOptions.OnlyOnFaulted);
        }

        private Task<bool> TryHandleIncomingPacketAsync(InputPacket packet, IClientHandlerMessageSink sink)
        {
            return packet.Type switch
            {
                PacketType.Clipboard => HandleClipboardPacketAsync(packet, sink),
                PacketType.PlatformInfo => Task.FromResult(HandlePlatformInfoPacket(packet)),
                PacketType.ClipboardFile => HandleClipboardFilePacketAsync(packet, sink),
                PacketType.ClipboardImage => HandleClipboardImagePacketAsync(packet, sink),
                PacketType.ClientDiagnosticLog => HandleClientDiagnosticLogPacketAsync(packet, sink),
                _ => Task.FromResult(true)
            };
        }

        private async Task<bool> HandleClipboardPacketAsync(InputPacket packet, IClientHandlerMessageSink sink)
        {
            var textBytes = await ProtocolStreamReader.ReadPayloadAsync(_stream, PacketType.Clipboard, packet.X).ConfigureAwait(false);
            if (textBytes == null) return false;

            string text = Encoding.UTF8.GetString(textBytes);
            sink.SetRemoteClipboard(text);
            return true;
        }

        private bool HandlePlatformInfoPacket(InputPacket packet)
        {
            IsMac = IsMacPlatformKeyCode(packet.KeyCode);
            return true;
        }

        private static bool IsMacPlatformKeyCode(int keyCode) => keyCode == 1;

        private async Task<bool> HandleClipboardFilePacketAsync(InputPacket packet, IClientHandlerMessageSink sink)
        {
            var fileBytes = await ProtocolStreamReader.ReadPayloadAsync(_stream, PacketType.ClipboardFile, packet.X).ConfigureAwait(false);
            if (fileBytes == null) return false;

            sink.ProcessReceivedFiles(fileBytes);
            return true;
        }

        private async Task<bool> HandleClipboardImagePacketAsync(InputPacket packet, IClientHandlerMessageSink sink)
        {
            var imageBytes = await ProtocolStreamReader.ReadPayloadAsync(_stream, PacketType.ClipboardImage, packet.X).ConfigureAwait(false);
            if (imageBytes == null) return false;

            sink.ProcessReceivedImage(imageBytes);
            return true;
        }

        private async Task<bool> HandleClientDiagnosticLogPacketAsync(InputPacket packet, IClientHandlerMessageSink sink)
        {
            var payload = await ProtocolStreamReader.ReadPayloadAsync(_stream, PacketType.ClientDiagnosticLog, packet.X).ConfigureAwait(false);
            if (payload == null) return false;

            string message = Encoding.UTF8.GetString(payload);
            sink.ProcessClientDiagnosticLog(Name, message);
            return true;
        }

        public void SendClipboardPacket(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (!ProtocolPayloadLimits.IsValidPayloadLength(PacketType.Clipboard, bytes.Length)) return;

            SendPacketAsync(new InputPacket { Type = PacketType.Clipboard, X = bytes.Length });
            TryEnqueue(bytes);
        }

        public void SendFilePacket(byte[] data)
        {
            if (!ProtocolPayloadLimits.IsValidPayloadLength(PacketType.ClipboardFile, data.Length)) return;

            SendPacketAsync(new InputPacket { Type = PacketType.ClipboardFile, X = data.Length });
            TryEnqueue(data);
        }

        public void SendImagePacket(byte[] data)
        {
            if (!ProtocolPayloadLimits.IsValidPayloadLength(PacketType.ClipboardImage, data.Length)) return;

            SendPacketAsync(new InputPacket { Type = PacketType.ClipboardImage, X = data.Length });
            TryEnqueue(data);
        }

        private bool TryEnqueue(byte[] data)
        {
            if (Volatile.Read(ref _isClosed) == 1) return false;

            try
            {
                _sendQueue.Add(data);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void SendPacketAsync(InputPacket p)
        {
            byte[] arr = InputPacketSerializer.Serialize(p);
            TryEnqueue(arr);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 1) return;

            try { _sendQueue.CompleteAdding(); } catch (ObjectDisposedException) { }
            try { Socket.Close(); } catch (Exception ex) { Debug.WriteLine($"[SharpKVM] Close error for {Name}: {ex.Message}"); }
        }

        public void Dispose()
        {
            Close();
            try { _sendQueue.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    internal interface IClientHandlerMessageSink
    {
        void SetRemoteClipboard(string text);
        void ProcessReceivedFiles(byte[] zipData);
        void ProcessReceivedImage(byte[] imgData);
        void ProcessClientDiagnosticLog(string clientName, string message);
    }
}
