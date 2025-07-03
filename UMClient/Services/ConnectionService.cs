using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public interface IConnection
    {
        bool IsConnected { get; }
        Task<bool> ConnectAsync();
        Task DisconnectAsync();
        Task SendAsync(byte[] data);

        event EventHandler<byte[]>? DataReceived;
    }

    public class ConnectionService
    {
        private readonly List<IConnection> connections = new List<IConnection>();

        public event EventHandler<string>? DataReceived;
        public event EventHandler<string>? StatusChanged;

        public async Task<bool> ConnectAsync(ConnectionMode mode, string endpoint)
        {
            await Task.Delay(100); // 模拟异步操作
            return true;
        }

        public async Task DisconnectAsync()
        {
            await Task.Delay(100);
        }

        public async Task SendDataAsync(string data, bool isHex = false)
        {
            await Task.Delay(10);
        }
    }



}
