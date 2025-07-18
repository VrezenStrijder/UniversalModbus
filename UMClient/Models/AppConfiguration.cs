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

        #region 串口配置

        public string LastSerialPort { get; set; } = "COM1";
        public int LastBaudRate { get; set; } = 115200;
        public string LastParity { get; set; } = "None";
        public int LastDataBits { get; set; } = 8;
        public string LastStopBits { get; set; } = "One";
        public string LastHandshake { get; set; } = "None";

        #endregion

        #region TCP配置

        public string TcpServerAddress { get; set; } = "127.0.0.1";
        public int TcpServerPort { get; set; } = 9006;
        public string TcpListenAddress { get; set; } = "0.0.0.0";
        public int TcpListenPort { get; set; } = 9016;
        public int TcpConnectTimeout { get; set; } = 5000;
        public int TcpMaxClients { get; set; } = 20;

        #endregion

        #region UDP配置

        #endregion

        #region 通用配置

        public bool IsHexReceiveMode { get; set; } = false;
        public bool IsHexSendMode { get; set; } = false;
        public int AutoSendInterval { get; set; } = 1000;
        public bool IsAutoSendEnabled { get; set; } = false;

        public bool ShowTimestamp { get; set; } = true;
        public bool AutoWrap { get; set; } = true;
        public bool AutoScroll { get; set; } = true;
        public bool SendNewLine { get; set; } = true;

        public bool IsCycleSend { get; set; } = false;
        public int CycleSendCount { get; set; } = 5;

        public string LastSendData { get; set; } = string.Empty;
        public List<string> SendHistory { get; set; } = new List<string>();

        #endregion

        #region 窗口状态

        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public bool WindowMaximized { get; set; } = false;

        #endregion


    }


}
