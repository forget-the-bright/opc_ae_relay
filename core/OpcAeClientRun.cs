using Opc.UaFx.Client;
using opcLearn.discoverServer;
using opcLearn.config;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YokogawaAE;

namespace opcLearn.core
{
    public class OpcAeClientRun
    {
        public static readonly List<OPCServerConfig> oPCServerConfigs = AppConfigLoader.GetOPCServers();

        public static Dictionary<String, Thread> opcThreads = new Dictionary<string, Thread>();

        public static Dictionary<String, Boolean> opcThreadsRunning = new Dictionary<string, Boolean>();

        public static Dictionary<String, Object> opcLocks = new Dictionary<string, Object>();


        // OPC 线程
        private static Thread _opcThread_1;
        // 线程停止标志
        private static Boolean _opcThreadRunning_1 = false;
        // 防止重复启动
        private static readonly object _opcLock_1 = new object();


        // OPC 线程
        private static Thread _opcThread_2;
        // 线程停止标志
        private static Boolean _opcThreadRunning_2 = false;
        // 防止重复启动
        private static readonly object _opcLock_2 = new object();

        public static string host1 = "10.100.107.1";
        public static string host2 = "10.100.107.2";

        public static Dictionary<String, String> hostInfo = new Dictionary<String, String>();
        public static bool get_opcThreadRunning_1()
        {
            return _opcThreadRunning_1;
        }

        public static string get_opcThreadRunning_1ID()
        {
            return _opcThread_1?.ToString() ?? "";
        }
        public static bool get_opcThreadRunning_2()
        {
            return _opcThreadRunning_2;
        }

        public static string get_opcThreadRunning_2ID()
        {
            return _opcThread_2?.ToString() ?? "";
        }

        public static void runOPC()
        {

            Log.Information("==========================================");
            Log.Information("   Yokogawa OPC UA AE 客户端 v1.0        ");
            Log.Information("==========================================");
            Log.Information("");


            try
            {
                oPCServerConfigs.ForEach(config =>
                {
                    opcThreads[config.IP] = null;
                    opcThreadsRunning[config.IP] = false;
                    opcLocks[config.IP] = new object();

                    StartOrRestartOpcThread(config.IP);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }

        }

        /// <summary>
        /// 看门狗：检查 OPC 线程是否存活
        /// </summary>
        public static void StartOpcWatchDog()
        {
            var watchThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        oPCServerConfigs.ForEach(config =>
                        {
                            Thread _opcThread = opcThreads[config.IP];
                            if (_opcThread == null || !_opcThread.IsAlive)
                            {
                                opcThreadsRunning[config.IP] = false;
                                Log.Warning($"{config.Name} 线程不存在或已死亡，准备重启...");
                                StartOrRestartOpcThread(config.IP);
                            }
                        });
                        Thread.Sleep(5000); // 5 秒检查一次
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "OPC 看门狗异常");
                        Thread.Sleep(5000);
                    }
                }
            })
            {
                Name = "OPC-WatchDog",
                IsBackground = true
            };

            watchThread.Start();
        }

        /// <summary>
        /// 启动或重启 OPC 线程
        /// </summary>
        private static void StartOrRestartOpcThread(string host)
        {
            Object _opcLock = opcLocks[host];
            Thread _opcThread = opcThreads[host];
            //opcThreadsRunning[host];
            lock (_opcLock)
            {
                // 如果线程还活着，不重复启动
                if (_opcThread != null && _opcThread.IsAlive)
                    return;


                Log.Information($"准备启动/重启 OPC {host} 监听线程...");

                //_opcThreadRunning_1 = true;

                opcThreads[host] = new Thread(() => { OpcWork(host); });
                opcThreads[host].Name = $"OPC-AE-[{host}]-Listener"; // 线程名，Serilog 会显示
                opcThreads[host].IsBackground = true;
                opcThreads[host].Start();

                Log.Information($"OPC {host} 线程已启动");
            }
        }


        private static void OpcWork(string host)
        {

            var list = DiscoverServer.getAEServer(host, isPrint: false);
            if (list == null || list.Count == 0)
            {
                Log.Warning("AEServer 获取为空");
                return;
            }
            //A&E Server（基础）	Yokogawa.ExaopcAECS1	最大客户端100	| 最大 Item/Tags1,000 事件订阅对象	
            //A&E 1.10；支持系统/过程/操作等8类事件
            var server = list[2];
            Log.Information($@"Name={server.Name}, ClassId={server.ClassId}, ProgId={server.ProgId}, Uri={server.Uri}");
            hostInfo[host] = server.Uri;
            using (var aeClient = new YokogawaAEClient(serverUrl: server.Uri, host: host))
            {
                try
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
                    opcThreadsRunning[host] = true;
                    // 1. 浏览节点树
                    // Console.WriteLine();
                    // Console.WriteLine("========== 步骤1: 浏览 AE 节点树 ==========");
                    // Console.WriteLine();
                    // Console.WriteLine(aeClient.GetAENodeTree());

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
                        Console.WriteLine("等待报警...");
                        //Console.WriteLine();
                        while (true)
                        {
                            if (aeClient.isConnected())
                            {
                                opcThreadsRunning[host] = false;
                                Console.WriteLine(aeClient.clientState());
                                break;
                            }
                            Thread.Sleep(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
            }
        }
    }
}
