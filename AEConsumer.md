# OPC AE 告警消息推送 — 客户端接入交付文档

## 1. 概述

本系统通过 **RabbitMQ** 将 OPC AE（报警与事件）实时数据推送给下游消费方。客户端只需连接 RabbitMQ，声明队列并绑定到指定交换机即可接收告警消息。

---

## 2. 连接信息

| 参数 | 值 | 说明 |
|---|---|---|
| **MQ 类型** | RabbitMQ | 当前版本，未来可扩展 Kafka |
| **Host** | `127.0.0.1` | 生产环境请替换为实际 IP |
| **Port** | `5672` | AMQP 协议默认端口 |
| **Username** | `guest` | 请联系运维获取正式账号 |
| **Password** | `guest` | 请联系运维获取正式密码 |
| **Virtual Host** | `/` | 默认虚拟主机 |

---

## 3. 交换机与路由规则

### 3.1 交换机（Exchange）

| 参数 | 值 |
|---|---|
| **名称** | `opc_ae_topic_exchange` |
| **类型** | `topic` |
| **持久化** | 是（Durable） |
| **自动删除** | 否 |

### 3.2 路由键（Routing Key）规则

消息的路由键格式为：

```
{RoutingKeyPrefix}.{SourceName}
```


- **前缀**：`opc.ae`（固定）
- **SourceName**：OPC 事件源名称（即点位/标签名），如 `WI-11301`

**示例**：
```
opc.ae.WI-11301
opc.ae.TI-22501
opc.ae.PDRC-33100
```


### 3.3 客户端订阅建议（Binding Key）

由于交换机类型为 `topic`，客户端可使用通配符灵活订阅：

| Binding Key | 说明 |
|---|---|
| `opc.ae.*` | 订阅所有单个标签的告警 |
| `opc.ae.WI-*` | 订阅所有以 `WI-` 开头的标签 |
| `opc.ae.#` | 订阅所有告警消息（等同于全量接收） |

> **建议**：客户端根据自身业务需要选择合适的 Binding Key，避免不必要的全量订阅造成资源浪费。

---

## 4. 消息数据结构

### 4.1 外层消息（MqMessage）

每条消息的 Body 为 **JSON 格式**，UTF-8 编码，结构如下：

```json
{
    "SourceName": "WI-11301",
    "Message": "<AlarmEventData 序列化的 JSON 字符串>",
    "Severity": 3,
    "Timestamp": "2026-07-14 10:30:45.123"
}
```


| 字段 | 类型 | 说明 |
|---|---|---|
| `SourceName` | `string` | 事件源名称（标签名/点位名），同时作为路由键后缀 |
| `Message` | `string` | **嵌套 JSON 字符串**，内容为完整的 `AlarmEventData` 对象序列化 |
| `Severity` | `int` | 报警级别：`1`=提示，`2`=警告，`3`=紧急 |
| `Timestamp` | `string` | 消息生成时间，格式 `yyyy-MM-dd HH:mm:ss.fff` |

### 4.2 内层消息（AlarmEventData）

`Message` 字段反序列化后的完整结构：

```json
{
    "Id": 1024,
    "EventId": "A1B2C3D4E5F6",
    "EventType": "AlarmCondition",
    "EventTypeId": "nsu=...;i=12345",
    "SourceName": "WI-11301",
    "SourceNodeId": "ns=2;s=WI-11301",
    "NodeId": "ns=2;s=WI-11301.HH",
    "Message": "WI-11301 温度高高报警 HH=98.5 °C",
    "Severity": 3,
    "Time": "2026-07-14T10:30:44.000Z",
    "ReceiveTime": "2026-07-14T10:30:44.015Z",
    "Host": "10.100.107.1",
    "IsActive": true,
    "IsAcked": false,
    "ConditionName": "HH",
    "MatchedRuleName": "高高报警",
    "MatchedRuleEventType": "Alarm",
    "MatchedRuleDescription": "测量值超过高高限"
}
```


| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | `int` | 数据库自增主键 |
| `EventId` | `string` | OPC 事件唯一标识（十六进制字符串） |
| `EventType` | `string` | 事件类型名称，如 `AlarmCondition` |
| `EventTypeId` | `string` | 事件类型节点 ID |
| `SourceName` | `string` | 事件源名称（标签名/点位名） |
| `SourceNodeId` | `string` | 事件源节点 ID |
| `NodeId` | `string` | 事件节点 ID |
| `Message` | `string` | 原始报警消息文本 |
| `Severity` | `int` | 报警级别：`1`=提示，`2`=警告，`3`=紧急 |
| `Time` | `DateTime` | 事件发生时间（OPC 服务器时间） |
| `ReceiveTime` | `DateTime` | 客户端接收时间 |
| `Host` | `string` | 来源 OPC 服务器 IP |
| `IsActive` | `bool?` | 报警是否激活 |
| `IsAcked` | `bool?` | 是否已确认 |
| `ConditionName` | `string` | 条件名称（如 HH、LO 等） |
| `MatchedRuleName` | `string` | 匹配到的告警规则名称 |
| `MatchedRuleEventType` | `string` | 规则事件类型：`Alarm` / `Status` / `Mode` |
| `MatchedRuleDescription` | `string` | 规则中文描述 |

---

## 5. 告警规则字典

### 5.1 报警状态（AlarmState）

| Code | 名称 | 级别 | 描述 |
|---|---|---|---|
| `NR` | 正常状态 | 1-提示 | 正常运行状态 |
| `OOP` | 输出开路报警 | 3-紧急 | 输出回路开路故障 |
| `IOP` | 输入开路报警（超出上限值） | 3-紧急 | 输入信号超出上限开路 |
| `IOP-` | 输入开路报警（低于下限值） | 3-紧急 | 输入信号低于下限开路 |
| `HH` | 高高报警 | 3-紧急 | 测量值超过高高限 |
| `HI` | 高报警 | 2-警告 | 测量值超过高限 |
| `LO` | 低报警 | 2-警告 | 测量值低于低限 |
| `LL` | 低低报警 | 3-紧急 | 测量值低于低低限 |
| `VEL+` | 速率正向变化报警 | 2-警告 | 测量值变化速率正向超限 |
| `VEL-` | 速率负向变化报警 | 2-警告 | 测量值变化速率负向超限 |
| `CNF` | 连接错误报警 | 3-紧急 | 设备连接故障 |
| `CERR` | 计算错误报警 | 3-紧急 | 数据计算异常 |
| `DV+` | 偏差正向报警 | 2-警告 | 测量值与设定值正向偏差超限 |
| `DV-` | 偏差负向报警 | 2-警告 | 测量值与设定值负向偏差超限 |
| `MHI` | 输出高限报警 | 2-警告 | 输出值被限制在高限 |
| `MLO` | 输出低限报警 | 2-警告 | 输出值被限制在低限 |
| `PERR` | 回讯不一致报警 | 3-紧急 | 高限位和低限位同时满足 |
| `ANS+` | 正回讯错误 | 3-紧急 | 输出为 ON，回讯不为 ON |
| `ANS-` | 负回讯错误 | 3-紧急 | 输出为 OFF，回讯不为 OFF |

### 5.2 数据状态（DataState）

| Code | 名称 | 级别 | 描述 |
|---|---|---|---|
| `BAD` | 坏值 | 2-警告 | 数据质量为坏值 |
| `QST` | 有问题值 | 2-警告 | 数据质量异常 |
| `CLP+` | 数据高钳位 | 1-提示 | 输出被限制在高限值 |
| `CLP-` | 数据低钳位 | 1-提示 | 输出被限制在低限值 |
| `CAL` | 调校状态 | 1-提示 | 设备正在调校 |

### 5.3 模式状态（ModeState）

| Code | 名称 | 级别 | 描述 |
|---|---|---|---|
| `O/S` | 失效 | 3-紧急 | 设备处于失效状态 |
| `IMAN` | 初始化手动状态 | 1-提示 | 手动模式初始化阶段 |
| `TRK` | 跟踪状态 | 1-提示 | 设备处于跟踪模式 |
| `MAN` | 手动状态 | 1-提示 | 设备处于手动控制模式 |
| `AUT` | 自动状态 | 1-提示 | 设备处于自动控制模式 |
| `CAS` | 串级状态 | 1-提示 | 设备处于串级控制模式 |
| `PRD` | 直接输出状态 | 1-提示 | 设备处于直接输出模式 |

---

## 6. 消息属性

| 属性 | 值 |
|---|---|
| **Content-Type** | `application/json` |
| **Delivery Mode** | 由配置决定（当前默认非持久化） |
| **编码** | UTF-8 |

