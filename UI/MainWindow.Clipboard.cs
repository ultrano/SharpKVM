using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpKVM
{
    public partial class MainWindow
    {
        private async void CheckClipboard()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel == null) return;
                    var clipboard = topLevel.Clipboard;
                    if (clipboard == null) return;

                    var formats = await clipboard.GetFormatsAsync();

                    bool hasImage = formats.Any(f => f.IndexOf("Bitmap", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hasImage && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _ = Task.Run(() =>
                        {
                            byte[]? imgData = ClipboardHelper.GetWindowsClipboardImage();
                            if (imgData != null && imgData.Length > 0)
                            {
                                string hash = $"IMG_{imgData.Length}_{imgData[0]}_{imgData[^1]}";
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (hash != _capturedFileHash)
                                    {
                                        _capturedFileHash = hash;
                                        _capturedImage = imgData;
                                        _capturedText = "";
                                        _capturedFiles.Clear();
                                        Log($"[Local] Image Captured ({imgData.Length} bytes). Pending...");

                                        if (_isClientRunning) SyncClientClipboardToServer();
                                    }
                                });
                            }
                        });
                        return;
                    }

                    if (formats.Contains(DataFormats.Files))
                    {
                        var filesObj = await clipboard.GetDataAsync(DataFormats.Files);
                        List<string> paths = new List<string>();
                        if (filesObj is IEnumerable<IStorageItem> storageItems) { foreach (var item in storageItems) if (item.TryGetLocalPath() is string p) paths.Add(p); }
                        else if (filesObj is IEnumerable<string> filePaths) { paths = filePaths.ToList(); }

                        if (paths.Count > 0)
                        {
                            string currentHash = string.Join("|", paths);
                            if (currentHash != _capturedFileHash)
                            {
                                _capturedFileHash = currentHash;
                                _capturedFiles = paths.ToList();
                                _capturedText = "";
                                _capturedImage = null;
                                Log($"[Local] Files Captured ({paths.Count}). Pending...");

                                if (_isClientRunning) SyncClientClipboardToServer();
                            }
                            return;
                        }
                    }

                    string? text = await clipboard.GetTextAsync();
                    if (!string.IsNullOrEmpty(text) && text != _capturedText)
                    {
                        _capturedText = text;
                        _capturedFileHash = "";
                        _capturedFiles.Clear();
                        _capturedImage = null;

                        if (_isClientRunning) SyncClientClipboardToServer();
                    }
                });
            }
            catch { }
        }

        private void SyncClientClipboardToServer()
        {
            if (!_isClientRunning) return;

            if (!string.IsNullOrEmpty(_capturedText))
            {
                if (_capturedText != _lastRecvText && _capturedText != _sentText)
                {
                    _sentText = _capturedText;
                    _sentFileHash = "";
                    SendClientData(PacketType.Clipboard, Encoding.UTF8.GetBytes(_capturedText));
                    Log("Sent Text to Server (Client Auto)");
                }
            }
            else if (!string.IsNullOrEmpty(_capturedFileHash))
            {
                if (_capturedFileHash != _lastRecvFileHash && _capturedFileHash != _sentFileHash)
                {
                    _sentFileHash = _capturedFileHash;
                    _sentText = "";

                    if (_capturedImage != null)
                    {
                        SendClientData(PacketType.ClipboardImage, _capturedImage);
                        Log("Sent Image to Server (Client Auto)");
                    }
                    else if (_capturedFiles.Count > 0)
                    {
                        BroadcastFiles(_capturedFiles);
                        Log("Sent Files to Server (Client Auto)");
                    }
                }
            }
        }

        private void TrySyncClipboardToRemote()
        {
            if (!string.IsNullOrEmpty(_capturedText) && _capturedText != _sentText)
            {
                _sentText = _capturedText;
                _sentFileHash = "";
                BroadcastClipboard(_capturedText);
                Log("Synced Text to Remote (Auto)");
            }
            else if (!string.IsNullOrEmpty(_capturedFileHash) && _capturedFileHash != _sentFileHash)
            {
                _sentFileHash = _capturedFileHash;
                _sentText = "";

                if (_capturedImage != null)
                {
                    BroadcastImage(_capturedImage);
                    Log("Synced Image to Remote (Auto)");
                }
                else if (_capturedFiles.Count > 0)
                {
                    BroadcastFiles(_capturedFiles);
                    Log("Synced Files to Remote (Auto)");
                }
            }
        }

        private void BroadcastClipboard(string text)
        {
            if (_isServerRunning) foreach (var client in _connectedClients) client.SendClipboardPacket(text);
            else if (_isClientRunning) SendClientData(PacketType.Clipboard, Encoding.UTF8.GetBytes(text));
        }

        private void BroadcastFiles(List<string> filePaths)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        {
                            foreach (var path in filePaths) if (File.Exists(path)) archive.CreateEntryFromFile(path, Path.GetFileName(path));
                        }
                        byte[] zipBytes = ms.ToArray();
                        if (zipBytes.Length > 0)
                        {
                            Dispatcher.UIThread.Post(() => Log($"Sending {filePaths.Count} files..."));
                            if (_isServerRunning) foreach (var client in _connectedClients) client.SendFilePacket(zipBytes);
                            else if (_isClientRunning) SendClientData(PacketType.ClipboardFile, zipBytes);
                        }
                    }
                }
                catch { }
            });
        }

        private void BroadcastImage(byte[] imgData)
        {
            if (_isServerRunning) foreach (var client in _connectedClients) client.SendImagePacket(imgData);
            else if (_isClientRunning) SendClientData(PacketType.ClipboardImage, imgData);
        }

        private void SendClientData(PacketType type, byte[] data)
        {
            if (_currentClientSocket != null && _currentClientSocket.Connected)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        InputPacket p = new InputPacket { Type = type, X = data.Length };
                        byte[] arr = InputPacketSerializer.Serialize(p);
                        lock (_currentClientSocket)
                        {
                            var stream = _currentClientSocket.GetStream(); stream.Write(arr, 0, arr.Length); stream.Write(data, 0, data.Length);
                        }
                    }
                    catch { }
                });
            }
        }
    }
}
