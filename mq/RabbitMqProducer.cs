using System;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Serilog;
using opc_ae_relay.config;

namespace opc_ae_relay.mq
{
    public class RabbitMqProducer : IMqProducer
    {
        private readonly MQConfig _config;
        private IConnection _connection;
        private IChannel _channel;
        private readonly object _lock = new object();
        private bool _disposed;

        public RabbitMqProducer(MQConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task StartAsync()
        {
            var factory = new ConnectionFactory
            {
                HostName = _config.Host,
                Port = _config.Port,
                UserName = _config.User,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost ?? "/",
                RequestedHeartbeat = TimeSpan.FromSeconds(_config.HeartbeatSeconds),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(_config.RecoveryIntervalSeconds)
            };

            _connection = await factory.CreateConnectionAsync($"OPC_AE_{_config.Name}");
            _channel = await _connection.CreateChannelAsync();

            await _channel.ExchangeDeclareAsync(
                exchange: _config.Exchange,
                type: _config.ExchangeType ?? "topic",
                durable: _config.Durable,
                autoDelete: false);

            Log.Information("[MQ:{Name}] RabbitMQ 连接成功 {Host}:{Port}，Exchange={Exchange}",
                _config.Name, _config.Host, _config.Port, _config.Exchange);
        }

        public async Task SendAsync(string topic, string message)
        {
            if (_channel == null || !_channel.IsOpen)
            {
                Log.Warning("[MQ:{Name}] Channel 不可用，尝试重连...", _config.Name);
                await StartAsync();
            }

            var props = new BasicProperties
            {
                Persistent = _config.Persistent,
                ContentType = "application/json"
            };

            var routingKey = $"{_config.RoutingKeyPrefix}.{topic}";
            var body = Encoding.UTF8.GetBytes(message);

            await _channel.BasicPublishAsync(
                exchange: _config.Exchange,
                routingKey: routingKey,
                mandatory: false,
                body: body,
                basicProperties: props);
        }

        public async Task StopAsync()
        {
            try
            {
                if (_channel != null && _channel.IsOpen)
                    await _channel.CloseAsync();
                if (_connection != null && _connection.IsOpen)
                    await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MQ:{Name}] 关闭连接时异常", _config.Name);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
