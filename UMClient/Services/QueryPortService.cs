using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reactive.Joins;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UMClient.Models;

namespace UMClient.Services
{
    public class QueryPortService
    {

        #region Windows

        public List<string> GetWindowsSerialPorts()
        {
            var ports = SerialPort.GetPortNames().ToList();
            return ports;
        }

        public List<SerialPortInfo> GetWindowsSerialPortInfo()
        {
            var ports = new List<SerialPortInfo>();

            try
            {
                // 使用 WMI 获取详细信息
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ClassGuid=\"{4d36e978-e325-11ce-bfc1-08002be10318}\"");

                var portNames = SerialPort.GetPortNames().ToHashSet();

                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? "";

                    string pattern = @"^(.*?)[(\[]*(COM\d+).*";
                    Match match = Regex.Match(name, pattern);
                    if (match.Success && match.Groups.Count > 2)
                    {
                        string prefix = match.Groups[1].Value;
                        string comPort = match.Groups[2].Value;
                        string friendlyName = Regex.Replace(prefix, @"^[^\w]+|[^\w]+$", "");

                        if (portNames.Contains(comPort))
                        {
                            ports.Add(new SerialPortInfo
                            {
                                PortName = comPort,
                                FriendlyName = friendlyName,
                                Description = name,
                                Manufacturer = manufacturer
                            });
                        }
                    }

                    // 从名称中提取COM端口号
                    //var match = System.Text.RegularExpressions.Regex.Match(name, @"(.*)\(COM(\d+)\)");
                    //if (match.Success)
                    //{
                    //    var comPort = $"COM{match.Groups[1].Value}";
                    //    if (portNames.Contains(comPort))
                    //    {
                    //        ports.Add(new SerialPortInfo
                    //        {
                    //            PortName = comPort,
                    //            FriendlyName = name.Replace($"({comPort})", "").Trim(),
                    //            Description = name,
                    //            Manufacturer = manufacturer
                    //        });
                    //    }
                    //}
                }

                // 添加没有详细信息的端口
                foreach (var portName in portNames)
                {
                    if (!ports.Any(p => p.PortName == portName))
                    {
                        ports.Add(new SerialPortInfo
                        {
                            PortName = portName,
                            FriendlyName = "串口设备",
                            Description = portName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows串口枚举错误: {ex.Message}");

                // 回退到基本方法
                var basicPorts = SerialPort.GetPortNames();
                foreach (var port in basicPorts)
                {
                    ports.Add(new SerialPortInfo
                    {
                        PortName = port,
                        FriendlyName = "串口设备",
                        Description = port
                    });
                }
            }

            return ports;
        }

        #endregion

        #region Linux

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

        public List<SerialPortInfo> GetLinuxSerialPortInfo()
        {
            var ports = new List<SerialPortInfo>();

            try
            {
                var devicePaths = new[]
                {
                    "/dev/ttyS*",
                    "/dev/ttyUSB*",
                    "/dev/ttyACM*",
                    "/dev/ttyAMA*",
                    "/dev/ttymxc*",
                    "/dev/ttyO*"
                };

                foreach (var pattern in devicePaths)
                {
                    var directory = Path.GetDirectoryName(pattern) ?? "/dev";
                    var filePattern = Path.GetFileName(pattern);

                    if (Directory.Exists(directory))
                    {
                        var matchingFiles = Directory.GetFiles(directory, filePattern)
                            .Where(f => IsValidSerialPort(f))
                            .OrderBy(f => f);

                        foreach (var devicePath in matchingFiles)
                        {
                            var friendlyName = GetLinuxPortFriendlyName(devicePath);
                            ports.Add(new SerialPortInfo
                            {
                                PortName = devicePath,
                                FriendlyName = friendlyName,
                                Description = $"{friendlyName} ({devicePath})"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Linux串口枚举错误: {ex.Message}");
            }

            return ports;
        }

        private string GetLinuxPortFriendlyName(string devicePath)
        {
            try
            {
                var deviceName = Path.GetFileName(devicePath);

                // 尝试从 /sys/class/tty/ 获取设备信息
                var sysPath = $"/sys/class/tty/{deviceName}/device";
                if (Directory.Exists(sysPath))
                {
                    // 读取制造商信息
                    var manufacturerFile = Path.Combine(sysPath, "../manufacturer");
                    var productFile = Path.Combine(sysPath, "../product");

                    string manufacturer = "";
                    string product = "";

                    if (File.Exists(manufacturerFile))
                    {
                        manufacturer = File.ReadAllText(manufacturerFile).Trim();
                    }

                    if (File.Exists(productFile))
                    {
                        product = File.ReadAllText(productFile).Trim();
                    }

                    if (!string.IsNullOrEmpty(manufacturer) && !string.IsNullOrEmpty(product))
                    {
                        return $"{manufacturer} {product}";
                    }
                    else if (!string.IsNullOrEmpty(product))
                    {
                        return product;
                    }
                }

                // 根据设备名称推断类型
                if (deviceName.StartsWith("ttyUSB"))
                {
                    return "USB串口转换器";
                }
                else if (deviceName.StartsWith("ttyACM"))
                {
                    return "USB CDC设备";
                }
                else if (deviceName.StartsWith("ttyAMA"))
                {
                    return "ARM串口";
                }
                else if (deviceName.StartsWith("ttyS"))
                {
                    return "标准串口";
                }
                else
                {
                    return "串口设备";
                }
            }
            catch
            {
                return "串口设备";
            }
        }


        #endregion

        #region macOS
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


        public List<SerialPortInfo> GetMacOSSerialPortInfo()
        {
            var ports = new List<SerialPortInfo>();

            try
            {
                var devicePaths = Directory.GetFiles("/dev", "cu.*")
                    .Concat(Directory.GetFiles("/dev", "tty.*"))
                    .Where(f => IsValidSerialPort(f) && !f.Contains("Bluetooth"))
                    .OrderBy(f => f);

                foreach (var devicePath in devicePaths)
                {
                    var friendlyName = GetMacOSPortFriendlyName(devicePath);
                    ports.Add(new SerialPortInfo
                    {
                        PortName = devicePath,
                        FriendlyName = friendlyName,
                        Description = $"{friendlyName} ({devicePath})"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"macOS串口枚举错误: {ex.Message}");
            }

            return ports;
        }

        private string GetMacOSPortFriendlyName(string devicePath)
        {
            var deviceName = Path.GetFileName(devicePath);

            if (deviceName.Contains("usbserial"))
            {
                return "USB串口转换器";
            }
            else if (deviceName.Contains("usbmodem"))
            {
                return "USB调制解调器";
            }
            else if (deviceName.Contains("serial"))
            {
                return "串口设备";
            }
            else
            {
                return "通信设备";
            }
        }

        #endregion

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
