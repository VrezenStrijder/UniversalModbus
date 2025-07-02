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

        public string LastSerialPort { get; set; } = "COM1";
        public int LastBaudRate { get; set; } = 115200;
        public string LastParity { get; set; } = "None";
        public int LastDataBits { get; set; } = 8;
        public string LastStopBits { get; set; } = "One";
        public string LastHandshake { get; set; } = "None";

        public bool IsHexReceiveMode { get; set; } = false;
        public bool IsHexSendMode { get; set; } = false;
        public int AutoSendInterval { get; set; } = 1000;
        public bool IsAutoSendEnabled { get; set; } = false;

        public bool ShowTimestamp { get; set; } = true;
        public bool AutoWrap { get; set; } = true;
        public bool AutoScroll { get; set; } = true;
        public bool SendNewLine { get; set; } = true;

        public string LastSendData { get; set; } = string.Empty;

        public List<string> SendHistory { get; set; } = new List<string>();

    }


}
