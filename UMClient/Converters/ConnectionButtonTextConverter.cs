using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using UMClient.Models;

namespace UMClient.Converters
{
    public class ConnectionButtonTextConverter : IMultiValueConverter
    {
        public static readonly ConnectionButtonTextConverter Instance = new ConnectionButtonTextConverter();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && (values[0] is bool isConnected) && values[1] is ConnectionMode connectionMode)
            {
                if (isConnected)
                {
                    return connectionMode switch
                    {
                        ConnectionMode.SerialPort => "断开串口",
                        ConnectionMode.TcpClient => "断开TCP",
                        ConnectionMode.TcpServer => "停止服务器",
                        ConnectionMode.UdpClient => "断开UDP",
                        ConnectionMode.UdpServer => "停止UDP服务器",
                        _ => "断开连接"
                    };
                }
                else
                {
                    return connectionMode switch
                    {
                        ConnectionMode.SerialPort => "打开串口",
                        ConnectionMode.TcpClient => "连接TCP",
                        ConnectionMode.TcpServer => "启动服务器",
                        ConnectionMode.UdpClient => "连接UDP",
                        ConnectionMode.UdpServer => "启动UDP服务器",
                        _ => "连接"
                    };
                }
            }

            return "连接";
        }
    }

}
