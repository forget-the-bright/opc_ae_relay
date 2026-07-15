using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using opc_ae_relay.mq;
using opc_ae_relay.util;
using Opc.UaFx;
using Opc.UaFx.Client;

namespace opc_ae_relay.opc;

/// <summary>
///     通用 OPC UA AE 客户端
/// </summary>
public class OpcClassicAEClient : IDisposable
{
    private readonly OpcNodeId _aeNodeId;
    private readonly OpcClient _client;
    private readonly string _host;
    private readonly string _serverUrl;
    private object _eventSubscription;

    public OpcClassicAEClient(string serverUrl = "opc.tcp://10.100.107.1:4840/", string host = "")
    {
        _serverUrl = serverUrl;
        _client = new OpcClient(_serverUrl);
        _host = host;
        _aeNodeId = OpcObjectTypes.Server;
        IsConnected = false;
    }

    public bool IsConnected { get; private set; }

    public void Dispose()
    {
        Disconnect();
        _client?.Dispose();
    }

    public event Action<AlarmEventData> OnAlarmReceived;
    public event Action<AlarmEventData> OnEventReceived;
    public event Action<string> OnLog;

    // ==================== 连接管理 ====================

    public bool Connect()
    {
        try
        {
            Log("正在连接服务器...");
            _client.Connect();
            IsConnected = true;
            Log("连接成功！");
            return true;
        }
        catch (Exception ex)
        {
            Log("连接失败: " + ex.Message);
            return false;
        }
    }

    public void Disconnect()
    {
        if (IsConnected)
        {
            if (_eventSubscription is IDisposable) ((IDisposable)_eventSubscription).Dispose();
            _eventSubscription = null;
            _client.Disconnect();
            IsConnected = false;
            Log("已断开连接");
        }
    }

    // ==================== 节点浏览 ====================

