using System;
using opc_ae_relay.core;
using opc_ae_relay.db;
using opc_ae_relay.util;

namespace opc_ae_relay
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            AppHost.Run();
        }

        public static void testSql()
        {
            string sql = $@"INSERT INTO AlarmEvent (
    EventId,
    EventType,
    EventTypeId,
    SourceName,
    SourceNodeId,
    NodeId,
    Message,
    Severity,
    Time,
    ReceiveTime,
    Host,
    IsActive,
    IsAcked,
    ConditionName,
    MatchedRuleName,
    MatchedRuleEventType,
    MatchedRuleDescription
)
VALUES (
    'TEST_EVENT_001',
    'Alarm',
    'ns=0;i=1234',
    'FIC101_PID',
    'ns=2;s=Channel1.Device1.Tag1',
    'ns=3;s=AlarmEvent1',
    '高高报警，超出量程上限',
    3,
    GETDATE(),
    GETDATE(),
    '10.100.107.1',
    1,
    0,
    'HH',
    '高高报警',
    'Alarm',
    '测量值超过高高限'
);SELECT SCOPE_IDENTITY();";
          Object id =  DBUtil.ExecuteScalar(sql);
          Console.WriteLine(id);
        }
    }
}