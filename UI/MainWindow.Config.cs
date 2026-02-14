using System;
using System.Collections.Generic;
using System.IO;

namespace SharpKVM
{
    public partial class MainWindow
    {
        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
                if (File.Exists(path))
                {
                    string ip = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(ip)) _txtServerIP.Text = ip; else _txtServerIP.Text = DEFAULT_IP;
                }
                else _txtServerIP.Text = DEFAULT_IP;
            }
            catch { _txtServerIP.Text = DEFAULT_IP; }
        }

        private void SaveConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
                File.WriteAllText(path, _txtServerIP.Text);
            }
            catch { }
        }

        private void LoadClientConfigs()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CLIENT_CONFIG_FILENAME);
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    bool loadedMode = false;
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 11)
                        {
                            var config = new ClientConfig
                            {
                                IP = parts[0],
                                Sensitivity = double.Parse(parts[1]),
                                WheelSensitivity = double.Parse(parts[2]),
                                LayoutMode = string.Equals(parts[3], "Free", StringComparison.OrdinalIgnoreCase) ? LayoutMode.Free : LayoutMode.Snap,
                                X = double.Parse(parts[4]),
                                Y = double.Parse(parts[5]),
                                Width = double.Parse(parts[6]),
                                Height = double.Parse(parts[7]),
                                IsPlaced = bool.Parse(parts[8]),
                                IsSnapped = bool.Parse(parts[9]),
                                SnapAnchorID = parts[10]
                            };
                            if (parts.Length >= 15)
                            {
                                config.DesktopX = double.Parse(parts[11]);
                                config.DesktopY = double.Parse(parts[12]);
                                config.DesktopWidth = double.Parse(parts[13]);
                                config.DesktopHeight = double.Parse(parts[14]);
                            }

                            _clientConfigs[config.IP] = config;
                            if (!loadedMode)
                            {
                                _layoutMode = config.LayoutMode;
                                loadedMode = true;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveClientConfigs()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CLIENT_CONFIG_FILENAME);
                var lines = new List<string>();
                foreach (var client in _connectedClients)
                {
                    string ip = GetClientKey(client);
                    if (!_clientConfigs.ContainsKey(ip)) _clientConfigs[ip] = new ClientConfig { IP = ip };

                    var cfg = _clientConfigs[ip];
                    cfg.Sensitivity = client.Sensitivity;
                    cfg.WheelSensitivity = client.WheelSensitivity;
                    cfg.LayoutMode = _layoutMode;

                    if (_clientLayouts.TryGetValue(ip, out var layout) && layout.IsPlaced)
                    {
                        cfg.X = layout.StageRect.X;
                        cfg.Y = layout.StageRect.Y;
                        cfg.Width = layout.StageRect.Width;
                        cfg.Height = layout.StageRect.Height;
                        cfg.DesktopX = layout.DesktopRect.X;
                        cfg.DesktopY = layout.DesktopRect.Y;
                        cfg.DesktopWidth = layout.DesktopRect.Width;
                        cfg.DesktopHeight = layout.DesktopRect.Height;
                        cfg.IsPlaced = true;
                        cfg.IsSnapped = layout.IsSnapped;
                        cfg.SnapAnchorID = layout.SnapAnchorID;
                    }
                    else
                    {
                        cfg.X = -1;
                        cfg.Y = -1;
                        cfg.Width = -1;
                        cfg.Height = -1;
                        cfg.DesktopX = -1;
                        cfg.DesktopY = -1;
                        cfg.DesktopWidth = -1;
                        cfg.DesktopHeight = -1;
                        cfg.IsPlaced = false;
                        cfg.IsSnapped = false;
                        cfg.SnapAnchorID = "";
                    }
                }

                foreach (var cfg in _clientConfigs.Values)
                {
                    lines.Add($"{cfg.IP}|{cfg.Sensitivity}|{cfg.WheelSensitivity}|{cfg.LayoutMode}|{cfg.X}|{cfg.Y}|{cfg.Width}|{cfg.Height}|{cfg.IsPlaced}|{cfg.IsSnapped}|{cfg.SnapAnchorID}|{cfg.DesktopX}|{cfg.DesktopY}|{cfg.DesktopWidth}|{cfg.DesktopHeight}");
                }
                File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }
}
