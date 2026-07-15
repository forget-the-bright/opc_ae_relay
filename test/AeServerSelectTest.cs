using System;
using System.Collections.Generic;
using System.Linq;
using opc_ae_relay.discoverServer;
using Opc.UaFx;
using Opc.UaFx.Client;
using Serilog;

namespace opc_ae_relay.test
{
    public static class AeServerSelectTest
    {
        public static void TestAeServerSelect()
        {
            var list = OpcDiscoverServer.getAEServer();
            if (list == null || list.Count == 0)
            {
                Log.Warning("AEServer 获取为空");
                return;
            }

            var client = new OpcClient(list[2].Uri);
            Console.WriteLine(list[2].Uri);
            try
            {
                client.Connect();
                Console.WriteLine("连接成功！\n");

                // 先看 Server 节点
                var serverNode = client.BrowseNode(OpcObjectTypes.Server);

                Console.WriteLine("=== Server 节点下的子节点 ===");
                printChildren(serverNode);


                Console.WriteLine("\n=== 查找 HasNotifier 引用 (报警通知器) ===");
                FindNotifiersRecursive(serverNode, 0, new HashSet<string>());

                Console.WriteLine("\n=== 查找 HasEventSource 引用 (事件源) ===");
                FindEventSourcesRecursive(serverNode, 0, new HashSet<string>());

                Console.WriteLine("\n=== 查找 HasCondition 引用 (条件/报警) ===");
                FindConditionsRecursive(serverNode, 0, new HashSet<string>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
            finally
            {
                client.Disconnect();
            }
        }

        private static void printChildren(OpcNodeInfo opcNodeInfo, int level = 1)
        {
            if (opcNodeInfo.Children().Count() == 0) return;
            if (level == 1)
                Console.WriteLine($"[{opcNodeInfo.Category}] [{opcNodeInfo.DisplayName}] = [{opcNodeInfo.NodeId}]");
            var tabStr = "";
            for (var i = 0; i < level; i++) tabStr += "\t";
            foreach (var child in opcNodeInfo.Children())
            {
                // Console.WriteLine($"{tabStr}[{child.Category}] [{child.DisplayName}] = [{child.NodeId}],[{child.Name}] = [{child.Context}]");

                Console.WriteLine($"{tabStr}[{child.Category}] [{child.DisplayName}] = [{child.NodeId}]");
                printChildren(child, level + 1);
            }
        }


        public static void FindNotifiersRecursive(OpcNodeInfo node, int depth, HashSet<string> visited)
        {
            if (depth > 5) return;

            var key = node.NodeId.ToString();
            if (visited.Contains(key)) return;
            visited.Add(key);

            try
            {
                // 查找 HasNotifier 子节点（报警通知器）
                var notifiers = node.Children(OpcReferenceType.HasNotifier);
                foreach (var notifier in notifiers)
                    Console.WriteLine(
                        $"  [HasNotifier] {notifier.DisplayName} = {notifier.NodeId} (父: {node.DisplayName})");

                // 继续递归所有子节点
                foreach (var child in node.Children()) FindNotifiersRecursive(child, depth + 1, visited);
            }
            catch
            {
            }
        }

        public static void FindEventSourcesRecursive(OpcNodeInfo node, int depth, HashSet<string> visited)
        {
            if (depth > 5) return;

            var key = node.NodeId.ToString();
            if (visited.Contains(key)) return;
            visited.Add(key);

            try
            {
                // 查找 HasEventSource 子节点（事件源）
                var eventSources = node.Children(OpcReferenceType.HasEventSource);
                foreach (var source in eventSources)
                    Console.WriteLine(
                        $"  [HasEventSource] {source.DisplayName} = {source.NodeId} (父: {node.DisplayName})");

                // 继续递归所有子节点
                foreach (var child in node.Children()) FindEventSourcesRecursive(child, depth + 1, visited);
            }
            catch
            {
            }
        }

        public static void FindConditionsRecursive(OpcNodeInfo node, int depth, HashSet<string> visited)
        {
            if (depth > 5) return;

            var key = node.NodeId.ToString();
            if (visited.Contains(key)) return;
            visited.Add(key);

            try
            {
                // 查找 HasCondition 子节点（条件/报警）
                var conditions = node.Children(OpcReferenceType.HasCondition);
                foreach (var condition in conditions)
                    Console.WriteLine(
                        $"  [HasCondition] {condition.DisplayName} = {condition.NodeId} (父: {node.DisplayName})");

                // 继续递归所有子节点
                foreach (var child in node.Children()) FindConditionsRecursive(child, depth + 1, visited);
            }
            catch
            {
            }
        }
    }
}