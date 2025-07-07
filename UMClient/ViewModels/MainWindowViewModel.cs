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


namespace UMClient.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ConfigurationService configService;
        private readonly SerialPortService serialPortService;
        private Timer? autoSendTimer;

        public MainWindowViewModel()
        {
            configService = new ConfigurationService();
            serialPortService = new SerialPortService();

            serialPortService.DataReceived += OnDataReceived;
            serialPortService.StatusChanged += OnStatusChanged;

            InitializeDefaults();
            LoadConfiguration();
            LoadSendTemplates();
            RefreshPorts();

        }



        [ObservableProperty]
        private string title = "串口调试工具";

        [ObservableProperty]
        private ConnectionMode selectedConnectionMode = ConnectionMode.SerialPort;

        [ObservableProperty]
        private string statusText = "就绪";

        [ObservableProperty]
        private bool isConnected = false;

        //[ObservableProperty]
        //private string receivedData = string.Empty;

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
        }

        [RelayCommand]
        private async Task ConnectAsync()       //匹配 ConnectCommand 
        {
            try
            {
                if (IsConnected)
                {
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
            if (!IsConnected || string.IsNullOrEmpty(SendData))
            {
                return;
            }

            try
            {
                byte[] dataToSend;
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                if (IsHexSendMode)
                {
                    // 十六进制模式
                    dataToSend = ConvertHexStringToBytes(SendData);
                }
                else
                {
                    // 字符模式
                    var text = SendData;
                    if (SendNewLine)
                    {
                        text += Environment.NewLine;
                    }
                    dataToSend = Encoding.UTF8.GetBytes(text);
                }

                await serialPortService.SendDataAsync(dataToSend);
                SentCount++;

                // 添加到发送历史
                AddToSendHistory(SendData);

                // 在接收区显示发送的数据（带时间戳）
                var displayText = IsHexSendMode ? ConvertBytesToHexString(dataToSend) : SendData;
                var sendDisplayText = $"[{timestamp}] 发送: {displayText}";
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

                // 发送后清空发送框(除自动发送模式)
                if (!IsAutoSendEnabled)
                {
                    SendData = string.Empty;
                }
                StatusText = $"发送成功 - 字节数: {dataToSend.Length}";
            }
            catch (Exception ex)
            {
                StatusText = $"发送错误: {ex.Message}";
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
            var ports = SerialPort.GetPortNames();

            if (ports.Length == 0)
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

        private void AddToSendHistory(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;

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
            // 这里可以实现保存到文件的功能
            StatusText = "保存功能待实现";
        }

        [RelayCommand]
        private async Task ImportFromFileAsync()
        {
            // 这里可以实现从文件导入的功能
            StatusText = "导入功能待实现";
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
            if (IsConnected)
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

            var finalText = $"[{timestamp}] 接收: {displayText}";

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
                if (AutoScroll && ReceivedMessages.Count > 500)
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
                SendHistory = SendHistory.ToList()
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

    }
}
