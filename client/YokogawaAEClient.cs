using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Opc.UaFx;
using Opc.UaFx.Client;
using opc_ae_relay.config;
using opc_ae_relay.mq;
using opc_ae_relay.util;

namespace opc_ae_relay.client
{
    /// <summary>
    ///     OPC AE 报警事件实体（对应数据库表 AlarmEvent）
    /// </summary>
    public class AlarmEventData
    {
        /// <summary>
        ///     自增主键ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        ///     OPC事件唯一ID
        /// </summary>
        public string EventId { get; set; }

        /// <summary>
        ///     事件类型名称
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        ///     事件类型节点ID
        /// </summary>
        public string EventTypeId { get; set; }

        /// <summary>
        ///     事件源名称（点位/标签名）
        /// </summary>
        public string SourceName { get; set; }

        /// <summary>
        ///     事件源节点ID
        /// </summary>
        public string SourceNodeId { get; set; }

        /// <summary>
        ///     事件节点ID
        /// </summary>
        public string NodeId { get; set; }

        /// <summary>
        ///     报警消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        ///     报警级别（1低、2中、3高）
        /// </summary>
        public int Severity { get; set; }

        /// <summary>
        ///     事件发生时间（OPC服务器时间）
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        ///     客户端接收时间
        /// </summary>
        public DateTime ReceiveTime { get; set; }

        /// <summary>
        ///     来源OPC服务器IP
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        ///     报警是否激活（当前未赋值，可为null）
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        ///     是否已确认（当前未赋值，可为null）
        /// </summary>
        public bool? IsAcked { get; set; }

        /// <summary>
        ///     条件名称（如HH、LO等，当前未赋值，可为null）
        /// </summary>
        public string ConditionName { get; set; }

        // ====================== 你新增的匹配规则字段 ======================

        /// <summary>
        ///     匹配到的报警规则名称（来自application.xml的AlarmRules.Name）
        /// </summary>
        public string MatchedRuleName { get; set; }

        /// <summary>
        ///     匹配到的规则事件类型（来自AlarmRules.EventType：Alarm/Status/Mode）
        /// </summary>
        public string MatchedRuleEventType { get; set; }

        /// <summary>
        ///     匹配到的规则描述（来自AlarmRules.Desc）
        /// </summary>
        public string MatchedRuleDescription { get; set; }

