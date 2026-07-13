using System;
using System.Collections.Generic;
using System.Linq;

namespace opc_ae_relay.config
{
    public static class AlarmConfigLoader
    {
        // 全局静态缓存：程序启动时加载一次
        private static List<AlarmRule> _alarmRules;

        static AlarmConfigLoader()
        {
            LoadAlarmRules();
        }

        /// <summary>
        ///     加载 XML 配置到内存（程序启动时调用）
        /// </summary>
        public static void LoadAlarmRules()
        {
            // 按 Code 长度倒序排序（避免短码优先匹配，如 IOP- 优先于 IOP）
            _alarmRules = AppConfigLoader.GetAlarmRules();
            _alarmRules = _alarmRules.OrderByDescending(r => r.Code.Length).ToList();
        }

        /// <summary>
        ///     获取所有加载的告警规则
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
            var substrLen = 15;
            return _alarmRules.FirstOrDefault(r =>
                (text.Length > substrLen ? text.Substring(text.Length - substrLen) : text).Contains(r.Code));
        }

        public static AlarmRule MatchNew(string eventTypeId)
        {
            if (string.IsNullOrWhiteSpace(eventTypeId)) return null;
            var alarmType = eventTypeId.Split(':').Last();
            //Log.Information(alarmType);
            return _alarmRules.FirstOrDefault(r => alarmType.Contains(r.Code));
        }

        public static void init()
        {
        }
    }
}