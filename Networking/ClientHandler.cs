using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SharpKVM
{
    public class ClientHandler
    {
        public TcpClient Socket;
        public string Name;
        private NetworkStream _stream;
        public int Width { get; private set; } = 1920;
        public int Height { get; private set; } = 1080;
        public bool IsMac { get; private set; } = false;
        public event Action<object>? Disconnected;
        
        public double Sensitivity { get; set; } = 3.0;
        public double WheelSensitivity { get; set; } = 1.0; 

        private BlockingCollection<byte[]> _sendQueue = new BlockingCollection<byte[]>();
        private readonly MainWindow _ownerWindow; 

        public ClientHandler(TcpClient s, MainWindow parent)
        {
            Socket = s;
            _stream = s.GetStream();
            _ownerWindow = parent;
            Name = "Client-" + ((IPEndPoint)s.Client.RemoteEndPoint!).Address.ToString();
            Task.Run(SendingLoop);
        }

        private void SendingLoop()
        {
            try
            {
                foreach (var data in _sendQueue.GetConsumingEnumerable())
                {
                    if (!_stream.CanWrite) break;
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch
            {
                Disconnected?.Invoke(this);
            }
        }

        public async Task<bool> HandshakeAsync()
        {
            try
            {
                int size = System.Runtime.InteropServices.Marshal.SizeOf<InputPacket>();
                byte[] buffer = new byte[size];
                int bytesRead = await _stream.ReadAsync(buffer, 0, size);
                if (bytesRead != size) return false;

                if (!InputPacketSerializer.TryDeserialize(buffer, out InputPacket p)) return false;
                
                if (p.Type == PacketType.Hello)
                {
                    this.Width = p.X;
                    this.Height = p.Y;
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
                byte[] buff = new byte[System.Runtime.InteropServices.Marshal.SizeOf<InputPacket>()];
                try
                {
                    while (true)
                    {
                        int read = await _stream.ReadAsync(buff, 0, buff.Length);
                        if (read == 0) break;

                        if (!InputPacketSerializer.TryDeserialize(buff, out InputPacket p)) continue;

                        if (p.Type == PacketType.Clipboard)
                        {
                            int len = p.X;
                            if (len > 0)
                            {
                                byte[] textBytes = new byte[len];
                                int totalRead = 0;
                                while (totalRead < len)
                                {
                                    int r = await _stream.ReadAsync(textBytes, totalRead, len - totalRead);
                                    if (r == 0) break;
                                    totalRead += r;
                                }
                                string text = Encoding.UTF8.GetString(textBytes);
                                
                                owner.SetRemoteClipboard(text); 
                            }
                        }
                        else if (p.Type == PacketType.PlatformInfo)
                        {
                            this.IsMac = (p.KeyCode == 1);
                        }
                        else if (p.Type == PacketType.ClipboardFile)
                        {
                            int len = p.X;
                            if (len > 0)
                            {
                                byte[] fileBytes = new byte[len];
                                int totalRead = 0;
                                while (totalRead < len)
                                {
                                    int r = await _stream.ReadAsync(fileBytes, totalRead, len - totalRead);
                                    if (r == 0) break;
                                    totalRead += r;
                                }
                                owner.ProcessReceivedFiles(fileBytes); 
                            }
                        }
                        else if (p.Type == PacketType.ClipboardImage)
                        {
                            int len = p.X;
                            if (len > 0)
                            {
                                byte[] imgBytes = new byte[len];
                                int totalRead = 0;
                                while (totalRead < len)
                                {
                                    int r = await _stream.ReadAsync(imgBytes, totalRead, len - totalRead);
                                    if (r == 0) break;
                                    totalRead += r;
                                }
                                owner.ProcessReceivedImage(imgBytes);
                            }
                        }
                        else 
                        {
                            // ??곗뺘 ??낆젾 ???땅 (筌띾뜆??????? 獄쏅뗀以?筌ｌ꼶??
                            // [?醫됲뇣] ?????곷섧??筌잛럩肉??뺣즲 ???땅 筌ｌ꼶??筌ㅼ뮇???(?袁⑹뒄??
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
            SendPacketAsync(new InputPacket { Type = PacketType.Clipboard, X = bytes.Length });
            _sendQueue.Add(bytes);
        }

        public void SendFilePacket(byte[] data)
        {
            SendPacketAsync(new InputPacket { Type = PacketType.ClipboardFile, X = data.Length });
            _sendQueue.Add(data);
        }

        public void SendImagePacket(byte[] data)
        {
            SendPacketAsync(new InputPacket { Type = PacketType.ClipboardImage, X = data.Length });
            _sendQueue.Add(data);
        }

        public void SendPacketAsync(InputPacket p)
        {
            byte[] arr = InputPacketSerializer.Serialize(p);
            _sendQueue.Add(arr);
        }

        public void Close()
        {
            _sendQueue.CompleteAdding();
            Socket.Close();
        }
    }
}
