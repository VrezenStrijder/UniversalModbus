using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UMClient.Models;
using System.Net.NetworkInformation;

namespace UMClient.Services
{
    public class UdpClientService : IDisposable
    {
        private UdpClient? udpClient;
        private bool disposed = false;
        private CancellationTokenSource? cancellationTokenSource;
        private IPEndPoint? serverEndPoint;
        private IPEndPoint? localEndPoint;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<string>? StatusChanged;

        public bool IsConnected => udpClient != null && !disposed;
        public string? LocalEndPoint => localEndPoint?.ToString();
        public string? ServerEndPoint => serverEndPoint?.ToString();

        public async Task<bool> ConnectAsync(UdpClientConfig config)
        {
            try
            {
                await DisconnectAsync();

                cancellationTokenSource = new CancellationTokenSource();

                // 解析服务器地址
                if (!IPAddress.TryParse(config.ServerAddress, out var serverIp))
                {
                    var hostEntry = await Dns.GetHostEntryAsync(config.ServerAddress);
                    serverIp = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                    if (serverIp == null)
                    {
                        StatusChanged?.Invoke(this, $"无法解析服务器地址: {config.ServerAddress}");
                        return false;
                    }
                }

                serverEndPoint = new IPEndPoint(serverIp, config.ServerPort);
                udpClient = new UdpClient();

                // 创建UDP客户端
                if (config.LocalPort > 0)
                {
                    localEndPoint = new IPEndPoint(IPAddress.Any, config.LocalPort); // 普通模式
                }
                else
                {
                    localEndPoint = new IPEndPoint(IPAddress.Any, config.ServerPort); // 广播模式，绑定服务端监听端口
                }

                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(localEndPoint);


                //
                //localEndPoint = new IPEndPoint(IPAddress.Any, listenPort);
                //udpClient = new UdpClient();
                //// 必须设置 ReuseAddress，允许多个客户端实例在同一台机器上监听同一端口
                //udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                //// 对于只接收的客户端，EnableBroadcast 不是必需的，但设置也无害
                //udpClient.EnableBroadcast = true;
                //udpClient.Client.Bind(localEndPoint);
                //

                // 设置接收超时
                udpClient.Client.ReceiveTimeout = config.ReceiveTimeout;

                StatusChanged?.Invoke(this, $"UDP客户端已启动，本地端点: {localEndPoint}, 目标服务器: {serverEndPoint}");

                // 启动接收数据的任务
                _ = Task.Run(() => ReceiveDataLoop(cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"UDP客户端启动失败: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();

                udpClient?.Close();
                udpClient?.Dispose();
                udpClient = null;

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;

                serverEndPoint = null;
                localEndPoint = null;

                StatusChanged?.Invoke(this, "UDP客户端已断开");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"断开UDP客户端时出错: {ex.Message}");
            }
        }

        public async Task SendDataAsync(byte[] data)
        {
            if (udpClient != null && serverEndPoint != null && IsConnected)
            {
                try
                {
                    var bytesSent = await udpClient.SendAsync(data, data.Length, serverEndPoint);
                    if (bytesSent != data.Length)
                    {
                        StatusChanged?.Invoke(this, $"警告: 期望发送 {data.Length} 字节，实际发送 {bytesSent} 字节");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"UDP发送数据失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("UDP客户端未连接");
            }
        }

        public async Task SendDataToAsync(byte[] data, string address, int port)
        {
            if (udpClient != null && IsConnected)
            {
                try
                {
                    var targetEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
                    var bytesSent = await udpClient.SendAsync(data, data.Length, targetEndPoint);
                    if (bytesSent != data.Length)
                    {
                        StatusChanged?.Invoke(this, $"警告: 期望发送 {data.Length} 字节，实际发送 {bytesSent} 字节");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"UDP发送数据到 {address}:{port} 失败: {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("UDP客户端未连接");
            }
        }

        private async Task ReceiveDataLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && udpClient != null && IsConnected)
                {
                    try
                    {
                        var result = await udpClient.ReceiveAsync();
                        if (result.Buffer.Length > 0)
                        {
                            // 触发数据接收事件，包含发送方信息
                            var senderInfo = $"来自 {result.RemoteEndPoint}: ";
                            var dataWithSender = Encoding.UTF8.GetBytes(senderInfo).Concat(result.Buffer).ToArray();
                            DataReceived?.Invoke(this, result.Buffer); // 原始数据

                            // 可以选择是否在状态中显示发送方信息
                            StatusChanged?.Invoke(this, $"收到来自 {result.RemoteEndPoint} 的数据，{result.Buffer.Length} 字节");
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        // UDP客户端已被释放
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
