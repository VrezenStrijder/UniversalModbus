using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public class TcpServerService : IDisposable
    {
        private TcpListener? tcpListener;
        private bool disposed = false;
        private CancellationTokenSource? cancellationTokenSource;
        private readonly ConcurrentDictionary<string, TcpClient> connectedClients = new();

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? ClientConnected;
        public event EventHandler<string>? ClientDisconnected;

        public bool IsListening => tcpListener != null;
        public int ConnectedClientCount => connectedClients.Count;

        public async Task<bool> StartAsync(TcpServerConfig config)
        {
            try
            {
                await StopAsync();

                var ipAddress = IPAddress.Parse(config.ListenAddress);
                tcpListener = new TcpListener(ipAddress, config.ListenPort);
                cancellationTokenSource = new CancellationTokenSource();

                tcpListener.Start();
                StatusChanged?.Invoke(this, $"TCP服务器已启动，监听 {config.ListenAddress}:{config.ListenPort}");

                // 启动接受客户端连接的任务
                _ = Task.Run(() => AcceptClientsLoop(cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"启动TCP服务器失败: {ex.Message}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();

                // 断开所有客户端连接
                foreach (var client in connectedClients.Values)
                {
                    client.Close();
                    client.Dispose();
                }
                connectedClients.Clear();

                tcpListener?.Stop();
                tcpListener = null;

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;

                StatusChanged?.Invoke(this, "TCP服务器已停止");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"停止TCP服务器时出错: {ex.Message}");
            }
        }

        public async Task SendDataToAllAsync(byte[] data)
        {
            var tasks = new List<Task>();

            foreach (var kvp in connectedClients)
            {
                tasks.Add(SendDataToClientAsync(kvp.Key, data));
            }

            await Task.WhenAll(tasks);
        }

        public async Task SendDataToClientAsync(string clientId, byte[] data)
        {
            if (connectedClients.TryGetValue(clientId, out var client))
            {
                try
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"向客户端 {clientId} 发送数据失败: {ex.Message}");
                    RemoveClient(clientId);
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException($"客户端 {clientId} 不存在");
            }
        }

        private async Task AcceptClientsLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && tcpListener != null)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    var clientId = Guid.NewGuid().ToString();

                    connectedClients[clientId] = tcpClient;
                    ClientConnected?.Invoke(this, clientEndpoint);
                    StatusChanged?.Invoke(this, $"客户端已连接: {clientEndpoint} (总计: {connectedClients.Count})");

                    // 为每个客户端启动接收数据的任务
                    _ = Task.Run(() => HandleClientAsync(clientId, tcpClient, cancellationToken));
                }
            }
            catch (ObjectDisposedException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"接受客户端连接时出错: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(string clientId, TcpClient client, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

            try
            {
                var stream = client.GetStream();

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead > 0)
                    {
                        var receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        DataReceived?.Invoke(this, receivedData);
                    }
                    else
                    {
                        // 客户端断开连接
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
                StatusChanged?.Invoke(this, $"处理客户端 {clientEndpoint} 时出错: {ex.Message}");
            }
            finally
            {
                RemoveClient(clientId);
                ClientDisconnected?.Invoke(this, clientEndpoint);
                StatusChanged?.Invoke(this, $"客户端已断开: {clientEndpoint} (剩余: {connectedClients.Count})");
            }
        }

        private void RemoveClient(string clientId)
        {
            if (connectedClients.TryRemove(clientId, out var client))
            {
                client.Close();
                client.Dispose();
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                StopAsync().Wait();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }


}
