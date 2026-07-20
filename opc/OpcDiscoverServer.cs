using System;
using System.Collections.Generic;
using GodSharp.Opc.Da;
using Opc.UaFx.Classic;
using Opc.UaFx.Client.Classic;
using Serilog;

namespace opc_ae_relay.discoverServer
{
    public class OpcDiscoverServer
    {
        public static string[] getDaServer(string host = "10.100.107.1")
        {
            // 目标主机：本机填 "." 或 "localhost"，远程填 IP/计算机名

            string[] serverProgIds = { };
            try
            {
                // 1. 创建服务器发现实例
                IServerDiscovery serverDiscovery = new ServerDiscovery();

                // 2. 获取 OPC DA 服务器 ProgID 列表（默认 DA20 版本）
                serverProgIds = serverDiscovery.GetServers(ServerSpecification.DA20, host);

                Console.WriteLine($"=== 主机 {host} 上的 OPC DA 服务器 ProgID ===");
                Console.WriteLine("-----------------------------------------------------------");

                foreach (var progId in serverProgIds) Console.WriteLine(progId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取失败: {ex.Message}");
            }

            return serverProgIds;
        }

        public static List<(string Name, string ClassId, string ProgId, string Uri)> getAEServer(
            string host = "10.100.107.1", bool isPrint = true)
        {
            var list = new List<(string Name, string ClassId, string ProgId, string Uri)>();
            try
            {
                using (var discoveryClient = new OpcClassicDiscoveryClient(host))
                {
                    
                    // 1. 手动初始化 OPC AE 接口（AE1.0 + AE2.0）
                    var ae10Interface = OpcClassicInterfaces.Ae10;
                    var servers = discoveryClient.DiscoverServers(ae10Interface);

                    foreach (var server in servers)
                    {
                        if (isPrint)
                            Console.WriteLine(
                                $@"Name={server.Name}, ClassId={server.ClassId}, ProgId={server.ProgId}, Uri={server.Uri}"
                            );
                        list.Add((server.Name, server.ClassId.ToString(), server.ProgId, server.Uri.ToString()));
                    }

                    return list;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                return list;
            }
        }
    }
}