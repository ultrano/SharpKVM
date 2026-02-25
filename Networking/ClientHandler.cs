using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpKVM
{
    public class ClientHandler
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
        private readonly MainWindow _ownerWindow;
        private int _isClosed;

        public ClientHandler(TcpClient s, MainWindow parent)
        {
            Socket = s;
            _stream = s.GetStream();
            _ownerWindow = parent;
            Name = "Client-" + ((IPEndPoint)s.Client.RemoteEndPoint!).Address;
            Task.Run(SendingLoop);
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
            catch
            {
                Disconnected?.Invoke(this);
            }
        }

        private async Task<bool> ReadExactAsync(byte[] buffer, int size)
        {
            int totalRead = 0;
            while (totalRead < size)
            {
                int bytesRead = await _stream.ReadAsync(buffer, totalRead, size - totalRead).ConfigureAwait(false);
                if (bytesRead == 0) return false;
                totalRead += bytesRead;
            }

            return true;
        }

        private async Task<byte[]?> ReadPayloadAsync(PacketType type, int length)
        {
            if (!ProtocolPayloadLimits.IsValidPayloadLength(type, length))
            {
                return null;
            }

            byte[] payload = new byte[length];
            if (!await ReadExactAsync(payload, length).ConfigureAwait(false))
            {
                return null;
            }

            return payload;
        }

        public async Task<bool> HandshakeAsync()
        {
            try
            {
                int size = System.Runtime.InteropServices.Marshal.SizeOf<InputPacket>();
                byte[] buffer = new byte[size];
                if (!await ReadExactAsync(buffer, size).ConfigureAwait(false)) return false;

                if (!InputPacketSerializer.TryDeserialize(buffer, out InputPacket p)) return false;

                if (p.Type == PacketType.Hello)
                {
                    if (p.X <= 0 || p.Y <= 0) return false;
                    Width = p.X;
                    Height = p.Y;
                    return true;
                }
            }
            catch { }

            return false;
        }

        public void StartReading()
        {
            var owner = _ownerWindow;

            _ = Task.Run(async () =>
            {
                byte[] buffer = new byte[System.Runtime.InteropServices.Marshal.SizeOf<InputPacket>()];
                try
                {
                    while (true)
                    {
                        if (!await ReadExactAsync(buffer, buffer.Length).ConfigureAwait(false)) break;

                        if (!InputPacketSerializer.TryDeserialize(buffer, out InputPacket packet)) continue;

                        if (packet.Type == PacketType.Clipboard)
                        {
                            var textBytes = await ReadPayloadAsync(PacketType.Clipboard, packet.X).ConfigureAwait(false);
                            if (textBytes == null) break;

                            string text = Encoding.UTF8.GetString(textBytes);
                            owner.SetRemoteClipboard(text);
                        }
                        else if (packet.Type == PacketType.PlatformInfo)
                        {
                            IsMac = (packet.KeyCode == 1);
                        }
                        else if (packet.Type == PacketType.ClipboardFile)
                        {
                            var fileBytes = await ReadPayloadAsync(PacketType.ClipboardFile, packet.X).ConfigureAwait(false);
                            if (fileBytes == null) break;

                            owner.ProcessReceivedFiles(fileBytes);
                        }
                        else if (packet.Type == PacketType.ClipboardImage)
                        {
                            var imageBytes = await ReadPayloadAsync(PacketType.ClipboardImage, packet.X).ConfigureAwait(false);
                            if (imageBytes == null) break;

                            owner.ProcessReceivedImage(imageBytes);
                        }
                    }
                }
                catch { }

                Disconnected?.Invoke(this);
            });
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

            try { _sendQueue.CompleteAdding(); } catch { }
            try { Socket.Close(); } catch { }
        }
    }
}
