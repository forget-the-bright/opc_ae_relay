using GodSharp.Opc.Da;
using Microsoft.Extensions.Hosting;
using Nancy.Hosting.Self;
using Opc.UaFx;
using Opc.UaFx.Client;
using opcLearn.config;
using opcLearn.core;
using opcLearn.discoverServer;
using Serilog;
using System;
using System.Threading;

namespace YokogawaAE
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Config.initAll();
                OpcAeClientRun.runOPC();
                OpcAeClientRun.StartOpcWatchDog();
                WebConfig.run();
                //var list = DiscoverServer.getAEServer("10.100.107.2", isPrint: true);
                //runAeClientServer();
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }


        public static void runAeClientServer()
        {

            var list = DiscoverServer.getAEServer("10.100.107.1", isPrint: false);
            //AeServerSelectTest.TestAeServerSelect();
            Log.Information("==========================================");
            Log.Information("   Yokogawa OPC UA AE 客户端 v1.0        ");
            Log.Information("==========================================");
            Log.Information("");
            //A&E Server（基础）	Yokogawa.ExaopcAECS1	最大客户端100	| 最大 Item/Tags1,000 事件订阅对象	
            //A&E 1.10；支持系统/过程/操作等8类事件
            var server = list[2];
            Log.Information($@"Name={server.Name}, ClassId={server.ClassId}, ProgId={server.ProgId}, Uri={server.Uri}");
            using (var aeClient = new YokogawaAEClient(serverUrl: server.Uri))
            {
                aeClient.OnLog += message =>
                {
                    Log.Information(message);
                };
                // 注册报警事件处理器
                aeClient.OnAlarmReceived += alarm =>
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine();
                    Console.WriteLine("==========================================");
                    Console.WriteLine("        收到报警事件                      ");
                    Console.WriteLine("==========================================");
                    Console.WriteLine("事件ID:   " + alarm.EventId);
                    Console.WriteLine("类型:     " + alarm.EventType);
                    Console.WriteLine("报警源:   " + alarm.SourceName);
                    Console.WriteLine("消息:     " + alarm.Message);
                    Console.WriteLine("严重程度: " + alarm.Severity);
                    Console.WriteLine("发生时间: " + alarm.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    Console.WriteLine("==========================================");
                    Console.ResetColor();
                };

                // 连接
                if (!aeClient.Connect())
                {
                    Console.ReadKey();
                    return;
                }

                // 1. 浏览节点树
                Console.WriteLine();
                Console.WriteLine("========== 步骤1: 浏览 AE 节点树 ==========");
                Console.WriteLine();
                Console.WriteLine(aeClient.GetAENodeTree());

                // 2. 获取报警源
                Console.WriteLine("========== 步骤2: 报警源列表 ==========");
                Console.WriteLine();
                var sources = aeClient.GetAlarmSources();
                foreach (var s in sources)
                {
                    Console.WriteLine("  * " + s.DisplayName + " (" + s.NodeId + ")");
                }

                // 3. 订阅报警
                Console.WriteLine();
                Console.WriteLine("========== 步骤3: 订阅报警 ==========");
                Console.WriteLine();
                if (aeClient.SubscribeAlarms())
                {
                    Console.WriteLine("等待报警...（按 Ctrl+C 退出）");
                    //Console.WriteLine();
                    while (true) Thread.Sleep(1000);
                }
            }
        }
    }
}