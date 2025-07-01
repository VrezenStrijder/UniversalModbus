using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    public enum ConnectionMode
    {
        SerialPort,
        TcpClient,
        TcpServer,
        UdpClient,
        UdpServer
    }


}
