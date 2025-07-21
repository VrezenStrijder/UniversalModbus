using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Models
{
    /// <summary>
    /// 串口信息
    /// </summary>
    public class SerialPortInfo : IComparable<SerialPortInfo>
    {
        public string PortName { get; set; } = string.Empty;

        public string FriendlyName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Manufacturer { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrEmpty(FriendlyName) ? PortName : $"{PortName} ({FriendlyName})";

        public override string ToString() => DisplayName;

        public int CompareTo(SerialPortInfo? other)
        {
            if (other == null)
            {
                return 1;
            }
            // 首先按端口名称排序
            var portComparison = ComparePortNames(PortName, other.PortName);
            if (portComparison != 0)
            {
                return portComparison;
            }
            // 如果端口名称相同，按友好名称排序
            return string.Compare(FriendlyName, other.FriendlyName, StringComparison.OrdinalIgnoreCase);
        }

        private static int ComparePortNames(string port1, string port2)
        {
            // 提取端口类型和数字进行智能排序
            var (type1, num1) = ExtractPortInfo(port1);
            var (type2, num2) = ExtractPortInfo(port2);

            // 首先按类型排序
            var typeComparison = string.Compare(type1, type2, StringComparison.OrdinalIgnoreCase);
            if (typeComparison != 0)
            {
                return typeComparison;
            }
            // 然后按数字排序
            return num1.CompareTo(num2);
        }

        private static (string type, int number) ExtractPortInfo(string portName)
        {
            if (string.IsNullOrEmpty(portName))
            {
                return ("", 0);
            }

            var windowsMatch = System.Text.RegularExpressions.Regex.Match(portName, @"^(COM)(\d+)$");
            if (windowsMatch.Success)
            {
                return (windowsMatch.Groups[1].Value, int.Parse(windowsMatch.Groups[2].Value));
            }

            var linuxMatch = System.Text.RegularExpressions.Regex.Match(portName, @"^/dev/(tty\w+?)(\d+)$");
            if (linuxMatch.Success)
            {
                return (linuxMatch.Groups[1].Value, int.Parse(linuxMatch.Groups[2].Value));
            }

            return (portName, 0);
        }
    }

}
