using System;
using opc_ae_relay.config;
using Serilog;

namespace opc_ae_relay.mq
{
    public static class MqProducerFactory
    {
        public static IMqProducer Create(MQConfig config)
        {
            switch ((config.Type ?? "").ToLower())
            {
                case "rabbitmq":
                    return new RabbitMqProducer(config);
                // case "kafka":
                //     return new KafkaProducer(config);
                default:
                    throw new NotSupportedException($"不支持的 MQ 类型: {config.Type}");
            }
        }
    }
}
