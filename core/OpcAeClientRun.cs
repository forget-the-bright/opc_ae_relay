using Opc.UaFx.Client;
using opcLearn.discoverServer;
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
                StartOrRestartOpcThread1();
                StartOrRestartOpcThread2();
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
                        // 线程不存在 OR 已死
                        if (_opcThread_1 == null || !_opcThread_1.IsAlive)
                        {
                            _opcThreadRunning_1 = false;
                            Log.Warning("OPC 1 线程不存在或已死亡，准备重启...");
                            StartOrRestartOpcThread1();
                        }

                        // 线程不存在 OR 已死
                        if (_opcThread_2 == null || !_opcThread_2.IsAlive)
                        {
                            _opcThreadRunning_2 = false;
                            Log.Warning("OPC 2 线程不存在或已死亡，准备重启...");
                            StartOrRestartOpcThread2();
                        }

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
        private static void StartOrRestartOpcThread1()
        {
            //"10.100.107.1"
            string host = host1;
            lock (_opcLock_1)
            {
                // 如果线程还活着，不重复启动
                if (_opcThread_1 != null && _opcThread_1.IsAlive)
                    return;


                Log.Information($"准备启动/重启 OPC {host} 监听线程...");

                //_opcThreadRunning_1 = true;

                _opcThread_1 = new Thread(() => { OpcWork(host, ref _opcThreadRunning_1); });
                _opcThread_1.Name = $"OPC-AE-[{host}]-Listener"; // 线程名，Serilog 会显示
                _opcThread_1.IsBackground = true;
                _opcThread_1.Start();

                Log.Information($"OPC {host} 线程已启动");
            }
        }


        private static void StartOrRestartOpcThread2()
        {
            //"10.100.107.1"
            string host = host2;
            lock (_opcLock_2)
            {
                // 如果线程还活着，不重复启动
                if (_opcThread_2 != null && _opcThread_2.IsAlive)
                    return;

                Log.Information($"准备启动/重启 OPC {host} 监听线程...");

                //_opcThreadRunning_2 = true;

                _opcThread_2 = new Thread(() =>
                {
                    try
                    {
                        OpcWork(host,ref _opcThreadRunning_2);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, ex.Message);
                    }
                });
                _opcThread_2.Name = $"OPC-AE-[{host}]-Listener"; // 线程名，Serilog 会显示
                _opcThread_2.IsBackground = true;
                _opcThread_2.Start();

                Log.Information($"OPC {host} 线程已启动");
            }
        }

        private static void OpcWork(string host, ref Boolean flag)
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
                    flag = true;
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
                                flag = false;
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
