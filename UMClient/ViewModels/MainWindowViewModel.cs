using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UMClient.Models;
using UMClient.Services;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.ComponentModel;
using System.Collections.Generic;
using UMClient.Controls;
using SukiUI.Models;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.IO;
using Avalonia.Threading;


namespace UMClient.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly QueryPortService queryPortService;
        private readonly ConfigurationService configService;
        private readonly SerialPortService serialPortService;
        private readonly AutoSendService autoSendService;
        private Timer? autoSendTimer;

        // 从导入文件自动定时发送
        private List<string>? pendingSendLines;
        private int currentSendLineIndex;
        private DispatcherTimer? autoSendLinesTimer; // 使用DispatcherTimer, 以保证在UI线程中执行
        // 循环发送
        private DispatcherTimer? cycleSendTimer;



        private static readonly Dictionary<string, SukiColorTheme> PredefinedThemes = new()
        {
            { "蓝色", new SukiColorTheme("Blue", Colors.Blue, Colors.LightBlue) },
            { "紫色", new SukiColorTheme("Purple", Colors.Purple, Colors.Violet) },
            { "粉色", new SukiColorTheme("Pink", Colors.DeepPink, Colors.Pink) },
            { "红色", new SukiColorTheme("Red", Colors.Red, Colors.Crimson) },
            { "橙色", new SukiColorTheme("Orange", Colors.Orange, Colors.DarkOrange) },
            { "黄色", new SukiColorTheme("Yellow", Colors.Gold, Colors.Yellow) },
            { "绿色", new SukiColorTheme("Green", Colors.Green, Colors.LimeGreen) },
            { "青色", new SukiColorTheme("Cyan", Colors.Cyan, Colors.Turquoise) }
        };


        public MainWindowViewModel()
        {
            queryPortService = new QueryPortService();
            configService = new ConfigurationService();
            serialPortService = new SerialPortService();
            autoSendService = new AutoSendService();

            serialPortService.DataReceived += OnDataReceived;
            serialPortService.StatusChanged += OnStatusChanged;

            InitializeDefaults();
            LoadConfiguration();
            LoadSendTemplates();
            RefreshPorts();

            InitializeThemeOptions();
            //SelectedTheme = ThemeOptions.First();

        }

        public ObservableCollection<ThemeOption> ThemeOptions { get; } = new ObservableCollection<ThemeOption>();

        private void InitializeThemeOptions()
        {
            ThemeOptions.Add(new ThemeOption { Display = "无 (默认)", Theme = null });

            foreach (var theme in PredefinedThemes)
            {
                ThemeOptions.Add(new ThemeOption
                {
                    Display = theme.Key,
                    Theme = theme.Value
                });
            }
        }

        //[ObservableProperty]
        //private ThemeOption? selectedTheme;

        //partial void OnSelectedThemeChanged(ThemeOption? value)
        //{
        //    if (value != null)
        //    {
        //        ApplyTheme(value.Theme);
        //    }
        //}

        //private void ApplyTheme(SukiColorTheme? theme)
        //{
        //    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        //    {
        //        var mainWindow = desktop.MainWindow as Views.MainWindow;
        //        if (mainWindow != null)
        //        {
        //            if (theme != null)
        //            {
        //                // 应用选定的主题
        //                SukiUI.Helpers.SukiTheme.ChangeColorTheme(mainWindow, theme);
        //            }
        //            else
        //            {
        //                // 恢复默认主题 - 使用蓝色作为默认
        //                var defaultTheme = new SukiColorTheme("Default", Colors.Blue, Colors.LightBlue);
        //                SukiUI.Helpers.SukiTheme.ChangeColorTheme(mainWindow, defaultTheme);
        //            }
        //        }
        //    }
        //}



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

        // 串口配置
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
        private string? selectedTemplate;

        [ObservableProperty]
        private string? selectedHistoryItem;



        public ObservableCollection<string> SendTemplates { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailablePorts { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> SendHistory { get; } = new ObservableCollection<string>();

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


        public ObservableCollection<ConnectionModeOption> ConnectionModeOptions { get; } = new ObservableCollection<ConnectionModeOption>()
        {
            new ConnectionModeOption() { Display = "串口", Value = ConnectionMode.SerialPort },
            new ConnectionModeOption() { Display = "TCP客户端", Value = ConnectionMode.TcpClient },
            new ConnectionModeOption() { Display = "TCP服务器", Value = ConnectionMode.TcpServer },
            new ConnectionModeOption() { Display = "UDP客户端", Value = ConnectionMode.UdpClient },
            new ConnectionModeOption() { Display = "UDP服务器", Value = ConnectionMode.UdpServer }
        };


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



        [RelayCommand]
        private async Task ConnectAsync()       //匹配 ConnectCommand 
        {
            try
            {
                if (IsConnected)
                {
                    // 断开连接时停止发送
                    StopAutoSendLines();
                    StopCycleSend();

                    // 断开连接
                    await serialPortService.DisconnectAsync();
                    IsConnected = false;
                    StatusText = "已断开连接";

                    // 停止自动发送定时器
                    autoSendTimer?.Dispose();
                    autoSendTimer = null;
                }
                else
                {
                    // 建立连接
                    var config = new SerialPortConfig
                    {
                        PortName = SelectedPortName,
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
                        StatusText = $"已连接到 {SelectedPortName} - {SelectedBaudRate}";

                        if (IsAutoSendEnabled)
                        {
                            StartAutoSendTimer();
                        }
                    }
                    else
                    {
                        StatusText = "连接失败";
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

                await serialPortService.SendDataAsync(dataToSend);
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
                if (clearSendData && !IsCycleSendRunning)
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


        [RelayCommand]
        private void SetBaudRate(string baudRate)
        {
            if (int.TryParse(baudRate, out int rate))
            {
                SelectedBaudRate = rate;
            }
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();

            try
            {
                List<string> ports = new List<string>();

                if (OperatingSystem.IsWindows())
                {
                    var windowsPorts = queryPortService.GetWindowsSerialPorts();
                    ports.AddRange(windowsPorts);
                }
                else if (OperatingSystem.IsLinux())
                {
                    ports.AddRange(queryPortService.GetLinuxSerialPorts());
                }
                else if (OperatingSystem.IsMacOS())
                {
                    ports.AddRange(queryPortService.GetMacOSSerialPorts());
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

                // 如果当前选择的串口不在列表中，选择第一个可用的
                if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(SelectedPortName))
                {
                    SelectedPortName = AvailablePorts[0];
                }

                StatusText = $"检测到 {AvailablePorts.Count} 个可用串口";
            }
            catch (Exception ex)
            {
                StatusText = $"枚举串口失败: {ex.Message}";
            }
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

        #region 导入发送数据功能

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

        private void OnStatusChanged(object? sender, string status)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = status;
            });
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

        private string ConvertBytesToHexString(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private void LoadConfiguration()
        {
            var config = configService.LoadConfiguration();

            // 只有在配置文件中有值时才覆盖默认值
            if (!string.IsNullOrEmpty(config.LastSerialPort))
                SelectedPortName = config.LastSerialPort;

            if (config.LastBaudRate > 0)
                SelectedBaudRate = config.LastBaudRate;

            if (!string.IsNullOrEmpty(config.LastParity))
                SelectedParity = config.LastParity;

            if (config.LastDataBits > 0)
                SelectedDataBits = config.LastDataBits;

            if (!string.IsNullOrEmpty(config.LastStopBits))
                SelectedStopBits = config.LastStopBits;

            if (!string.IsNullOrEmpty(config.LastHandshake))
                SelectedHandshake = config.LastHandshake;

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

            SelectedConnectionModeOption = ConnectionModeOptions.FirstOrDefault(x => x.Value == config.LastConnectionMode) ?? ConnectionModeOptions.First();


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

        private void LoadSendTemplates()
        {
            var templates = configService.LoadSendTemplates();
            SendTemplates.Clear();
            foreach (var template in templates)
            {
                SendTemplates.Add(template);
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
                IsHexReceiveMode = IsHexReceiveMode,
                IsHexSendMode = IsHexSendMode,
                AutoSendInterval = AutoSendInterval,
                IsAutoSendEnabled = IsAutoSendEnabled,
                ShowTimestamp = ShowTimestamp,
                AutoWrap = AutoWrap,
                AutoScroll = AutoScroll,
                SendNewLine = SendNewLine,
                LastSendData = SendData,
                SendHistory = SendHistory.ToList(),
                IsCycleSend = IsCycleSend,
                CycleSendCount = CycleSendCount

            };
            configService.SaveConfiguration(config);
        }

        private Avalonia.Input.Platform.IClipboard? GetClipboard()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.Clipboard;
            }
            return null;
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

    }
}
