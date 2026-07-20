using System;
using System.Threading;
using opc_ae_relay.config;
using opc_ae_relay.db;
using opc_ae_relay.mq;
using opc_ae_relay.util;
using Serilog;

namespace opc_ae_relay.core
{
    public static class AppHost
    {
        private static readonly ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        public static void Run()
        {
            try
            {
                
                Log.Information("==========================================");
                Log.Information("      通用 OPC UA AE 采集服务 启动中...     ");
                Log.Information("==========================================");

                // 顺序初始化 配置监听服务 web服务 数据库服务 消息队列服务 opc核心管理服务
                Config.InitAll();
                EtwTrafficMonitor.Instance.Start();
                WebConfig.Start();
                DbManager.Init();
                MqManager.InitAsync().GetAwaiter().GetResult();
                OpcAeClientRun.runOPC(isRunWatchDog: true);


                Console.CancelKeyPress += OnShutdown;
                AppDomain.CurrentDomain.ProcessExit += OnShutdown;

                Log.Information("服务已就绪，按 Ctrl+C 优雅退出");
                ShutdownEvent.WaitOne();
                
                Log.Information("正在关闭服务...");
                // 顺序关闭 配置监听服务 opc核心管理服务 消息队列服务 数据库服务 web服务
                Config.StopAll();
                OpcAeClientRun.StopAll();
                MqManager.ShutdownAsync().GetAwaiter().GetResult();
                DbManager.Shutdown();
                WebConfig.Stop();
                EtwTrafficMonitor.Instance.Dispose();

                Log.Information("服务已安全退出");
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void OnShutdown(object sender, EventArgs e)
        {
            // 阻止运行时直接终止进程，让我们有机会执行 Stop() 清理
            if (e is ConsoleCancelEventArgs consoleArgs)
            {
                consoleArgs.Cancel = true;
            }

            ShutdownEvent.Set();
        }
    }
}