# opc_ae_relay

**OPC UA Alarm & Events 告警采集转发服务**

Github：https://github.com/forget-the-bright/opc_ae_relay

## 项目简介

基于 .NET Framework 4.8 开发的工业告警采集转发服务，通过 OPC UA/Classic（DCOM）协议采集横河（Yokogawa）等 DCS 系统的告警事件，支持多数据库持久化、RabbitMQ 实时推送、标签白名单过滤，内置 Web 监控面板提供 WebSocket 实时日志、性能监控与进程级网络流量统计。

## 核心功能

- ✅ OPC UA AE 事件订阅，兼容横河 Exaopc 等 OPC Classic（DCOM）服务
- ✅ OPC AE 服务自动发现（基于 OPC Classic COM 接口枚举远程服务器）
- ✅ 多 OPC 服务器并发采集，每服务器独立后台线程
- ✅ 看门狗守护进程，线程死亡 5 秒内自动重启
- ✅ 标签白名单过滤（`tagFilter.xlsx`），支持文件变更自动热加载
- ✅ XML 配置告警规则匹配（告警码分类、分级：AlarmState / DataState / ModeState）
- ✅ 多数据库持久化存储（SQL Server / MySQL，Provider 架构可扩展）
- ✅ RabbitMQ 实时告警推送（Topic 交换机，支持通配符订阅）
- ✅ 内置 EmbedIO + Scriban 轻量 Web 监控面板
  - WebSocket 实时日志推送（最近 200 条历史回放）
  - OPC 服务器连接状态实时展示
  - 程序性能监控（内存 / CPU / GC / 线程 / 运行时长）
  - 基于 ETW 的进程级 TCP 连接与流量统计
  - Web 应用层流量计量（入站 / 出站 / 请求数）
- ✅ Serilog 结构化日志（控制台 + 按天滚动文件 + 错误日志独立文件 + 内存 Sink 供 Web 展示）
- ✅ 优雅退出（Ctrl+C 触发顺序关闭）

## 数据流转架构

```

Yokogawa OPC Classic AE Server（可多台，DCOM 协议）
↓（实时告警事件，OPC UA/Classic 订阅）
opc_ae_relay 核心采集程序（多线程 + 看门狗）
↓
标签白名单过滤（tagFilter.xlsx）
↓
告警规则匹配（application.xml AlarmRules）
↓
├─→ 数据库（SQL Server / MySQL，全量告警持久化，含规则匹配字段）
├─→ RabbitMQ（实时告警推送，Topic 交换机 opc_ae_topic_exchange）
└─→ 内置 Web 面板（WebSocket 实时日志 + 性能监控 + 网络流量统计）
```
## 技术栈

| 组件 | 技术选型 | 版本 |
|------|----------|------|
| 运行框架 | .NET Framework | 4.8 |
| OPC 客户端 | Opc.UaFx.Advanced（Traeger） | 2.54.0 |
| OPC 服务发现 | GodSharp.Opc.Da.OpcAutomation | 2022.308.10 |
| Web 服务 | EmbedIO（自托管，无需 IIS） | 3.5.2 |
| 模板引擎 | Scriban | 7.2.5 |
| 数据库 ORM | Dapper | 2.1.79 |
| SQL Server 驱动 | Microsoft.Data.SqlClient | 7.0.2 |
| MySQL 驱动 | MySqlConnector | 2.6.1 |
| 消息队列 | RabbitMQ.Client | 7.2.1 |
| 日志 | Serilog + Sinks（Console / File） | 4.3.1 |
| Excel 解析 | ClosedXML | 0.105.0 |
| ETW 网络监控 | Microsoft.Diagnostics.Tracing.TraceEvent | 3.2.5 |
| JSON 序列化 | Newtonsoft.Json | 13.0.4 |

## 项目目录结构

