using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json; // 替换为 Newtonsoft.Json

namespace opc_ae_relay.mq
{
    public static class OpcAeAlarmSender
    {
        private const string ExchangeName = "opc_ae_topic_exchange";

        public static async Task SendAlarmAsync(string sourceName, string message, int severity)
        {
            var conn = await RabbitMqConnectionManager.GetConnectionAsync();
            IChannel channel = null;
            try
            {
                channel = await conn.CreateChannelAsync();

                await channel.ExchangeDeclareAsync(
                    exchange: ExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false);

                // 2. 直接 new，不要写 .Framing
                var props = new BasicProperties();
                props.Persistent = false; // 非持久化
                props.ContentType = "application/json";

                var alarmObj = new
                {
                    SourceName = sourceName,
                    Message = message,
                    Severity = severity,
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };

                // 使用 Newtonsoft.Json 序列化
                string json = JsonConvert.SerializeObject(alarmObj);
                byte[] body = Encoding.UTF8.GetBytes(json);
                string routingKey = $"opc.ae.{sourceName}";

                await channel.BasicPublishAsync(
                    exchange: ExchangeName,
                    routingKey: routingKey,
                    mandatory: false,      // 必须传 是啥
                    body: body,
                    basicProperties: props);
            }
            finally
            {
                if (channel != null)
                {
                    if (channel.IsOpen)
                        await channel.CloseAsync();
                    await channel.DisposeAsync();
                }
            }
        }
    }
}
