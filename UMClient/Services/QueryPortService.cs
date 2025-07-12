using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMClient.Services
{
    public class QueryPortService
    {

        public List<string> GetWindowsSerialPorts()
        {
            var ports = SerialPort.GetPortNames().ToList();
            return ports;
        }

        public List<string> GetLinuxSerialPorts()
        {
            var ports = new List<string>();

            try
            {
                // Linux常见的串口设备路径
                var commonPaths = new[]
                {
                    "/dev/ttyS*",      // 标准串口
                    "/dev/ttyUSB*",    // USB转串口
                    "/dev/ttyACM*",    // USB CDC ACM设备
                    "/dev/ttyAMA*",    // ARM串口（如树莓派）
                    "/dev/ttymxc*",    // i.MX处理器串口
                    "/dev/ttyO*",      // OMAP处理器串口
                };

                foreach (var pattern in commonPaths)
                {
                    var devicePath = pattern.Replace("*", "");
                    var directory = Path.GetDirectoryName(devicePath) ?? "/dev";
                    var filePattern = Path.GetFileName(pattern);

                    if (Directory.Exists(directory))
                    {
                        var matchingFiles = Directory.GetFiles(directory, filePattern)
                            .Where(f => IsValidSerialPort(f))
                            .OrderBy(f => f);

                        ports.AddRange(matchingFiles);
                    }
                }

                // 去重并排序
                ports = ports.Distinct().OrderBy(p => p).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Linux串口枚举错误: {ex.Message}");
            }

            return ports;
        }

        public List<string> GetMacOSSerialPorts()
        {
            var ports = new List<string>();

            try
            {
                // macOS常见的串口设备路径
                var commonPaths = new[]
                {
                    "/dev/cu.*",       // Call-out设备
                    "/dev/tty.*",      // Terminal设备
                };

                foreach (var pattern in commonPaths)
                {
                    var directory = "/dev";
                    var filePattern = pattern.Replace("/dev/", "");

                    if (Directory.Exists(directory))
                    {
                        var matchingFiles = Directory.GetFiles(directory, filePattern)
                            .Where(f => IsValidSerialPort(f) && !f.Contains("Bluetooth"))
                            .OrderBy(f => f);

                        ports.AddRange(matchingFiles);
                    }
                }

                ports = ports.Distinct().OrderBy(p => p).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"macOS串口枚举错误: {ex.Message}");
            }

            return ports;
        }

        private bool IsValidSerialPort(string devicePath)
        {
            try
            {
                // 检查设备文件是否存在且可访问
                if (!File.Exists(devicePath))
                {
                    return false;
                }
                // 检查是否是字符设备
                var fileInfo = new FileInfo(devicePath);

                // 尝试获取文件属性，如果成功说明设备可访问
                var attributes = fileInfo.Attributes;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
