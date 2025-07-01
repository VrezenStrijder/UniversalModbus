using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UMClient.Models;

namespace UMClient.Services
{
    public class ConfigurationService
    {
        private const string ConfigFileName = "config.json";
        private const string SendTemplateFileName = "SendTemplate.txt";
        private const string AutoReplyFileName = "AutoReply.txt";

        public AppConfiguration LoadConfiguration()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    var json = File.ReadAllText(ConfigFileName);
                    return JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置文件失败: {ex.Message}");
            }

            return new AppConfiguration();
        }

        public void SaveConfiguration(AppConfiguration config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置文件失败: {ex.Message}");
            }
        }

        public List<string> LoadSendTemplates()
        {
            var templates = new List<string>();
            try
            {
                if (File.Exists(SendTemplateFileName))
                {
                    var lines = File.ReadAllLines(SendTemplateFileName);
                    templates.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载发送模板失败: {ex.Message}");
            }

            return templates;
        }

        public List<string> LoadAutoReplyTemplates()
        {
            var templates = new List<string>();
            try
            {
                if (File.Exists(AutoReplyFileName))
                {
                    var lines = File.ReadAllLines(AutoReplyFileName);
                    templates.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载自动回复模板失败: {ex.Message}");
            }

            return templates;
        }
    }


}
