using System;
using System.Collections.Generic;
using System.Threading;
using opc_ae_relay.config;
using opc_ae_relay.discoverServer;
using opc_ae_relay.client;
using Serilog;

namespace opc_ae_relay.core
{
    public class OpcAeClientRun
    {
        // 全局关闭标志：所有 OPC 线程循环检查此标志
        public static volatile bool ShuttingDown = false;

        // OPC 服务配置
        public static readonly List<OPCServerConfig> oPCServerConfigs = AppConfigLoader.GetOPCServers();

        // OPC 线程
        public static Dictionary<string, Thread> opcThreads = new Dictionary<string, Thread>();

        // 线程停止标志
        public static Dictionary<string, bool> opcThreadsRunning = new Dictionary<string, bool>();

        // 防止重复启动
        public static Dictionary<string, object> opcLocks = new Dictionary<string, object>();

        // OPC 服务链接信息
        public static Dictionary<string, string> hostInfo = new Dictionary<string, string>();

        // OPC 线程重启次数
        public static Dictionary<string, int> restartCount = new Dictionary<string, int>();


        public static void runOPC()
        {
            try
            {
                oPCServerConfigs.ForEach(config =>
                {
                    opcThreads[config.IP] = null;
                    opcThreadsRunning[config.IP] = false;
                    opcLocks[config.IP] = new object();
                    restartCount[config.IP] = -1;

                    StartOrRestartOpcThread(config.IP, config.ProgId);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }

        /// <summary>
        ///     看门狗：检查 OPC 线程是否存活
        /// </summary>
        public static void StartOpcWatchDog()
        {
            var watchThread = new Thread(() =>
            {
                while (!ShuttingDown)
                    try
                    {
                        oPCServerConfigs.ForEach(config =>
                        {
                            var _opcThread = opcThreads[config.IP];
                            if (_opcThread == null || !_opcThread.IsAlive)
                            {
                                opcThreadsRunning[config.IP] = false;
                                if (!ShuttingDown)
                                {
                                    Log.Warning($"{config.Name} 线程不存在或已死亡，准备重启...");
                                    StartOrRestartOpcThread(config.IP, config.ProgId);
                                }
                            }
                        });
                        Thread.Sleep(5000); // 5 秒检查一次
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "OPC 看门狗异常");
                        Thread.Sleep(5000);
                    }
            })
            {
                Name = "OPC-WatchDog",
                IsBackground = true
            };

            watchThread.Start();
        }

        /// <summary>
        ///     启动或重启 OPC 线程
        /// </summary>
        private static void StartOrRestartOpcThread(string host, string ProgId)
        {
            var _opcLock = opcLocks[host];
            var _opcThread = opcThreads[host];
            lock (_opcLock)
            {
                // 如果线程还活着，不重复启动
                if (_opcThread != null && _opcThread.IsAlive)
                    return;
                if (ShuttingDown)
                    return;

                Log.Information($"准备启动/重启 OPC {host} 监听线程...");

                opcThreads[host] = new Thread(() => { OpcWork(host, ProgId); });
                opcThreads[host].Name = $"OPC-AE-[{host}]-Listener"; // 线程名，Serilog 会显示
                opcThreads[host].IsBackground = true;
                opcThreads[host].Start();

                Log.Information($"OPC {host} 线程已启动");
            }
        }


        private static void OpcWork(string host, string ProgId)
        {
            if (ShuttingDown)
                return;
            restartCount[host] += 1;
            // 先判断 host 是否存在，且对应的值是否为 null
            if (!hostInfo.TryGetValue(host, out var uri) || uri == null)
            {
                var list = DiscoverServer.getAEServer(host, false);
                if (list == null || list.Count == 0)
                {
                    Log.Warning("AEServer 获取为空");
                    return;
                }

                foreach (var item in list)
                    if (ProgId.Equals(item.ProgId))
                    {
                        hostInfo[host] = item.Uri; // 不存在的 key 会自动添加
                        Log.Information(
                            $"Name={item.Name}, ClassId={item.ClassId}, ProgId={item.ProgId}, Uri={item.Uri}");
                        break;
                    }

                // 再次判断是否成功赋值
                if (!hostInfo.TryGetValue(host, out uri) || uri == null)
                {
                    Log.Warning($"目标opc服务器:[{host}]没有配置指定的ProgId:[{ProgId}]");
                    return;
                }
            }

            using (var aeClient = new YokogawaAEClient(hostInfo[host], host))
            {
                try
                {
                    aeClient.OnLog += message => { Log.Information(message); };
                    // 注册报警事件处理器
                    /*aeClient.OnAlarmReceived += alarm =>
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
                    };*/

                    // 连接
                    if (!aeClient.Connect())
                    {
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
                    foreach (var s in sources) Console.WriteLine("  * " + s.DisplayName + " (" + s.NodeId + ")");

                    // 3. 订阅报警
                    Console.WriteLine();
                    Console.WriteLine("========== 步骤3: 订阅报警 ==========");
                    Console.WriteLine();
                    if (aeClient.SubscribeAlarms())
                    {
                        Console.WriteLine("等待报警...");
                        while (!ShuttingDown)
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
                finally
                {
                    opcThreadsRunning[host] = false;
                    Log.Information($"OPC {host} 线程已退出");
                }
            }
        }

        /// <summary>
        ///     停止所有 OPC 线程
        /// </summary>
        public static void StopAll()
        {
            ShuttingDown = true;
            Log.Information("正在停止 OPC 线程...");

            foreach (var kvp in opcThreads)
            {
                var thread = kvp.Value;
                if (thread != null && thread.IsAlive)
                {
                    thread.Join(5000);
                    if (thread.IsAlive)
                    {
                        Log.Warning($"OPC 线程 {kvp.Key} 未能在 5 秒内退出");
                    }
                }
            }

            Log.Information("所有 OPC 线程已停止");
        }
    }
}