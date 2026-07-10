using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace opcLearn.config
{
    /// <summary>
    /// 告警规则配置
    /// </summary>
    public class AlarmRule
    {
        /// <summary>
        /// 告警码（如 IOP-、HH、MAN）
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// 分类（AlarmState/DataState/ModeState）
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// 事件类型（Alarm/Status/Mode）
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// 告警级别（1=提示，2=警告，3=紧急）
        /// </summary>
        public int Severity { get; set; }

        /// <summary>
        /// 中文名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 详细描述
        /// </summary>
        public string Description { get; set; }
    }

    public static class AlarmConfigLoader
    {
        // 全局静态缓存：程序启动时加载一次
        private static List<AlarmRule> _alarmRules;

        static AlarmConfigLoader(){
            LoadAlarmRules();
        }

        /// <summary>
        /// 加载 XML 配置到内存（程序启动时调用）
        /// </summary>
        public static void LoadAlarmRules(string xmlFilePath = null)
        {
            if (xmlFilePath == null)
            {
                xmlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlarmRule.xml");
            }

            if (!File.Exists(xmlFilePath))
                throw new FileNotFoundException("告警规则配置文件不存在", xmlFilePath);

            XDocument doc = XDocument.Load(xmlFilePath);
            _alarmRules = new List<AlarmRule>();

            foreach (var ruleElement in doc.Root.Elements("Rule"))
            {
                int severity = 0;
                int.TryParse(ruleElement.Attribute("Severity")?.Value, out severity);

                _alarmRules.Add(new AlarmRule
                {
                    Code = ruleElement.Attribute("Code").Value.Trim(),
                    Category = ruleElement.Attribute("Category")?.Value.Trim() ?? "",
                    EventType = ruleElement.Attribute("EventType")?.Value.Trim() ?? "",
                    Severity = severity,
                    Name = ruleElement.Attribute("Name")?.Value.Trim() ?? "",
                    Description = ruleElement.Attribute("Desc")?.Value.Trim() ?? ""
                });
            }

            // 按 Code 长度倒序排序（避免短码优先匹配，如 IOP- 优先于 IOP）
            _alarmRules = _alarmRules.OrderByDescending(r => r.Code.Length).ToList();
        }

        /// <summary>
        /// 获取所有加载的告警规则
        /// </summary>
        public static List<AlarmRule> GetAllRules()
        {
            if (_alarmRules == null)
                throw new InvalidOperationException("请先调用 LoadAlarmRules() 加载配置");
            return _alarmRules;
        }

        // 匹配告警码方法
        public static AlarmRule Match(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int substrLen = 15;
            return _alarmRules.FirstOrDefault(r => (text.Length > substrLen ? text.Substring(text.Length - substrLen) : text).Contains(r.Code));
        }
        public static AlarmRule MatchNew(string eventTypeId)
        {
            if (string.IsNullOrWhiteSpace(eventTypeId)) return null;
            string alarmType = eventTypeId.Split(':').Last();
            //Log.Information(alarmType);
            return _alarmRules.FirstOrDefault(r => alarmType.Contains(r.Code));
        }
        public static void init() { 
        }
    }
}