    /// <summary>
    ///     获取 AE 节点的树状结构字符串
    /// </summary>
    public string GetAENodeTree()
    {
        if (!EnsureConnected()) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== AE 节点树状结构 ===");
        sb.AppendLine();

        try
        {
            var aeNode = _client.BrowseNode(_aeNodeId);
            BuildTreeString(aeNode, "", sb, new HashSet<string>());
        }
        catch (Exception ex)
        {
            sb.AppendLine("获取节点树失败: " + ex.Message);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     获取所有报警源节点
    /// </summary>
    public List<OpcNodeInfo> GetAlarmSources()
    {
        var sources = new List<OpcNodeInfo>();
        if (!EnsureConnected()) return sources;

        try
        {
            var aeNode = _client.BrowseNode(_aeNodeId);
            CollectAlarmSources(aeNode, sources, new HashSet<string>());
            Log(string.Format("找到 {0} 个报警源", sources.Count));
        }
        catch (Exception ex)
        {
            Log("获取报警源失败: " + ex.Message);
        }

        return sources;
    }

    /// <summary>
    ///     获取所有报警条件节点
    /// </summary>
    public List<OpcNodeInfo> GetAlarmConditions()
    {
        var conditions = new List<OpcNodeInfo>();
        if (!EnsureConnected()) return conditions;

        try
        {
            var aeNode = _client.BrowseNode(_aeNodeId);
            CollectConditions(aeNode, conditions, new HashSet<string>());
            Log(string.Format("找到 {0} 个报警条件", conditions.Count));
        }
        catch (Exception ex)
        {
            Log("获取报警条件失败: " + ex.Message);
        }

        return conditions;
    }

    public bool isConnected()
    {
        return _client.State == OpcClientState.Disconnected || _client.State == OpcClientState.Disconnecting;
    }

    public OpcClientState clientState()
    {
        return _client.State;
    }
    // ==================== 报警订阅 ====================

    /// <summary>
    ///     订阅 AE 节点的报警和事件
    /// </summary>
    public bool SubscribeAlarms()
    {
        if (!EnsureConnected()) return false;

        try
        {
            if (_eventSubscription is IDisposable) ((IDisposable)_eventSubscription).Dispose();

            _eventSubscription = _client.SubscribeEvent(
                _aeNodeId,
                HandleOpcEvent
            );

            Log("报警订阅成功！等待报警事件...");
            return true;
        }
        catch (Exception ex)
        {
            Log("报警订阅失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    ///     读取指定节点的当前值
    /// </summary>
    public object ReadNodeValue(string nodeId)
    {
        if (!EnsureConnected()) return null;

        try
        {
            return _client.ReadNode(new OpcNodeId(nodeId));
        }
        catch (Exception ex)
        {
            Log(string.Format("读取节点值失败 [{0}]: {1}", nodeId, ex.Message));
            return null;
        }
    }

    // ==================== 私有方法 ====================

    private bool EnsureConnected()
    {
        if (!IsConnected)
        {
            Log("未连接，请先调用 Connect()");
            return false;
        }

        return true;
    }

    private void HandleOpcEvent(object sender, OpcEventReceivedEventArgs e)
    {
        var message = "";
        try
        {
            var opcEvent = e.Event;
            if (opcEvent == null) return;

            // 3. 获取所有原始值（仅调试，丢失字段名）
            // object[] allVals = opcEvent.GetAllDataStoreValues();
            //string result = OpcEventDataStoreHelper.FormatObjectArray(allVals);
            // Log($"[OPC-AE-{_host}]- {result}");
            //string log = $"Time={opcEvent.Time},SourceNodeId={opcEvent.SourceNodeId},SourceName={opcEvent.SourceName},Severity={(ushort)opcEvent.Severity},ReceiveTime={opcEvent.ReceiveTime},NodeId={opcEvent.NodeId},Msg={opcEvent.Message},EventTypeId={opcEvent.EventTypeId},EventType={opcEvent.EventType},EventId={ConvertByteString(opcEvent.EventId)}";
            // Log($"[OPC-AE-{_host}]- {log}");
            // 反射获取OpcEvent所有公开属性名
            var alarmData = new AlarmEventData
            {
                EventId = ConvertByteString(opcEvent.EventId),
                EventType = opcEvent.EventType.ToString(),
                EventTypeId = opcEvent.EventTypeId != null ? opcEvent.EventTypeId.ToString() : "N/A",
                SourceName = opcEvent.SourceName ?? "N/A",
                SourceNodeId = opcEvent.SourceNodeId != null ? opcEvent.SourceNodeId.ToString() : "N/A",
                NodeId = opcEvent.NodeId != null ? opcEvent.NodeId.ToString() : "N/A",
                Message = opcEvent.Message != null ? opcEvent.Message.ToString() : "",
                Severity = (int)opcEvent.Severity,
                Time = opcEvent.Time,
                ReceiveTime = opcEvent.ReceiveTime,
                Host = _host
            };

            var alarmInfo = AlarmInfo.ParseAlarm(alarmData.Message, alarmData.EventTypeId);
            alarmData.ConditionName = alarmInfo.MatchedRule.Code;
            alarmData.MatchedRuleName = alarmInfo.MatchedRule?.Name ?? "";
            alarmData.MatchedRuleEventType = alarmInfo.MatchedRule?.EventType ?? "";
            alarmData.MatchedRuleDescription = alarmInfo.MatchedRule?.Description ?? "";

            message = alarmData.Message;
            var alarm = e.Event as OpcAlarmCondition;
            //这里有问题不是OpcAlarmCondition 也能转换，且永不为null
            if (alarm != null)
                if (alarm.IsActive)
                {
                    Console.Write($"Alarm {alarm.ConditionName} is {alarmData.SourceName} ");
                    Console.WriteLine($"{(alarm.IsActive ? "active" : "inactive")}!");
                }

            var idObject = DBUtil.ExecuteScalar(AlarmEventData.getInsertSql(), alarmData);

            var mqMsg = MqMessage.BuildMessage(alarmData);
            if (mqMsg != null)
            {
                MqManager.SendAlarmAsync(mqMsg).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Log($"MQ 发送失败: {t.Exception?.GetBaseException().Message}");
                });
            }

            Log(string.Format("[OPC-AE-{0}] - [事件类型:{1}] [来源:{2}] - [消息:{3}] - [{4}] - [{5}] - [{6}] - [{7}]]",
                _host,
                opcEvent.EventType,
                alarmData.SourceName,
                alarmData.Message,
                alarmInfo.MatchedRule?.Name ?? "",
                alarmInfo.MatchedRule?.EventType ?? "",
                alarmInfo.MatchedRule?.Description ?? "",
                idObject
            ));


            // 用下面的判断
            /*                if (opcEvent.EventType == OpcEventType.AlarmCondition)
                            {
                                Log(string.Format("[报警] [来源:{0}] - [消息:{1}]", alarmData.SourceName, alarmData.Message));
                                if (OnAlarmReceived != null) OnAlarmReceived(alarmData);
                            }
                            else
                            {

                                Log(string.Format("[事件] [类型:{0}] [来源:{1}] - [消息:{2} | {3} | {4} | {5} | {6}]", opcEvent.EventType, alarmData.SourceName,
                                    alarmInfo.TagName,
                                    alarmInfo.FieldType,
                                    alarmInfo.Value,
                                    alarmInfo.Description,
                                    alarmInfo.Unit
                                    ));
                            }

                            if (OnEventReceived != null) OnEventReceived(alarmData);*/
            if (OnAlarmReceived != null) OnAlarmReceived(alarmData);
            if (OnEventReceived != null) OnEventReceived(alarmData);
        }
        catch (Exception ex)
        {
            Log($"处理事件出错: {ex.Message} {message}");
        }
    }

    private string ConvertByteString(ByteString byteString)
    {
        if (byteString == null) return "N/A";
        try
        {
            var bytes = byteString.ToArray();
            return BitConverter.ToString(bytes).Replace("-", "");
        }
        catch
        {
            return byteString.ToString();
        }
    }

    private void BuildTreeString(OpcNodeInfo node, string prefix, StringBuilder sb, HashSet<string> visited)
    {
        var nodeKey = node.NodeId.ToString();
        if (visited.Contains(nodeKey)) return;
        visited.Add(nodeKey);

        // 节点图标（兼容 .NET Framework 4.7.2）
        var icon = GetNodeIcon(node.Category);

        sb.AppendLine(prefix + icon + " " + node.DisplayName + " [" + node.Category + "]");

        try
        {
            var children = node.Children().ToList();
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var isLast = i == children.Count - 1;
                var newPrefix = prefix + (isLast ? "  └─ " : "  ├─ ");
                var childPrefix = prefix + (isLast ? "     " : "  │  ");

                if (child.Category == OpcNodeCategory.Object)
                {
                    BuildTreeString(child, newPrefix, sb, visited);
                }
                else if (child.Category == OpcNodeCategory.Variable)
                {
                    sb.AppendLine(newPrefix + GetNodeIcon(child.Category) + " " + child.DisplayName + " [" +
                                  child.Category + "]");
                    try
                    {
                        var value = child.AttributeValue(OpcAttribute.Value);
                        if (value != null)
                            sb.AppendLine(childPrefix + "  值: " + value);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    sb.AppendLine(newPrefix + child.DisplayName + " [" + child.Category + "]");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine(prefix + "  [无法读取子节点: " + ex.Message + "]");
        }
    }

    /// <summary>
    ///     根据节点类型返回图标字符
    /// </summary>
    private string GetNodeIcon(OpcNodeCategory category)
    {
        if (category == OpcNodeCategory.Object)
            return "[O]";
        if (category == OpcNodeCategory.Variable)
            return "[V]";
        if (category == OpcNodeCategory.Method)
            return "[M]";
        return "[ ]";
    }

    private void CollectAlarmSources(OpcNodeInfo node, List<OpcNodeInfo> sources, HashSet<string> visited)
    {
        var key = node.NodeId.ToString();
        if (visited.Contains(key)) return;
        visited.Add(key);

        try
        {
            var notifiers = node.Children(OpcReferenceType.HasNotifier);
            foreach (var n in notifiers)
                if (!sources.Any(s => s.NodeId == n.NodeId))
                {
                    sources.Add(n);
                    CollectAlarmSources(n, sources, visited);
                }

            var eventSources = node.Children(OpcReferenceType.HasEventSource);
            foreach (var es in eventSources)
                if (!sources.Any(s => s.NodeId == es.NodeId))
                {
                    sources.Add(es);
                    CollectAlarmSources(es, sources, visited);
                }

            foreach (var child in node.Children())
                if (child.Category == OpcNodeCategory.Object)
                    CollectAlarmSources(child, sources, visited);
        }
        catch
        {
        }
    }

    private void CollectConditions(OpcNodeInfo node, List<OpcNodeInfo> conditions, HashSet<string> visited)
    {
        var key = node.NodeId.ToString();
        if (visited.Contains(key)) return;
        visited.Add(key);

        try
        {
            var hasConditions = node.Children(OpcReferenceType.HasCondition);
            foreach (var c in hasConditions)
                if (!conditions.Any(x => x.NodeId == c.NodeId))
                    conditions.Add(c);

            foreach (var child in node.Children())
                if (child.Category == OpcNodeCategory.Object)
                    CollectConditions(child, conditions, visited);
        }
        catch
        {
        }
    }

    private void Log(string message)
    {
        var logMessage = string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, message);
        //Console.WriteLine(logMessage);
        if (OnLog != null) OnLog(message);
    }
}