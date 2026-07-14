using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using opc_ae_relay.config;

namespace opc_ae_relay.mq
{
    public static class MqManager
    {
        private static readonly List<IMqProducer> _producers = new List<IMqProducer>();

        /// <summary>
        /// 发送测试消息，用于在无 AE 监听时验证 MQ 连通性和消息格式
        /// </summary>
        /// <param name="tag">自定义标签名，模拟 SourceName，默认 "TEST_TAG"</param>
        /// <returns>发送是否成功</returns>
        public static async Task<bool> SendTestMessageAsync(string tag = "TEST_TAG")
        {
            if (_producers.Count == 0)
            {
                Log.Warning("[MQ测试] 无可用 Producer，请先确认 MQ 配置和连接");
                return false;
            }

            var testMsg = new MqMessage
            {
                SourceName = tag,
                Message = "这是一条测试消息，用于验证 MQ 推送通道",
                Severity = 1
            };

            string json = JsonConvert.SerializeObject(testMsg);
            Log.Information("[MQ测试] 准备发送测试消息: {Json}", json);

            var success = true;
            foreach (var producer in _producers)
            {
                try
                {
                    await producer.SendAsync(tag, json);
                    Log.Information("[MQ测试] 发送成功");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MQ测试] 发送失败");
                    success = false;
                }
            }

            return success;
        }

        public static async Task InitAsync()
        {
            var mqConfigs = AppConfigLoader.Config.MQs;
            if (mqConfigs == null || mqConfigs.Count == 0)
            {
                Log.Warning("未配置任何 MQ，消息推送功能不可用");
                return;
            }

            foreach (var mqConfig in mqConfigs)
            {
                if (!mqConfig.Enabled)
                {
                    Log.Information("[MQ:{Name}] 已禁用，跳过", mqConfig.Name);
                    continue;
                }

                try
                {
                    var producer = MqProducerFactory.Create(mqConfig);
                    await producer.StartAsync();
                    _producers.Add(producer);
                    Log.Information("[MQ:{Name}] 初始化成功，类型={Type}", mqConfig.Name, mqConfig.Type);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MQ:{Name}] 初始化失败", mqConfig.Name);
                }
            }
        }

        public static async Task SendAlarmAsync(MqMessage mqMessage)
        {
            if (mqMessage == null)
            {
                Log.Warning("MqMessage 为 null，跳过发送");
                return;
            }

            if (_producers.Count == 0)
            {
                Log.Warning("无可用 MQ Producer，消息未发送");
                return;
            }
            
            string json = JsonConvert.SerializeObject(mqMessage);

            foreach (var producer in _producers)
            {
                try
                {
                    await producer.SendAsync(mqMessage.SourceName, json);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "发送告警消息失败");
                }
            }
        }

        public static async Task SendAlarmAsync(string sourceName, string message, int severity)
        {
            if (_producers.Count == 0)
            {
                Log.Warning("无可用 MQ Producer，消息未发送");
                return;
            }

            var mqMessage = new MqMessage
            {
                SourceName = sourceName,
                Message = message,
                Severity = severity
            };

            string json = JsonConvert.SerializeObject(mqMessage);

            foreach (var producer in _producers)
            {
                try
                {
                    await producer.SendAsync(sourceName, json);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "发送告警消息失败");
                }
            }
        }

        public static async Task ShutdownAsync()
        {
            foreach (var producer in _producers)
            {
                try
                {
                    await producer.StopAsync();
                    producer.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "关闭 MQ Producer 时异常");
                }
            }
            _producers.Clear();
            Log.Information("所有 MQ Producer 已关闭");
        }
    }
}
