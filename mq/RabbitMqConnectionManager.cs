using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace opc_ae_relay.mq
{
    public static class RabbitMqConnectionManager
    {
        private static IConnection _connection;
        private static readonly object _lock = new object();

        private static readonly ConnectionFactory _factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "guest",
            Password = "guest",
            RequestedHeartbeat = TimeSpan.FromSeconds(30),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        /// <summary>
        /// 获取单例异步连接（只有 CreateConnectionAsync）
        /// </summary>
        public static async Task<IConnection> GetConnectionAsync()
        {
            lock (_lock)
            {
                if (_connection != null && _connection.IsOpen)
                    return _connection;
            }

            // 异步创建连接
            var conn = await _factory.CreateConnectionAsync("OPC_AE_Producer");

            lock (_lock)
            {
                _connection = conn;
            }

            return _connection;
        }
    }
}
