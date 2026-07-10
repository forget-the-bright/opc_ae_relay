using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace opcLearn.config
{

    public static class Config
    {
        public static void initAll()
        {
            LogConfig.init();
            AlarmConfigLoader.init();
        }
    }

    public static class AppConfigLoader
    {
        private static readonly ApplicationConfig _config;

        static AppConfigLoader()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application.xml");
                var serializer = new XmlSerializer(typeof(ApplicationConfig));
                using (var fs = File.OpenRead(path))
                {
                    _config = (ApplicationConfig)serializer.Deserialize(fs);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("加载 application.xml 失败：" + ex.Message);
            }
        }

        public static ApplicationConfig Config => _config;

        public static string GetDbConnection(string name = "Default")
        {
            return _config.Databases?.Find(d => d.Name == name)?.ConnectionString
                   ?? throw new Exception($"未找到数据库配置：{name}");
        }
        public static List<AlarmRule> GetAlarmRules()
        {
            return _config.AlarmRules;
        }
    }



    [XmlRoot("ApplicationConfig")]
    public class ApplicationConfig
    {
        [XmlArray("Databases"), XmlArrayItem("Database")]
        public List<DatabaseConfig> Databases { get; set; }

        [XmlElement("MQ")]
        public MQConfig MQ { get; set; }

        [XmlArray("OPCServers"), XmlArrayItem("Server")]
        public List<OPCServerConfig> OPCServers { get; set; }

        [XmlArray("AlarmRules"), XmlArrayItem("Rule")]
        public List<AlarmRule> AlarmRules { get; set; }
    }

    public class DatabaseConfig
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("connectionString")]
        public string ConnectionString { get; set; }
    }

    public class MQConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string QueueName { get; set; }
    }

    public class OPCServerConfig
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
        [XmlAttribute("progId")]
        public string ProgId { get; set; }
        [XmlAttribute("ip")]
        public string IP { get; set; }
        [XmlAttribute("port")]
        public int Port { get; set; }
    }
    /// <summary>
    /// 告警规则配置
    /// </summary>
    public class AlarmRule
    {
        /// <summary>
        /// 告警码（如 IOP-、HH、MAN）
        /// </summary>
        [XmlAttribute("Code")]
        public string Code { get; set; }

        /// <summary>
        /// 分类（AlarmState/DataState/ModeState）
        /// </summary>
        [XmlAttribute("Category")]
        public string Category { get; set; }

        /// <summary>
        /// 事件类型（Alarm/Status/Mode）
        /// </summary>
        [XmlAttribute("EventType")]
        public string EventType { get; set; }

        /// <summary>
        /// 告警级别（1=提示，2=警告，3=紧急）
        /// </summary>
        [XmlAttribute("Severity")]
        public int Severity { get; set; }

        /// <summary>
        /// 中文名称
        /// </summary>
        [XmlAttribute("Name")]
        public string Name { get; set; }

        /// <summary>
        /// 详细描述
        /// </summary>
        [XmlAttribute("Desc")]
        public string Description { get; set; }
    }
}
