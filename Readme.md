# opc_ae_relay

OPC UA Alarm & Events 告警采集转发服务
Github：https://github.com/forget-the-bright/opc_ae_relay

## 项目简介

基于 .NET Framework 4.8、Traeger Opc.UaFx 开发的工业告警采集转发工具，专门采集横河（Yokogawa）等 OPC UA 服务器的告警事件。

### 核心设计特点

1. **多 OPC 服务器并发采集**：每个 OPC 服务器独立线程监听，支持同时采集多台横河 DCS 告警；
2. **看门狗自动重连**：后台守护线程每 5 秒检测 OPC 线程存活状态，线程异常退出自动重启；
3. **告警规则匹配**：支持 XML 配置告警码（如 HH/LO/IOP-/MAN 等）分类、分级，匹配结果随事件入库；
4. **SQL Server 全量持久化**：每条告警事件完整字段写入数据库，支持历史追溯；
5. **内置轻量 Web 面板**：Nancy + Kestrel 自托管 Web 页面，实时展示 OPC 连接状态与近期告警日志。

## 核心功能

- ✅ OPC UA AE 事件订阅，兼容横河 Exaopc 等工业 OPC UA 服务
- ✅ OPC AE 服务自动发现（基于 OPC Classic COM 接口）
- ✅ 多 OPC 服务器同时采集，每服务器独立后台线程
- ✅ 看门狗守护进程，线程死亡自动重启
- ✅ SQL Server 全量告警持久化存储（Dapper ORM，每条事件完整字段入库）
- ✅ XML 配置告警过滤规则（告警码匹配、分级分类：AlarmState / DataState / ModeState）
- ✅ 内置 Nancy + Kestrel 轻量 Web 前端，实时查看 OPC 连接状态、近期告警日志
- ✅ Serilog 日志（控制台 + 按天滚动文件 + 内存缓冲供 Web 展示）

## 数据流转架构

```

Yokogawa OPC UA AE Server（可多台）
        ↓（实时告警事件，OPC UA 订阅）
opc_ae_relay 核心采集程序（多线程 + 看门狗）
        ├─→ SQL Server（全量告警持久化，含规则匹配字段）
        └─→ 内置 Web 面板（实时日志 + OPC 连接状态）
```
## 环境依赖

- 运行框架：.NET Framework 4.8
- OPC UA 驱动：Opc.UaFx.Advanced 2.54.0（Traeger）
- OPC 服务发现：GodSharp.Opc.Da 2022.308.10（OPC Classic COM）
- 数据库：SQL Server 2016+（通过 Microsoft.Data.SqlClient + Dapper）
- Web 服务：Nancy 2.0.0 + Microsoft.AspNetCore.Server.Kestrel 2.3.0（自托管，无需 IIS）
- 日志：Serilog 4.3.1（控制台 + 文件 + 内存 Sink）
- 序列化：Newtonsoft.Json 13.0.4

## 项目目录结构

```

opc_ae_relay
├── .gitignore                # .NET Framework 忽略文件
├── application.xml           # 主配置（OPC服务器/数据库/MQ/告警规则）
├── App.config                # 框架绑定重定向配置
├── opcLearn.sln              # VS 解决方案
├── opcLearn.csproj           # 项目文件
├── Program.cs                # 程序入口、启动调度
├── Properties/               # 程序集信息
├── client/                   # OPC AE 客户端封装
│   └── YokogawaAEClient.cs   # 横河 OPC 事件采集、告警解析、规则匹配、入库
├── config/                   # 全局配置解析
│   ├── Config.cs             # application.xml 配置模型与加载
│   ├── AlarmRuleConfig.cs    # 告警规则匹配逻辑
│   ├── LogConfig.cs          # Serilog 日志配置（控制台+文件+内存）
│   └── WebConfig.cs          # Nancy Web 路由与启动（HomeModule/Bootstrapper）
├── core/                     # 业务核心调度
│   └── OpcAeClientRun.cs     # 多线程 OPC 订阅、看门狗守护
├── discoverServer/           # OPC 服务自动发现
│   ├── DiscoverServer.cs     # OPC DA/AE 服务发现（COM 接口）
│   └── AeServerSelectTest.cs
├── util/                     # 工具类
│   └── DBUtil.cs             # Dapper 数据库操作封装（CRUD/事务/泛型Insert）
├── view/                     # Nancy Web 静态前端
│   ├── index.html            # 告警监控面板页面（Nancy 模板）
│   └── static/
│       ├── app.js            # 前端轮询逻辑（状态刷新+日志拉取）
│       └── style.css         # 面板样式
├── logs/                     # 本地日志目录（按天滚动，保留30天）
└── packages/                 # NuGet 依赖包
```
## 快速部署

