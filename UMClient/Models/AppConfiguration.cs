using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    public class AppConfiguration
    {
        public ConnectionMode LastConnectionMode { get; set; } = ConnectionMode.SerialPort;
        public string LastSerialPort { get; set; } = string.Empty;
        public int LastBaudRate { get; set; } = 9600;
        public bool IsHexReceiveMode { get; set; } = false;
        public bool IsHexSendMode { get; set; } = false;
        public int AutoSendInterval { get; set; } = 1000;
        public bool IsAutoSendEnabled { get; set; } = false;
        public string LastSendData { get; set; } = string.Empty;
    }


}