```

opc_ae_relay/
├── Program.cs                    # 程序入口
├── application.xml               # 主配置（OPC服务器/数据库/MQ/告警规则/Web）
├── tagFilter.xlsx                # 标签白名单过滤表（第一列为标签名）
├── App.config                    # 框架绑定重定向
├── opc_ae_relay.sln              # VS 解决方案
├── opc_ae_relay.csproj           # 项目文件
├── publish_release.ps1           # Release 发布脚本
│
├── core/                         # 应用宿主
│   └── AppHost.cs                # 服务启动、初始化、优雅退出调度
│
├── config/                       # 配置解析
│   ├── Config.cs                 # application.xml 配置模型与加载（AppConfigLoader）
│   ├── AlarmRuleConfig.cs        # 告警规则匹配逻辑（长码优先）
│   ├── LogConfig.cs              # Serilog 日志配置（控制台+文件+错误+内存Sink）
│   └── TagFilterConfig.cs        # 标签白名单过滤（xlsx解析 + FileSystemWatcher热加载）
│
├── opc/                          # OPC 客户端核心
│   ├── OpcClassicAEClient.cs     # OPC AE 客户端（DCOM安全配置、事件订阅、告警解析）
│   ├── OpcAeClientRun.cs         # 多线程调度、看门狗守护
│   ├── OpcDiscoverServer.cs      # OPC 服务自动发现（COM枚举远程AE服务器）
│   ├── AlarmEventData.cs         # 告警事件数据模型（对应 AlarmEvent 表）
│   ├── AlarmInfo.cs              # 告警信息解析封装
│   └── OpcEventDataStoreHelper.cs # OPC事件 DataStore 反射辅助
│
├── db/                           # 数据库访问层（Provider 架构）
│   ├── IDbProvider.cs            # 数据库提供者接口
│   ├── SqlServerProvider.cs      # SQL Server 实现
│   ├── MySqlProvider.cs          # MySQL 实现
│   ├── DbProviderFactory.cs      # 提供者工厂（按 type 创建实例）
│   └── DbManager.cs              # 多数据库实例生命周期管理
│
├── mq/                           # 消息队列层（Producer 架构）
│   ├── IMqProducer.cs            # 消息生产者接口
│   ├── RabbitMqProducer.cs       # RabbitMQ 实现（自动重连）
│   ├── MqProducerFactory.cs      # 生产者工厂
│   ├── MqManager.cs              # 多 MQ 实例生命周期管理
│   └── MqMessage.cs              # 消息数据模型
│
├── web/                          # Web 服务层（EmbedIO + Scriban）
│   ├── EmbedIoWebServer.cs       # Web 服务启动、路由、API、模板渲染
│   ├── LogWebSocketModule.cs     # WebSocket 日志实时推送模块
│   ├── TrafficTrackingModule.cs  # 全局 HTTP 流量统计中间件
│   ├── WebTrafficCounter.cs      # Web 应用层流量计数器
│   └── OpcServerViewModel.cs     # 首页模板视图模型
│
├── util/                         # 工具类
│   ├── CpuUtil.cs                # CPU 使用率 / 运行时长计算
│   ├── DBUtil.cs                 # 数据库工具类（兼容层，委托 DbManager）
│   └── EtwTrafficMonitor.cs      # ETW 内核级 TCP 连接与流量监控
│
├── view/                         # Web 前端
│   ├── index.html                # Scriban 模板（监控面板）
│   └── static/
│       ├── app.js                # 前端逻辑（WebSocket日志 + API轮询 + 性能面板）
│       └── style.css             # 面板样式
│
├── test/                         # 测试代码
│   └── AeServerSelectTest.cs     # OPC AE 服务发现测试
│
├── AEConsumer.md                 # RabbitMQ 消费方接入文档
├── RunApp.md                     # 运行说明
└── OPC DA_AE DCOM 完整配置原理+避坑手册（客户端+服务端）.md
```
## 快速部署

### 1. 环境要求

- Windows 系统（DCOM 协议依赖 Windows 平台）
- .NET Framework 4.8 运行时
- 管理员权限（ETW 网络监控需要）
- OPC 服务端 DCOM 配置完成（参见项目内 DCOM 配置手册）

