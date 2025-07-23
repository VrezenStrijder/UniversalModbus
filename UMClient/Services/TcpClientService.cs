using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public class TcpClientService : IDisposable
    {
        private TcpClient? tcpClient;
        private NetworkStream? networkStream;
        private bool disposed = false;
        private CancellationTokenSource? cancellationTokenSource;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<string>? StatusChanged;

        public bool IsConnected => tcpClient?.Connected ?? false;

        public async Task<bool> ConnectAsync(TcpClientConfig config)
        {
            try
            {
                await DisconnectAsync();

                cancellationTokenSource = new CancellationTokenSource();
                tcpClient = new TcpClient();

                StatusChanged?.Invoke(this, $"正在连接到 {config.ServerAddress}:{config.ServerPort}...");

                await tcpClient.ConnectAsync(config.ServerAddress, config.ServerPort);

                if (tcpClient.Connected)
                {
                    networkStream = tcpClient.GetStream();
                    StatusChanged?.Invoke(this, $"已连接到 {config.ServerAddress}:{config.ServerPort}");

                    // 启动接收数据的任务
                    _ = Task.Run(() => ReceiveDataLoop(cancellationTokenSource.Token));

                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, "连接失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"连接失败: {ex.Message}");
                return false;
            } 
        }

        public async Task DisconnectAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();

                networkStream?.Close();
                networkStream?.Dispose();
                networkStream = null;

                tcpClient?.Close();
                tcpClient?.Dispose();
                tcpClient = null;

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;

                StatusChanged?.Invoke(this, "TCP连接已断开");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"断开连接时出错: {ex.Message}");
            }
        }

        public async Task SendDataAsync(byte[] data)
        {
            if (networkStream != null && IsConnected)
            {
                try
                {
                    await networkStream.WriteAsync(data, 0, data.Length);
                    await networkStream.FlushAsync();
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"发送数据失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("TCP连接未建立");
            }
        }

        private async Task ReceiveDataLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested && networkStream != null && IsConnected)
                {
                    var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead > 0)
                    {
                        var receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        DataReceived?.Invoke(this, receivedData);
                    }
                    else
                    {
                        // 连接已关闭
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"接收数据时出错: {ex.Message}");
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
    }

}
