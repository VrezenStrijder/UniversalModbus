using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    public class TcpServerConfig
    {
        public string ListenAddress { get; set; } = "0.0.0.0";

        public int ListenPort { get; set; } = 9010;

        public int MaxClients { get; set; } = 20;
    }

    public class TcpClientConfig
    {
        public string ServerAddress { get; set; } = "127.0.0.1";

        public int ServerPort { get; set; } = 9010;

        public int ConnectTimeout { get; set; } = 5000; // 连接超时(ms)
    }


}
