using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpKVM
{
    public enum PacketType : byte { Hello = 0, MouseMove = 1, MouseDown = 2, MouseUp = 3, KeyDown = 4, KeyUp = 5, MouseWheel = 6, Clipboard = 7, ClipboardFile = 8, ClipboardImage = 9, PlatformInfo = 10 }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InputPacket { public PacketType Type; public int X; public int Y; public int KeyCode; public int ClickCount; }

    public class ScreenInfo
    {
        public string ID { get; set; } = string.Empty; 
        public Rect Bounds;
        public bool IsPrimary;
        public Rect UIBounds; 
    }

    public class ClientConfig
    {
        public string IP { get; set; } = "";
        public double X { get; set; } = -1;
        public double Y { get; set; } = -1;
        public double Width { get; set; } = -1;
        public double Height { get; set; } = -1;
        public bool IsPlaced { get; set; } = false;
        public bool IsSnapped { get; set; } = false;
        public string SnapAnchorID { get; set; } = "";
        public LayoutMode LayoutMode { get; set; } = LayoutMode.Snap;
        public double Sensitivity { get; set; } = 3.0; 
        public double WheelSensitivity { get; set; } = 1.0; 
    }

    public enum LayoutMode
    {
        Snap,
        Free
    }

    public enum EdgeDirection
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    public class ClientLayout
    {
        public string ClientKey { get; set; } = "";
        public Rect StageRect { get; set; }
        public bool IsPlaced { get; set; }
        public bool IsSnapped { get; set; }
        public string SnapAnchorID { get; set; } = "";
        public string AnchorScreenID { get; set; } = "";
        public EdgeDirection AnchorEdge { get; set; } = EdgeDirection.None;
    }

    class Program
    {
        public static string[] LaunchArgs { get; private set; } = Array.Empty<string>();

        [STAThread]
        public static void Main(string[] args)
        {
            LaunchArgs = args ?? Array.Empty<string>();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(LaunchArgs);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }

    public class App : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    public static class CursorManager
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct CGPoint { public double x; public double y; }

        [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
        [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr hcur, uint id);
        [DllImport("user32.dll")] private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGDisplayHideCursor(uint display);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGDisplayShowCursor(uint display);
        
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGWarpMouseCursorPosition(CGPoint newCursorPosition);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGAssociateMouseAndMouseCursorPosition(bool connected);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint mouseCursorPosition, int mouseButton);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventSourceCreate(int sourceState);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventPost(uint tap, IntPtr ev);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CFRelease(IntPtr obj);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSetIntegerValueField(IntPtr ev, int field, long value);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSetLocation(IntPtr ev, CGPoint pos);

        private static bool _isHidden = false;
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint OCR_NORMAL = 32512;

        public static void LockToRect(Rect bounds)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                RECT r = new RECT { left = (int)bounds.X, top = (int)bounds.Y, right = (int)(bounds.X + 1), bottom = (int)(bounds.Y + 1) };
                ClipCursor(ref r);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                CGWarpMouseCursorPosition(new CGPoint { x = bounds.X, y = bounds.Y });
            }
        }

        public static void SendMacRawClick(double x, double y, int button, bool isDown, int clickCount = 1)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                uint type = 0;
                if (button == 0) type = isDown ? 1u : 2u; // Left MouseDown = 1, MouseUp = 2
                else if (button == 1) type = isDown ? 3u : 4u; // Right MouseDown = 3, MouseUp = 4
                else if (button == 2) type = isDown ? 25u : 26u; // Other MouseDown = 25, MouseUp = 26

                if (type == 0) return;

                CGPoint pos = new CGPoint { x = x, y = y };
                // [v7.1] kCGEventSourceStateCombinedSessionState(0) ???IntPtr.Zero ?ъ슜
                // Zoom ?곹깭?먯꽌 醫뚰몴媛 ????꾩긽??諛⑹??섍린 ?꾪븿
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, type, pos, button);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventSetIntegerValueField(mouseEvent, 1, clickCount);

                    CGEventPost(0, mouseEvent); 
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        // [v6.7] 留??대룞 吏?먯쓣 ?꾪븳 ?ㅼ씠?곕툕 硫붿꽌??異붽? (Zoom ?먰봽 ?꾩긽 ?닿껐)
        public static void SendMacRawMove(double x, double y)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                CGPoint pos = new CGPoint { x = x, y = y };
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, 5u, pos, 0);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventPost(0, mouseEvent);
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        // [v6.4] 留??쒕옒洹?吏?먯쓣 ?꾪븳 ?ㅼ씠?곕툕 硫붿꽌??異붽?
        public static void SendMacRawDrag(double x, double y, int button)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                uint type = 0;
                if (button == 0) type = 6u; // Left MouseDragged = 6
                else if (button == 1) type = 7u; // Right MouseDragged = 7
                else if (button == 2) type = 27u; // Other MouseDragged = 27

                if (type == 0) return;

                CGPoint pos = new CGPoint { x = x, y = y };
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, type, pos, button);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventPost(0, mouseEvent);
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        public static void Unlock()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ClipCursor(IntPtr.Zero);
            }
        }

        public static void Hide()
        {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    if (_isHidden) return;
                    byte[] andPlane = new byte[128]; byte[] xorPlane = new byte[128];
                    for(int i=0; i<128; i++) andPlane[i] = 0xFF; 
                    IntPtr transparentCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
                    SetSystemCursor(transparentCursor, OCR_NORMAL);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    CGDisplayHideCursor(0);
                }
            } catch {}
            _isHidden = true;
        }

        public static void Show()
        {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    CGAssociateMouseAndMouseCursorPosition(true);
                    for(int i=0; i<5; i++) CGDisplayShowCursor(0);
                }
            } catch {}
            _isHidden = false;
        }
    }

    public static class ClipboardHelper
    {
        public static byte[]? GetWindowsClipboardImage()
        {
            try {
                string tempFile = Path.Combine(Path.GetTempPath(), "sharpkvm_clip.png");
                if (File.Exists(tempFile)) File.Delete(tempFile);
                var psCommand = "Add-Type -AssemblyName System.Windows.Forms; if ([System.Windows.Forms.Clipboard]::ContainsImage()) { $img = [System.Windows.Forms.Clipboard]::GetImage(); $img.Save('" + tempFile + "', [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose(); }";
                var info = new ProcessStartInfo("powershell", $"-Sta -Command \"{psCommand}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
                if (File.Exists(tempFile)) {
                    byte[] data = File.ReadAllBytes(tempFile);
                    File.Delete(tempFile);
                    return data;
                }
            } catch {}
            return null;
        }

        public static void SetMacClipboardImage(string imagePath)
        {
            try {
                var script = $"set the clipboard to (read (POSIX file \"{imagePath}\") as {{짬class PNGf쨩}})";
                var info = new ProcessStartInfo("osascript", $"-e '{script}'") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
            } catch {}
        }
        
        public static void SetWindowsClipboardImage(string imagePath)
        {
            try {
                var psCommand = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::SetImage([System.Drawing.Image]::FromFile('{imagePath}'))";
                var info = new ProcessStartInfo("powershell", $"-Sta -Command \"{psCommand}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
            } catch {}
        }
    }

    public class MainWindow : Window
    {
        private const int DEFAULT_PORT = 11000;
        private const string DEFAULT_IP = "127.0.0.1";
        private const string CONFIG_FILENAME = "server_ip.txt"; 
        private const string CLIENT_CONFIG_FILENAME = "client_config.txt"; 

        private const double BASE_BOX_HEIGHT = 50; 
        private const double SLOT_GAP = 5;

        private TabControl _tabControl = null!;
        private Canvas _pnlStage = null!;
        private StackPanel _pnlDock = null!;
        private TextBlock _lblStatus = null!;
        private TextBlock _lblServerInfo = null!;
        private Button _btnStartServer = null!;
        private TextBox _txtServerIP = null!;
        private Button _btnConnect = null!;
        private TextBox _txtLog = null!;
        private ComboBox _cmbLayoutMode = null!;

        private TcpListener? _serverListener;
        private List<ClientHandler> _connectedClients = new List<ClientHandler>();
        private Dictionary<string, ClientLayout> _clientLayouts = new Dictionary<string, ClientLayout>();
        private Dictionary<string, Border> _clientBoxes = new Dictionary<string, Border>();
        private List<ScreenInfo> _cachedScreens = new List<ScreenInfo>();

        private Dictionary<string, ClientConfig> _clientConfigs = new Dictionary<string, ClientConfig>();

        private SimpleGlobalHook? _hook;
        private EventSimulator? _simulator;
        
        private bool _isServerRunning = false;
        private bool _isRemoteActive = false;
        private ClientHandler? _activeRemoteClient = null;
        private ScreenInfo? _activeRootScreen = null;
        private EdgeDirection _activeEntryEdge = EdgeDirection.None;
        private double _virtualX = 0, _virtualY = 0;
        
        private bool _ignoreNextMove = false;
        private int _skipMoveCount = 0;

        private double _scaleX = 1.0, _scaleY = 1.0;
        private bool _isLeftDragging = false;
        
        private bool _isClientRunning = false; 
        private TcpClient? _currentClientSocket = null;

        private List<SnapSlot> _snapSlots = new List<SnapSlot>();
        private DispatcherTimer _clipboardTimer;
        private LayoutMode _layoutMode = LayoutMode.Snap;
        private double _layoutMinX = 0;
        private double _layoutMinY = 0;
        private double _layoutScale = 1.0;
        private double _layoutOffsetX = 0;
        private double _layoutOffsetY = 0;
        private bool _autoStartClientMode = false;
        private string _autoServerIP = "";
        
        private string _capturedText = "";
        private string _capturedFileHash = ""; 
        private List<string> _capturedFiles = new List<string>();
        private byte[]? _capturedImage = null;

        private string _lastRecvText = "";
        private string _lastRecvFileHash = "";

        private string _sentText = "";
        private string _sentFileHash = ""; 
        
        private bool _isRemoteCtrlDown = false;
        private DateTime _lastMacShortcutTime = DateTime.MinValue;
        private bool _isLocalCtrlDown = false;
        private bool _isLocalMetaDown = false;

        private DateTime _lastReturnTime = DateTime.MinValue;

        private double _resolutionScale = 1.0; 
        
        private const double BASE_SENSITIVITY = 3.0; 

        private int _pendingMouseX = -1;
        private int _pendingMouseY = -1;
        private bool _hasPendingMouse = false;
        private CancellationTokenSource? _mouseSenderCts;

        private double _wheelAccumulator = 0;

        // [?좉퇋] ?붾툝?대┃ 吏?먯쓣 ?꾪븳 蹂??
        private DateTime _lastClickTime = DateTime.MinValue;
        private int _lastClickButton = -1;
        private int _clickCount = 1;

        // [?좉퇋] ?대씪?댁뼵???꾩옱 ?꾩튂 異붿쟻 蹂??(Zoom 臾몄젣 ?닿껐??
        private double _currentClientX = -1;
        private double _currentClientY = -1;

        public MainWindow()
        {
            this.Title = "SharpKVM (v7.1)";
            this.Width = 1000;
            this.Height = 750;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _simulator = new EventSimulator();

            InitializeUI();
            LoadConfig();
            ParseLaunchArguments();
            LoadClientConfigs(); 
            _cmbLayoutMode.SelectedIndex = _layoutMode == LayoutMode.Free ? 1 : 0;
            
            this.Opened += OnWindowOpened;
            this.Closing += (s, e) => { StopServer(); StopClient(); CursorManager.Show(); CursorManager.Unlock(); SaveConfig(); };

            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) }; 
            _clipboardTimer.Tick += (s,e) => _ = Task.Run(() => CheckClipboard()); 
            _clipboardTimer.Start();
        }

        private void ParseLaunchArguments()
        {
            var args = Program.LaunchArgs ?? Array.Empty<string>();
            if (args.Length == 0) return;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim();
                if (a.Equals("client", StringComparison.OrdinalIgnoreCase))
                {
                    _autoStartClientMode = true;
                    if (i + 1 < args.Length) _autoServerIP = args[i + 1].Trim();
                }
                else if (a.Equals("--client", StringComparison.OrdinalIgnoreCase) || a.Equals("-c", StringComparison.OrdinalIgnoreCase))
                {
                    _autoStartClientMode = true;
                    if (i + 1 < args.Length) _autoServerIP = args[i + 1].Trim();
                }
                else if (a.StartsWith("--client=", StringComparison.OrdinalIgnoreCase))
                {
                    _autoStartClientMode = true;
                    _autoServerIP = a.Substring("--client=".Length).Trim();
                }
                else if (a.Equals("--server", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) _autoServerIP = args[i + 1].Trim();
                }
                else if (a.StartsWith("--server=", StringComparison.OrdinalIgnoreCase))
                {
                    _autoServerIP = a.Substring("--server=".Length).Trim();
                }
            }
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
                // Let UI settle first, then start connection loop in background.
                await Task.Delay(200);
                _ = StartClientLoop();
            }
            finally
            {
                _btnConnect.IsEnabled = true;
            }
        }

        private void InitializeUI()
        {
            var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(120)) } };

            _txtLog = new TextBox { IsReadOnly = true, Background = Brushes.Black, Foreground = Brushes.LightGreen, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(_txtLog, 3);
            root.Children.Add(_txtLog);

            _tabControl = new TabControl();
            Grid.SetRow(_tabControl, 1);

            var serverTab = new TabItem { Header = "Server" };
            var serverGrid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
            
            var srvHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(10) };
            _btnStartServer = new Button { Content = "Start Server" };
            _btnStartServer.Click += ToggleServerState;
            _lblServerInfo = new TextBlock { Text = "Status: Stopped", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold };
            _cmbLayoutMode = new ComboBox
            {
                Width = 130,
                ItemsSource = new[] { "Snap", "Free" },
                SelectedIndex = 0
            };
            _cmbLayoutMode.SelectionChanged += (s, e) =>
            {
                _layoutMode = (_cmbLayoutMode.SelectedIndex == 1) ? LayoutMode.Free : LayoutMode.Snap;
                ApplyLayoutModeToPlacedClients();
                SaveClientConfigs();
            };
            
            srvHeader.Children.Add(_btnStartServer);
            srvHeader.Children.Add(_lblServerInfo);
            srvHeader.Children.Add(new TextBlock { Text = "Layout", VerticalAlignment = VerticalAlignment.Center });
            srvHeader.Children.Add(_cmbLayoutMode);
            serverGrid.Children.Add(srvHeader);

            var editorBorder = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Background = Brushes.DarkGray };
            var editorGrid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
            _pnlStage = new Canvas { Background = Brushes.DimGray, ClipToBounds = true };
            _pnlDock = new StackPanel { Orientation = Orientation.Horizontal, Height = 80, Background = Brushes.Gray };
            
            editorGrid.Children.Add(_pnlStage);
            Grid.SetRow(_pnlDock, 1);
            editorGrid.Children.Add(_pnlDock);
            editorBorder.Child = editorGrid;
            Grid.SetRow(editorBorder, 1);
            serverGrid.Children.Add(editorBorder);

            serverTab.Content = serverGrid;

            var clientTab = new TabItem { Header = "Client" };
            var clientPanel = new StackPanel { Margin = new Thickness(20), Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            clientPanel.Children.Add(new TextBlock { Text = "Server IP:" });
            _txtServerIP = new TextBox { Width = 200 };
            clientPanel.Children.Add(_txtServerIP);
            _btnConnect = new Button { Content = "Connect", Width = 200, HorizontalContentAlignment = HorizontalAlignment.Center };
            _btnConnect.Click += ToggleClientConnection;
            clientPanel.Children.Add(_btnConnect);
            clientTab.Content = clientPanel;

            _tabControl.Items.Add(serverTab);
            _tabControl.Items.Add(clientTab);
            root.Children.Add(_tabControl);

            _lblStatus = new TextBlock { Text = "Ready", Margin = new Thickness(5), Background = Brushes.LightGray };
            Grid.SetRow(_lblStatus, 2);
            root.Children.Add(_lblStatus);

            this.Content = root;
        }

        private void Log(string msg) => Dispatcher.UIThread.Post(() => {
            _txtLog.Text += $"[{DateTime.Now:mm:ss}] {msg}\n";
            _txtLog.CaretIndex = _txtLog.Text.Length;
        });

        private void LoadConfig()
        {
            try {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
                if (File.Exists(path)) {
                    string ip = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(ip)) _txtServerIP.Text = ip; else _txtServerIP.Text = DEFAULT_IP;
                } else _txtServerIP.Text = DEFAULT_IP;
            } catch { _txtServerIP.Text = DEFAULT_IP; }
        }

        private void SaveConfig() { try { string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME); File.WriteAllText(path, _txtServerIP.Text); } catch { } }

        private void LoadClientConfigs()
        {
            try {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CLIENT_CONFIG_FILENAME);
                if (File.Exists(path)) {
                    var lines = File.ReadAllLines(path);
                    bool loadedMode = false;
                    foreach(var line in lines) {
                        var parts = line.Split('|');
                        if (parts.Length >= 11) {
                            var config = new ClientConfig {
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
                            if (parts.Length >= 12) config.SnapAnchorID = parts[10];
                            if (parts.Length >= 13) {
                                // Reserved for future compatibility: anchor screen and edge are optional.
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
            } catch { }
        }

        private void SaveClientConfigs()
        {
            try {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CLIENT_CONFIG_FILENAME);
                var lines = new List<string>();
                foreach(var client in _connectedClients) {
                    string ip = GetClientKey(client);
                    if (!_clientConfigs.ContainsKey(ip)) _clientConfigs[ip] = new ClientConfig { IP = ip };

                    var cfg = _clientConfigs[ip];
                    cfg.Sensitivity = client.Sensitivity;
                    cfg.WheelSensitivity = client.WheelSensitivity;
                    cfg.LayoutMode = _layoutMode;

                    if (_clientLayouts.TryGetValue(ip, out var layout) && layout.IsPlaced) {
                        cfg.X = layout.StageRect.X;
                        cfg.Y = layout.StageRect.Y;
                        cfg.Width = layout.StageRect.Width;
                        cfg.Height = layout.StageRect.Height;
                        cfg.IsPlaced = true;
                        cfg.IsSnapped = layout.IsSnapped;
                        cfg.SnapAnchorID = layout.SnapAnchorID;
                    } else {
                        cfg.X = -1;
                        cfg.Y = -1;
                        cfg.Width = -1;
                        cfg.Height = -1;
                        cfg.IsPlaced = false;
                        cfg.IsSnapped = false;
                        cfg.SnapAnchorID = "";
                    }
                }
                
                foreach(var cfg in _clientConfigs.Values) {
                    lines.Add($"{cfg.IP}|{cfg.Sensitivity}|{cfg.WheelSensitivity}|{cfg.LayoutMode}|{cfg.X}|{cfg.Y}|{cfg.Width}|{cfg.Height}|{cfg.IsPlaced}|{cfg.IsSnapped}|{cfg.SnapAnchorID}");
                }
                File.WriteAllLines(path, lines);
            } catch { }
        }

        private async void CheckClipboard()
        {
            try {
                await Dispatcher.UIThread.InvokeAsync(async () => {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel == null) return;
                    var clipboard = topLevel.Clipboard;
                    if (clipboard == null) return;
                    
                    var formats = await clipboard.GetFormatsAsync();

                    bool hasImage = formats.Any(f => f.IndexOf("Bitmap", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hasImage && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _ = Task.Run(() => {
                            byte[]? imgData = ClipboardHelper.GetWindowsClipboardImage();
                            if (imgData != null && imgData.Length > 0) {
                                string hash = $"IMG_{imgData.Length}_{imgData[0]}_{imgData[^1]}";
                                Dispatcher.UIThread.Post(() => {
                                    if (hash != _capturedFileHash) {
                                        _capturedFileHash = hash; 
                                        _capturedImage = imgData;
                                        _capturedText = ""; 
                                        _capturedFiles.Clear();
                                        Log($"[Local] Image Captured ({imgData.Length} bytes). Pending...");
                                        
                                        if(_isClientRunning) SyncClientClipboardToServer();
                                    }
                                });
                            }
                        });
                        return;
                    }

                    if (formats.Contains(DataFormats.Files)) {
                        var filesObj = await clipboard.GetDataAsync(DataFormats.Files);
                        List<string> paths = new List<string>();
                        if (filesObj is IEnumerable<IStorageItem> storageItems) { foreach(var item in storageItems) if(item.TryGetLocalPath() is string p) paths.Add(p); }
                        else if (filesObj is IEnumerable<string> filePaths) { paths = filePaths.ToList(); }

                        if (paths.Count > 0) {
                            string currentHash = string.Join("|", paths);
                            if (currentHash != _capturedFileHash) {
                                _capturedFileHash = currentHash; 
                                _capturedFiles = paths.ToList();
                                _capturedText = ""; 
                                _capturedImage = null;
                                Log($"[Local] Files Captured ({paths.Count}). Pending...");
                                
                                if(_isClientRunning) SyncClientClipboardToServer();
                            }
                            return;
                        }
                    }

                    string? text = await clipboard.GetTextAsync();
                    if (!string.IsNullOrEmpty(text) && text != _capturedText) {
                        _capturedText = text; 
                        _capturedFileHash = ""; 
                        _capturedFiles.Clear();
                        _capturedImage = null;
                        
                        if(_isClientRunning) SyncClientClipboardToServer();
                    }
                });
            } catch {}
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
                
                if (_capturedImage != null) {
                    BroadcastImage(_capturedImage);
                    Log("Synced Image to Remote (Auto)");
                }
                else if (_capturedFiles.Count > 0) {
                    BroadcastFiles(_capturedFiles);
                    Log("Synced Files to Remote (Auto)");
                }
            }
        }

        private void BroadcastClipboard(string text) {
            if (_isServerRunning) foreach(var client in _connectedClients) client.SendClipboardPacket(text);
            else if (_isClientRunning) SendClientData(PacketType.Clipboard, Encoding.UTF8.GetBytes(text));
        }

        private void BroadcastFiles(List<string> filePaths) {
            _ = Task.Run(() => {
                try {
                    using (var ms = new MemoryStream()) {
                        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
                            foreach (var path in filePaths) if (File.Exists(path)) archive.CreateEntryFromFile(path, Path.GetFileName(path));
                        }
                        byte[] zipBytes = ms.ToArray();
                        if (zipBytes.Length > 0) {
                            Dispatcher.UIThread.Post(() => Log($"Sending {filePaths.Count} files..."));
                            if (_isServerRunning) foreach(var client in _connectedClients) client.SendFilePacket(zipBytes);
                            else if (_isClientRunning) SendClientData(PacketType.ClipboardFile, zipBytes);
                        }
                    }
                } catch {}
            });
        }

        private void BroadcastImage(byte[] imgData) {
            if (_isServerRunning) foreach(var client in _connectedClients) client.SendImagePacket(imgData);
            else if (_isClientRunning) SendClientData(PacketType.ClipboardImage, imgData);
        }

        private void SendClientData(PacketType type, byte[] data) {
            if (_currentClientSocket != null && _currentClientSocket.Connected) {
                _ = Task.Run(() => {
                    try {
                        InputPacket p = new InputPacket { Type = type, X = data.Length };
                        int size = Marshal.SizeOf(p); byte[] arr = new byte[size];
                        IntPtr ptr = Marshal.AllocHGlobal(size); Marshal.StructureToPtr(p, ptr, true); Marshal.Copy(ptr, arr, 0, size); Marshal.FreeHGlobal(ptr);
                        lock(_currentClientSocket) { 
                            var stream = _currentClientSocket.GetStream(); stream.Write(arr, 0, size); stream.Write(data, 0, data.Length);
                        }
                    } catch {}
                });
            }
        }
        
        public void SetRemoteClipboard(string text) {
            _lastRecvText = text;
            _lastRecvFileHash = "";

            if (_isServerRunning && _isRemoteActive) {
                TrySyncClipboardToRemote(); 
            }

            if (_capturedText == text) return; 
            _capturedText = text; _capturedFileHash = ""; 
            
            Dispatcher.UIThread.Post(async () => {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null) { await clipboard.SetTextAsync(text); Log("Clipboard Text Synced"); }
            });
        }

        public void ProcessReceivedImage(byte[] imgData) {
            string hash = $"IMG_{imgData.Length}_{imgData[0]}_{imgData[^1]}";
            
            _lastRecvFileHash = hash;
            _lastRecvText = "";

            if (_isServerRunning && _isRemoteActive) {
                TrySyncClipboardToRemote();
            }

            if (_capturedFileHash == hash) return;
            _capturedFileHash = hash;

            Dispatcher.UIThread.Post(async () => {
                try {
                    string tempPath = Path.Combine(Path.GetTempPath(), "sharpkvm_recv.png");
                    File.WriteAllBytes(tempPath, imgData);
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) ClipboardHelper.SetMacClipboardImage(tempPath);
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ClipboardHelper.SetWindowsClipboardImage(tempPath);

                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard != null && topLevel?.StorageProvider != null) {
                        var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(tempPath));
                        if(file != null) {
                            var data = new DataObject(); data.Set(DataFormats.Files, new List<IStorageItem> { file });
                            await topLevel.Clipboard.SetDataObjectAsync(data);
                        }
                    }
                    Log($"Recv Image ({imgData.Length} bytes). (Ctrl+V)");
                } catch (Exception ex) { Log($"Img Recv Error: {ex.Message}"); }
            });
        }
        
        public void ProcessReceivedFiles(byte[] zipData) {
            Dispatcher.UIThread.Post(async () => {
                try {
                    string saveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
                    if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true);
                    Directory.CreateDirectory(saveDir);
                    using (var ms = new MemoryStream(zipData)) using (var archive = new ZipArchive(ms, ZipArchiveMode.Read)) { archive.ExtractToDirectory(saveDir); }
                    var files = Directory.GetFiles(saveDir).ToList();
                    
                    string newHash = string.Join("|", files);
                    _lastRecvFileHash = newHash;
                    _lastRecvText = "";

                    if (files.Count > 0) {
                        var topLevel = TopLevel.GetTopLevel(this);
                        if (topLevel?.Clipboard != null && topLevel?.StorageProvider != null) {
                            var data = new DataObject(); var storageItems = new List<IStorageItem>();
                            foreach(var f in files) { var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(f)); if(file != null) storageItems.Add(file); }
                            data.Set(DataFormats.Files, storageItems);
                            await topLevel.Clipboard.SetDataObjectAsync(data);
                            
                            _capturedFileHash = newHash; 
                            Log($"Recv {files.Count} files. (Ctrl+V)");

                            if (_isServerRunning && _isRemoteActive) TrySyncClipboardToRemote();
                        }
                    }
                } catch {}
            });
        }

        private void UpdateScreenCache() {
            if(this.Screens.All.Count == 0) return;
            _cachedScreens.Clear();
            var primary = this.Screens.Primary ?? this.Screens.All[0];
            _cachedScreens.Add(new ScreenInfo { ID = "S1", Bounds = new Rect(primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width, primary.Bounds.Height), IsPrimary = true });
            int counter = 2;
            foreach(var s in this.Screens.All) {
                if (s.Equals(primary)) continue;
                _cachedScreens.Add(new ScreenInfo { ID = $"S{counter++}", Bounds = new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), IsPrimary = false });
            }
            Log($"Screens Detected: {_cachedScreens.Count}");
            DrawVisualLayout();
        }

        private void DrawVisualLayout() {
            if(_pnlStage == null) return;
            _pnlStage.Children.Clear(); _snapSlots.Clear();
            double minX = _cachedScreens.Min(s => s.Bounds.X), minY = _cachedScreens.Min(s => s.Bounds.Y);
            double totalW = _cachedScreens.Max(s => s.Bounds.Right) - minX, totalH = _cachedScreens.Max(s => s.Bounds.Bottom) - minY;
            double stageW = _pnlStage.Bounds.Width, stageH = _pnlStage.Bounds.Height;
            if(stageW <= 0) return;
            double padding = 40;
            double scale = Math.Min((stageW - padding*2)/totalW, (stageH - padding*2)/totalH);
            if(scale > 0.3) scale = 0.3;
            double offsetX = (stageW - totalW*scale)/2, offsetY = (stageH - totalH*scale)/2;

            _layoutMinX = minX;
            _layoutMinY = minY;
            _layoutScale = scale;
            _layoutOffsetX = offsetX;
            _layoutOffsetY = offsetY;

            foreach(var s in _cachedScreens) {
                double x = (s.Bounds.X - minX) * scale + offsetX, y = (s.Bounds.Y - minY) * scale + offsetY;
                double w = s.Bounds.Width * scale, h = s.Bounds.Height * scale;
                s.UIBounds = new Rect(x, y, w, h);
                var border = new Border { Width = w, Height = h, Background = Brushes.SteelBlue, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Child = new TextBlock { Text = $"{s.ID}\n{s.Bounds.Width}x{s.Bounds.Height}", Foreground=Brushes.White, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, FontSize=10 } };
                Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
                _pnlStage.Children.Add(border);
                _snapSlots.Add(new SnapSlot(s.ID + "_Left", new Rect(x - 20, y, 20, h), s.ID, "Left"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Right", new Rect(x + w, y, 20, h), s.ID, "Right"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Top", new Rect(x, y - 20, w, 20), s.ID, "Top"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Bottom", new Rect(x, y + h, w, 20), s.ID, "Bottom"));
            }

            ApplyLayoutModeToPlacedClients();
        }

        private void RedrawClientBoxes()
        {
            foreach (var kvp in _clientBoxes)
            {
                var clientKey = kvp.Key;
                var box = kvp.Value;
                if (!_clientLayouts.TryGetValue(clientKey, out var layout) || !layout.IsPlaced)
                {
                    if (box.Parent == _pnlStage)
                    {
                        _pnlStage.Children.Remove(box);
                    }

                    if (box.Parent == null)
                    {
                        box.Margin = new Thickness(5);
                        _pnlDock.Children.Add(box);
                    }
                    continue;
                }

                if (box.Parent == _pnlDock)
                {
                    _pnlDock.Children.Remove(box);
                }

                if (box.Parent == null)
                {
                    _pnlStage.Children.Add(box);
                }

                box.Margin = new Thickness(0);
                box.Width = layout.StageRect.Width;
                box.Height = layout.StageRect.Height;
                Canvas.SetLeft(box, layout.StageRect.X);
                Canvas.SetTop(box, layout.StageRect.Y);
                box.Background = Brushes.Green;
            }
        }

        private Rect StageToDesktop(Rect stageRect)
        {
            if (_layoutScale <= 0) return new Rect();
            return new Rect(
                ((stageRect.X - _layoutOffsetX) / _layoutScale) + _layoutMinX,
                ((stageRect.Y - _layoutOffsetY) / _layoutScale) + _layoutMinY,
                stageRect.Width / _layoutScale,
                stageRect.Height / _layoutScale
            );
        }

        private EdgeDirection OppositeEdge(EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return EdgeDirection.Right;
            if (edge == EdgeDirection.Right) return EdgeDirection.Left;
            if (edge == EdgeDirection.Top) return EdgeDirection.Bottom;
            if (edge == EdgeDirection.Bottom) return EdgeDirection.Top;
            return EdgeDirection.None;
        }

        private string GetClientKey(ClientHandler client) => client.Name.Replace("Client-", "");

        private ClientHandler? GetClientByKey(string key)
        {
            return _connectedClients.FirstOrDefault(c => GetClientKey(c) == key);
        }

        private Size GetNaturalStageSize(ClientHandler client)
        {
            double w = Math.Max(40, client.Width * _layoutScale);
            double h = Math.Max(25, client.Height * _layoutScale);
            return new Size(w, h);
        }

        private Rect ClampRectToStage(Rect rect)
        {
            double maxX = Math.Max(0, _pnlStage.Bounds.Width - rect.Width);
            double maxY = Math.Max(0, _pnlStage.Bounds.Height - rect.Height);
            double x = Math.Clamp(rect.X, 0, maxX);
            double y = Math.Clamp(rect.Y, 0, maxY);
            return new Rect(x, y, rect.Width, rect.Height);
        }

        private double GetStageOverflow(Rect rect)
        {
            double overflow = 0;
            if (rect.Left < 0) overflow += -rect.Left;
            if (rect.Top < 0) overflow += -rect.Top;
            if (rect.Right > _pnlStage.Bounds.Width) overflow += rect.Right - _pnlStage.Bounds.Width;
            if (rect.Bottom > _pnlStage.Bounds.Height) overflow += rect.Bottom - _pnlStage.Bounds.Height;
            return overflow;
        }

        private bool IsHorizontalEdge(EdgeDirection edge) => edge == EdgeDirection.Left || edge == EdgeDirection.Right;

        private bool HasPerpendicularOverlapForEntry(Rect sourceRect, Rect targetRect, EdgeDirection entryEdge)
        {
            if (entryEdge == EdgeDirection.Left || entryEdge == EdgeDirection.Right)
            {
                return Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > 8;
            }
            if (entryEdge == EdgeDirection.Top || entryEdge == EdgeDirection.Bottom)
            {
                return Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > 8;
            }
            return false;
        }

        private double Overlap1D(double a1, double a2, double b1, double b2)
        {
            double lo = Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));
            double hi = Math.Min(Math.Max(a1, a2), Math.Max(b1, b2));
            return Math.Max(0, hi - lo);
        }

        private bool AreEdgesAdjacent(Rect sourceRect, EdgeDirection exitEdge, Rect targetRect, double tolerance)
        {
            if (exitEdge == EdgeDirection.Left)
            {
                return Math.Abs(sourceRect.Left - targetRect.Right) <= tolerance &&
                       Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > 8;
            }
            if (exitEdge == EdgeDirection.Right)
            {
                return Math.Abs(sourceRect.Right - targetRect.Left) <= tolerance &&
                       Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > 8;
            }
            if (exitEdge == EdgeDirection.Top)
            {
                return Math.Abs(sourceRect.Top - targetRect.Bottom) <= tolerance &&
                       Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > 8;
            }
            if (exitEdge == EdgeDirection.Bottom)
            {
                return Math.Abs(sourceRect.Bottom - targetRect.Top) <= tolerance &&
                       Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > 8;
            }
            return false;
        }

        private Point MapPointAcrossEdge(Point sourcePoint, Rect sourceRect, EdgeDirection exitEdge, Rect targetRect)
        {
            if (exitEdge == EdgeDirection.Left || exitEdge == EdgeDirection.Right)
            {
                double overlapTop = Math.Max(sourceRect.Top, targetRect.Top);
                double overlapBottom = Math.Min(sourceRect.Bottom, targetRect.Bottom);
                double mappedY;
                if (overlapBottom > overlapTop)
                {
                    mappedY = Math.Clamp(sourcePoint.Y, overlapTop, overlapBottom);
                }
                else
                {
                    double ratio = sourceRect.Height > 0 ? Math.Clamp((sourcePoint.Y - sourceRect.Top) / sourceRect.Height, 0.0, 1.0) : 0.5;
                    mappedY = targetRect.Top + ratio * targetRect.Height;
                }
                double mappedX = exitEdge == EdgeDirection.Left ? targetRect.Right : targetRect.Left;
                return new Point(mappedX, mappedY);
            }

            double overlapLeft = Math.Max(sourceRect.Left, targetRect.Left);
            double overlapRight = Math.Min(sourceRect.Right, targetRect.Right);
            double mappedX2;
            if (overlapRight > overlapLeft)
            {
                mappedX2 = Math.Clamp(sourcePoint.X, overlapLeft, overlapRight);
            }
            else
            {
                double ratio = sourceRect.Width > 0 ? Math.Clamp((sourcePoint.X - sourceRect.Left) / sourceRect.Width, 0.0, 1.0) : 0.5;
                mappedX2 = targetRect.Left + ratio * targetRect.Width;
            }
            double mappedY2 = exitEdge == EdgeDirection.Top ? targetRect.Bottom : targetRect.Top;
            return new Point(mappedX2, mappedY2);
        }

        private Rect AttachToScreenEdge(Rect rect, ScreenInfo screen, EdgeDirection edge)
        {
            Rect screenRect = screen.UIBounds;

            if (edge == EdgeDirection.Left)
            {
                double y = Math.Clamp(rect.Y, screenRect.Top - rect.Height + 8, screenRect.Bottom - 8);
                return new Rect(screenRect.Left - rect.Width, y, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Right)
            {
                double y = Math.Clamp(rect.Y, screenRect.Top - rect.Height + 8, screenRect.Bottom - 8);
                return new Rect(screenRect.Right, y, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Top)
            {
                double x = Math.Clamp(rect.X, screenRect.Left - rect.Width + 8, screenRect.Right - 8);
                return new Rect(x, screenRect.Top - rect.Height, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Bottom)
            {
                double x = Math.Clamp(rect.X, screenRect.Left - rect.Width + 8, screenRect.Right - 8);
                return new Rect(x, screenRect.Bottom, rect.Width, rect.Height);
            }
            return ClampRectToStage(rect);
        }

        private (ScreenInfo Screen, EdgeDirection Edge, double Distance) FindNearestScreenEdge(Rect rect)
        {
            Point center = rect.Center;
            ScreenInfo bestScreen = _cachedScreens[0];
            EdgeDirection bestEdge = EdgeDirection.Right;
            double best = double.MaxValue;

            foreach (var screen in _cachedScreens)
            {
                Rect r = screen.UIBounds;
                var candidates = new (EdgeDirection Edge, Point P)[] {
                    (EdgeDirection.Left, new Point(r.Left, Math.Clamp(center.Y, r.Top, r.Bottom))),
                    (EdgeDirection.Right, new Point(r.Right, Math.Clamp(center.Y, r.Top, r.Bottom))),
                    (EdgeDirection.Top, new Point(Math.Clamp(center.X, r.Left, r.Right), r.Top)),
                    (EdgeDirection.Bottom, new Point(Math.Clamp(center.X, r.Left, r.Right), r.Bottom))
                };

                foreach (var c in candidates)
                {
                    double d = Math.Sqrt(Math.Pow(center.X - c.P.X, 2) + Math.Pow(center.Y - c.P.Y, 2));
                    if (d < best)
                    {
                        best = d;
                        bestScreen = screen;
                        bestEdge = c.Edge;
                    }
                }
            }

            return (bestScreen, bestEdge, best);
        }

        private Rect GetAnchoredFreeRect(Rect rect, string clientKey, out string anchorScreenID, out EdgeDirection anchorEdge)
        {
            anchorScreenID = "";
            anchorEdge = EdgeDirection.None;

            var nearest = FindNearestScreenEdge(rect);
            Rect bestRect = ClampRectToStage(rect);
            double bestScore = double.MaxValue;

            foreach (var screen in _cachedScreens)
            {
                foreach (var edge in new[] { EdgeDirection.Left, EdgeDirection.Right, EdgeDirection.Top, EdgeDirection.Bottom })
                {
                    var attached = AttachToScreenEdge(rect, screen, edge);
                    double overflow = GetStageOverflow(attached);

                    double edgeBias = 0;
                    if (screen.ID == nearest.Screen.ID && edge == nearest.Edge) edgeBias = -500;

                    double score = (overflow * 10000) + edgeBias;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestRect = attached;
                        anchorScreenID = screen.ID;
                        anchorEdge = edge switch
                        {
                            EdgeDirection.Left => EdgeDirection.Right,
                            EdgeDirection.Right => EdgeDirection.Left,
                            EdgeDirection.Top => EdgeDirection.Bottom,
                            EdgeDirection.Bottom => EdgeDirection.Top,
                            _ => EdgeDirection.None
                        };
                    }
                }
            }

            var magnetic = ApplyMagneticSnap(bestRect, clientKey);
            if (anchorEdge == EdgeDirection.Left || anchorEdge == EdgeDirection.Right)
            {
                magnetic = new Rect(bestRect.X, magnetic.Y, magnetic.Width, magnetic.Height);
            }
            else if (anchorEdge == EdgeDirection.Top || anchorEdge == EdgeDirection.Bottom)
            {
                magnetic = new Rect(magnetic.X, bestRect.Y, magnetic.Width, magnetic.Height);
            }
            return ClampRectToStage(magnetic);
        }

        private Rect ApplyMagneticSnap(Rect rect, string movingClientKey, double threshold = 14)
        {
            var targets = new List<Rect>();
            targets.AddRange(_cachedScreens.Select(s => s.UIBounds));
            foreach (var kv in _clientLayouts)
            {
                if (kv.Key == movingClientKey || !kv.Value.IsPlaced) continue;
                targets.Add(kv.Value.StageRect);
            }

            double x = rect.X;
            double y = rect.Y;
            double bestDx = threshold + 1;
            double bestDy = threshold + 1;

            foreach (var t in targets)
            {
                var xCandidates = new[]
                {
                    t.Left,
                    t.Right - rect.Width,
                    t.Left - rect.Width,
                    t.Right
                };

                foreach (var cx in xCandidates)
                {
                    double dx = Math.Abs(cx - x);
                    if (dx < bestDx && dx <= threshold)
                    {
                        bestDx = dx;
                        x = cx;
                    }
                }

                var yCandidates = new[]
                {
                    t.Top,
                    t.Bottom - rect.Height,
                    t.Top - rect.Height,
                    t.Bottom
                };

                foreach (var cy in yCandidates)
                {
                    double dy = Math.Abs(cy - y);
                    if (dy < bestDy && dy <= threshold)
                    {
                        bestDy = dy;
                        y = cy;
                    }
                }
            }

            return ClampRectToStage(new Rect(x, y, rect.Width, rect.Height));
        }

        private Rect GetSnapRect(SnapSlot slot, double ratio)
        {
            double newW = slot.Rect.Width;
            double newH = slot.Rect.Height;
            if (slot.Direction.EndsWith("Left") || slot.Direction.EndsWith("Right"))
            {
                newH = slot.Rect.Height;
                newW = newH * ratio;
            }
            else
            {
                newW = slot.Rect.Width;
                newH = newW / ratio;
            }

            double finalX = slot.Rect.X;
            double finalY = slot.Rect.Y;
            if (slot.Direction.EndsWith("Left")) finalX = slot.Rect.X + slot.Rect.Width - newW;
            else if (slot.Direction.EndsWith("Right")) finalX = slot.Rect.X;
            else if (slot.Direction.EndsWith("Top")) finalY = slot.Rect.Y + slot.Rect.Height - newH;
            else if (slot.Direction.EndsWith("Bottom")) finalY = slot.Rect.Y;

            return new Rect(finalX, finalY, newW, newH);
        }

        private void ApplyLayoutModeToPlacedClients()
        {
            foreach (var client in _connectedClients)
            {
                string key = GetClientKey(client);
                if (!_clientLayouts.TryGetValue(key, out var layout) || !layout.IsPlaced) continue;

                if (_layoutMode == LayoutMode.Free)
                {
                    var natural = GetNaturalStageSize(client);
                    var current = layout.StageRect;
                    var centered = new Rect(
                        current.Center.X - (natural.Width / 2),
                        current.Center.Y - (natural.Height / 2),
                        natural.Width,
                        natural.Height
                    );
                    layout.StageRect = GetAnchoredFreeRect(centered, key, out var anchorScreenID, out var anchorEdge);
                    layout.IsSnapped = false;
                    layout.SnapAnchorID = "";
                    layout.AnchorScreenID = anchorScreenID;
                    layout.AnchorEdge = anchorEdge;
                }
                else
                {
                    SnapSlot? slot = null;
                    if (!string.IsNullOrEmpty(layout.SnapAnchorID))
                    {
                        slot = _snapSlots.FirstOrDefault(s => s.ID == layout.SnapAnchorID);
                    }

                    if (slot == null)
                    {
                        Point center = layout.StageRect.Center;
                        slot = _snapSlots
                            .Select(s => new { Slot = s, Dist = GetDistanceToRect(center, s.Rect) })
                            .OrderBy(x => x.Dist)
                            .FirstOrDefault()?.Slot;
                    }

                    if (slot != null)
                    {
                        layout.StageRect = GetSnapRect(slot, (double)client.Width / client.Height);
                        layout.IsSnapped = true;
                        layout.SnapAnchorID = slot.ID;
                        layout.AnchorScreenID = slot.ParentID;
                        layout.AnchorEdge = slot.Direction switch
                        {
                            "Left" => EdgeDirection.Left,
                            "Right" => EdgeDirection.Right,
                            "Top" => EdgeDirection.Top,
                            "Bottom" => EdgeDirection.Bottom,
                            _ => EdgeDirection.None
                        };
                    }
                }

                _clientLayouts[key] = layout;
            }

            RedrawClientBoxes();
        }

        private double GetDistanceToRect(Point pt, Rect rect) {
            double dx = Math.Max(rect.Left - pt.X, Math.Max(0, pt.X - rect.Right)), dy = Math.Max(rect.Top - pt.Y, Math.Max(0, pt.Y - rect.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void AddClientToDock(ClientHandler client) {
            double ratio = (double)client.Width / client.Height;
            double boxHeight = BASE_BOX_HEIGHT, boxWidth = boxHeight * ratio; 
            
            var ctxMenu = new ContextMenu();
            var slider = new Slider { 
                Minimum = 0.1, Maximum = 5.0, Width = 150, 
                Value = client.Sensitivity,
                Margin = new Thickness(5)
            };
            var header = new MenuItem { Header = "Mouse Sensitivity" };
            var itemSlider = new MenuItem { 
                Header = new StackPanel { 
                    Children = { 
                        new TextBlock { Text = $"Speed: {client.Sensitivity:F1}x" }, 
                        slider 
                    } 
                } 
            };
            
            slider.ValueChanged += (s, e) => {
                client.Sensitivity = e.NewValue;
                if(itemSlider.Header is StackPanel sp && sp.Children[0] is TextBlock tb) 
                    tb.Text = $"Speed: {client.Sensitivity:F1}x";
                SaveClientConfigs(); 
            };

            var wheelSlider = new Slider { Minimum = 0.1, Maximum = 3.0, Width = 150, Value = client.WheelSensitivity, Margin = new Thickness(5) };
            var wheelHeader = new MenuItem { Header = "Wheel Sensitivity" };
            var itemWheelSlider = new MenuItem { Header = new StackPanel { Children = { new TextBlock { Text = $"Wheel: {client.WheelSensitivity:F1}x" }, wheelSlider } } };
            
            wheelSlider.ValueChanged += (s, e) => {
                client.WheelSensitivity = e.NewValue;
                if(itemWheelSlider.Header is StackPanel sp && sp.Children[0] is TextBlock tb) tb.Text = $"Wheel: {client.WheelSensitivity:F1}x";
                SaveClientConfigs(); 
            };

            ctxMenu.Items.Add(header);
            ctxMenu.Items.Add(itemSlider);
            ctxMenu.Items.Add(new Separator());
            ctxMenu.Items.Add(wheelHeader);
            ctxMenu.Items.Add(itemWheelSlider);

            var box = new Border { 
                Width = boxWidth, Height = boxHeight, 
                Background = Brushes.Orange, Margin = new Thickness(5), 
                Cursor = new Cursor(StandardCursorType.Hand),
                ContextMenu = ctxMenu, 
                Child = new TextBlock { 
                    Text = $"{client.Name}\n{client.Width}x{client.Height}", 
                    FontSize=9, Foreground=Brushes.White, TextAlignment=TextAlignment.Center,
                    HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center 
                } 
            };
            ToolTip.SetTip(box, $"Client: {client.Name}\nRes: {client.Width}x{client.Height}\nRight-click for settings.");

            var clientKey = GetClientKey(client);
            _clientBoxes[clientKey] = box;

            bool isDragging = false; Point start = new Point();
            box.PointerPressed += (s, e) => { 
                if(!e.GetCurrentPoint(box).Properties.IsRightButtonPressed) {
                    isDragging = true;
                    start = e.GetPosition(box);
                    if(box.Parent is StackPanel) {
                        _pnlDock.Children.Remove(box);
                        _pnlStage.Children.Add(box);
                        box.Margin = new Thickness(0);

                        double oldW = Math.Max(1, box.Width);
                        double oldH = Math.Max(1, box.Height);
                        double relX = start.X / oldW;
                        double relY = start.Y / oldH;

                        if (_layoutMode == LayoutMode.Free)
                        {
                            var natural = GetNaturalStageSize(client);
                            box.Width = natural.Width;
                            box.Height = natural.Height;
                        }
                        start = new Point(relX * box.Width, relY * box.Height);

                        var stagePos = e.GetPosition(_pnlStage);
                        Canvas.SetLeft(box, stagePos.X - start.X);
                        Canvas.SetTop(box, stagePos.Y - start.Y);
                    } 
                    e.Pointer.Capture(box); 
                }
            };
            box.PointerMoved += (s, e) => { if(isDragging) { var p = e.GetPosition(_pnlStage); Canvas.SetLeft(box, p.X - start.X); Canvas.SetTop(box, p.Y - start.Y); } };
            box.PointerReleased += (s, e) => {
                if(isDragging) {
                    isDragging = false; e.Pointer.Capture(null);
                    Point boxCenter = new Point(Canvas.GetLeft(box) + box.Width/2, Canvas.GetTop(box) + box.Height/2);
                    var bestMatch = _snapSlots.Select(slot => new { Slot = slot, Dist = GetDistanceToRect(boxCenter, slot.Rect) }).Where(x => x.Dist < 100).OrderBy(x => x.Dist).FirstOrDefault();
                    if(_layoutMode == LayoutMode.Snap && bestMatch != null) {
                        var slot = bestMatch.Slot;
                        var snappedRect = GetSnapRect(slot, ratio);
                        box.Width = snappedRect.Width;
                        box.Height = snappedRect.Height;
                        Canvas.SetLeft(box, snappedRect.X);
                        Canvas.SetTop(box, snappedRect.Y);
                        box.Background = Brushes.Green;

                        _clientLayouts[clientKey] = new ClientLayout
                        {
                            ClientKey = clientKey,
                            StageRect = snappedRect,
                            IsPlaced = true,
                            IsSnapped = true,
                            SnapAnchorID = slot.ID,
                            AnchorScreenID = slot.ParentID,
                            AnchorEdge = slot.Direction switch
                            {
                                "Left" => EdgeDirection.Left,
                                "Right" => EdgeDirection.Right,
                                "Top" => EdgeDirection.Top,
                                "Bottom" => EdgeDirection.Bottom,
                                _ => EdgeDirection.None
                            }
                        };
                        Log($"Mapped {client.Name} to {slot.ID}");
                        SaveClientConfigs();
                    } else {
                        if (_layoutMode == LayoutMode.Free)
                        {
                            double left = Canvas.GetLeft(box);
                            double top = Canvas.GetTop(box);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;

                            var freeRect = ClampRectToStage(new Rect(left, top, box.Width, box.Height));
                            freeRect = GetAnchoredFreeRect(freeRect, clientKey, out var anchorScreenID, out var anchorEdge);
                            Canvas.SetLeft(box, freeRect.X);
                            Canvas.SetTop(box, freeRect.Y);
                            box.Width = freeRect.Width;
                            box.Height = freeRect.Height;
                            box.Background = Brushes.Green;

                            _clientLayouts[clientKey] = new ClientLayout
                            {
                                ClientKey = clientKey,
                                StageRect = freeRect,
                                IsPlaced = true,
                                IsSnapped = false,
                                SnapAnchorID = "",
                                AnchorScreenID = anchorScreenID,
                                AnchorEdge = anchorEdge
                            };
                            SaveClientConfigs();
                        }
                        else
                        {
                            box.Width = BASE_BOX_HEIGHT * ratio; box.Height = BASE_BOX_HEIGHT;
                            _pnlStage.Children.Remove(box); _pnlDock.Children.Add(box);
                            box.Margin = new Thickness(5);
                            box.Background = Brushes.Orange;
                            if (_clientLayouts.ContainsKey(clientKey)) _clientLayouts.Remove(clientKey);
                            SaveClientConfigs();
                        }
                    }
                }
            };

            string ip = clientKey;
            if (_clientConfigs.ContainsKey(ip)) {
                var config = _clientConfigs[ip];
                client.Sensitivity = config.Sensitivity;
                client.WheelSensitivity = config.WheelSensitivity; 
                slider.Value = config.Sensitivity;
                wheelSlider.Value = config.WheelSensitivity;

                if (config.IsPlaced && config.Width > 0 && config.Height > 0) {
                    _clientLayouts[ip] = new ClientLayout
                    {
                        ClientKey = ip,
                        StageRect = new Rect(config.X, config.Y, config.Width, config.Height),
                        IsPlaced = true,
                        IsSnapped = config.IsSnapped,
                        SnapAnchorID = config.SnapAnchorID ?? ""
                    };
                }
            }

            _pnlDock.Children.Add(box);
            ApplyLayoutModeToPlacedClients();
        }

        private void CreateExtensionSlots(string currentSlotID, Rect clientRect) {
            string incomingDir = "";
            if (currentSlotID.EndsWith("_Left")) incomingDir = "Right";
            else if (currentSlotID.EndsWith("_Right")) incomingDir = "Left";
            else if (currentSlotID.EndsWith("_Top")) incomingDir = "Bottom";
            else if (currentSlotID.EndsWith("_Bottom")) incomingDir = "Top";
            double thickness = 20;

            void AddSlot(string suffix, Rect r, string dir) {
                var id = currentSlotID + suffix;
                if(!_snapSlots.Any(s=>s.ID == id)) {
                    _snapSlots.Add(new SnapSlot(id, r, currentSlotID, dir));
                    var visualSlot = new Border {
                        Width = r.Width, Height = r.Height,
                        BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                        Background = new SolidColorBrush(Colors.LightGray, 0.3),
                        IsHitTestVisible = false 
                    };
                    Canvas.SetLeft(visualSlot, r.X); Canvas.SetTop(visualSlot, r.Y);
                    _pnlStage.Children.Add(visualSlot);
                }
            }

            if (incomingDir != "Left") AddSlot("_Left", new Rect(clientRect.X - thickness, clientRect.Y, thickness, clientRect.Height), "Left");
            if (incomingDir != "Right") AddSlot("_Right", new Rect(clientRect.Right, clientRect.Y, thickness, clientRect.Height), "Right");
            if (incomingDir != "Top") AddSlot("_Top", new Rect(clientRect.X, clientRect.Y - thickness, clientRect.Width, thickness), "Top");
            if (incomingDir != "Bottom") AddSlot("_Bottom", new Rect(clientRect.X, clientRect.Bottom, clientRect.Width, thickness), "Bottom");
        }

        private void ToggleServerState(object? s, RoutedEventArgs e) { if(_isServerRunning) StopServer(); else StartServer(); }
        
        private void StartServer() {
            try {
                _serverListener = new TcpListener(IPAddress.Any, DEFAULT_PORT);
                _serverListener.Start();
                _isServerRunning = true;
                _btnStartServer.Content = "Stop Server";
                
                string myIP = "Unknown";
                try 
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                        .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                    foreach (var ni in interfaces)
                    {
                        var props = ni.GetIPProperties();
                        var addr = props.UnicastAddresses
                            .FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address));
                        
                        if (addr != null)
                        {
                            myIP = addr.Address.ToString();
                            break;
                        }
                    }
                }
                catch {}

                if (myIP == "Unknown") 
                    myIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";

                _lblServerInfo.Text = $"IP: {myIP} / Port: {DEFAULT_PORT}";
                Log($"Server Started. IP: {myIP}");
                
                _hook = new SimpleGlobalHook();
                _hook.MouseMoved += OnHookMouseMoved; _hook.MouseDragged += OnHookMouseMoved;
                _hook.KeyPressed += OnHookKeyPressed; _hook.KeyReleased += OnHookKeyReleased;
                _hook.MousePressed += OnHookMousePressed; _hook.MouseReleased += OnHookMouseReleased;
                _hook.MouseWheel += OnHookMouseWheel;
                
                // [?좉퇋] ?대쭅 ?꾩넚 ?쒖옉
                _mouseSenderCts = new CancellationTokenSource();
                Task.Run(() => StartMouseSenderLoop(_mouseSenderCts.Token));

                // [?섏젙] ?대씪?댁뼵??蹂닿컙 ?쒓굅濡??명빐 愿??Task ?쒓굅??

                Task.Run(() => _hook.Run());
                AcceptClients();
            } catch(Exception ex) { Log("Start Error: " + ex.Message); }
        }

        private void StopServer() {
            _isServerRunning = false;
            
            // [?좉퇋] ?꾩넚 猷⑦봽 ?뺤?
            _mouseSenderCts?.Cancel();
            _mouseSenderCts = null;
            
            // [?섏젙] 蹂닿컙 ?쒓굅濡?愿??濡쒖쭅 ??젣

            _serverListener?.Stop(); _hook?.Dispose();
            CursorManager.Show(); CursorManager.Unlock(); 
            _connectedClients.ForEach(c => c.Close());
            _connectedClients.Clear();
            _clientBoxes.Clear();
            _clientLayouts.Clear();
            _btnStartServer.Content = "Start Server"; _lblServerInfo.Text = "Status: Stopped";
            _pnlDock.Children.Clear(); DrawVisualLayout();
            SaveClientConfigs(); 
        }

        // [?좉퇋] 留덉슦??醫뚰몴 ?꾩넚 ?꾩슜 猷⑦봽 (120Hz = ~8ms)
        private async Task StartMouseSenderLoop(CancellationToken token)
        {
            try 
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isRemoteActive && _activeRemoteClient != null && _hasPendingMouse)
                    {
                        // ???놁씠 ?먯옄?곸쑝濡??쎄린 ?쒕룄 (int??atomic)
                        int x = _pendingMouseX;
                        int y = _pendingMouseY;
                        
                        // ?대? 蹂대궦 醫뚰몴硫??ㅽ궢
                        _hasPendingMouse = false; 

                        // [v6.4] ?쒕옒洹?以묒뿉???⑦궥 ?좎떎 諛⑹?瑜??꾪빐 利됱떆 ?꾩넚???좊━?????덉쑝?? 
                        // ?꾩옱 猷⑦봽媛 5ms濡?異⑸텇??鍮좊Ⅴ誘濡??쇰떒 ?좎?.
                        // ?ㅻ쭔, MouseDown/Up??利됱떆 ?꾩넚?섎?濡??쒖꽌 ??쟾 諛⑹?瑜??꾪빐 
                        // ?쒕옒洹??쒖옉/醫낅즺 ?쒖젏???쒕뵫??醫뚰몴媛 ?덈떎硫?癒쇱? 蹂대궡??濡쒖쭅???꾩슂?????덉쓬.
                        
                        // ?ㅼ젣 ?꾩넚
                        _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = x, Y = y });
                    }
                    await Task.Delay(5, token); // ??200Hz
                }
            }
            catch (TaskCanceledException) { }
        }

        private async void AcceptClients() {
            while(_isServerRunning) {
                try {
                    if(_serverListener == null) break;
                    var client = await _serverListener.AcceptTcpClientAsync();
                    // [?섏젙] NoDelay ?ㅼ젙
                    client.NoDelay = true;

                    var handler = new ClientHandler(client, this);
                    handler.Disconnected += (h) => {
                        if (h is ClientHandler ch) {
                            Dispatcher.UIThread.Post(() => {
                                _connectedClients.Remove(ch); Log($"Client {ch.Name} disconnected.");
                                string key = GetClientKey(ch);
                                if (_clientBoxes.TryGetValue(key, out var box))
                                {
                                    if (box.Parent == _pnlStage) _pnlStage.Children.Remove(box);
                                    if (box.Parent == _pnlDock) _pnlDock.Children.Remove(box);
                                    _clientBoxes.Remove(key);
                                }
                                if (_clientLayouts.ContainsKey(key)) _clientLayouts.Remove(key);
                                if (_activeRemoteClient == ch) { _isRemoteActive = false; _activeRemoteClient = null; CursorManager.Unlock(); CursorManager.Show(); }
                            });
                        }
                    };
                    if (await handler.HandshakeAsync()) {
                        _connectedClients.Add(handler); handler.StartReading(); 
                        Dispatcher.UIThread.Post(() => { AddClientToDock(handler); Log($"Client Connected: {handler.Width}x{handler.Height}"); });
                    } else { handler.Close(); }
                } catch {}
            }
        }

        // [蹂듦뎄] 硫붿꽌??異붽?
        private void OnHookMousePressed(object? sender, MouseHookEventArgs e) { 
            if (e.Data.Button == SharpHook.Native.MouseButton.Button1) _isLeftDragging = true; 
            if (_isRemoteActive && _activeRemoteClient != null) { 
                // [v6.5] ?붾툝?대┃ 濡쒖쭅 異붽?
                var now = DateTime.Now;
                if (_lastClickButton == (int)e.Data.Button && (now - _lastClickTime).TotalMilliseconds < 500) {
                    _clickCount++;
                } else {
                    _clickCount = 1;
                }
                _lastClickTime = now;
                _lastClickButton = (int)e.Data.Button;

                // [v6.4] ?대┃ ???쒕뵫???대룞 醫뚰몴媛 ?덈떎硫?利됱떆 ?꾩넚?섏뿬 ?쒖꽌 蹂댁옣
                if (_hasPendingMouse) {
                    _hasPendingMouse = false;
                    _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = _pendingMouseX, Y = _pendingMouseY });
                }

                _activeRemoteClient.SendPacketAsync(new InputPacket { 
                    Type = PacketType.MouseDown, 
                    KeyCode = (int)e.Data.Button,
                    X = (int)_virtualX, // [?좉퇋] ?대┃ ?쒖젏 醫뚰몴 ?숆린??
                    Y = (int)_virtualY,
                    ClickCount = _clickCount
                }); 
                e.SuppressEvent = true; 
            } 
        }

        // [蹂듦뎄] 硫붿꽌??異붽?
        private void OnHookMouseReleased(object? sender, MouseHookEventArgs e) { 
            if (e.Data.Button == SharpHook.Native.MouseButton.Button1) _isLeftDragging = false; 
            if (_isRemoteActive && _activeRemoteClient != null) { 
                // [v6.4] ?대┃ ?댁젣 ???쒕뵫???대룞 醫뚰몴媛 ?덈떎硫?利됱떆 ?꾩넚
                if (_hasPendingMouse) {
                    _hasPendingMouse = false;
                    _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = _pendingMouseX, Y = _pendingMouseY });
                }

                _activeRemoteClient.SendPacketAsync(new InputPacket { 
                    Type = PacketType.MouseUp, 
                    KeyCode = (int)e.Data.Button,
                    X = (int)_virtualX, // [?좉퇋] ?대┃ ?쒖젏 醫뚰몴 ?숆린??
                    Y = (int)_virtualY,
                    ClickCount = _clickCount
                }); 
                e.SuppressEvent = true; 
            } 
        }

        private void OnHookMouseWheel(object? sender, MouseWheelHookEventArgs e) { 
            if (_isRemoteActive && _activeRemoteClient != null) { 
                double sensitivity = _activeRemoteClient.WheelSensitivity;
                _wheelAccumulator += e.Data.Rotation * sensitivity;

                int delta = (int)_wheelAccumulator;
                if (delta != 0) {
                    _wheelAccumulator -= delta;
                    _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseWheel, KeyCode = delta }); 
                }
                e.SuppressEvent = true; 
            } 
        }

        private bool IsCandidateOnSide(Rect rootBounds, Rect candidate, EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return candidate.Center.X <= rootBounds.Center.X;
            if (edge == EdgeDirection.Right) return candidate.Center.X >= rootBounds.Center.X;
            if (edge == EdgeDirection.Top) return candidate.Center.Y <= rootBounds.Center.Y;
            if (edge == EdgeDirection.Bottom) return candidate.Center.Y >= rootBounds.Center.Y;
            return false;
        }

        private Point GetNearestPointOnEdge(Point source, Rect rect, EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return new Point(rect.Left, Math.Clamp(source.Y, rect.Top, rect.Bottom));
            if (edge == EdgeDirection.Right) return new Point(rect.Right, Math.Clamp(source.Y, rect.Top, rect.Bottom));
            if (edge == EdgeDirection.Top) return new Point(Math.Clamp(source.X, rect.Left, rect.Right), rect.Top);
            if (edge == EdgeDirection.Bottom) return new Point(Math.Clamp(source.X, rect.Left, rect.Right), rect.Bottom);
            return source;
        }

        private bool TryGetEdgeAtLocalScreen(ScreenInfo screen, int x, int y, out EdgeDirection edge)
        {
            edge = EdgeDirection.None;
            int buffer = (int)(Math.Min(screen.Bounds.Width, screen.Bounds.Height) * 0.01);
            buffer = Math.Clamp(buffer, 5, 30);

            bool inY = y >= screen.Bounds.Top - buffer && y <= screen.Bounds.Bottom + buffer;
            bool inX = x >= screen.Bounds.Left - buffer && x <= screen.Bounds.Right + buffer;

            if (inY && x <= screen.Bounds.Left + buffer) edge = EdgeDirection.Left;
            else if (inY && x >= screen.Bounds.Right - 1 - buffer) edge = EdgeDirection.Right;
            else if (inX && y <= screen.Bounds.Top + buffer) edge = EdgeDirection.Top;
            else if (inX && y >= screen.Bounds.Bottom - 1 - buffer) edge = EdgeDirection.Bottom;

            return edge != EdgeDirection.None;
        }

        private bool TryFindAttachTarget(ScreenInfo rootScreen, EdgeDirection localExitEdge, Point cursorPoint, out ClientHandler targetClient, out Rect targetDesktopRect, out Point targetEdgePoint, out EdgeDirection targetEntryEdge)
        {
            targetClient = null!;
            targetDesktopRect = new Rect();
            targetEdgePoint = default;
            targetEntryEdge = EdgeDirection.None;

            Rect sourceRect = rootScreen.Bounds;
            double bestDist = double.MaxValue;

            foreach (var layout in _clientLayouts.Values.Where(l => l.IsPlaced))
            {
                var candidate = GetClientByKey(layout.ClientKey);
                if (candidate == null) continue;
                var expectedEntryEdge = layout.AnchorEdge != EdgeDirection.None ? layout.AnchorEdge : OppositeEdge(localExitEdge);

                var desktopRect = StageToDesktop(layout.StageRect);
                if (desktopRect.Width <= 0 || desktopRect.Height <= 0) continue;
                if (!HasPerpendicularOverlapForEntry(sourceRect, desktopRect, expectedEntryEdge)) continue;

                var p = GetNearestPointOnEdge(cursorPoint, desktopRect, expectedEntryEdge);
                double dist = Math.Sqrt(Math.Pow(cursorPoint.X - p.X, 2) + Math.Pow(cursorPoint.Y - p.Y, 2));

                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetClient = candidate;
                    targetDesktopRect = desktopRect;
                    targetEdgePoint = p;
                    targetEntryEdge = expectedEntryEdge;
                }
            }

            return bestDist < 1200;
        }

        private bool TryFindNeighborFromRemote(EdgeDirection exitEdge, out ClientHandler targetClient, out Rect targetDesktopRect, out Point targetEdgePoint, out EdgeDirection targetEntryEdge)
        {
            targetClient = null!;
            targetDesktopRect = new Rect();
            targetEdgePoint = default;
            targetEntryEdge = EdgeDirection.None;
            if (_activeRemoteClient == null) return false;

            string activeKey = GetClientKey(_activeRemoteClient);
            if (!_clientLayouts.TryGetValue(activeKey, out var activeLayout) || !activeLayout.IsPlaced) return false;

            Rect activeRect = StageToDesktop(activeLayout.StageRect);
            if (activeRect.Width <= 0 || activeRect.Height <= 0) return false;

            double clientW = _activeRemoteClient.Width > 0 ? _activeRemoteClient.Width : 1920;
            double clientH = _activeRemoteClient.Height > 0 ? _activeRemoteClient.Height : 1080;
            double ratioX = Math.Clamp(_virtualX / clientW, 0.0, 1.0);
            double ratioY = Math.Clamp(_virtualY / clientH, 0.0, 1.0);

            Point exitPoint = exitEdge switch
            {
                EdgeDirection.Left => new Point(activeRect.Left, activeRect.Top + activeRect.Height * ratioY),
                EdgeDirection.Right => new Point(activeRect.Right, activeRect.Top + activeRect.Height * ratioY),
                EdgeDirection.Top => new Point(activeRect.Left + activeRect.Width * ratioX, activeRect.Top),
                EdgeDirection.Bottom => new Point(activeRect.Left + activeRect.Width * ratioX, activeRect.Bottom),
                _ => activeRect.Center
            };

            double bestDist = double.MaxValue;

            foreach (var layout in _clientLayouts.Values.Where(l => l.IsPlaced && l.ClientKey != activeKey))
            {
                var candidate = GetClientByKey(layout.ClientKey);
                if (candidate == null) continue;
                var expectedEntryEdge = layout.AnchorEdge != EdgeDirection.None ? layout.AnchorEdge : OppositeEdge(exitEdge);

                var desktopRect = StageToDesktop(layout.StageRect);
                if (desktopRect.Width <= 0 || desktopRect.Height <= 0) continue;
                if (!HasPerpendicularOverlapForEntry(activeRect, desktopRect, expectedEntryEdge)) continue;

                var p = GetNearestPointOnEdge(exitPoint, desktopRect, expectedEntryEdge);
                double dist = Math.Sqrt(Math.Pow(exitPoint.X - p.X, 2) + Math.Pow(exitPoint.Y - p.Y, 2));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetClient = candidate;
                    targetDesktopRect = desktopRect;
                    targetEdgePoint = p;
                    targetEntryEdge = expectedEntryEdge;
                }
            }

            return bestDist < 1400;
        }

        private void SwitchToRemoteClient(ClientHandler targetClient, ScreenInfo rootScreen, EdgeDirection entryEdge, Rect targetDesktopRect, Point targetEdgePoint)
        {
            _activeRemoteClient = targetClient;
            _activeRootScreen = rootScreen;
            _activeEntryEdge = entryEdge;
            _isRemoteActive = true;
            _scaleX = 1.0;
            _scaleY = 1.0;

            if (targetClient.Width > 0 && rootScreen.Bounds.Width > 0)
            {
                _resolutionScale = targetClient.Width / (double)rootScreen.Bounds.Width;
                if (_resolutionScale < 1.0) _resolutionScale = 1.0;
            }
            else
            {
                _resolutionScale = 1.0;
            }

            double ratioX = targetDesktopRect.Width > 0 ? Math.Clamp((targetEdgePoint.X - targetDesktopRect.Left) / targetDesktopRect.Width, 0.0, 1.0) : 0.5;
            double ratioY = targetDesktopRect.Height > 0 ? Math.Clamp((targetEdgePoint.Y - targetDesktopRect.Top) / targetDesktopRect.Height, 0.0, 1.0) : 0.5;
            if (entryEdge == EdgeDirection.Left) { _virtualX = 0; _virtualY = ratioY * targetClient.Height; }
            else if (entryEdge == EdgeDirection.Right) { _virtualX = targetClient.Width - 1; _virtualY = ratioY * targetClient.Height; }
            else if (entryEdge == EdgeDirection.Top) { _virtualX = ratioX * targetClient.Width; _virtualY = 0; }
            else if (entryEdge == EdgeDirection.Bottom) { _virtualX = ratioX * targetClient.Width; _virtualY = targetClient.Height - 1; }

            Dispatcher.UIThread.Post(() => Log($"Switched to {targetClient.Name} (Sens: {targetClient.Sensitivity:F1})"));
            TrySyncClipboardToRemote();

            CursorManager.LockToRect(new Rect(rootScreen.Bounds.Center.X, rootScreen.Bounds.Center.Y, 1, 1));
            CursorManager.Hide();
            _ignoreNextMove = true;
            _simulator?.SimulateMouseMovement((short)rootScreen.Bounds.Center.X, (short)rootScreen.Bounds.Center.Y);

            _pendingMouseX = (int)_virtualX;
            _pendingMouseY = (int)_virtualY;
            _hasPendingMouse = true;
        }

        private void ReturnToLocal(EdgeDirection exitEdge)
        {
            _lastReturnTime = DateTime.Now;
            Dispatcher.UIThread.Post(() => Log("Returned to Local"));

            CursorManager.Show();
            CursorManager.Unlock();

            var rootScreen = _activeRootScreen ?? _cachedScreens.FirstOrDefault();
            if (rootScreen == null)
            {
                _isRemoteActive = false;
                _activeRemoteClient = null;
                return;
            }

            double ratioX = 0.5;
            double ratioY = 0.5;
            if (_activeRemoteClient != null && _activeRemoteClient.Width > 0 && _activeRemoteClient.Height > 0)
            {
                ratioX = Math.Clamp(_virtualX / _activeRemoteClient.Width, 0.0, 1.0);
                ratioY = Math.Clamp(_virtualY / _activeRemoteClient.Height, 0.0, 1.0);
            }

            double targetX = rootScreen.Bounds.X + (rootScreen.Bounds.Width * ratioX);
            double targetY = rootScreen.Bounds.Y + (rootScreen.Bounds.Height * ratioY);
            int buffer = 10;

            if (exitEdge == EdgeDirection.Left) targetX = rootScreen.Bounds.Left + buffer;
            else if (exitEdge == EdgeDirection.Right) targetX = rootScreen.Bounds.Right - buffer;
            else if (exitEdge == EdgeDirection.Top) targetY = rootScreen.Bounds.Top + buffer;
            else if (exitEdge == EdgeDirection.Bottom) targetY = rootScreen.Bounds.Bottom - buffer;

            _isRemoteActive = false;
            _activeRemoteClient = null;
            _activeRootScreen = null;
            _activeEntryEdge = EdgeDirection.None;
            _ignoreNextMove = true;
            _simulator?.SimulateMouseMovement((short)targetX, (short)targetY);
        }

        private void OnHookMouseMoved(object? s, MouseHookEventArgs e)
        {
            if (_ignoreNextMove)
            {
                _ignoreNextMove = false;
                return;
            }

            bool dragging = _isLeftDragging;
            if (_skipMoveCount > 0 && !dragging)
            {
                _skipMoveCount--;
                return;
            }

            if ((DateTime.Now - _lastReturnTime).TotalMilliseconds < 300) return;
            int x = e.Data.X;
            int y = e.Data.Y;

            if (!_isRemoteActive)
            {
                ScreenInfo? currentScreen = null;
                foreach (var screen in _cachedScreens) { if (screen.Bounds.Contains(new Point(x, y))) { currentScreen = screen; break; } }
                if (currentScreen == null) currentScreen = _cachedScreens.OrderBy(scr => Math.Pow(x - scr.Bounds.Center.X, 2) + Math.Pow(y - scr.Bounds.Center.Y, 2)).FirstOrDefault();
                if (currentScreen != null && TryGetEdgeAtLocalScreen(currentScreen, x, y, out var exitEdge))
                {
                    var cursorPoint = new Point(x, y);
                    if (TryFindAttachTarget(currentScreen, exitEdge, cursorPoint, out var targetClient, out var targetDesktopRect, out var targetEdgePoint, out var targetEntryEdge))
                    {
                        SwitchToRemoteClient(targetClient, currentScreen, targetEntryEdge, targetDesktopRect, targetEdgePoint);
                        e.SuppressEvent = true;
                        return;
                    }
                }
                return;
            }

            if (_activeRemoteClient == null || _activeRootScreen == null) return;

            double centerX = _activeRootScreen.Bounds.Center.X;
            double centerY = _activeRootScreen.Bounds.Center.Y;
            int dx = x - (int)centerX;
            int dy = y - (int)centerY;
            if (dx == 0 && dy == 0) return;

            if (Math.Abs(dx) > 500 || Math.Abs(dy) > 500)
            {
                _ignoreNextMove = true;
                _simulator?.SimulateMouseMovement((short)centerX, (short)centerY);
                return;
            }

            double accelX = Math.Abs(dx) > 10 ? 1.1 : 1.0;
            double accelY = Math.Abs(dy) > 10 ? 1.1 : 1.0;
            double currentSensitivity = dragging ? 1.0 : _activeRemoteClient.Sensitivity;
            _virtualX += dx * _scaleX * _resolutionScale * currentSensitivity * (dragging ? 1.0 : accelX);
            _virtualY += dy * _scaleY * _resolutionScale * currentSensitivity * (dragging ? 1.0 : accelY);

            double clientW = _activeRemoteClient.Width > 0 ? _activeRemoteClient.Width : 1920;
            double clientH = _activeRemoteClient.Height > 0 ? _activeRemoteClient.Height : 1080;
            EdgeDirection exit = EdgeDirection.None;
            if (_virtualX < 0) exit = EdgeDirection.Left;
            else if (_virtualX > clientW) exit = EdgeDirection.Right;
            else if (_virtualY < 0) exit = EdgeDirection.Top;
            else if (_virtualY > clientH) exit = EdgeDirection.Bottom;

            if (exit != EdgeDirection.None)
            {
                if (TryFindNeighborFromRemote(exit, out var nextClient, out var nextDesktopRect, out var nextEdgePoint, out var nextEntryEdge))
                {
                    SwitchToRemoteClient(nextClient, _activeRootScreen, nextEntryEdge, nextDesktopRect, nextEdgePoint);
                }
                else
                {
                    ReturnToLocal(exit);
                }
                e.SuppressEvent = true;
                return;
            }

            _pendingMouseX = (int)_virtualX;
            _pendingMouseY = (int)_virtualY;
            _hasPendingMouse = true;

            if (dragging && _activeRemoteClient != null)
            {
                _hasPendingMouse = false;
                _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = _pendingMouseX, Y = _pendingMouseY });
            }

            _ignoreNextMove = true;
            _skipMoveCount = dragging ? 0 : 2;
            _simulator?.SimulateMouseMovement((short)centerX, (short)centerY);
            CursorManager.LockToRect(new Rect(centerX, centerY, 1, 1));
            CursorManager.Hide();
            e.SuppressEvent = true;
        }

        private void OnHookKeyPressed(object? s, KeyboardHookEventArgs e) {
            var code = e.Data.KeyCode;
            
            if (code == KeyCode.VcLeftControl || code == KeyCode.VcRightControl) _isLocalCtrlDown = true;
            if (code == KeyCode.VcLeftMeta || code == KeyCode.VcRightMeta) _isLocalMetaDown = true;
            
            if ((DateTime.Now - _lastReturnTime).TotalSeconds > 1 && !_isRemoteActive) {
            }

            if (code == KeyCode.VcC && (_isLocalCtrlDown || _isLocalMetaDown)) { 
                _ = Task.Run(async () => { await Task.Delay(100); Dispatcher.UIThread.Post(() => CheckClipboard()); }); 
            }
            if (code == KeyCode.VcV && (_isLocalCtrlDown || _isLocalMetaDown)) { 
                if (_isRemoteActive) TrySyncClipboardToRemote(); 
            }

            if (_isRemoteActive && _activeRemoteClient != null) { 
                if (_activeRemoteClient.IsMac) { 
                    // ?덈룄???쒕쾭) -> 留??대씪) ?????⑥텞??留ㅽ븨 (v6.2 ?섏젙)
                    // Ctrl -> Ctrl (?숈씪)
                    // Win -> Opt (VcLeftMeta -> VcLeftAlt)
                    // Alt -> Cmd (VcLeftAlt -> VcLeftMeta)
                    if (code == KeyCode.VcLeftMeta) code = KeyCode.VcLeftAlt;
                    else if (code == KeyCode.VcRightMeta) code = KeyCode.VcRightAlt;
                    else if (code == KeyCode.VcLeftAlt) code = KeyCode.VcLeftMeta;
                    else if (code == KeyCode.VcRightAlt) code = KeyCode.VcRightMeta;
                }

                // [v6.6] Caps Lock ?몄뼱 蹂寃?吏?? 
                // 留μ뿉?쒕뒗 Caps Lock??吏㏐쾶 ?뚮윭 ?몄뼱 蹂寃쎌쓣 ?섎뒗 湲곕뒫???덉쓬.
                // ?덈룄???쒕쾭?먯꽌 Caps Lock???뚮졇?????대? ?먭꺽吏濡??뺥솗???꾨떖?섎룄濡???

                if (code == KeyCode.VcInsert) code = (KeyCode)0xE052; 
                
                _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.KeyDown, KeyCode = (int)code }); 
                e.SuppressEvent = true; 
            }
        }
        
        private void OnHookKeyReleased(object? s, KeyboardHookEventArgs e) { 
            var code = e.Data.KeyCode;
            if (code == KeyCode.VcLeftControl || code == KeyCode.VcRightControl) _isLocalCtrlDown = false;
            if (code == KeyCode.VcLeftMeta || code == KeyCode.VcRightMeta) _isLocalMetaDown = false;
            if (_isRemoteActive && _activeRemoteClient != null) { 
                if (_activeRemoteClient.IsMac) { 
                    if (code == KeyCode.VcLeftMeta) code = KeyCode.VcLeftAlt; 
                    else if (code == KeyCode.VcRightMeta) code = KeyCode.VcRightAlt;
                    else if (code == KeyCode.VcLeftAlt) code = KeyCode.VcLeftMeta;
                    else if (code == KeyCode.VcRightAlt) code = KeyCode.VcRightMeta;
                }
                _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.KeyUp, KeyCode = (int)code }); e.SuppressEvent = true; 
            } 
        }

        private async void ToggleClientConnection(object? s, RoutedEventArgs e) { if (_isClientRunning) StopClient(); else await StartClientLoop(); }
        private void StopClient() { _isClientRunning = false; _currentClientSocket?.Close(); _btnConnect.Content = "Connect"; _btnConnect.IsEnabled = true; Log("Client Stopped."); }
        
        private async Task StartClientLoop() {
            _isClientRunning = true; _btnConnect.Content = "Disconnect"; SaveConfig();
            string ip = _txtServerIP.Text ?? DEFAULT_IP;
            await Task.Run(async () => {
                while (_isClientRunning) {
                    try {
                        Dispatcher.UIThread.Post(() => Log("Connecting..."));
                        _currentClientSocket = new TcpClient();
                        _currentClientSocket.NoDelay = true;
                        
                        await _currentClientSocket.ConnectAsync(ip, DEFAULT_PORT);
                        Dispatcher.UIThread.Post(() => Log("Connected!"));
                        using (var stream = _currentClientSocket.GetStream()) {
                                if (this.Screens.Primary != null) {
                                var bounds = this.Screens.Primary.Bounds;
                                var hello = new InputPacket { Type = PacketType.Hello, X = (int)bounds.Width, Y = (int)bounds.Height };
                                byte[] raw = new byte[Marshal.SizeOf(hello)]; IntPtr ptr = Marshal.AllocHGlobal(raw.Length); Marshal.StructureToPtr(hello, ptr, true); Marshal.Copy(ptr, raw, 0, raw.Length); Marshal.FreeHGlobal(ptr);
                                await stream.WriteAsync(raw, 0, raw.Length);

                                // [?좉퇋] ?뚮옯???뺣낫 ?꾩넚
                                int platformCode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 1 : 0;
                                var platformPacket = new InputPacket { Type = PacketType.PlatformInfo, KeyCode = platformCode };
                                byte[] pRaw = new byte[Marshal.SizeOf(platformPacket)]; IntPtr pPtr = Marshal.AllocHGlobal(pRaw.Length); Marshal.StructureToPtr(platformPacket, pPtr, true); Marshal.Copy(pPtr, pRaw, 0, pRaw.Length); Marshal.FreeHGlobal(pPtr);
                                await stream.WriteAsync(pRaw, 0, pRaw.Length);
                            }
                            var buffer = new byte[Marshal.SizeOf(typeof(InputPacket))];
                            while (_isClientRunning) {
                                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) throw new Exception("Disconnected"); 
                                GCHandle h = GCHandle.Alloc(buffer, GCHandleType.Pinned); InputPacket p = (InputPacket)Marshal.PtrToStructure(h.AddrOfPinnedObject(), typeof(InputPacket))!; h.Free();
                                if (p.Type == PacketType.Clipboard) {
                                    int len = p.X; if (len > 0) { byte[] textBytes = new byte[len]; int totalRead = 0; while(totalRead < len) { int r = await stream.ReadAsync(textBytes, totalRead, len - totalRead); if(r==0) break; totalRead += r; } string text = Encoding.UTF8.GetString(textBytes); SetRemoteClipboard(text); }
                                } 
                                else if (p.Type == PacketType.ClipboardFile) {
                                    int len = p.X; if (len > 0) { byte[] fileBytes = new byte[len]; int totalRead = 0; while (totalRead < len) { int r = await stream.ReadAsync(fileBytes, totalRead, len - totalRead); if (r == 0) break; totalRead += r; } ProcessReceivedFiles(fileBytes); }
                                }
                                else if (p.Type == PacketType.ClipboardImage) {
                                    int len = p.X; if (len > 0) { byte[] imgBytes = new byte[len]; int totalRead = 0; while (totalRead < len) { int r = await stream.ReadAsync(imgBytes, totalRead, len - totalRead); if (r == 0) break; totalRead += r; } ProcessReceivedImage(imgBytes); }
                                }
                                else { 
                                     SimulateInput(p); // [?섏젙] MainWindow 硫붿꽌???몄텧
                                }
                            }
                        }
                    } catch (Exception ex) { if (!_isClientRunning) break; Dispatcher.UIThread.Post(() => Log($"Disconnected. Retry 3s.. ({ex.Message})")); await Task.Delay(3000); }
                }
            });
        }

        // [蹂듦뎄] 硫붿꽌??異붽?
        private void TriggerMacMissionControl(KeyCode code) {
            int macCode = 0;
            if (code == KeyCode.VcLeft) macCode = 123;
            else if (code == KeyCode.VcRight) macCode = 124;
            else if (code == KeyCode.VcUp) macCode = 126; 
            else if (code == KeyCode.VcDown) macCode = 125; 

            if (macCode > 0) {
                if ((DateTime.Now - _lastMacShortcutTime).TotalMilliseconds < 200) return;
                _lastMacShortcutTime = DateTime.Now;

                _ = Task.Run(() => {
                    try {
                        Process.Start(new ProcessStartInfo {
                            FileName = "osascript",
                            Arguments = $"-e \"tell application \\\"System Events\\\" to key code {macCode} using control down\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        Dispatcher.UIThread.Post(() => Log($"Mac Shortcut Triggered ({macCode})"));
                    } catch (Exception ex) { Dispatcher.UIThread.Post(() => Log($"Mac script error: {ex.Message}")); }
                });
            }
        }

        private void SimulateInput(InputPacket p) {
            try {
                switch(p.Type) {
                    case PacketType.MouseMove: 
                         // [?섏젙] 利됱떆 ?대룞 諛??꾩옱 ?꾩튂 ?낅뜲?댄듃
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                            if (_isLeftDragging) {
                                // [v6.4] 留μ뿉???쒕옒洹?以묒씪 ?뚮뒗 MouseDragged ?대깽?몃? 吏곸젒 諛쒖깮?쒗궡
                                CursorManager.SendMacRawDrag(p.X, p.Y, 0); // 0 = LeftButton
                            } else {
                                // [v6.7] 留?Zoom ?섍꼍?먯꽌 Warp ???먰봽 ?꾩긽??留됯린 ?꾪빐 Raw Move ?ъ슜
                                CursorManager.SendMacRawMove(p.X, p.Y);
                            }
                        } else {
                            _simulator?.SimulateMouseMovement((short)p.X, (short)p.Y);
                        }
                        _currentClientX = p.X;
                        _currentClientY = p.Y;
                        break;
                    
                    case PacketType.MouseDown: 
                        if (p.KeyCode == (int)SharpHook.Native.MouseButton.Button1) _isLeftDragging = true;
                        // [?섏젙] 留?Zoom ?섍꼍 ??? SimulateMouseMovement + SimulateMousePress 議고빀? 酉고룷?멸? ????꾩긽???덉쓬.
                        // 吏곸젒 CGEvent瑜??앹꽦?섏뿬 醫뚰몴? ?대┃???숈떆??蹂대궡硫?Zoom ?곹깭?먯꽌???뺥솗???꾩튂???대┃??
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && p.X >= 0 && p.Y >= 0) {
                            CursorManager.SendMacRawClick(p.X, p.Y, (int)p.KeyCode - 1, true, p.ClickCount);
                        } else {
                            if (p.X >= 0 && p.Y >= 0) {
                                _simulator?.SimulateMouseMovement((short)p.X, (short)p.Y);
                            }
                            _simulator?.SimulateMousePress((SharpHook.Native.MouseButton)p.KeyCode); 
                        }
                        break;

                    case PacketType.MouseUp: 
                        if (p.KeyCode == (int)SharpHook.Native.MouseButton.Button1) _isLeftDragging = false;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && p.X >= 0 && p.Y >= 0) {
                            CursorManager.SendMacRawClick(p.X, p.Y, (int)p.KeyCode - 1, false, p.ClickCount);
                        } else {
                            if (p.X >= 0 && p.Y >= 0) {
                                _simulator?.SimulateMouseMovement((short)p.X, (short)p.Y);
                            }
                            _simulator?.SimulateMouseRelease((SharpHook.Native.MouseButton)p.KeyCode); 
                        }
                        break;
                    
                    case PacketType.MouseWheel: _simulator?.SimulateMouseWheel((short)p.KeyCode); break; 
                    case PacketType.KeyDown: 
                    case PacketType.KeyUp:
                        var code = (KeyCode)p.KeyCode;
                        // [?섏젙] ?대? ?쒕쾭?먯꽌 OS??留욊쾶 ??肄붾뱶瑜?蹂?섑빐??蹂대궡二쇰?濡??대씪?댁뼵?몃뒗 理쒖냼?쒖쓽 泥섎━留??섑뻾
                        // (?? ?대씪?댁뼵?멸? 留μ씪 ??Mission Control ?몃━嫄?濡쒖쭅? ?좎?)
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                            if (code == KeyCode.VcLeftControl || code == KeyCode.VcRightControl) {
                                _isRemoteCtrlDown = (p.Type == PacketType.KeyDown);
                            }

                            if (p.Type == PacketType.KeyDown && _isRemoteCtrlDown) {
                                if (code == KeyCode.VcLeft || code == KeyCode.VcRight || code == KeyCode.VcUp || code == KeyCode.VcDown) {
                                    TriggerMacMissionControl(code);
                                    return; 
                                }
                            }
                        }

                        if (p.Type == PacketType.KeyDown) _simulator?.SimulateKeyPress(code);
                        else _simulator?.SimulateKeyRelease(code);
                        break;
                }
            } catch {}
        }
    }
    
    // [ClientHandler 諛?SnapSlot ?좎?]
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
                int size = Marshal.SizeOf(typeof(InputPacket));
                byte[] buffer = new byte[size];
                int bytesRead = await _stream.ReadAsync(buffer, 0, size);
                if (bytesRead != size) return false;
                
                GCHandle h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                object? structObj = Marshal.PtrToStructure(h.AddrOfPinnedObject(), typeof(InputPacket));
                h.Free();

                if (structObj == null) return false;
                InputPacket p = (InputPacket)structObj;
                
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
                byte[] buff = new byte[Marshal.SizeOf(typeof(InputPacket))];
                try
                {
                    while (true)
                    {
                        int read = await _stream.ReadAsync(buff, 0, buff.Length);
                        if (read == 0) break;

                        GCHandle h = GCHandle.Alloc(buff, GCHandleType.Pinned);
                        object? structObj = Marshal.PtrToStructure(h.AddrOfPinnedObject(), typeof(InputPacket));
                        h.Free();

                        if (structObj == null) continue;
                        InputPacket p = (InputPacket)structObj;

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
                            // ?쇰컲 ?낅젰 ?⑦궥 (留덉슦????? 諛붾줈 泥섎━
                            // [?좉퇋] ?대씪?댁뼵??履쎌뿉?쒕룄 ?⑦궥 泥섎━ 理쒖쟻??(?꾩슂??
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
            int size = Marshal.SizeOf(p);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(p, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            _sendQueue.Add(arr);
        }

        public void Close()
        {
            _sendQueue.CompleteAdding();
            Socket.Close();
        }
    }

    public class SnapSlot { public string ID; public Rect Rect; public string ParentID; public string Direction; public SnapSlot(string id, Rect r, string pid, string dir) { ID = id; Rect = r; ParentID = pid; Direction = dir; } }
}

