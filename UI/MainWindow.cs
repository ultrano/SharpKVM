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
    public partial class MainWindow : Window
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
#if DEBUG
        private Button? _btnAddVirtualClient;
        private ComboBox? _cmbVirtualResolution;
        private VirtualClientHost? _virtualClientHost;
        private int _selectedVirtualWidth = 1920;
        private int _selectedVirtualHeight = 1080;
#endif

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
        private Rect _globalStageBounds = new Rect();
        private Rect _globalDesktopBounds = new Rect();
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

        // [?醫됲뇣] ?遺얩닜????筌왖?癒?뱽 ?袁る립 癰궰??
        private DateTime _lastClickTime = DateTime.MinValue;
        private int _lastClickButton = -1;
        private int _clickCount = 1;

        // [?醫됲뇣] ?????곷섧???袁⑹삺 ?袁⑺뒄 ?곕뗄??癰궰??(Zoom ?얜챷????욧퍙??
        private double _currentClientX = -1;
        private double _currentClientY = -1;

        public MainWindow()
        {
            this.Title = "SharpKVM (v7.6)";
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
            this.SizeChanged += (s, e) => DrawVisualLayout();
            this.Closing += (s, e) => { StopServer(); StopClient(); CursorManager.Show(); CursorManager.Unlock(); SaveConfig(); };

            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) }; 
            _clipboardTimer.Tick += (s,e) => _ = Task.Run(() => CheckClipboard()); 
            _clipboardTimer.Start();
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
#if DEBUG
            _btnAddVirtualClient = new Button { Content = "Add Virtual Client" };
            _btnAddVirtualClient.Click += OnAddVirtualClientClicked;
            _cmbVirtualResolution = new ComboBox
            {
                Width = 140
            };
            _cmbVirtualResolution.SelectionChanged += OnVirtualResolutionChanged;
#endif
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
#if DEBUG
            srvHeader.Children.Add(_btnAddVirtualClient);
            srvHeader.Children.Add(new TextBlock { Text = "Virtual Resolution", VerticalAlignment = VerticalAlignment.Center });
            srvHeader.Children.Add(_cmbVirtualResolution);
#endif
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

#if DEBUG
            InitializeVirtualResolutionPresets();
