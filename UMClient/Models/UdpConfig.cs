using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    public class UdpServerConfig
    {
        public string ListenAddress { get; set; } = "0.0.0.0";
        public int ListenPort { get; set; } = 9060;
        public int ReceiveBufferSize { get; set; } = 4096;
    }

    public class UdpClientConfig
    {
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 9060;
        public int LocalPort { get; set; } = 0; // 0表示自动分配
        public int ReceiveTimeout { get; set; } = 5000; // 接收超时(ms)
    }


}
