using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using DynamicData;
using UMClient.Controls;
using UMClient.Models;

namespace UMClient.Services
{
    public class AutoSendService
    {

        public async Task<AutoSendData> ProcessImportedContent(string content, string fileName, bool isConnected)
        {
            var ret = new AutoSendData();

            // 按换行符分割内容
            var lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
            {
                ret.StatusText = "文件中没有有效内容";
                return ret;
            }

            // 清理每行内容, 去除时间戳和模式提示
            var cleanedLines = lines.Select(CleanLineContent).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            if (cleanedLines.Count == 0)
            {
                ret.StatusText = "文件中没有有效的发送内容";
                return ret;
            }

            if (cleanedLines.Count == 1)
            {
                ret.SendData = cleanedLines[0];
                ret.StatusText = $"已从 {fileName} 导入单行数据";
            }
            else
            {
                // 多行内容, 询问用户是否要自动发送
                var opt = await ShowImportOptionsDialog(cleanedLines.Count, isConnected);

                ret.ImportOption = opt;
                ret.SendLines = cleanedLines;

                switch (opt)
                {
                    case ImportOption.SetToSendBox:
                        // 将所有行合并到发送框(用换行符分隔)
                        ret.SendData = string.Join(Environment.NewLine, cleanedLines);
                        ret.StatusText = $"已从 {fileName} 导入 {cleanedLines.Count} 行数据到发送框";
                        break;

                    case ImportOption.AutoSend:
                        // 启动自动发送
                        //await StartAutoSendLines(cleanedLines, fileName);
                        break;

                    case ImportOption.Cancel:
                        ret.StatusText = "导入已取消";
                        break;
                }
            }

            return ret;
        }

        private string CleanLineContent(string line)
        {
            // 过滤掉空行和接收行
            if (string.IsNullOrWhiteSpace(line) || line.Contains("接收"))
            {
                return string.Empty;
            }
            var cleanedLine = line.Trim();

            // 使用正则表达式去除时间戳和模式提示
            // 匹配模式: [时间戳] 发送: 内容 或 [时间戳] 接收: 内容
            var timeStampPattern = @"^\[[\d:\.]+\]\s*(发送|接收):\s*";
            var match = System.Text.RegularExpressions.Regex.Match(cleanedLine, timeStampPattern);

            if (match.Success)
            {
                // 去除时间戳和模式提示, 只保留内容部分
                cleanedLine = cleanedLine.Substring(match.Length).Trim();
            }
            else
            {
                // 如果没有时间戳, 尝试去除简单的模式提示
                var simplePrefixes = new[] { "发送", " [发送]" };
                foreach (var prefix in simplePrefixes)
                {
                    if (cleanedLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanedLine = cleanedLine.Substring(prefix.Length).Trim();
                        break;
                    }
                }
            }

            return cleanedLine;
        }

        private async Task<ImportOption> ShowImportOptionsDialog(int lineCount, bool isConnected)
        {
            // 如果没有连接, 添加内容至发送框
            if (!isConnected)
            {
                return ImportOption.SetToSendBox;
            }

            // 如果已连接, 默认启用自动发送
            return ImportOption.AutoSend;
        }


    }


    public struct AutoSendData
    {
        public string StatusText { get; set; }

        public ImportOption ImportOption { get; set; }

        public string SendData { get; set; }

        public List<string> SendLines { get; set; }

    }

    public enum ImportOption
    {
        Cancel,
        SetToSendBox,
        AutoSend
    }
}
