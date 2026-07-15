using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace opc_ae_relay.config
{
    public static class Config
    {
        public static void InitAll()
        {
            LogConfig.Init();
            AlarmConfigLoader.Init();
            TagFilterConfig.Init();
        }
        
        public static void StopAll()
        {
            TagFilterConfig.Shutdown();
        }
    }

    public static class AppConfigLoader
    {
        static AppConfigLoader()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application.xml");
                var serializer = new XmlSerializer(typeof(ApplicationConfig));
                using (var fs = File.OpenRead(path))
                {
                    Config = (ApplicationConfig)serializer.Deserialize(fs);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("加载 application.xml 失败：" + ex.Message);
            }
        }

        public static ApplicationConfig Config { get; }

        public static string GetDbConnection(string name = "Default")
        {
            // 优先从新配置中查找
            var dbConfig = Config.Databases?.FirstOrDefault(d => d.Name == name);
            if (dbConfig != null)
            {
                return dbConfig.ConnectionString;
            }

            // 兼容旧版：如果 name 是 "Default"，查找 isDefault=true 的配置
            if (name == "Default")
            {
                dbConfig = Config.Databases?.FirstOrDefault(d => d.IsDefault);
                if (dbConfig != null)
                {
                    return dbConfig.ConnectionString;
                }
            }

            throw new Exception($"未找到数据库配置：{name}");
        }

        public static List<AlarmRule> GetAlarmRules()
        {
            return Config.AlarmRules;
        }

        public static List<OPCServerConfig> GetOPCServers()
        {
            return Config.OPCServers;
        }
    }


    [XmlRoot("ApplicationConfig")]
    public class ApplicationConfig
    {
        [XmlArray("Databases")]
        [XmlArrayItem("Database")]
        public List<DatabaseConfig> Databases { get; set; }

        [XmlArray("MQs")]
        [XmlArrayItem("MQ")]
        public List<MQConfig> MQs { get; set; }

        [XmlArray("OPCServers")]
        [XmlArrayItem("Server")]
        public List<OPCServerConfig> OPCServers { get; set; }

        [XmlElement("Web")] public WebServerConfig Web { get; set; }

        [XmlArray("AlarmRules")]
        [XmlArrayItem("Rule")]
        public List<AlarmRule> AlarmRules { get; set; }
    }

    public class DatabaseConfig
    {
        /// <summary>
        /// 数据库实例名称，用于日志标识和多实例区分
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// 数据库类型标识（如 sqlserver, mysql），决定工厂创建哪种 Provider 实例
        /// </summary>
        [XmlAttribute("type")]
        public string Type { get; set; } = "sqlserver";

        /// <summary>
        /// 是否启用该数据库实例，默认启用；设为 false 则启动时跳过初始化
        /// </summary>
        [XmlAttribute("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 连接字符串
        /// </summary>
        [XmlAttribute("connectionString")]
        public string ConnectionString { get; set; }

        /// <summary>
        /// 是否为默认数据库，用于兼容旧版 DBUtil.GetConnection() 无参调用
        /// </summary>
        [XmlAttribute("isDefault")]
        public bool IsDefault { get; set; } = false;
    }

    /// <summary>
    /// MQ 消息队列配置模型，对应 application.xml 中 &lt;MQs&gt; 节点下的单个 &lt;MQ&gt; 配置项。
    /// 支持通过 type 属性区分不同 MQ 类型（如 rabbitmq），通过 enabled 属性控制是否启用。
    /// </summary>
    public class MQConfig
    {
        /// <summary>
        /// MQ 实例名称，用于日志标识和多实例区分
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// MQ 类型标识（如 rabbitmq），决定工厂创建哪种 Producer 实例
        /// </summary>
        [XmlAttribute("type")]
        public string Type { get; set; }

        /// <summary>
        /// 是否启用该 MQ 实例，默认启用；设为 false 则启动时跳过初始化
        /// </summary>
        [XmlAttribute("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// MQ 服务器地址（IP 或域名）
        /// </summary>
        [XmlElement("Host")]
        public string Host { get; set; }

        /// <summary>
        /// MQ 服务器端口号（如 RabbitMQ 默认 5672）
        /// </summary>
        [XmlElement("Port")]
        public int Port { get; set; }

        /// <summary>
        /// 连接认证用户名
        /// </summary>
        [XmlElement("User")]
        public string User { get; set; }

        /// <summary>
        /// 连接认证密码
        /// </summary>
        [XmlElement("Password")]
        public string Password { get; set; }

        /// <summary>
        /// 虚拟主机路径，RabbitMQ 默认 "/"
        /// </summary>
        [XmlElement("VirtualHost")]
        public string VirtualHost { get; set; } = "/";

        /// <summary>
        /// 交换机名称（如 opc_ae_topic_exchange）
        /// </summary>
        [XmlElement("Exchange")]
        public string Exchange { get; set; }

        /// <summary>
        /// 交换机类型，支持 topic / fanout / direct，默认 topic
        /// </summary>
        [XmlElement("ExchangeType")]
        public string ExchangeType { get; set; } = "topic";

        /// <summary>
        /// 队列名称（如 mes_data_queue）
        /// </summary>
        [XmlElement("QueueName")]
        public string QueueName { get; set; }

        /// <summary>
        /// 路由键前缀，最终路由键格式为 "{RoutingKeyPrefix}.{sourceName}"，默认 "opc.ae"
        /// </summary>
        [XmlElement("RoutingKeyPrefix")]
        public string RoutingKeyPrefix { get; set; } = "opc.ae";

        /// <summary>
        /// 交换机和队列是否持久化（重启后保留），默认 true
        /// </summary>
        [XmlElement("Durable")]
        public bool Durable { get; set; } = true;

        /// <summary>
        /// 消息是否持久化到磁盘，false 表示消息仅存内存，默认 false
        /// </summary>
        [XmlElement("Persistent")]
        public bool Persistent { get; set; } = false;

        /// <summary>
        /// 心跳间隔秒数，用于检测连接存活状态，默认 30 秒
        /// </summary>
        [XmlElement("HeartbeatSeconds")]
        public int HeartbeatSeconds { get; set; } = 30;

        /// <summary>
        /// 断线自动重连间隔秒数，默认 5 秒
        /// </summary>
        [XmlElement("RecoveryIntervalSeconds")]
        public int RecoveryIntervalSeconds { get; set; } = 5;
    }

    public class OPCServerConfig
    {
        [XmlAttribute("name")] public string Name { get; set; }

        [XmlAttribute("progId")] public string ProgId { get; set; }

        [XmlAttribute("ip")] public string IP { get; set; }

        [XmlAttribute("port")] public int Port { get; set; }
    }

    public class WebServerConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 9000;

        public string BaseUrl => $"http://{Host}:{Port}";
    }

    /// <summary>
    ///     告警规则配置
    /// </summary>
    public class AlarmRule
    {
        /// <summary>
        ///     告警码（如 IOP-、HH、MAN）
        /// </summary>
        [XmlAttribute("Code")]
        public string Code { get; set; }

        /// <summary>
        ///     分类（AlarmState/DataState/ModeState）
        /// </summary>
        [XmlAttribute("Category")]
        public string Category { get; set; }

        /// <summary>
        ///     事件类型（Alarm/Status/Mode）
        /// </summary>
        [XmlAttribute("EventType")]
        public string EventType { get; set; }

        /// <summary>
        ///     告警级别（1=提示，2=警告，3=紧急）
        /// </summary>
        [XmlAttribute("Severity")]
        public int Severity { get; set; }

        /// <summary>
        ///     中文名称
        /// </summary>
        [XmlAttribute("Name")]
        public string Name { get; set; }

        /// <summary>
        ///     详细描述
        /// </summary>
        [XmlAttribute("Desc")]
        public string Description { get; set; }
    }
}