### 2. 克隆代码

```
bash
git clone https://github.com/forget-the-bright/opc_ae_relay
```
### 3. 修改配置文件 `application.xml`

| 配置节点 | 说明 |
|----------|------|
| `<OPCServers>` | OPC 服务器列表：名称、ProgId、IP、端口 |
| `<Databases>` | 数据库实例：type（sqlserver/mysql）、enabled、isDefault、connectionString |
| `<MQs>` | RabbitMQ 连接：地址、端口、认证、交换机、队列、路由键前缀 |
| `<Web>` | Web 服务监听地址和端口（默认 `+:9000`） |
| `<AlarmRules>` | 告警规则：Code、Category、EventType、Severity、Name、Desc |

### 4. 配置标签过滤表（可选）

编辑 `tagFilter.xlsx`，在第一列填入需要采集的标签名（SourceName）。

- 文件不存在或为空 → 全部放行，不过滤
- 支持 Excel 保存后自动热加载，无需重启服务

### 5. 编译运行

```
powershell
# Visual Studio 2019/2022 打开解决方案 → NuGet 还原 → 编译 Release
# 或直接运行发布脚本
.\publish_release.ps1

# 运行（需管理员权限以启用 ETW 网络监控）
.\bin\Release\opc_ae_relay.exe
```
浏览器访问 `http://localhost:9000` 打开监控面板。

## Web 监控面板

内置 EmbedIO 自托管 Web 服务，Scriban 模板渲染，功能包括：

### 实时日志（WebSocket）

- 连接 `/ws/logs` 后实时接收所有日志
- 新客户端首次连接自动推送最近 200 条历史日志
- 无需轮询，真正的实时推送

### OPC 服务器状态

- 左侧菜单展示所有已配置的 OPC 服务器
- 状态面板显示：服务器 IP、ProgID、运行状态（在线/离线）、线程重启次数
- `/api/status` 接口每 5 秒自动刷新

### 性能监控

- `/api/performance` 接口提供：
  - **内存**：工作集、专用内存、GC 托管堆、GC 各代回收次数、句柄数
  - **CPU**：使用率、活跃线程数、运行时长
  - **网络**：TCP 连接总数、已建立连接数、Web 累计入站/出站流量、请求数

### 网络流量统计（ETW）

- 基于 Windows ETW 内核网络事件，精确追踪本进程所有 TCP 连接
- 归一化四元组：Send/Recv 事件自动合并为同一连接记录
- 连接明细表展示：本地地址、远程地址、状态、接收/发送字节数
- 自动聚合 Web 服务器入站连接和中间件连接

## 告警规则说明

告警规则在 `application.xml` 的 `<AlarmRules>` 中配置，支持三类：

| 分类 | Category | 示例 |
|------|----------|------|
| 报警状态 | AlarmState | HH（高高报警）、LO（低报警）、IOP-（输入开路）等 |
| 数据状态 | DataState | BAD（坏值）、QST（有问题值）、CLP+（高钳位）等 |
| 模式状态 | ModeState | MAN（手动）、AUT（自动）、CAS（串级）等 |

匹配逻辑：按告警码长度倒序优先匹配长码（如 `IOP-` 优先于 `IOP`），避免短码误匹配。

## 数据库架构

采用 **Provider 模式**，支持多数据库类型、可配置、可关闭。

```

IDbProvider（接口）
├── SqlServerProvider（SQL Server 实现）
├── MySqlProvider（MySQL 实现）
└── ...（可扩展 PostgreSQL / Oracle 等）

DbProviderFactory → 根据配置 type 创建对应 Provider
DbManager → 统一管理多数据库实例生命周期
```
### 配置示例

```
xml
<Databases>
<Database name="SQLServer-Primary" type="sqlserver" enabled="true" isDefault="true"
connectionString="Data Source=172.16.176.15;Initial Catalog=OPC_AE_DATA;..."/>
<Database name="MySQL-Secondary" type="mysql" enabled="false"
connectionString="Server=127.0.0.1;Port=3306;Database=opc_ae_data;..."/>
</Databases>
```
### 数据库表结构（AlarmEvent）

