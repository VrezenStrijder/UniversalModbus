using System;
using System.Collections.Generic;
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

                await Task.Run(() => serialPort.Open());

                StatusChanged?.Invoke(this, $"已连接到 {config.PortName}");
                return true;
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
