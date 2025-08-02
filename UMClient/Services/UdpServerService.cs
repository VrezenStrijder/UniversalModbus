using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public class UdpServerService : IDisposable
    {
        private UdpClient? udpServer;
        private bool disposed = false;
        private CancellationTokenSource? cancellationTokenSource;
        private IPEndPoint? localEndPoint;
        private readonly ConcurrentDictionary<string, IPEndPoint> knownClients = new();

        public event EventHandler<(byte[] data, IPEndPoint sender)>? DataReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<IPEndPoint>? ClientDiscovered;

        public bool IsListening => udpServer != null && !disposed;
        public string? LocalEndPoint => localEndPoint?.ToString();
        public int KnownClientCount => knownClients.Count;
        public IEnumerable<IPEndPoint> KnownClients => knownClients.Values;

        public async Task<bool> StartAsync(UdpServerConfig config)
        {
            try
            {
                await StopAsync();

                cancellationTokenSource = new CancellationTokenSource();

                // 解析监听地址
                if (!IPAddress.TryParse(config.ListenAddress, out var listenIp))
                {
                    StatusChanged?.Invoke(this, $"无效的监听地址: {config.ListenAddress}");
                    return false;
                }

                localEndPoint = new IPEndPoint(listenIp, config.ListenPort);
                udpServer = new UdpClient(localEndPoint);

                // 设置接收缓冲区大小
                udpServer.Client.ReceiveBufferSize = config.ReceiveBufferSize;

                StatusChanged?.Invoke(this, $"UDP服务器已启动，监听 {localEndPoint}");

                // 启动接收数据的任务
                _ = Task.Run(() => ReceiveDataLoop(cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"UDP服务器启动失败: {ex.Message}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();

                udpServer?.Close();
                udpServer?.Dispose();
                udpServer = null;

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;

                knownClients.Clear();
                localEndPoint = null;

                StatusChanged?.Invoke(this, "UDP服务器已停止");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"停止UDP服务器时出错: {ex.Message}");
            }
        }

        public async Task SendDataToAllAsync(byte[] data)
        {
            var tasks = new List<Task>();

            foreach (var client in knownClients.Values)
            {
                tasks.Add(SendDataToClientAsync(data, client));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            else
            {
                StatusChanged?.Invoke(this, "没有已知的客户端，无法发送数据");
            }
        }

        public async Task SendDataToClientAsync(byte[] data, IPEndPoint clientEndPoint)
        {
            if (udpServer != null && IsListening)
            {
                try
                {
                    var bytesSent = await udpServer.SendAsync(data, data.Length, clientEndPoint);
                    if (bytesSent != data.Length)
                    {
                        StatusChanged?.Invoke(this, $"警告: 向 {clientEndPoint} 期望发送 {data.Length} 字节，实际发送 {bytesSent} 字节");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"向客户端 {clientEndPoint} 发送数据失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("UDP服务器未启动");
            }
        }

        public async Task SendDataToLastClientAsync(byte[] data)
        {
            var lastClient = knownClients.Values.LastOrDefault();
            if (lastClient != null)
            {
                await SendDataToClientAsync(data, lastClient);
            }
            else
            {
                StatusChanged?.Invoke(this, "没有已知的客户端");
            }
        }

        public async Task BroadcastDataAsync(byte[] data, int port)
        {
            if (udpServer != null && IsListening)
            {
                try
                {
                    var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, port);
                    var bytesSent = await udpServer.SendAsync(data, data.Length, broadcastEndPoint);
                    StatusChanged?.Invoke(this, $"广播数据到端口 {port}，发送 {bytesSent} 字节");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"广播数据失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("UDP服务器未启动");
            }
        }

        private async Task ReceiveDataLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && udpServer != null && IsListening)
                {
                    try
                    {
                        var result = await udpServer.ReceiveAsync();
                        if (result.Buffer.Length > 0)
                        {
                            var clientKey = result.RemoteEndPoint.ToString();

                            // 记录新发现的客户端
                            if (!knownClients.ContainsKey(clientKey))
                            {
                                knownClients[clientKey] = result.RemoteEndPoint;
                                ClientDiscovered?.Invoke(this, result.RemoteEndPoint);
                                StatusChanged?.Invoke(this, $"发现新客户端: {result.RemoteEndPoint} (总计: {knownClients.Count})");
                            }

                            // 触发数据接收事件
                            DataReceived?.Invoke(this, (result.Buffer, result.RemoteEndPoint));
                        }
                    }
                    catch (ObjectDisposedException)
                    {
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
                StatusChanged?.Invoke(this, $"UDP接收数据时出错: {ex.Message}");
            }
        }

        public void ClearKnownClients()
        {
            knownClients.Clear();
            StatusChanged?.Invoke(this, "已清空已知客户端列表");
        }

        public void RemoveClient(IPEndPoint clientEndPoint)
        {
            var clientKey = clientEndPoint.ToString();
            if (knownClients.TryRemove(clientKey, out _))
            {
                StatusChanged?.Invoke(this, $"已移除客户端: {clientEndPoint}");
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
