using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    public class SerialPortConfig
    {
        public string PortName { get; set; } = "COM1";

        public int BaudRate { get; set; } = 9600;

        public Parity Parity { get; set; } = Parity.None;

        public int DataBits { get; set; } = 8;

        public StopBits StopBits { get; set; } = StopBits.One;

        public Handshake Handshake { get; set; } = Handshake.None;
    }


}