### 1. 克隆代码

```
bash
git clone https://github.com/forget-the-bright/opc_ae_relay
```
### 2. 修改配置文件 `application.xml`

填写三组核心配置：

1. **OPC 服务器**：`<OPCServers>` 节点，配置每台 OPC 服务器的名称、ProgId、IP、端口；
2. **数据库**：`<Databases>` 节点，填写 SQL Server 连接字符串；
3. **告警规则**：`<AlarmRules>` 节点，配置需要匹配的告警码（Code）、分类（Category）、事件类型（EventType）、严重级别（Severity）、名称与描述。

### 3. 编译运行

1. Visual Studio 2019/2022 打开解决方案；
2. NuGet 还原依赖包；
3. 编译 Release；
4. 运行 `opcLearn.exe`；
5. 浏览器访问 `http://localhost:9000` 打开告警监控面板。

## Web 面板说明

内置 Nancy 自托管 Web 页面（端口 9000），功能：

- 左侧菜单展示所有已配置的 OPC 服务器，点击切换查看各服务器状态；
- 状态面板显示：服务器 IP、ProgID、运行状态（在线/离线）、监听线程 ID；
- 实时日志面板：轮询展示最近 100 条 OPC 告警日志（500ms 刷新）；
- 状态接口 `/api/status` 每 5 秒自动刷新。

## 告警规则说明

告警规则在 `application.xml` 的 `<AlarmRules>` 中配置，支持三类：

| 分类 | Category | 示例 |
|------|----------|------|
| 报警状态 | AlarmState | HH（高高报警）、LO（低报警）、IOP-（输入开路）等 |
| 数据状态 | DataState | BAD（坏值）、QST（有问题值）、CLP+（高钳位）等 |
| 模式状态 | ModeState | MAN（手动）、AUT（自动）、CAS（串级）等 |

每条规则包含：告警码（Code）、分类（Category）、事件类型（EventType）、严重级别（1~3）、中文名称、描述。
匹配逻辑：按告警码长度倒序优先匹配长码（如 `IOP-` 优先于 `IOP`），避免短码误匹配。

## 数据库表结构

告警事件写入 `AlarmEvent` 表，字段：

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
| ConditionName | 条件名称 |
| MatchedRuleName | 匹配到的规则名称 |
| MatchedRuleEventType | 匹配到的规则事件类型 |
| MatchedRuleDescription | 匹配到的规则描述 |

## 关键依赖包

| 包名 | 版本 | 用途 |
|------|------|------|
| Opc.UaFx.Advanced | 2.54.0 | OPC UA 客户端，事件订阅 |
| GodSharp.Opc.Da | 2022.308.10 | OPC Classic COM 服务发现 |
| Nancy / Nancy.Hosting.Self | 2.0.0 | 内置轻量 Web 服务 |
| Microsoft.AspNetCore.Server.Kestrel | 2.3.0 | Web 底层 HTTP 服务器 |
| Microsoft.Data.SqlClient | 7.0.2 | SQL Server 连接驱动 |
| Dapper | 2.1.79 | 轻量 ORM，数据库操作 |
| Serilog + Sinks | 4.3.1 | 日志（控制台 + 文件 + 内存） |
| Newtonsoft.Json | 13.0.4 | JSON 序列化 |
| Portable.BouncyCastle | 1.9.0 | OPC 证书加密 |

## 许可证

MIT License，可自由修改、商用二次开发。

## 开发说明

- 适配场景：冶金、化工横河 DCS 系统告警采集；
- 拓展方向：可增加 MQTT 消息广播、MySQL/InfluxDB 存储、Web 历史查询页、告警短信/钉钉推送；
- 不依赖第三方 Web 服务，单 exe 开箱即用。

## 问题反馈

如有 Bug、功能需求，提交 GitHub Issue。

