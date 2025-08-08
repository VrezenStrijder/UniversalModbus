using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI;
using SukiUI.Models;
using UMClient.Controls;
using UMClient.Models;
using UMClient.Services;

namespace UMClient.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        #region 依赖服务和系统变量

        // 依赖服务
        private readonly QueryPortService queryPortService;
        private readonly ConfigurationService configService;
        private readonly AutoSendService autoSendService;

        private readonly SerialPortService serialPortService;
        private TcpClientService? tcpClientService;
        private TcpServerService? tcpServerService;
        private UdpClientService? udpClientService;
        private UdpServerService? udpServerService;

        // 自动发送定时器
        private Timer? autoSendTimer;

        // 从导入文件自动定时发送
        private List<string>? pendingSendLines;
        private int currentSendLineIndex;
        private DispatcherTimer? autoSendLinesTimer; // 使用DispatcherTimer, 以保证在UI线程中执行
        // 循环发送
        private DispatcherTimer? cycleSendTimer;

        #endregion

        #region 连接模式

        public bool IsSerialPortMode => SelectedConnectionMode == ConnectionMode.SerialPort;
        public bool IsTcpClientMode => SelectedConnectionMode == ConnectionMode.TcpClient;
        public bool IsTcpServerMode => SelectedConnectionMode == ConnectionMode.TcpServer;
        public bool IsUdpClientMode => SelectedConnectionMode == ConnectionMode.UdpClient;
        public bool IsUdpServerMode => SelectedConnectionMode == ConnectionMode.UdpServer;

        public ObservableCollection<ConnectionModeOption> ConnectionModeOptions { get; } = new ObservableCollection<ConnectionModeOption>()
        {
            new ConnectionModeOption() { Display = "串口", Value = ConnectionMode.SerialPort },
            new ConnectionModeOption() { Display = "TCP服务器", Value = ConnectionMode.TcpServer },
            new ConnectionModeOption() { Display = "TCP客户端", Value = ConnectionMode.TcpClient },
            new ConnectionModeOption() { Display = "UDP服务器", Value = ConnectionMode.UdpServer },
            new ConnectionModeOption() { Display = "UDP客户端", Value = ConnectionMode.UdpClient }
        };

        #endregion

        #region 主题颜色

        private readonly SukiTheme theme = SukiTheme.GetInstance();

        public IAvaloniaReadOnlyList<SukiColorTheme> AvailableColors { get; }

        #endregion

        #region 构造函数和初始化配置

        public MainWindowViewModel()
        {
            queryPortService = new QueryPortService();
            configService = new ConfigurationService();
            serialPortService = new SerialPortService();
            autoSendService = new AutoSendService();

            serialPortService.DataReceived += OnDataReceived;
            serialPortService.StatusChanged += OnStatusChanged;

            AvailableColors = theme.ColorThemes;

            InitializeDefaults();
            LoadConfiguration();
            LoadSendTemplates();
            RefreshPorts();

            SelectedTheme = AvailableColors?.First();
        }

        private void InitializeDefaults()
        {
            // 设置默认的串口配置
            SelectedBaudRate = 115200;
            SelectedParity = "None";
            SelectedDataBits = 8;
            SelectedStopBits = "One";
            SelectedHandshake = "None";
            ShowTimestamp = true;
            AutoWrap = true;
            AutoScroll = true;
            SendNewLine = true;

            SelectedConnectionModeOption = ConnectionModeOptions.First(x => x.Value == ConnectionMode.SerialPort);

            // Linux环境下执行额外初始化
            if (OperatingSystem.IsLinux())
            {
                InitializeLinuxSpecific();
            }

        }

        /// <summary>
        /// 初始化Linux环境
        /// </summary>
        private void InitializeLinuxSpecific()
        {
            try
            {
                // 设置Linux下的默认串口
                if (File.Exists("/dev/ttyUSB0"))
                {
                    SelectedPortName = "/dev/ttyUSB0";
                }
                else if (File.Exists("/dev/ttyACM0"))
                {
                    SelectedPortName = "/dev/ttyACM0";
                }
                else if (File.Exists("/dev/ttyS0"))
                {
                    SelectedPortName = "/dev/ttyS0";
                }

                StatusText = "Linux环境已初始化";
            }
            catch (Exception ex)
            {
                StatusText = $"Linux初始化警告: {ex.Message}";
            }
        }

        private void LoadSendTemplates()
        {
            var templates = configService.LoadSendTemplates();
            SendTemplates.Clear();
            foreach (var template in templates)
            {
                SendTemplates.Add(template);
            }
        }

        #endregion

        #region 主题设置

        [ObservableProperty]
        private bool isDarkMode = false;

        [ObservableProperty]
        private SukiColorTheme? selectedTheme;

        [RelayCommand]
        private void SetDarkMode(bool value)
        {
            theme.ChangeBaseTheme(value ? ThemeVariant.Dark : ThemeVariant.Light);
            IsDarkMode = value;
        }

        [RelayCommand]
        private void SetThemeColor(SukiColorTheme colorTheme)
        {
            theme.ChangeColorTheme(colorTheme);
            SelectedTheme = colorTheme;
        }

        #endregion

        #region 通用属性

        [ObservableProperty]
        private string title = "串口调试工具";

        [ObservableProperty]
        private ConnectionModeOption? selectedConnectionModeOption;

        [ObservableProperty]
        private ConnectionMode selectedConnectionMode = ConnectionMode.SerialPort;

        [ObservableProperty]
        private string statusText = "就绪";

        [ObservableProperty]
        private bool isConnected = false;

        [ObservableProperty]
        private ObservableCollection<ColoredTextItem> receivedMessages = new ObservableCollection<ColoredTextItem>();

        [ObservableProperty]
        private string sendData = string.Empty;

        [ObservableProperty]
        private bool isHexReceiveMode = false;

        [ObservableProperty]
        private bool isHexSendMode = false;

        [ObservableProperty]
        private bool isAutoSendEnabled = false;

        [ObservableProperty]
        private int autoSendInterval = 1000;

        [ObservableProperty]
        private bool isCycleSend = false;

        [ObservableProperty]
        private int cycleSendCount = 1; // 循环发送次数,0表示无限循环

        [ObservableProperty]
        private int currentCycleIndex = 0; // 当前循环次数

        [ObservableProperty]
        private bool isCycleSendRunning = false; // 是否正在循环发送

        [ObservableProperty]
        private bool sendNewLine = true;

        [ObservableProperty]
        private bool autoWrap = true;

        [ObservableProperty]
        private bool showTimestamp = true;

        [ObservableProperty]
        private bool autoScroll = true;

        [ObservableProperty]
        private bool saveLog = false;

        [ObservableProperty]
        private bool isDisplayPaused = false;

        [ObservableProperty]
        private int receivedCount = 0;

        [ObservableProperty]
        private int sentCount = 0;

        [ObservableProperty]
        private string? selectedTemplate;

        [ObservableProperty]
        private string? selectedHistoryItem;

        public ObservableCollection<string> SendTemplates { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> SendHistory { get; } = new ObservableCollection<string>();

        #endregion

        #region 串口配置属性

        [ObservableProperty]
        private string selectedPortName = "COM1";

        [ObservableProperty]
        private int selectedBaudRate = 115200;

        [ObservableProperty]
        private string selectedParity = "None";

        [ObservableProperty]
        private int selectedDataBits = 8;

        [ObservableProperty]
        private string selectedStopBits = "One";

        [ObservableProperty]
        private string selectedHandshake = "None";

        [ObservableProperty]
        private SerialPortInfo? selectedPortInfo;

        public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = new ObservableCollection<SerialPortInfo>();

        public ObservableCollection<string> ParityOptions { get; } = new ObservableCollection<string>()
        {
            "None", "Odd", "Even", "Mark", "Space"
        };

        public ObservableCollection<int> DataBitsOptions { get; } = new ObservableCollection<int>() { 5, 6, 7, 8 };

        public ObservableCollection<string> StopBitsOptions { get; } = new ObservableCollection<string>()
        {
            "One", "OnePointFive", "Two"
        };

        public ObservableCollection<string> HandshakeOptions { get; } = new ObservableCollection<string>()
        {
            "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff"
        };

        #endregion

        #region Tcp配置属性

        [ObservableProperty]
        private string tcpServerAddress = "127.0.0.1";

        [ObservableProperty]
        private int tcpServerPort = 9010;

        [ObservableProperty]
        private string tcpListenAddress = "0.0.0.0";

        [ObservableProperty]
        private int tcpListenPort = 9010;

        [ObservableProperty]
        private int tcpConnectTimeout = 5000;

        [ObservableProperty]
        private int tcpMaxClients = 10;

        #endregion

        #region Udp配置属性

        [ObservableProperty]
        private string udpServerAddress = "127.0.0.1";

        [ObservableProperty]
        private int udpServerPort = 9060;

        [ObservableProperty]
        private int udpLocalPort = 0; // 0表示自动分配

        [ObservableProperty]
        private string udpListenAddress = "0.0.0.0";

        [ObservableProperty]
        private int udpListenPort = 9060;

        [ObservableProperty]
        private int udpReceiveTimeout = 5000;

        [ObservableProperty]
        private int udpReceiveBufferSize = 4096;

        [ObservableProperty]
        private bool udpBroadcastMode = false; // UDP服务器是否使用广播模式

        public ObservableCollection<IPEndPoint> UdpKnownClients { get; } = new ObservableCollection<IPEndPoint>();

        #endregion

        #region 命令

        [RelayCommand]
        private async Task ConnectAsync()       //匹配 ConnectCommand 
        {
            try
            {
                if (IsConnected)
                {
                    // 断开连接
                    await DisconnectCurrentService();
                    //StatusText = "已断开连接";
                }
                else
                {
                    switch (SelectedConnectionMode)
                    {
                        case ConnectionMode.SerialPort:
                            await ConnectSerialPortAsync();
                            break;
                        case ConnectionMode.TcpServer:
                            await StartTcpServerAsync();
                            break;
                        case ConnectionMode.TcpClient:
                            await ConnectTcpClientAsync();
                            break;
                        case ConnectionMode.UdpServer:
                            await StartUdpServerAsync();
                            break;
                        case ConnectionMode.UdpClient:
                            await ConnectUdpClientAsync();
                            break;
                    }

                }
            }
            catch (Exception ex)
            {
                StatusText = $"连接错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SendDataAsync()
        {
            // 如果正在进行文件自动发送,先停止
            if (autoSendLinesTimer != null)
            {
                StopAutoSendLines();
                StatusText = "已停止文件自动发送";
                return;
            }

            if (!IsConnected || string.IsNullOrEmpty(SendData))
            {
                return;
            }

            // 如果启用了循环发送且不是正在循环中, 启动循环发送
            if (IsCycleSend && !IsCycleSendRunning)
            {
                StartCycleSend();
                return;
            }

            // 执行单次发送
            await ExecuteSingleSend();
        }

        [RelayCommand]
        private void SetBaudRate(string baudRate)
        {
            if (int.TryParse(baudRate, out int rate))
            {
                SelectedBaudRate = rate;
            }
        }

        [RelayCommand]
        private void SetTcpAddress(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                TcpListenAddress = address;
            }
        }

        [RelayCommand]
        private void SetUdpAddress(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                UdpListenAddress = address;
            }
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();

            try
            {
                List<SerialPortInfo> ports = new List<SerialPortInfo>();

                if (OperatingSystem.IsWindows())
                {
                    var windowsPorts = queryPortService.GetWindowsSerialPortInfo();
                    ports.AddRange(windowsPorts);
                }
                else if (OperatingSystem.IsLinux())
                {
                    ports.AddRange(queryPortService.GetLinuxSerialPortInfo());
                }
                else if (OperatingSystem.IsMacOS())
                {
                    ports.AddRange(queryPortService.GetMacOSSerialPortInfo());
                }

                if (ports.Count == 0)
                {
                    StatusText = "未检测到可用串口";
                    return;
                }

                foreach (var port in ports.OrderBy(p => p))
                {
                    AvailablePorts.Add(port);
                }

                // 恢复之前选中的端口
                var previousSelection = AvailablePorts.FirstOrDefault(p => p.PortName == SelectedPortName);
                if (previousSelection != null)
                {
                    SelectedPortInfo = previousSelection;
                }
                else if (AvailablePorts.Count > 0)
                {
                    SelectedPortInfo = AvailablePorts[0];
                }

                // 如果当前选择的串口不在列表中，选择第一个可用的
                //if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(SelectedPortName))
                //{
                //    SelectedPortName = AvailablePorts[0];
                //}

                StatusText = $"检测到 {AvailablePorts.Count} 个可用串口";
            }
            catch (Exception ex)
            {
                StatusText = $"枚举串口失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ToggleCycleSend()
        {
            if (IsCycleSendRunning)
            {
                StopCycleSend();
            }
            else
            {
                StartCycleSend();
            }
        }

        [RelayCommand]
        private void ClearUdpClients()
        {
            if (udpServerService != null)
            {
                udpServerService.ClearKnownClients();
                UdpKnownClients.Clear();
                StatusText = "已清空UDP客户端列表";
            }
        }

        [RelayCommand]
        private async Task SendToSpecificUdpClientAsync(IPEndPoint? clientEndPoint)
        {
            if (clientEndPoint != null && udpServerService != null && !string.IsNullOrEmpty(SendData))
            {
                try
                {
                    byte[] dataToSend;
                    if (IsHexSendMode)
                    {
                        dataToSend = ConvertHexStringToBytes(SendData);
                    }
                    else
                    {
                        var text = SendData;
                        if (SendNewLine)
                        {
                            text += Environment.NewLine;
                        }
                        dataToSend = Encoding.UTF8.GetBytes(text);
                    }

                    await udpServerService.SendDataToClientAsync(dataToSend, clientEndPoint);
                    StatusText = $"已发送数据到 {clientEndPoint}";
                }
                catch (Exception ex)
                {
                    StatusText = $"发送到指定客户端失败: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private async Task SendUdpBroadcastAsync()
        {
            if (SelectedConnectionMode == ConnectionMode.UdpServer && udpServerService != null)
            {
                if (!string.IsNullOrEmpty(SendData))
                {
                    try
                    {
                        byte[] dataToSend;
                        if (IsHexSendMode)
                        {
                            dataToSend = ConvertHexStringToBytes(SendData);
                        }
                        else
                        {
                            var text = SendData;
                            if (SendNewLine)
                            {
                                text += Environment.NewLine;
                            }
                            dataToSend = Encoding.UTF8.GetBytes(text);
                        }

                        await udpServerService.BroadcastDataAsync(dataToSend, UdpListenPort);
                        StatusText = "UDP广播数据已发送";
                    }
                    catch (Exception ex)
                    {
                        StatusText = $"UDP广播失败: {ex.Message}";
                    }
                }
                else
                {
                    StatusText = "请输入要广播的数据";
                }
            }
        }

        [RelayCommand]
        private void ClearReceiveData()
        {
            ReceivedMessages.Clear();
            ReceivedCount = 0;
            StatusText = "接收区已清空";
        }

        [RelayCommand]
        private void ClearSendData()
        {
            SendData = string.Empty;
            StatusText = "发送区已清空";
        }

        [RelayCommand]
        private void ClearCounter()
        {
            ReceivedCount = 0;
            SentCount = 0;
            StatusText = "计数器已清零";
        }

        [RelayCommand]
        private async Task CopyReceiveDataAsync()
        {
            if (ReceivedMessages.Count > 0)
            {
                try
                {
                    var clipboard = GetClipboard();
                    if (clipboard != null)
                    {
                        string data = ReceivedMessages.Select(t => t.Text).Aggregate(ReceivedMessages.Count > 1 ? Environment.NewLine : "", (a, b) => a + b);
                        await clipboard.SetTextAsync(data);
                        StatusText = "接收数据已复制到剪贴板";
                    }
                    else
                    {
                        StatusText = "无法访问剪贴板";
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"复制失败: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private async Task CopySendDataAsync()
        {
            if (!string.IsNullOrEmpty(SendData))
            {
                try
                {
                    var clipboard = GetClipboard();
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(SendData);
                        StatusText = "发送数据已复制到剪贴板";
                    }
                    else
                    {
                        StatusText = "无法访问剪贴板";
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"复制失败: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void PauseDisplay()
        {
            IsDisplayPaused = !IsDisplayPaused;
            StatusText = IsDisplayPaused ? "显示已暂停" : "显示已恢复";
        }

        [RelayCommand]
        private async Task SaveToFileAsync()
        {
            try
            {
                if (ReceivedMessages.Count == 0)
                {
                    StatusText = "没有数据可保存";
                    return;
                }

                // 获取文件保存对话框
                var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);

                if (topLevel != null)
                {
                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "保存接收数据",
                        DefaultExtension = "txt",
                        SuggestedFileName = $"SerialData_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("文本文件")
                            {
                                Patterns = new[] { "*.txt" }
                            },
                            new FilePickerFileType("日志文件")
                            {
                                Patterns = new[] { "*.log" }
                            },
                            new FilePickerFileType("所有文件")
                            {
                                Patterns = new[] { "*.*" }
                            }
                        }
                    });

                    if (file != null)
                    {
                        // 收集所有文本数据
                        var allText = string.Join(Environment.NewLine, ReceivedMessages.Select(m => m.Text));

                        // 写入文件
                        await using var stream = await file.OpenWriteAsync();
                        await using var writer = new StreamWriter(stream, Encoding.UTF8);
                        await writer.WriteAsync(allText);

                        StatusText = $"数据已保存到: {file.Name}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"保存失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopFileSending()
        {
            if (autoSendLinesTimer != null)
            {
                StopAutoSendLines();
                StatusText = "文件自动发送已停止";
            }
            else
            {
                StatusText = "当前没有进行文件发送";
            }
        }


        [RelayCommand]
        private async Task ImportFromFileAsync()
        {
            try
            {
                // 获取文件打开对话框
                var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);

                if (topLevel != null)
                {
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "导入发送数据",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("文本文件")
                            {
                                Patterns = new[] { "*.txt" }
                            },
                            new FilePickerFileType("日志文件")
                            {
                                Patterns = new[] { "*.log" }
                            },
                            new FilePickerFileType("所有文件")
                            {
                                Patterns = new[] { "*.*" }
                            }
                        }
                    });

                    if (files.Count > 0)
                    {
                        var file = files[0];

                        // 读取文件内容
                        await using var stream = await file.OpenReadAsync();
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var content = await reader.ReadToEndAsync();

                        if (!string.IsNullOrEmpty(content))
                        {
                            var ret = await autoSendService.ProcessImportedContent(content, file.Name, IsConnected);
                            // 自动发送模式
                            if (ret.ImportOption == ImportOption.AutoSend)
                            {
                                await StartAutoSendLines(ret.SendLines);
                            }
                            // 其他模式
                            else
                            {
                                SendData = ret.SendData;
                                StatusText = ret.StatusText;
                            }
                        }
                        else
                        {
                            StatusText = "文件内容为空";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"导入失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ScrollToTop()
        {
            // 通过引用获取控件并滚动到顶部
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow as Views.MainWindow;
                var coloredTextDisplay = mainWindow?.FindControl<ColoredTextDisplay>("ReceiveDisplay");

                coloredTextDisplay?.ScrollToTop();
            }
        }

        [RelayCommand]
        private void ScrollToBottom()
        {
            // 滚动到底部
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow as Views.MainWindow;
                var coloredTextDisplay = mainWindow?.FindControl<ColoredTextDisplay>("ReceiveDisplay");
                coloredTextDisplay?.ScrollToEnd();
            }
        }

        #endregion

        #region 连接/断开连接

        /// <summary>
        /// 串口连接
        /// </summary>
        private async Task ConnectSerialPortAsync()
        {
            var config = new SerialPortConfig
            {
                PortName = SelectedPortInfo.PortName,  // SelectedPortName,,
                BaudRate = SelectedBaudRate,
                Parity = Enum.Parse<Parity>(SelectedParity),
                DataBits = SelectedDataBits,
                StopBits = Enum.Parse<StopBits>(SelectedStopBits),
                Handshake = Enum.Parse<Handshake>(SelectedHandshake)
            };

            var success = await serialPortService.ConnectAsync(config);
            if (success)
            {
                IsConnected = true;
                //StatusText = $"已连接到 {SelectedPortName} - {SelectedBaudRate}";
                if (IsAutoSendEnabled)
                {
                    StartAutoSendTimer();
                }
            }
        }

        /// <summary>
        /// TCP客户端连接
        /// </summary>
        private async Task ConnectTcpClientAsync()
        {
            if (tcpClientService == null)
            {
                tcpClientService = new TcpClientService();
                tcpClientService.DataReceived += OnDataReceived;
                tcpClientService.StatusChanged += OnStatusChanged;
            }

            var config = new TcpClientConfig
            {
                ServerAddress = TcpServerAddress,
                ServerPort = TcpServerPort,
                ConnectTimeout = TcpConnectTimeout
            };

            var success = await tcpClientService.ConnectAsync(config);
            if (success)
            {
                IsConnected = true;
                if (IsAutoSendEnabled)
                {
                    StartAutoSendTimer();
                }
            }
        }

        /// <summary>
        /// 启动TCP服务器
        /// </summary>
        /// <returns></returns>
        private async Task StartTcpServerAsync()
        {
            if (tcpServerService == null)
            {
                tcpServerService = new TcpServerService();
                tcpServerService.DataReceived += OnDataReceived;
                tcpServerService.StatusChanged += OnStatusChanged;
                tcpServerService.ClientConnected += OnTcpClientConnected;
                tcpServerService.ClientDisconnected += OnTcpClientDisconnected;
            }

            var config = new TcpServerConfig
            {
                ListenAddress = TcpListenAddress,
                ListenPort = TcpListenPort,
                MaxClients = TcpMaxClients
            };

            var success = await tcpServerService.StartAsync(config);
            if (success)
            {
                IsConnected = true;
            }
        }

        private void OnTcpClientConnected(object? sender, string clientEndpoint)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var message = $"[{timestamp}] 客户端连接: {clientEndpoint}";

                ReceivedMessages.Add(new ColoredTextItem
                {
                    Text = message,
                    Foreground = Brushes.Cyan
                });
            });
        }

        private void OnTcpClientDisconnected(object? sender, string clientEndpoint)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var message = $"[{timestamp}] 客户端断开: {clientEndpoint}";

                ReceivedMessages.Add(new ColoredTextItem
                {
                    Text = message,
                    Foreground = Brushes.Orange
                });
            });
        }

        /// <summary>
        /// UDP客户端连接
        /// </summary>
        private async Task ConnectUdpClientAsync()
        {
            if (udpClientService == null)
            {
                udpClientService = new UdpClientService();
                udpClientService.DataReceived += OnDataReceived;
                udpClientService.StatusChanged += OnStatusChanged;
            }

            var config = new UdpClientConfig
            {
                ServerAddress = UdpServerAddress,
                ServerPort = UdpServerPort,
                LocalPort = UdpLocalPort,
                ReceiveTimeout = UdpReceiveTimeout
            };

            var success = await udpClientService.ConnectAsync(config);
            if (success)
            {
                IsConnected = true;
                if (IsAutoSendEnabled)
                {
                    StartAutoSendTimer();
                }
            }
        }

        /// <summary>
        /// 启动UDP服务器
        /// </summary>
        private async Task StartUdpServerAsync()
        {
            if (udpServerService == null)
            {
                udpServerService = new UdpServerService();
                udpServerService.DataReceived += OnUdpServerDataReceived;
                udpServerService.StatusChanged += OnStatusChanged;
                udpServerService.ClientDiscovered += OnUdpClientDiscovered;
            }

            var config = new UdpServerConfig
            {
                ListenAddress = UdpListenAddress,
                ListenPort = UdpListenPort,
                ReceiveBufferSize = UdpReceiveBufferSize
            };

            var success = await udpServerService.StartAsync(config);
            if (success)
            {
                IsConnected = true;
            }
        }

        /// <summary>
        /// UDP服务器数据接收处理
        /// </summary>
        private void OnUdpServerDataReceived(object? sender, (byte[] data, IPEndPoint sender) e)
        {
            if (IsDisplayPaused) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var displayText = IsHexReceiveMode
                ? ConvertBytesToHexString(e.data)
                : Encoding.UTF8.GetString(e.data);

            var finalText = ShowTimestamp
                ? $"[{timestamp}] 接收自 {e.sender}: {displayText}"
                : $"接收自 {e.sender}: {displayText}";

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ReceivedMessages.Add(new ColoredTextItem
                {
                    Text = finalText,
                    Foreground = Brushes.Lime
                });
                ReceivedCount++;

                if (ReceivedMessages.Count > 1000)
                {
                    ReceivedMessages.RemoveAt(0);
                }
            });
        }

        /// <summary>
        /// UDP客户端发现处理
        /// </summary>
        private void OnUdpClientDiscovered(object? sender, IPEndPoint clientEndPoint)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!UdpKnownClients.Contains(clientEndPoint))
                {
                    UdpKnownClients.Add(clientEndPoint);
                }

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var message = $"[{timestamp}] 发现UDP客户端: {clientEndPoint}";

                ReceivedMessages.Add(new ColoredTextItem
                {
                    Text = message,
                    Foreground = Brushes.Cyan
                });
            });
        }


        private async Task DisconnectCurrentService()
        {
            try
            {
                if (serialPortService != null)
                {
                    // 断开连接时停止发送
                    StopAutoSendLines();
                    StopCycleSend();

                    await serialPortService.DisconnectAsync();

                    // 停止自动发送定时器
                    autoSendTimer?.Dispose();
                    autoSendTimer = null;
                }

                if (tcpServerService != null)
                {
                    await tcpServerService?.StopAsync();
                }

                if (tcpClientService != null)
                {
                    await tcpClientService?.DisconnectAsync();
                }

                if (udpServerService != null)
                {
                    await udpServerService.StopAsync();
                    UdpKnownClients.Clear();
                }

                if (udpClientService != null)
                {
                    await udpClientService.DisconnectAsync();
                }

                IsConnected = false;
            }
            catch (Exception ex)
            {
                StatusText = $"断开连接时出错: {ex.Message}";
            }
        }

        #endregion

        #region 配置读取/保存

        private void LoadConfiguration()
        {
            var config = configService.LoadConfiguration();

            // 加载连接模式
            SelectedConnectionModeOption = ConnectionModeOptions.FirstOrDefault(x => x.Value == config.LastConnectionMode) ?? ConnectionModeOptions.First();

            // 加载串口配置
            if (!string.IsNullOrEmpty(config.LastSerialPort))
            {
                SelectedPortName = config.LastSerialPort;
            }
            if (config.LastBaudRate > 0)
            {
                SelectedBaudRate = config.LastBaudRate;
            }
            if (!string.IsNullOrEmpty(config.LastParity))
            {
                SelectedParity = config.LastParity;
            }
            if (config.LastDataBits > 0)
            {
                SelectedDataBits = config.LastDataBits;
            }
            if (!string.IsNullOrEmpty(config.LastStopBits))
            {
                SelectedStopBits = config.LastStopBits;
            }
            if (!string.IsNullOrEmpty(config.LastHandshake))
            {
                SelectedHandshake = config.LastHandshake;
            }

            // 加载TCP配置
            TcpServerAddress = config.TcpServerAddress;
            TcpServerPort = config.TcpServerPort;
            TcpListenAddress = config.TcpListenAddress;
            TcpListenPort = config.TcpListenPort;
            TcpConnectTimeout = config.TcpConnectTimeout;
            TcpMaxClients = config.TcpMaxClients;

            // 加载UDP配置
            UdpServerAddress = config.UdpServerAddress;
            UdpServerPort = config.UdpServerPort;
            UdpLocalPort = config.UdpLocalPort;
            UdpListenAddress = config.UdpListenAddress;
            UdpListenPort = config.UdpListenPort;
            UdpReceiveTimeout = config.UdpReceiveTimeout;
            UdpReceiveBufferSize = config.UdpReceiveBufferSize;
            UdpBroadcastMode = config.UdpBroadcastMode;

            // 其他配置
            IsHexReceiveMode = config.IsHexReceiveMode;
            IsHexSendMode = config.IsHexSendMode;
            AutoSendInterval = config.AutoSendInterval > 0 ? config.AutoSendInterval : 1000;
            IsAutoSendEnabled = config.IsAutoSendEnabled;
            ShowTimestamp = config.ShowTimestamp;
            AutoWrap = config.AutoWrap;
            AutoScroll = config.AutoScroll;
            SendNewLine = config.SendNewLine;
            IsCycleSend = config.IsCycleSend;
            CycleSendCount = config.CycleSendCount;


            if (!string.IsNullOrEmpty(config.LastSendData))
            {
                SendData = config.LastSendData;
            }
            // 加载发送历史
            if (config.SendHistory != null)
            {
                SendHistory.Clear();
                foreach (var item in config.SendHistory)
                {
                    SendHistory.Add(item);
                }
            }
        }

        public void SaveConfiguration()
        {
            var config = new AppConfiguration
            {
                LastConnectionMode = SelectedConnectionMode,
                LastSerialPort = SelectedPortName,
                LastBaudRate = SelectedBaudRate,
                LastParity = SelectedParity,
                LastDataBits = SelectedDataBits,
                LastStopBits = SelectedStopBits,
                LastHandshake = SelectedHandshake,
                TcpServerAddress = TcpServerAddress,
                TcpServerPort = TcpServerPort,
                TcpListenAddress = TcpListenAddress,
                TcpListenPort = TcpListenPort,
                TcpConnectTimeout = TcpConnectTimeout,
                TcpMaxClients = TcpMaxClients,
                UdpServerAddress = UdpServerAddress,
                UdpServerPort = UdpServerPort,
                UdpLocalPort = UdpLocalPort,
                UdpListenAddress = UdpListenAddress,
                UdpListenPort = UdpListenPort,
                UdpReceiveTimeout = UdpReceiveTimeout,
                UdpReceiveBufferSize = UdpReceiveBufferSize,
                UdpBroadcastMode = UdpBroadcastMode,
                IsHexReceiveMode = IsHexReceiveMode,
                IsHexSendMode = IsHexSendMode,
                AutoSendInterval = AutoSendInterval,
                IsAutoSendEnabled = IsAutoSendEnabled,
                ShowTimestamp = ShowTimestamp,
                AutoWrap = AutoWrap,
                AutoScroll = AutoScroll,
                SendNewLine = SendNewLine,
                IsCycleSend = IsCycleSend,
                CycleSendCount = CycleSendCount,
                LastSendData = SendData,
                SendHistory = SendHistory.ToList()

            };
            configService.SaveConfiguration(config);
        }

        #endregion

        #region 循环发送

        private void StartCycleSend()
        {
            if (!IsConnected || string.IsNullOrEmpty(SendData))
            {
                StatusText = "请先连接串口并输入发送数据";
                return;
            }

            // 停止现有的循环发送
            StopCycleSend();

            IsCycleSendRunning = true;
            CurrentCycleIndex = 0;

            // 添加到发送历史
            AddToSendHistory(SendData);

            var intervalMs = Math.Max(AutoSendInterval, 100); // 最小间隔100ms

            StatusText = $"开始循环发送, 间隔 {intervalMs}ms";

            // 创建循环发送定时器
            cycleSendTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };

            cycleSendTimer.Tick += async (sender, e) => await ExecuteCycleSend();

            // 立即发送第一次
            _ = Task.Run(async () =>
            {
                await Task.Delay(50); // 短暂延迟确保UI更新
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ExecuteCycleSend();
                    cycleSendTimer?.Start(); // 发送完第一次后启动定时器
                });
            });
        }

        private async Task ExecuteCycleSend()
        {
            if (!IsCycleSendRunning || !IsConnected)
            {
                StopCycleSend();
                return;
            }

            // 检查是否达到循环次数限制
            if (CycleSendCount > 0 && CurrentCycleIndex >= CycleSendCount)
            {
                StopCycleSend();
                StatusText = $"循环发送完成,共发送 {CurrentCycleIndex} 次";
                return;
            }

            try
            {
                await ExecuteSingleSend(false); // false表示不清空发送框
                CurrentCycleIndex++;

                // 更新状态
                StatusText = $"循环发送中 - 字节数: {Encoding.UTF8.GetBytes(SendData).Length}";

            }
            catch (Exception ex)
            {
                StopCycleSend();
                StatusText = $"循环发送出错: {ex.Message}";
            }
        }

        private async Task ExecuteSingleSend(bool clearSendData = true)
        {
            if (!IsConnected || string.IsNullOrEmpty(SendData))
            {
                return;
            }
            try
            {
                byte[] dataToSend;
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var originalSendData = SendData; // 保存原始数据

                if (IsHexSendMode)
                {
                    dataToSend = ConvertHexStringToBytes(SendData);
                }
                else
                {
                    var text = SendData;
                    if (SendNewLine)
                    {
                        text += Environment.NewLine;
                    }
                    dataToSend = Encoding.UTF8.GetBytes(text);
                }

                // 根据连接模式发送数据
                switch (SelectedConnectionMode)
                {
                    case ConnectionMode.SerialPort:
                        await serialPortService.SendDataAsync(dataToSend);
                        break;
                    case ConnectionMode.TcpServer:
                        await tcpServerService?.SendDataToAllAsync(dataToSend);
                        break;
                    case ConnectionMode.TcpClient:
                        await tcpClientService?.SendDataAsync(dataToSend);
                        break;
                    case ConnectionMode.UdpServer:
                        if (UdpBroadcastMode)
                        {
                            await udpServerService?.BroadcastDataAsync(dataToSend, UdpListenPort);
                        }
                        else
                        {
                            await udpServerService?.SendDataToAllAsync(dataToSend);
                        }
                        break;
                    case ConnectionMode.UdpClient:
                        await udpClientService?.SendDataAsync(dataToSend);
                        break;
                    default:
                        throw new NotSupportedException($"发送模式 {SelectedConnectionMode} 暂不支持");
                }

                SentCount++;

                // 如果不是循环发送,添加到发送历史
                if (!IsCycleSendRunning)
                {
                    AddToSendHistory(originalSendData);
                }

                // 在接收区显示发送的数据
                var displayText = IsHexSendMode ? ConvertBytesToHexString(dataToSend) : originalSendData;
                var sendDisplayText = ShowTimestamp
                                    ? $"[{timestamp}] 发送: {displayText}"
                                    : $"发送: {displayText}";

                if (!sendDisplayText.EndsWith(Environment.NewLine))
                {
                    sendDisplayText += Environment.NewLine;
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ReceivedMessages.Add(new ColoredTextItem
                    {
                        Text = sendDisplayText,
                        Foreground = Brushes.Yellow
                    });

                });

                // 根据参数决定是否清空发送框
                if (clearSendData && !IsCycleSendRunning && !IsAutoSendEnabled)
                {
                    SendData = string.Empty;
                }

                if (!IsCycleSendRunning)
                {
                    StatusText = $"发送成功 - 字节数: {dataToSend.Length}";
                }
            }
            catch (Exception ex)
            {
                if (!IsCycleSendRunning)
                {
                    StatusText = $"发送错误: {ex.Message}";
                }
                throw; // 重新抛出异常,让调用者处理
            }
        }

        private void StopCycleSend()
        {
            cycleSendTimer?.Stop();
            cycleSendTimer = null;

            var wasRunning = IsCycleSendRunning;
            IsCycleSendRunning = false;

            if (wasRunning && CurrentCycleIndex > 0)
            {
                StatusText = $"循环发送已停止，共发送 {CurrentCycleIndex} 次";
            }
            else if (wasRunning)
            {
                StatusText = "循环发送已停止";
            }
        }

        #endregion

        #region 导入发送数据功能

        private async Task StartAutoSendLines(List<string> lines)
        {
            if (!IsConnected)
            {
                StatusText = "请先连接串口";
                return;
            }

            // 停止现有的自动发送
            StopAutoSendLines();

            pendingSendLines = lines;
            currentSendLineIndex = 0;

            // 启用自动发送,设置合适的间隔
            IsAutoSendEnabled = true;
            if (AutoSendInterval < 1000)
            {
                AutoSendInterval = 1000;
            }

            StatusText = $"开始自动发送 {lines.Count} 行数据,间隔 {AutoSendInterval}ms";

            autoSendLinesTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoSendInterval)
            };
            autoSendLinesTimer.Tick += async (sender, e) => await SendNextLineFromFile();
            autoSendLinesTimer.Start();
        }

        private async Task SendNextLineFromFile()
        {
            if (pendingSendLines == null || currentSendLineIndex >= pendingSendLines.Count)
            {
                // 发送完成
                StopAutoSendLines();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"文件内容发送完成";
                });
                return;
            }

            if (!IsConnected)
            {
                // 连接断开,停止发送
                StopAutoSendLines();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "连接断开,自动发送已停止";
                });
                return;
            }

            try
            {
                var lineToSend = pendingSendLines[currentSendLineIndex];

                // 在UI线程上更新发送框内容（可选）
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SendData = lineToSend;
                    StatusText = $"正在发送第 {currentSendLineIndex + 1}/{pendingSendLines.Count} 行";
                });

                // 发送数据
                byte[] dataToSend;
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                if (IsHexSendMode)
                {
                    dataToSend = ConvertHexStringToBytes(lineToSend);
                }
                else
                {
                    var text = lineToSend;
                    if (SendNewLine)
                    {
                        text += Environment.NewLine;
                    }
                    dataToSend = Encoding.UTF8.GetBytes(text);
                }

                await serialPortService.SendDataAsync(dataToSend);

                // 更新计数器
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SentCount++;

                    // 在接收区显示发送的数据
                    var displayText = IsHexSendMode ? ConvertBytesToHexString(dataToSend) : lineToSend;
                    var sendDisplayText = ShowTimestamp
                        ? $"[{timestamp}] 发送: {displayText}"
                        : $"发送: {displayText}";

                    ReceivedMessages.Add(new ColoredTextItem
                    {
                        Text = sendDisplayText,
                        Foreground = Brushes.Yellow
                    });
                });

                currentSendLineIndex++;
            }
            catch (Exception ex)
            {
                StopAutoSendLines();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"发送第 {currentSendLineIndex + 1} 行时出错: {ex.Message}";
                });
            }
        }

        private void StopAutoSendLines()
        {
            autoSendLinesTimer?.Stop();
            autoSendLinesTimer = null;
            pendingSendLines = null;
            currentSendLineIndex = 0;
        }

        #endregion

        #region 属性变更

        private string ConvertBytesToHexString(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }


        private void OnStatusChanged(object? sender, string status)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = status;
            });
        }

        partial void OnSelectedTemplateChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                SendData = value;
            }
        }

        partial void OnSelectedHistoryItemChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                SendData = value;
            }
        }

        partial void OnIsAutoSendEnabledChanged(bool value)
        {
            // 如果是导入文件发送模式, 则不启动自动发送
            if (IsConnected && pendingSendLines == null)
            {
                if (value)
                {
                    StartAutoSendTimer();
                }
                else
                {
                    autoSendTimer?.Dispose();
                    autoSendTimer = null;
                }
            }
        }

        partial void OnAutoSendIntervalChanged(int value)
        {
            // 如果正在循环发送,更新定时器间隔
            if (IsCycleSendRunning && cycleSendTimer != null)
            {
                cycleSendTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(value, 100));
            }

            if (IsAutoSendEnabled && IsConnected)
            {
                StartAutoSendTimer();
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // 当重要配置改变时自动保存
            if (e.PropertyName == nameof(SelectedPortName) ||
                e.PropertyName == nameof(SelectedBaudRate) ||
                e.PropertyName == nameof(SelectedParity) ||
                e.PropertyName == nameof(SelectedDataBits) ||
                e.PropertyName == nameof(SelectedStopBits) ||
                e.PropertyName == nameof(SelectedHandshake) ||
                e.PropertyName == nameof(IsHexReceiveMode) ||
                e.PropertyName == nameof(IsHexSendMode) ||
                e.PropertyName == nameof(ShowTimestamp) ||
                e.PropertyName == nameof(AutoWrap))
            {
                SaveConfiguration();
            }
        }

        partial void OnSelectedConnectionModeChanged(ConnectionMode value)
        {
            // 断开当前连接
            if (IsConnected)
            {
                _ = Task.Run(async () => await DisconnectCurrentService());
            }

            // 通知UI更新配置面板
            OnPropertyChanged(nameof(IsSerialPortMode));
            OnPropertyChanged(nameof(IsTcpServerMode));
            OnPropertyChanged(nameof(IsTcpClientMode));
            OnPropertyChanged(nameof(IsUdpServerMode));
            OnPropertyChanged(nameof(IsUdpClientMode));
        }

        partial void OnSelectedPortInfoChanged(SerialPortInfo? value)
        {
            if (value != null)
            {
                SelectedPortName = value.PortName;
            }
        }

        partial void OnSelectedConnectionModeOptionChanged(ConnectionModeOption? value)
        {
            if (value != null)
            {
                SelectedConnectionMode = value.Value;
            }
        }

        partial void OnIsCycleSendChanged(bool value)
        {
            if (!value && IsCycleSendRunning)
            {
                StopCycleSend();
            }
        }

        #endregion

        #region 其他

        private void StartAutoSendTimer()
        {
            autoSendTimer?.Dispose();
            autoSendTimer = new Timer(async _ => await SendDataAsync(), null,
                TimeSpan.FromMilliseconds(AutoSendInterval),
                TimeSpan.FromMilliseconds(AutoSendInterval));
        }

        private void OnDataReceived(object? sender, byte[] data)
        {
            if (IsDisplayPaused)
            {
                return;
            }
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var displayText = IsHexReceiveMode
                ? ConvertBytesToHexString(data)
                : Encoding.UTF8.GetString(data);

            var finalText = ShowTimestamp
                           ? $"[{timestamp}] 接收: {displayText}"
                           : $"接收: {displayText}";

            // 在UI线程上更新
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ReceivedMessages.Add(new ColoredTextItem
                {
                    Text = finalText,
                    Foreground = Brushes.DarkGray
                });
                ReceivedCount++;

                // 自动滚动到底部
                if (AutoScroll && ReceivedMessages.Count > 1000)
                {
                    ReceivedMessages.RemoveAt(0);
                }

            });
        }

        private void AddToSendHistory(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }
            // 移除重复项
            if (SendHistory.Contains(data))
            {
                SendHistory.Remove(data);
            }

            // 添加到开头
            SendHistory.Insert(0, data);

            // 限制历史记录数量
            while (SendHistory.Count > 20)
            {
                SendHistory.RemoveAt(SendHistory.Count - 1);
            }
        }

        private Avalonia.Input.Platform.IClipboard? GetClipboard()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.Clipboard;
            }
            return null;
        }

        private byte[] ConvertHexStringToBytes(string hex)
        {
            // 移除空格和非十六进制字符
            hex = System.Text.RegularExpressions.Regex.Replace(hex, @"[^0-9A-Fa-f]", "");

            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex; // 补齐偶数位
            }
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        #endregion

    }
}
