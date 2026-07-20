using System;
using System.IO;
using System.Text;
using opc_ae_relay.web;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace opc_ae_relay.config
{
    public static class LogConfig
    {
        public static void Init()
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

                // 异常日志：只记录 Error 及以上级别，输出到 ErrorLog 目录
                .WriteTo.File(
                    "./logs/error-.log",
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    retainedFileTimeLimit: TimeSpan.FromDays(30),
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [T:{ThreadName}({ThreadId})]  [{Level:u4}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.Sink(new InMemoryLogSink(
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [T:{ThreadName}({ThreadId})]  [{Level:u4}] {Message:lj}{NewLine}{Exception}"))
                .CreateLogger();
        }
    }

    public class InMemoryLogSink : ILogEventSink
    {
        private readonly MessageTemplateTextFormatter _formatter;

        public InMemoryLogSink(string OutputTemplate)
        {
            // 复用和 ConsoleSink 完全相同的格式化器
            _formatter = new MessageTemplateTextFormatter(OutputTemplate);
        }

        public void Emit(LogEvent logEvent)
        {
            // 渲染成和控制台完全一样的完整日志文本
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            {
                _formatter.Format(logEvent, sw);
            }

            string fullLog = sb.ToString().TrimEnd();
            // WebSocket 实时推送
            LogBroadcaster.Push(fullLog);
        }
    }
}