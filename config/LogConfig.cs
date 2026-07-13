using System;
using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace opc_ae_relay.config
{
    public static class LogConfig
    {
        public static void init()
        {
            // 全局初始化日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information() // 最低日志级别
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // 过滤系统冗余日志
                .Enrich.WithThreadId() // 系统线程 ID
                .Enrich.WithThreadName() // 线程 Name（你自己设置的）
                .Enrich.FromLogContext()

                // 控制台输出
                // 控制台输出（指定格式）
                .WriteTo.Console(
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [T:{ThreadName}({ThreadId})]  [{Level:u4}] {Message:lj}{NewLine}{Exception}"
                )

                // 文件输出：按天切片 + 保留30天
                .WriteTo.File(
                    "./logs/log-.log", // 日志目录+文件名前缀
                    rollingInterval: RollingInterval.Day, // 按天切片
                    retainedFileCountLimit: null, // 不按数量限制
                    retainedFileTimeLimit: TimeSpan.FromDays(30), // 只保留30天
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [T:{ThreadName}({ThreadId})]  [{Level:u4}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.Sink(new InMemoryLogSink())
                .CreateLogger();
        }
    }

    public static class LogBuffer
    {
        public static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        public static int MaxCount = 100;
    }

    public class InMemoryLogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            var log = logEvent.RenderMessage();
            if (log.Contains("OPC-AE-"))
            {
                LogBuffer.Queue.Enqueue(log);

                while (LogBuffer.Queue.Count > LogBuffer.MaxCount) LogBuffer.Queue.TryDequeue(out _);
            }
        }
    }
}