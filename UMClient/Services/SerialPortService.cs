using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public class SerialPortService : IDisposable
    {
        private SerialPort? serialPort;
        private bool disposed = false;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<string>? StatusChanged;

        public bool IsConnected => serialPort?.IsOpen ?? false;

        public async Task<bool> ConnectAsync(SerialPortConfig config)
        {
            try
            {
                await DisconnectAsync();

                serialPort = new SerialPort
                {
                    PortName = config.PortName,
                    BaudRate = config.BaudRate,
                    Parity = config.Parity,
                    DataBits = config.DataBits,
                    StopBits = config.StopBits,
                    Handshake = config.Handshake,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                serialPort.DataReceived += OnSerialPortDataReceived;
                serialPort.ErrorReceived += OnSerialPortErrorReceived;

                // Linux上需要特殊处理权限问题
                if (OperatingSystem.IsLinux())
                {
                    await CheckLinuxSerialPortPermissions(config.PortName);
                }

                await Task.Run(() => serialPort.Open());

                StatusChanged?.Invoke(this, $"已连接到 {config.PortName}");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                var errorMessage = OperatingSystem.IsLinux()
                    ? $"权限不足,请确保用户在dialout组中: {ex.Message}"
                    : $"访问被拒绝: {ex.Message}";
                StatusChanged?.Invoke(this, errorMessage);
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"连接失败: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (serialPort != null)
            {
                try
                {
                    if (serialPort.IsOpen)
                    {
                        await Task.Run(() => serialPort.Close());
                    }

                    serialPort.DataReceived -= OnSerialPortDataReceived;
                    serialPort.ErrorReceived -= OnSerialPortErrorReceived;
                    serialPort.Dispose();
                    serialPort = null;

                    StatusChanged?.Invoke(this, "已断开连接");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"断开连接时出错: {ex.Message}");
                }
            }
        }

        public async Task SendDataAsync(byte[] data)
        {
            if (serialPort?.IsOpen == true)
            {
                try
                {
                    await Task.Run(() => serialPort.Write(data, 0, data.Length));
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"发送数据失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("串口未连接");
            }
        }

        private void OnSerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (serialPort?.IsOpen == true)
            {
                try
                {
                    var bytesToRead = serialPort.BytesToRead;
                    if (bytesToRead > 0)
                    {
                        var buffer = new byte[bytesToRead];
                        var bytesRead = serialPort.Read(buffer, 0, bytesToRead);

                        if (bytesRead > 0)
                        {
                            var actualData = new byte[bytesRead];
                            Array.Copy(buffer, actualData, bytesRead);
                            DataReceived?.Invoke(this, actualData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"接收数据时出错: {ex.Message}");
                }
            }
        }

        private void OnSerialPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            StatusChanged?.Invoke(this, $"串口错误: {e.EventType}");
        }

        /// <summary>
        /// Linux上需要特殊处理权限问题
        /// </summary>
        /// <param name="portName">串口名称</param>
        private async Task CheckLinuxSerialPortPermissions(string portName)
        {
            try
            {
                // 检查用户是否在dialout组中
                var currentUser = Environment.UserName;

                // 使用id命令检查用户组
                var processInfo = new ProcessStartInfo
                {
                    FileName = "id",
                    Arguments = "-Gn",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = await process.StandardOutput.ReadToEndAsync();

                    if (!output.Contains("dialout") && !output.Contains("uucp"))
                    {
                        StatusChanged?.Invoke(this, "警告: 用户可能不在dialout组中,如果连接失败,请运行: sudo usermod -a -G dialout $USER");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"权限检查失败: {ex.Message}");
            }
        }



        public void Dispose()
        {
            if (!disposed)
            {
                DisconnectAsync().Wait();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~SerialPortService()
        {
            Dispose();
        }
    }


}