| 字段 | 说明 |
|------|------|
| Id | 自增主键 |
| EventId | OPC 事件唯一 ID |
| EventType | 事件类型名称 |
| EventTypeId | 事件类型节点 ID |
| SourceName | 事件源名称（标签名） |
| SourceNodeId | 事件源节点 ID |
| NodeId | 事件节点 ID |
| Message | 告警消息原文 |
| Severity | 告警级别（1=提示，2=警告，3=紧急） |
| Time | 事件发生时间（OPC 服务器时间） |
| ReceiveTime | 客户端接收时间 |
| Host | 来源 OPC 服务器 IP |
| IsActive | 报警是否激活 |
| IsAcked | 是否已确认 |
| ConditionName | 匹配的告警码（如 HH、IOP-） |
| MatchedRuleName | 匹配到的规则名称 |
| MatchedRuleEventType | 匹配到的规则事件类型 |
| MatchedRuleDescription | 匹配到的规则描述 |

## RabbitMQ 消息推送

### 连接配置

| 参数 | 默认值 |
|------|--------|
| 交换机 | `opc_ae_topic_exchange`（topic 类型，持久化） |
| 路由键 | `opc.ae.{SourceName}`（如 `opc.ae.WI-11301`） |
| 默认队列 | `mes_data_queue`（Durable，TTL 24h，最大 50 万条，溢出丢弃头部） |
| 默认绑定 | `opc.ae.#`（全量订阅） |
| 心跳 | 30 秒 |
| 断线重连 | 5 秒间隔自动恢复 |

### 消息结构

```
json
{
"SourceName": "WI-11301",
"Message": "{...AlarmEventData 完整 JSON...}",
"Severity": 3,
"Timestamp": "2026-07-14 10:30:45.123"
}
```
> 详细接入文档见 [AEConsumer.md](./AEConsumer.md)

## 服务启动顺序

```

Config.InitAll()          → 日志 / 告警规则 / 标签过滤器初始化
EtwTrafficMonitor.Start() → ETW 网络流量监控启动
EmbedIoWebServer.Start()  → Web 服务启动
DbManager.Init()          → 数据库连接初始化
MqManager.InitAsync()     → RabbitMQ 连接初始化
OpcAeClientRun.runOPC()   → OPC 采集线程 + 看门狗启动
```
关闭时按逆序优雅退出（Ctrl+C 触发）。

## DCOM 配置要点

本项目通过 OPC Classic（DCOM）协议连接横河 OPC AE 服务器，客户端安全配置已做最大兼容：

- AuthenticationLevel = None（关闭 DCOM 加密校验）
- ImpersonationLevel = Identify（最低权限）
- AuthenticationService = WinNt（NTLM）
- AuthorizationService = None
- ProxyCapabilities = None

> 完整 DCOM 配置原理与避坑指南见项目内文档：[OPC_DA_AE-DCOM-完整双端配置原理+避坑手册.md](./OPC_DA_AE-DCOM-完整双端配置原理+避坑手册.md)

## 开发说明

- **适配场景**：冶金、化工横河 DCS 系统告警采集
- **架构特点**：数据库层和消息队列层均采用 Provider / Factory / Manager 模式，扩展新类型只需新增实现类 + 配置
- **平台限制**：依赖 Windows DCOM + ETW，仅支持 Windows 平台
- **Release 输出整理**：编译 Release 后自动将 DLL / XML / PDB / 资源文件归入 `lib/` 子目录，主目录仅保留 exe + 配置 + view
- **拓展方向**：可增加 MQTT 广播、PostgreSQL/InfluxDB 存储、Web 历史查询页、告警短信/钉钉推送

## 许可证

MIT License，可自由修改、商用二次开发。

## 问题反馈

如有 Bug、功能需求，提交 GitHub Issue。
```


