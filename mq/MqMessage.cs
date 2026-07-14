using System;
using Newtonsoft.Json;
using opc_ae_relay.client;

namespace opc_ae_relay.mq
{
    public class MqMessage
    {
        public string SourceName { get; set; }
        public string Message { get; set; }
        public int Severity { get; set; }
        public string Timestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");


        public static MqMessage BuildMessage(AlarmEventData alarmData)
        {
            if (alarmData == null || alarmData.Message == null)
            {
                return null;
            }
            string json = JsonConvert.SerializeObject(alarmData);
            return new MqMessage
            {
                SourceName = alarmData.SourceName,
                Message = json,
                Severity = alarmData.Severity
            };
        }
    }
}
