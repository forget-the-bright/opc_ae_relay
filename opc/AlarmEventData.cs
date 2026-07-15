using System;

namespace opc_ae_relay.opc;

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