---

## 7. 默认队列与策略配置

为方便快速接入，服务端已在 RabbitMQ 管理界面创建了默认队列 `mes_data_queue`，并完成与交换机的绑定。客户端可直接使用该队列消费消息，也可根据自身需求自行创建队列并绑定。

### 7.1 默认队列信息

| 参数 | 值 | 说明 |
|---|---|---|
| **队列名称** | `mes_data_queue` | 默认队列名，客户端可自定义 |
| **Virtual Host** | `/` | 默认虚拟主机 |
| **持久化** | Durable | 队列持久化，RabbitMQ 重启后队列不丢失 |

### 7.2 队列策略（Arguments）

| 参数 | 值 | 说明 |
|---|---|---|
| `x-message-ttl` | `86400000` | 消息存活时间，单位毫秒（24 小时），超时消息将被自动丢弃 |
| `x-max-length` | `500000` | 队列最大消息条数，达到上限后触发溢出策略 |
| `x-overflow` | `drop-head` | 溢出行为：丢弃队列头部（最早）的消息，保留最新消息 |

> **说明**：以上策略旨在防止消息无限堆积导致内存溢出。当消费方处理速度跟不上生产速度时，超过 50 万条或超过 24 小时的旧消息将被自动清理，消费方始终获取到的是最新告警数据。

### 7.3 默认绑定关系

默认队列已绑定到交换机 `opc_ae_topic_exchange`，绑定信息如下：

| 参数 | 值 |
|---|---|
| **Exchange** | `opc_ae_topic_exchange` |
| **To Queue** | `mes_data_queue` |
| **Routing Key** | `opc.ae.#` |

> **注意**：默认绑定使用 `opc.ae.#` 通配符，表示接收所有 OPC AE 告警消息。若客户端自行创建队列，请根据实际业务需要选择合适的 Binding Key（参见 [3.3 客户端订阅建议](#33-客户端订阅建议binding-key)）。

### 7.4 客户端自行创建队列（可选）
客户端若需自行创建队列，可参考以下步骤：
```
1. 在 RabbitMQ 管理界面或代码中声明新队列（建议设置为 Durable）
2. 根据业务需要设置队列策略（x-message-ttl / x-max-length / x-overflow）
3. 将新队列绑定到交换机 opc_ae_topic_exchange
4. 设置合适的 Binding Key（如 opc.ae.# 或 opc.ae.WI-*）
5. 开始消费消息
```

## 8. 客户端接入步骤（参考）
```
1. 建立 RabbitMQ 连接（使用上方连接信息）
2. 使用默认队列 mes_data_queue，或自行声明新队列
3. 将队列绑定到交换机 opc_ae_topic_exchange（默认队列已绑定）
4. 设置 Binding Key（如：opc.ae.# 全量订阅）
5. 开始消费消息
6. 解析消息 Body（JSON）得到 MqMessage
7. 二次解析 MqMessage.Message 字段（嵌套 JSON）得到 AlarmEventData

```


### 消费示例（伪代码）

```
# 1. 解析外层
mq_message = json.loads(body)
source = mq_message["SourceName"]       # "WI-11301"
severity = mq_message["Severity"]       # 3
timestamp = mq_message["Timestamp"]     # "2026-07-14 10:30:45.123"

# 2. 解析内层（嵌套 JSON）
alarm_data = json.loads(mq_message["Message"])
tag_name = alarm_data["SourceName"]          # "WI-11301"
alarm_msg = alarm_data["Message"]            # 原始报警文本
rule_name = alarm_data["MatchedRuleName"]    # "高高报警"
rule_desc = alarm_data["MatchedRuleDescription"]  # "测量值超过高高限"
host = alarm_data["Host"]                    # "10.100.107.1"
```


---

## 9. 注意事项

1. **消息为实时推送，不保证顺序**：消费方需自行处理乱序场景
2. **消息非持久化**：当前配置 `Persistent=false`，RabbitMQ 重启后消息丢失；如需持久化请联系管理员修改配置
3. **嵌套 JSON**：`MqMessage.Message` 是字符串类型的嵌套 JSON，需要**两次反序列化**
4. **断线重连**：服务端已启用自动重连（间隔 5 秒），客户端也应实现相应的重连机制
5. **心跳检测**：服务端心跳间隔 30 秒，建议客户端设置相同或略大的心跳超时