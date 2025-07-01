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


namespace UMClient.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ConfigurationService configService;
        private readonly SerialPortService serialPortService;
        private Timer? autoSendTimer;

        [ObservableProperty]
        private string title = "串口调试工具";

        [ObservableProperty]
        private ConnectionMode selectedConnectionMode = ConnectionMode.SerialPort;

        [ObservableProperty]
        private string statusText = "就绪";

        [ObservableProperty]
        private bool isConnected = false;

        [ObservableProperty]
        private string receivedData = string.Empty;

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
        private bool showTimestamp = false;

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
        private int selectedBaudRate = 9600;

        [ObservableProperty]
        private string selectedParity = "None";

        [ObservableProperty]
        private int selectedDataBits = 8;

        [ObservableProperty]
        private string selectedStopBits = "One";

        [ObservableProperty]
        private string? selectedTemplate;


        public ObservableCollection<string> SendTemplates { get; } = new();
        public ObservableCollection<string> AvailablePorts { get; } = new();

        public MainWindowViewModel()
        {
            configService = new ConfigurationService();
            serialPortService = new SerialPortService();

            serialPortService.DataReceived += OnDataReceived;
            serialPortService.StatusChanged += OnStatusChanged;

            LoadConfiguration();
            LoadSendTemplates();
            RefreshPorts();
        }

        [RelayCommand]
        private async Task ConnectAsync()
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
                        StopBits = Enum.Parse<StopBits>(SelectedStopBits)
                    };

                    var success = await serialPortService.ConnectAsync(config);
                    if (success)
                    {
                        IsConnected = true;
                        StatusText = $"已连接到 {SelectedPortName}";

                        // 启动自动发送定时器（如果启用）
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
                return;

            try
            {
                byte[] dataToSend;

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
                StatusText = $"发送: {SendData}";
            }
            catch (Exception ex)
            {
                StatusText = $"发送错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            var ports = SerialPort.GetPortNames();
            foreach (var port in ports.OrderBy(p => p))
            {
                AvailablePorts.Add(port);
            }

            if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(SelectedPortName))
            {
                SelectedPortName = AvailablePorts[0];
            }
        }

        [RelayCommand]
        private void ClearReceiveData()
        {
            ReceivedData = string.Empty;
            ReceivedCount = 0;
        }

        [RelayCommand]
        private void ClearSendData()
        {
            SendData = string.Empty;
        }

        [RelayCommand]
        private void ClearCounter()
        {
            ReceivedCount = 0;
            SentCount = 0;
        }

        [RelayCommand]
        private async Task CopyReceiveDataAsync()
        {
            if (!string.IsNullOrEmpty(ReceivedData))
            {
                try
                {
                    var clipboard = GetClipboard();
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(ReceivedData);
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
            if (IsDisplayPaused) return;

            var displayText = IsHexReceiveMode
                ? ConvertBytesToHexString(data)
                : Encoding.UTF8.GetString(data);

            if (ShowTimestamp)
            {
                displayText = $"[{DateTime.Now:HH:mm:ss.fff}] {displayText}";
            }

            // 在UI线程上更新
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ReceivedData += displayText;
                if (AutoWrap && !displayText.EndsWith(Environment.NewLine))
                {
                    ReceivedData += Environment.NewLine;
                }
                ReceivedCount++;
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
                hex = "0" + hex; // 补齐偶数位

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
            SelectedPortName = config.LastSerialPort;
            SelectedBaudRate = config.LastBaudRate;
            IsHexReceiveMode = config.IsHexReceiveMode;
            IsHexSendMode = config.IsHexSendMode;
            AutoSendInterval = config.AutoSendInterval;
            IsAutoSendEnabled = config.IsAutoSendEnabled;
            SendData = config.LastSendData;
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
                IsHexReceiveMode = IsHexReceiveMode,
                IsHexSendMode = IsHexSendMode,
                AutoSendInterval = AutoSendInterval,
                IsAutoSendEnabled = IsAutoSendEnabled,
                LastSendData = SendData
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

    }
}