        public override string ToString()
        {
            return string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}: {3} (类型:{4}, 激活:{5}, 确认:{6})",
                Time, Severity, SourceName, Message, EventType, IsActive, IsAcked);
        }

        public static string getInsertSql()
        {
            var sql = @"
INSERT INTO AlarmEvent
(
    EventId, EventType, EventTypeId, SourceName, SourceNodeId, NodeId,
    Message, Severity, Time, ReceiveTime, Host,
    IsActive, IsAcked, ConditionName,
    MatchedRuleName, MatchedRuleEventType, MatchedRuleDescription
)
VALUES
(
    @EventId, @EventType, @EventTypeId, @SourceName, @SourceNodeId, @NodeId,
    @Message, @Severity, @Time, @ReceiveTime, @Host,
    @IsActive, @IsAcked, @ConditionName,
    @MatchedRuleName, @MatchedRuleEventType, @MatchedRuleDescription
);
SELECT SCOPE_IDENTITY();";
            return sql;
        }
    }

    /// <summary>
    ///     Yokogawa OPC UA AE 客户端
    /// </summary>
    public class YokogawaAEClient : IDisposable
    {
        private readonly OpcNodeId _aeNodeId;
        private readonly OpcClient _client;
        private readonly string _host;
        private readonly string _serverUrl;
        private object _eventSubscription;

        public YokogawaAEClient(string serverUrl = "opc.tcp://10.100.107.1:4840/", string host = "")
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


    public class AlarmInfo
    {
        /// 标签名称 WI-11301
        public string TagName { get; set; } = string.Empty;

        /// 中文/设备描述
        public string Description { get; set; } = string.Empty;

        /// 字段类型 PV/SP/OP/MV/IN
        public string FieldType { get; set; } = string.Empty;

        /// 等号后数值/状态文本，无=则空
        public string Value { get; set; } = string.Empty;

        /// 单位
        public string Unit { get; set; } = string.Empty;

        /// 末尾全部后缀完整保留
        public string Suffix { get; set; } = string.Empty;

        public AlarmRule MatchedRule { get; set; } // 匹配到的规则

        /// 全局匹配解析方法
        public static AlarmInfo ParseAlarm(string input, string eventTypeId)
        {
            var result = new AlarmInfo();
            var raw = input.Trim();

            var alarmRule = AlarmConfigLoader.Match(raw);
            if (alarmRule != null) result.MatchedRule = alarmRule;
            // 分支1：包含 = 号（数值/状态格式）
            if (raw.Contains("="))
            {
                // 修复正则：等号后(.+?)包含值+单位，两个以上空格才分割后缀
                var regEqual = @"^(\S+)\s+(.+?)\s+(\w+)\s*=\s*(.+?)(?=\s{2,}.*)?(\s{2}.*)?$";
                var match = Regex.Match(raw, regEqual, RegexOptions.Singleline);
                if (match.Success)
                {
                    result.TagName = match.Groups[1].Value.Trim();
                    result.Description = match.Groups[2].Value.Trim();
                    result.FieldType = match.Groups[3].Value.Trim();
                    var valRaw = match.Groups[4].Value.Trim();
                    result.Suffix = match.Groups[5].Value?.Trim() ?? "";

                    // 拆分 valRaw 提取数值、单位
                    var valParts = valRaw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (valParts.Length >= 1 && double.TryParse(valParts[0], out _))
                    {
                        result.Value = valParts[0];
                        // 存在第二段就是单位
                        result.Unit = valParts.Length >= 2 ? valParts[1] : "";
                    }
                    else
                    {
                        // 文字状态无单位
                        result.Value = valRaw;
                        result.Unit = "";
                    }

                    return result;
                }
            }

            // 分支2：无= 纯设备告警，修复正则贪婪匹配，解决带符号描述匹配失败
            // ^(\S+) 标签，\s+(.+)整段描述，最后\s+(\w+)固定英文字段，剩余全部后缀
            var regNoEqual = @"^(\S+)\s+(.+)\s+(\w+)(\s+.*)?$";
            var matchNoEq = Regex.Match(raw, regNoEqual, RegexOptions.Singleline);
            if (matchNoEq.Success)
            {
                result.TagName = matchNoEq.Groups[1].Value.Trim();
                result.Description = matchNoEq.Groups[2].Value.Trim();
                result.FieldType = matchNoEq.Groups[3].Value.Trim();
                result.Value = "";
                result.Unit = "";
                result.Suffix = matchNoEq.Groups[4].Value?.Trim() ?? "";
                return result;
            }

            // 兜底：两种正则都匹配失败，原始文本塞入Suffix，不抛异常阻断事件
            result.Suffix = raw;
            return result;
        }
    }

    public static class OpcEventDataStoreHelper
    {
        /// <summary>
        ///     实时反射获取 OpcEvent 的 protected internal DataStore
        ///     无静态构造，不会触发类型初始化异常
        /// </summary>
        /// <param name="evt">OPC事件对象</param>
        /// <returns>底层IOpcReadOnlyNodeDataStore，失败返回null</returns>
        public static IOpcReadOnlyNodeDataStore TryGetDataStore(this OpcEvent evt)
        {
            if (evt == null)
                return null;

            try
            {
                // 反射查找 DataStore 属性，字符串规避nameof权限报错
                var dataStoreProp = typeof(OpcEvent)
                    .GetProperty(
                        "DataStore",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    );

                if (dataStoreProp == null)
                    return null;

                // 取值转换接口
                var rawValue = dataStoreProp.GetValue(evt);
                return rawValue as IOpcReadOnlyNodeDataStore;
            }
            catch (Exception)
            {
                // 任意反射失败直接返回null，不抛出全局初始化异常
                return null;
            }
        }

        /// <summary>
        ///     通过DataStore读取指定字段值
        /// </summary>
        public static T GetDataStoreField<T>(this OpcEvent evt, string fieldName)
        {
            var store = evt.TryGetDataStore();
            if (store == null)
                return default;

            try
            {
                // 查找IOpcReadOnlyNodeDataStore的Get<T>(string)泛型方法
                var getMethod = store.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(string)
                    );

                if (getMethod == null)
                    return default;

                // 构造泛型方法调用
                var genericGet = getMethod.MakeGenericMethod(typeof(T));
                var result = genericGet.Invoke(store, new object[] { fieldName });
                return result == null ? default : (T)result;
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        ///     获取DataStore内所有原始值数组（无字段名，仅调试）
        /// </summary>
        public static object[] GetAllDataStoreValues(this OpcEvent evt)
        {
            var store = evt.TryGetDataStore();
            if (store == null)
                return Array.Empty<object>();

            try
            {
                return store.ToArray();
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        /// <summary>
        ///     object[] 转为 [[0],[1],[2]] 格式字符串
        /// </summary>
        public static string FormatObjectArray(object[] arr)
        {
            if (arr == null || arr.Length == 0)
                return "[]";

            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < arr.Length; i++)
            {
                var val = arr[i];
                var strVal = val == null ? "" : val.ToString();
                sb.Append($"[{strVal}]");
                // 非最后一个元素加逗号分隔
                if (i != arr.Length - 1)
                    sb.Append(',');
            }

            sb.Append(']');
            return sb.ToString();
        }
    }
}