#endif

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
            RecalculateViewport(includePlacedClients: true);

            _pnlStage.Children.Clear();
            _snapSlots.Clear();

            double thickness = 20;
            foreach(var s in _cachedScreens) {
                var uiRect = DesktopToStage(s.Bounds);
                double x = uiRect.X;
                double y = uiRect.Y;
                double w = uiRect.Width;
                double h = uiRect.Height;
                s.UIBounds = uiRect;
                var border = new Border { Width = w, Height = h, Background = Brushes.SteelBlue, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Child = new TextBlock { Text = $"{s.ID}\n{s.Bounds.Width}x{s.Bounds.Height}", Foreground=Brushes.White, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, FontSize=10 } };
                Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
                _pnlStage.Children.Add(border);
                _snapSlots.Add(new SnapSlot(s.ID + "_Left", new Rect(x - thickness, y, thickness, h), s.ID, "Left"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Right", new Rect(x + w, y, thickness, h), s.ID, "Right"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Top", new Rect(x, y - thickness, w, thickness), s.ID, "Top"));
                _snapSlots.Add(new SnapSlot(s.ID + "_Bottom", new Rect(x, y + h, w, thickness), s.ID, "Bottom"));
            }

            RedrawClientBoxes();
        }

        private void RecalculateViewport(bool includePlacedClients)
        {
            if (_pnlStage == null || _cachedScreens.Count == 0) return;

            double minX = _cachedScreens.Min(s => s.Bounds.X);
            double minY = _cachedScreens.Min(s => s.Bounds.Y);
            double maxX = _cachedScreens.Max(s => s.Bounds.Right);
            double maxY = _cachedScreens.Max(s => s.Bounds.Bottom);

            _globalDesktopBounds = new Rect(minX, minY, maxX - minX, maxY - minY);

            if (includePlacedClients)
            {
                foreach (var layout in _clientLayouts.Values)
                {
                    if (!layout.IsPlaced || layout.DesktopRect.Width <= 0 || layout.DesktopRect.Height <= 0) continue;
                    minX = Math.Min(minX, layout.DesktopRect.Left);
                    minY = Math.Min(minY, layout.DesktopRect.Top);
                    maxX = Math.Max(maxX, layout.DesktopRect.Right);
                    maxY = Math.Max(maxY, layout.DesktopRect.Bottom);
                }
            }

            double totalW = Math.Max(1, maxX - minX);
            double totalH = Math.Max(1, maxY - minY);
            double stageW = _pnlStage.Bounds.Width, stageH = _pnlStage.Bounds.Height;
            if(stageW <= 0) return;
            double padding = 40;
            double scale = Math.Min((stageW - padding*2)/totalW, (stageH - padding*2)/totalH);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) scale = 0.01;
            double offsetX = (stageW - totalW*scale)/2, offsetY = (stageH - totalH*scale)/2;

            _layoutMinX = minX;
            _layoutMinY = minY;
            _layoutScale = scale;
            _layoutOffsetX = offsetX;
            _layoutOffsetY = offsetY;
            _globalStageBounds = DesktopToStage(_globalDesktopBounds);
        }

        private Rect DesktopToStage(Rect desktopRect)
        {
            if (_layoutScale <= 0) return new Rect();
            return new Rect(
                (desktopRect.X - _layoutMinX) * _layoutScale + _layoutOffsetX,
                (desktopRect.Y - _layoutMinY) * _layoutScale + _layoutOffsetY,
                desktopRect.Width * _layoutScale,
                desktopRect.Height * _layoutScale
            );
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

                var stageRect = layout.DesktopRect.Width > 0 && layout.DesktopRect.Height > 0
                    ? DesktopToStage(layout.DesktopRect)
                    : layout.StageRect;
                layout.StageRect = stageRect;
                _clientLayouts[clientKey] = layout;

                box.Margin = new Thickness(0);
                box.Width = stageRect.Width;
                box.Height = stageRect.Height;
                Canvas.SetLeft(box, stageRect.X);
                Canvas.SetTop(box, stageRect.Y);
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

        private Rect ClampRectByAnchor(Rect rect, EdgeDirection anchorEdge)
        {
            if (anchorEdge == EdgeDirection.Left || anchorEdge == EdgeDirection.Right)
            {
                double y = Math.Clamp(rect.Y, _globalDesktopBounds.Top, Math.Max(_globalDesktopBounds.Top, _globalDesktopBounds.Bottom - rect.Height));
                return new Rect(rect.X, y, rect.Width, rect.Height);
            }

            if (anchorEdge == EdgeDirection.Top || anchorEdge == EdgeDirection.Bottom)
            {
                double x = Math.Clamp(rect.X, _globalDesktopBounds.Left, Math.Max(_globalDesktopBounds.Left, _globalDesktopBounds.Right - rect.Width));
                return new Rect(x, rect.Y, rect.Width, rect.Height);
            }

            return rect;
        }

        private double GetDesktopSnapThreshold()
        {
            double scale = Math.Max(0.0001, _layoutScale);
            return Math.Max(8, 14 / scale);
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

        private bool IsGlobalOuterEdgeDesktop(ScreenInfo screen, EdgeDirection edge)
        {
            const double tol = 1.0;
            if (edge == EdgeDirection.Left) return Math.Abs(screen.Bounds.Left - _globalDesktopBounds.Left) <= tol;
            if (edge == EdgeDirection.Right) return Math.Abs(screen.Bounds.Right - _globalDesktopBounds.Right) <= tol;
            if (edge == EdgeDirection.Top) return Math.Abs(screen.Bounds.Top - _globalDesktopBounds.Top) <= tol;
            if (edge == EdgeDirection.Bottom) return Math.Abs(screen.Bounds.Bottom - _globalDesktopBounds.Bottom) <= tol;
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
            Rect screenRect = screen.Bounds;

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
            return rect;
        }

        private (ScreenInfo Screen, EdgeDirection Edge, double Distance) FindNearestScreenEdge(Rect rect)
        {
            Point center = rect.Center;
            ScreenInfo bestScreen = _cachedScreens[0];
            EdgeDirection bestEdge = EdgeDirection.Right;
            double best = double.MaxValue;

            foreach (var screen in _cachedScreens)
            {
                Rect r = screen.Bounds;
                var candidates = new List<(EdgeDirection Edge, Point P)>();
                if (IsGlobalOuterEdgeDesktop(screen, EdgeDirection.Left))
                    candidates.Add((EdgeDirection.Left, new Point(r.Left, Math.Clamp(center.Y, r.Top, r.Bottom))));
                if (IsGlobalOuterEdgeDesktop(screen, EdgeDirection.Right))
                    candidates.Add((EdgeDirection.Right, new Point(r.Right, Math.Clamp(center.Y, r.Top, r.Bottom))));
                if (IsGlobalOuterEdgeDesktop(screen, EdgeDirection.Top))
                    candidates.Add((EdgeDirection.Top, new Point(Math.Clamp(center.X, r.Left, r.Right), r.Top)));
                if (IsGlobalOuterEdgeDesktop(screen, EdgeDirection.Bottom))
                    candidates.Add((EdgeDirection.Bottom, new Point(Math.Clamp(center.X, r.Left, r.Right), r.Bottom)));

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
            var bestRect = AttachToScreenEdge(rect, nearest.Screen, nearest.Edge);
            anchorScreenID = nearest.Screen.ID;
            anchorEdge = nearest.Edge switch
            {
                EdgeDirection.Left => EdgeDirection.Right,
                EdgeDirection.Right => EdgeDirection.Left,
                EdgeDirection.Top => EdgeDirection.Bottom,
                EdgeDirection.Bottom => EdgeDirection.Top,
                _ => EdgeDirection.None
            };

            var magnetic = ApplyMagneticSnap(ClampRectByAnchor(bestRect, anchorEdge), clientKey, GetDesktopSnapThreshold());
            if (anchorEdge == EdgeDirection.Left || anchorEdge == EdgeDirection.Right)
            {
                magnetic = new Rect(bestRect.X, magnetic.Y, magnetic.Width, magnetic.Height);
            }
            else if (anchorEdge == EdgeDirection.Top || anchorEdge == EdgeDirection.Bottom)
            {
                magnetic = new Rect(magnetic.X, bestRect.Y, magnetic.Width, magnetic.Height);
            }
            var resolved = ResolveFreeOverlap(ClampRectByAnchor(magnetic, anchorEdge), clientKey, anchorEdge);
            return ClampRectByAnchor(resolved, anchorEdge);
        }

        private Rect ApplyMagneticSnap(Rect rect, string movingClientKey, double threshold = 14)
        {
            double x = rect.X;
            double y = rect.Y;
            double bestDx = threshold + 1;
            double bestDy = threshold + 1;

            {
                var t = _globalDesktopBounds;
                var xCandidates = new[] { t.Left, t.Right - rect.Width, t.Left - rect.Width, t.Right };

                foreach (var cx in xCandidates)
                {
                    double dx = Math.Abs(cx - x);
                    if (dx < bestDx && dx <= threshold)
                    {
                        bestDx = dx;
                        x = cx;
                    }
                }

                var yCandidates = new[] { t.Top, t.Bottom - rect.Height, t.Top - rect.Height, t.Bottom };

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

            foreach (var kv in _clientLayouts)
            {
                if (kv.Key == movingClientKey || !kv.Value.IsPlaced) continue;
                var t = kv.Value.DesktopRect.Width > 0 && kv.Value.DesktopRect.Height > 0
                    ? kv.Value.DesktopRect
                    : StageToDesktop(kv.Value.StageRect);

                var xCandidates = new[] { t.Left, t.Right - rect.Width, t.Left - rect.Width, t.Right };
                foreach (var cx in xCandidates)
                {
                    double dx = Math.Abs(cx - x);
                    if (dx < bestDx && dx <= threshold)
                    {
                        bestDx = dx;
                        x = cx;
                    }
                }

                var yCandidates = new[] { t.Top, t.Bottom - rect.Height, t.Top - rect.Height, t.Bottom };
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

            return new Rect(x, y, rect.Width, rect.Height);
        }

        private static bool RectsOverlap(Rect a, Rect b, double gap = 0)
        {
            return a.Left < b.Right + gap &&
                   a.Right > b.Left - gap &&
                   a.Top < b.Bottom + gap &&
                   a.Bottom > b.Top - gap;
        }

        private Rect ResolveFreeOverlap(Rect rect, string movingClientKey, EdgeDirection anchorEdge, double gap = 6)
        {
            var candidate = rect;

            for (int i = 0; i < 40; i++)
            {
                Rect? overlapped = null;
                foreach (var kv in _clientLayouts)
                {
                    if (kv.Key == movingClientKey || !kv.Value.IsPlaced) continue;
                    var other = kv.Value.DesktopRect.Width > 0 && kv.Value.DesktopRect.Height > 0
                        ? kv.Value.DesktopRect
                        : StageToDesktop(kv.Value.StageRect);

                    if (RectsOverlap(candidate, other, -gap))
                    {
                        overlapped = other;
                        break;
                    }
                }

                if (overlapped == null) return candidate;

                var t = overlapped.Value;
                double moveLeft = (t.Left - gap) - candidate.Right;
                double moveRight = (t.Right + gap) - candidate.Left;
                double moveUp = (t.Top - gap) - candidate.Bottom;
                double moveDown = (t.Bottom + gap) - candidate.Top;

                if (anchorEdge == EdgeDirection.Left || anchorEdge == EdgeDirection.Right)
                {
                    // Preserve anchored X, move only on Y.
                    candidate = Math.Abs(moveUp) <= Math.Abs(moveDown)
                        ? candidate.WithY(candidate.Y + moveUp)
                        : candidate.WithY(candidate.Y + moveDown);
                }
                else if (anchorEdge == EdgeDirection.Top || anchorEdge == EdgeDirection.Bottom)
                {
                    // Preserve anchored Y, move only on X.
                    candidate = Math.Abs(moveLeft) <= Math.Abs(moveRight)
                        ? candidate.WithX(candidate.X + moveLeft)
                        : candidate.WithX(candidate.X + moveRight);
                }
                else
                {
                    var options = new[] { moveLeft, moveRight, moveUp, moveDown };
                    double best = options.OrderBy(Math.Abs).First();
                    if (best == moveLeft || best == moveRight) candidate = candidate.WithX(candidate.X + best);
                    else candidate = candidate.WithY(candidate.Y + best);
                }

                candidate = ClampRectByAnchor(candidate, anchorEdge);
            }

            return candidate;
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
                    var current = layout.DesktopRect.Width > 0 && layout.DesktopRect.Height > 0
                        ? layout.DesktopRect
                        : StageToDesktop(layout.StageRect);
                    var centered = new Rect(
                        current.Center.X - (client.Width / 2.0),
                        current.Center.Y - (client.Height / 2.0),
                        Math.Max(1, client.Width),
                        Math.Max(1, client.Height)
                    );
                    layout.DesktopRect = GetAnchoredFreeRect(centered, key, out var anchorScreenID, out var anchorEdge);
                    layout.StageRect = DesktopToStage(layout.DesktopRect);
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
                        var snapDesktop = StageToDesktop(GetSnapRect(slot, (double)client.Width / client.Height));
                        layout.DesktopRect = snapDesktop;
                        layout.StageRect = DesktopToStage(snapDesktop);
                        layout.IsSnapped = true;
                        layout.SnapAnchorID = slot.ID;
                        layout.AnchorScreenID = slot.ParentID;
                        layout.AnchorEdge = slot.Direction switch
                        {
                            "Left" => EdgeDirection.Right,
                            "Right" => EdgeDirection.Left,
                            "Top" => EdgeDirection.Bottom,
                            "Bottom" => EdgeDirection.Top,
                            _ => EdgeDirection.None
                        };
                    }
                }

                _clientLayouts[key] = layout;
            }

            DrawVisualLayout();
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
                        var snappedDesktop = StageToDesktop(snappedRect);
                        box.Width = snappedRect.Width;
                        box.Height = snappedRect.Height;
                        Canvas.SetLeft(box, snappedRect.X);
                        Canvas.SetTop(box, snappedRect.Y);
                        box.Background = Brushes.Green;

                        _clientLayouts[clientKey] = new ClientLayout
                        {
                            ClientKey = clientKey,
                            StageRect = snappedRect,
                            DesktopRect = snappedDesktop,
                            IsPlaced = true,
                            IsSnapped = true,
                            SnapAnchorID = slot.ID,
                            AnchorScreenID = slot.ParentID,
                            AnchorEdge = slot.Direction switch
                            {
                                "Left" => EdgeDirection.Right,
                                "Right" => EdgeDirection.Left,
                                "Top" => EdgeDirection.Bottom,
                                "Bottom" => EdgeDirection.Top,
                                _ => EdgeDirection.None
                            }
                        };
                        DrawVisualLayout();
                        Log($"Mapped {client.Name} to {slot.ID}");
                        SaveClientConfigs();
                    } else {
                        if (_layoutMode == LayoutMode.Free)
                        {
                            double left = Canvas.GetLeft(box);
                            double top = Canvas.GetTop(box);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;

                            var desktopRect = StageToDesktop(new Rect(left, top, box.Width, box.Height));
                            var freeRect = GetAnchoredFreeRect(desktopRect, clientKey, out var anchorScreenID, out var anchorEdge);
                            var freeStageRect = DesktopToStage(freeRect);
                            Canvas.SetLeft(box, freeStageRect.X);
                            Canvas.SetTop(box, freeStageRect.Y);
                            box.Width = freeStageRect.Width;
                            box.Height = freeStageRect.Height;
                            box.Background = Brushes.Green;

                            _clientLayouts[clientKey] = new ClientLayout
                            {
                                ClientKey = clientKey,
                                StageRect = freeStageRect,
                                DesktopRect = freeRect,
                                IsPlaced = true,
                                IsSnapped = false,
                                SnapAnchorID = "",
                                AnchorScreenID = anchorScreenID,
                                AnchorEdge = anchorEdge
                            };
                            DrawVisualLayout();
                            SaveClientConfigs();
                        }
                        else
                        {
                            box.Width = BASE_BOX_HEIGHT * ratio; box.Height = BASE_BOX_HEIGHT;
                            _pnlStage.Children.Remove(box); _pnlDock.Children.Add(box);
                            box.Margin = new Thickness(5);
                            box.Background = Brushes.Orange;
                            if (_clientLayouts.ContainsKey(clientKey)) _clientLayouts.Remove(clientKey);
                            DrawVisualLayout();
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
                    Rect desktopRect = (config.DesktopWidth > 0 && config.DesktopHeight > 0)
                        ? new Rect(config.DesktopX, config.DesktopY, config.DesktopWidth, config.DesktopHeight)
                        : StageToDesktop(new Rect(config.X, config.Y, config.Width, config.Height));
                    _clientLayouts[ip] = new ClientLayout
                    {
                        ClientKey = ip,
                        DesktopRect = desktopRect,
                        StageRect = DesktopToStage(desktopRect),
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
                
                // [?醫됲뇣] ??彛??袁⑸꽊 ??뽰삂
                _mouseSenderCts = new CancellationTokenSource();
                Task.Run(() => StartMouseSenderLoop(_mouseSenderCts.Token));

                // [??륁젟] ?????곷섧??癰귣떯而???볤탢嚥??紐낅퉸 ?온??Task ??볤탢??

                Task.Run(() => _hook.Run());
                AcceptClients();
            } catch(Exception ex) { Log("Start Error: " + ex.Message); }
        }

        private void StopServer() {
            _isServerRunning = false;
            StopVirtualClientForDebug();
            
            // [?醫됲뇣] ?袁⑸꽊 ?룐뫂遊??類?
            _mouseSenderCts?.Cancel();
            _mouseSenderCts = null;
            
            // [??륁젟] 癰귣떯而???볤탢嚥??온??嚥≪뮇彛?????

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

        // [?醫됲뇣] 筌띾뜆????ル슦紐??袁⑸꽊 ?袁⑹뒠 ?룐뫂遊?(120Hz = ~8ms)
        private async Task StartMouseSenderLoop(CancellationToken token)
        {
            try 
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isRemoteActive && _activeRemoteClient != null && _hasPendingMouse)
                    {
                        // ????곸뵠 ?癒?쁽?怨몄몵嚥???꾨┛ ??뺣즲 (int??atomic)
                        int x = _pendingMouseX;
                        int y = _pendingMouseY;
                        
                        // ??? 癰귣?沅??ル슦紐댐쭖???쎄땁
                        _hasPendingMouse = false; 

                        // [v6.4] ??뺤삋域?餓λ쵐肉?????땅 ?醫롫뼄 獄쎻뫗????袁る퉸 筌앸맩???袁⑸꽊???醫듼봺??????됱몵?? 
                        // ?袁⑹삺 ?룐뫂遊썲첎? 5ms嚥??겸뫖?????쥓?ㅸ첋?嚥???곕뼊 ?醫?.
                        // ??살춸, MouseDown/Up??筌앸맩???袁⑸꽊???嚥???뽮퐣 ????獄쎻뫗????袁る퉸 
                        // ??뺤삋域???뽰삂/?ル굝利???뽰젎????뺣뎃???ル슦紐닷첎? ??덈뼄筌??믪눘? 癰귣?沅??嚥≪뮇彛???袁⑹뒄??????됱벉.
                        
                        // ??쇱젫 ?袁⑸꽊
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
                    // [??륁젟] NoDelay ??쇱젟
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
                                DrawVisualLayout();
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

        // [癰귣벀?? 筌롫뗄苑???곕떽?
        private void OnHookMousePressed(object? sender, MouseHookEventArgs e) { 
            if (e.Data.Button == SharpHook.Native.MouseButton.Button1) _isLeftDragging = true; 
            if (_isRemoteActive && _activeRemoteClient != null) { 
                // [v6.5] ?遺얩닜????嚥≪뮇彛??곕떽?
                var now = DateTime.Now;
                if (_lastClickButton == (int)e.Data.Button && (now - _lastClickTime).TotalMilliseconds < 500) {
                    _clickCount++;
                } else {
                    _clickCount = 1;
                }
                _lastClickTime = now;
                _lastClickButton = (int)e.Data.Button;

                // [v6.4] ????????뺣뎃????猷??ル슦紐닷첎? ??덈뼄筌?筌앸맩???袁⑸꽊??뤿연 ??뽮퐣 癰귣똻??
                if (_hasPendingMouse) {
                    _hasPendingMouse = false;
                    _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = _pendingMouseX, Y = _pendingMouseY });
                }

                _activeRemoteClient.SendPacketAsync(new InputPacket { 
                    Type = PacketType.MouseDown, 
                    KeyCode = (int)e.Data.Button,
                    X = (int)_virtualX, // [?醫됲뇣] ??????뽰젎 ?ル슦紐???녿┛??
                    Y = (int)_virtualY,
                    ClickCount = _clickCount
                }); 
                e.SuppressEvent = true; 
            } 
        }

        // [癰귣벀?? 筌롫뗄苑???곕떽?
        private void OnHookMouseReleased(object? sender, MouseHookEventArgs e) { 
            if (e.Data.Button == SharpHook.Native.MouseButton.Button1) _isLeftDragging = false; 
            if (_isRemoteActive && _activeRemoteClient != null) { 
                // [v6.4] ??????곸젫 ????뺣뎃????猷??ル슦紐닷첎? ??덈뼄筌?筌앸맩???袁⑸꽊
                if (_hasPendingMouse) {
                    _hasPendingMouse = false;
                    _activeRemoteClient.SendPacketAsync(new InputPacket { Type = PacketType.MouseMove, X = _pendingMouseX, Y = _pendingMouseY });
                }

                _activeRemoteClient.SendPacketAsync(new InputPacket { 
                    Type = PacketType.MouseUp, 
                    KeyCode = (int)e.Data.Button,
                    X = (int)_virtualX, // [?醫됲뇣] ??????뽰젎 ?ル슦紐???녿┛??
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
                if (localExitEdge != OppositeEdge(expectedEntryEdge)) continue;
                if (!string.IsNullOrEmpty(layout.AnchorScreenID) &&
                    (expectedEntryEdge == EdgeDirection.Left || expectedEntryEdge == EdgeDirection.Right) &&
                    layout.AnchorScreenID != rootScreen.ID) continue;

                var desktopRect = layout.DesktopRect.Width > 0 && layout.DesktopRect.Height > 0
                    ? layout.DesktopRect
                    : StageToDesktop(layout.StageRect);
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

            Rect activeRect = activeLayout.DesktopRect.Width > 0 && activeLayout.DesktopRect.Height > 0
                ? activeLayout.DesktopRect
                : StageToDesktop(activeLayout.StageRect);
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

                var desktopRect = layout.DesktopRect.Width > 0 && layout.DesktopRect.Height > 0
                    ? layout.DesktopRect
                    : StageToDesktop(layout.StageRect);
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

            // Return through the boundary that is actually connected to local.
            // Using exitEdge directly can place cursor on the opposite side.
            var localEntryEdge = _activeEntryEdge != EdgeDirection.None
                ? OppositeEdge(_activeEntryEdge)
                : exitEdge;

            // Free layout: use absolute edge mapping between remote desktop rect and local root screen,
            // so returning point aligns with the touching boundary instead of snap-like ratios.
            if (_layoutMode == LayoutMode.Free && _activeRemoteClient != null)
            {
                string activeKey = GetClientKey(_activeRemoteClient);
                if (_clientLayouts.TryGetValue(activeKey, out var activeLayout) && activeLayout.IsPlaced)
                {
                    Rect activeRect = activeLayout.DesktopRect.Width > 0 && activeLayout.DesktopRect.Height > 0
                        ? activeLayout.DesktopRect
                        : StageToDesktop(activeLayout.StageRect);

                    Point exitPoint = localEntryEdge switch
                    {
                        EdgeDirection.Left or EdgeDirection.Right => new Point(
                            localEntryEdge == EdgeDirection.Left ? activeRect.Left : activeRect.Right,
                            activeRect.Top + activeRect.Height * ratioY),
                        EdgeDirection.Top or EdgeDirection.Bottom => new Point(
                            activeRect.Left + activeRect.Width * ratioX,
                            localEntryEdge == EdgeDirection.Top ? activeRect.Top : activeRect.Bottom),
                        _ => activeRect.Center
                    };

                    if (localEntryEdge == EdgeDirection.Left)
                    {
                        targetX = rootScreen.Bounds.Left + buffer;
                        targetY = Math.Clamp(exitPoint.Y, rootScreen.Bounds.Top + buffer, rootScreen.Bounds.Bottom - buffer);
                    }
                    else if (localEntryEdge == EdgeDirection.Right)
                    {
                        targetX = rootScreen.Bounds.Right - buffer;
                        targetY = Math.Clamp(exitPoint.Y, rootScreen.Bounds.Top + buffer, rootScreen.Bounds.Bottom - buffer);
                    }
                    else if (localEntryEdge == EdgeDirection.Top)
                    {
                        targetY = rootScreen.Bounds.Top + buffer;
                        targetX = Math.Clamp(exitPoint.X, rootScreen.Bounds.Left + buffer, rootScreen.Bounds.Right - buffer);
                    }
                    else if (localEntryEdge == EdgeDirection.Bottom)
                    {
                        targetY = rootScreen.Bounds.Bottom - buffer;
                        targetX = Math.Clamp(exitPoint.X, rootScreen.Bounds.Left + buffer, rootScreen.Bounds.Right - buffer);
                    }
                }
                else
                {
                    if (localEntryEdge == EdgeDirection.Left) targetX = rootScreen.Bounds.Left + buffer;
                    else if (localEntryEdge == EdgeDirection.Right) targetX = rootScreen.Bounds.Right - buffer;
                    else if (localEntryEdge == EdgeDirection.Top) targetY = rootScreen.Bounds.Top + buffer;
                    else if (localEntryEdge == EdgeDirection.Bottom) targetY = rootScreen.Bounds.Bottom - buffer;
                }
            }
            else
            {
                // Snap layout: keep existing ratio-based behavior with edge anchoring.
                if (localEntryEdge == EdgeDirection.Left) targetX = rootScreen.Bounds.Left + buffer;
                else if (localEntryEdge == EdgeDirection.Right) targetX = rootScreen.Bounds.Right - buffer;
                else if (localEntryEdge == EdgeDirection.Top) targetY = rootScreen.Bounds.Top + buffer;
                else if (localEntryEdge == EdgeDirection.Bottom) targetY = rootScreen.Bounds.Bottom - buffer;
            }

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
                    if (!IsGlobalOuterEdgeDesktop(currentScreen, exitEdge))
                    {
                        return;
                    }
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
                    // Return to local only through the edge that is connected to local.
                    // Other edges should stay on current remote client.
                    bool canReturnToLocal = _activeEntryEdge == EdgeDirection.None || exit == _activeEntryEdge;
                    if (canReturnToLocal)
                    {
                        ReturnToLocal(exit);
                    }
                    else
                    {
                        _virtualX = Math.Clamp(_virtualX, 0, Math.Max(0, clientW - 1));
                        _virtualY = Math.Clamp(_virtualY, 0, Math.Max(0, clientH - 1));

                        _pendingMouseX = (int)_virtualX;
                        _pendingMouseY = (int)_virtualY;
                        _hasPendingMouse = true;

                        _ignoreNextMove = true;
                        _skipMoveCount = dragging ? 0 : 2;
                        _simulator?.SimulateMouseMovement((short)centerX, (short)centerY);
                        CursorManager.LockToRect(new Rect(centerX, centerY, 1, 1));
                        CursorManager.Hide();
                    }
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
                    // ??덈즲????뺤쒔) -> 筌????? ??????ν뀧??筌띲끋釉?(v6.2 ??륁젟)
                    // Ctrl -> Ctrl (??덉뵬)
                    // Win -> Opt (VcLeftMeta -> VcLeftAlt)
                    // Alt -> Cmd (VcLeftAlt -> VcLeftMeta)
                    code = MacInputMapping.MapKeyCodeForMacRemote(code);
                }

                // [v6.6] Caps Lock ?紐꾨선 癰궰野?筌왖?? 
                // 筌띘쇰퓠??뺣뮉 Caps Lock??筌욁룓苡????쑎 ?紐꾨선 癰궰野껋럩????롫뮉 疫꿸퀡?????됱벉.
                // ??덈즲????뺤쒔?癒?퐣 Caps Lock?????죬??????? ?癒?봄筌왖嚥??類μ넇???袁⑤뼎??롫즲嚥???

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
                    code = MacInputMapping.MapKeyCodeForMacRemote(code);
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
                                byte[] raw = InputPacketSerializer.Serialize(hello);
                                await stream.WriteAsync(raw, 0, raw.Length);

                                // [?醫됲뇣] ???삸???類ｋ궖 ?袁⑸꽊
                                int platformCode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 1 : 0;
                                var platformPacket = new InputPacket { Type = PacketType.PlatformInfo, KeyCode = platformCode };
                                byte[] pRaw = InputPacketSerializer.Serialize(platformPacket);
                                await stream.WriteAsync(pRaw, 0, pRaw.Length);
                            }
                            int packetSize = Marshal.SizeOf<InputPacket>();
                            var buffer = new byte[packetSize];
                            while (_isClientRunning) {
                                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) throw new Exception("Disconnected"); 
                                if (!InputPacketSerializer.TryDeserialize(buffer, out InputPacket p)) continue;
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
                                     SimulateInput(p); // [??륁젟] MainWindow 筌롫뗄苑???紐꾪뀱
                                }
                            }
                        }
                    } catch (Exception ex) { if (!_isClientRunning) break; Dispatcher.UIThread.Post(() => Log($"Disconnected. Retry 3s.. ({ex.Message})")); await Task.Delay(3000); }
                }
            });
        }

        // [癰귣벀?? 筌롫뗄苑???곕떽?
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
                         // [??륁젟] 筌앸맩????猷?獄??袁⑹삺 ?袁⑺뒄 ??낅쑓??꾨뱜
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                            if (_isLeftDragging) {
                                // [v6.4] 筌띘쇰퓠????뺤삋域?餓λ쵐?????뮉 MouseDragged ??源?紐? 筌욊낯??獄쏆뮇源??쀪땀
                                CursorManager.SendMacRawDrag(p.X, p.Y, 0); // 0 = LeftButton
                            } else {
                                // [v6.7] 筌?Zoom ??띻펾?癒?퐣 Warp ???癒곕늄 ?袁⑷맒??筌띾맦由??袁る퉸 Raw Move ????
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
                        // [??륁젟] 筌?Zoom ??띻펾 ???? SimulateMouseMovement + SimulateMousePress 鈺곌퀬鍮?? ?됯퀬猷?硫? ?????袁⑷맒????됱벉.
                        // 筌욊낯??CGEvent????밴쉐??뤿연 ?ル슦紐?? ???????덈뻻??癰귣?沅∽쭖?Zoom ?怨밴묶?癒?퐣???類μ넇???袁⑺뒄???????
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
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && code == KeyCode.VcCapsLock) {
                            CursorManager.SendMacRawKey(CursorManager.MacCapsLockKeyCode, p.Type == PacketType.KeyDown);
                            return;
                        }
                        // [??륁젟] ??? ??뺤쒔?癒?퐣 OS??筌띿쉳苡????꾨뗀諭띄몴?癰궰??묐퉸??癰귣?沅▽틠??嚥??????곷섧?紐껊뮉 筌ㅼ뮇???뽰벥 筌ｌ꼶?곻쭕???묐뻬
                        // (?? ?????곷섧?硫? 筌띘쇱뵬 ??Mission Control ?紐꺿봺椰?嚥≪뮇彛?? ?醫?)
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
    
}
