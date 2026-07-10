# opc_ae_relay
OPC UA Alarm & Events 告警转发中继服务
Github：https://github.com/forget-the-bright/opc_ae_relay

## 项目简介
基于 .NET Framework 4.7.2、Traeger Opc.UaFx 开发的工业告警转发工具，专门采集横河（Yokogawa）等OPC UA服务器的告警事件。
### 核心设计特点
1. **双持久出口**：所有告警完整写入SQL Server + MQTT消息广播，全量数据永久留存；
2. **简易Web监控面板**：内置轻量Web页面，仅展示近期告警概览，不加载全量历史（轻量化预览）；
3. **断线自动重连** OPC服务、数据库、MQTT三方断开均自动重试；
4. **告警过滤规则** 支持XML配置告警筛选、分级过滤；
5. 内置日志：本地日志仅输出少量预览信息，完整告警只存库与MQ。

## 核心功能
- ✅ OPC UA AE 事件订阅、服务自动发现、多服务同时采集
- ✅ SQL Server 全量告警持久化存储（每条事件完整字段入库）
- ✅ MQTT 实时JSON广播告警（供MES/大屏/数据平台消费）
- ✅ 内置Nancy轻量Web前端，可视化查看近期告警列表、OPC连接状态
- ✅ XML配置告警过滤规则（过滤无用事件、分级分流）
- ✅ Serilog本地日志输出（仅概要，完整告警不落地日志文件）
- ✅ 兼容横河Exaopc等工业OPC UA服务

## 数据流转架构
```
Yokogawa OPC UA AE Server
        ↓（实时告警事件）
opc_ae_relay 核心采集程序
        ├─→ SQL Server（全量完整告警持久，可历史查询）
        └─→ MQTT Broker（JSON全量广播）
                ↓
        内置Web页面（仅展示最近预览告警，不全量加载）
```

## 环境依赖
- 运行框架：.NET Framework 4.7.2
- OPC驱动：Opc.UaFx.Advanced 2.54.0
- 数据库：SQL Server 2016+
- MQTT Broker：EMQX / Mosquitto 任意标准MQ服务
- Web服务：Nancy SelfHost（内置无需IIS）
- 日志：Serilog

## 项目目录结构
```
opc_ae_relay
├── .gitignore                # .NET Framework 完整忽略文件
├── AlarmRule.xml             # 告警过滤规则配置
├── App.config                # 主配置(OPC/SQL/MQ/Web端口)
├── opcLearn.sln              # VS解决方案
├── opcLearn.csproj           # 项目文件
├── Program.cs                # 程序入口、启动调度
├── Properties/               # 程序集信息
├── client/                   # OPC AE客户端封装
│   └── YokogawaAEClient.cs   # 横河OPC事件采集实现
├── config/                   # 全局配置解析
│   ├── AlarmRuleConfig.cs    # 告警规则解析
│   ├── Config.cs             # 主配置读取
│   ├── LogConfig.cs          # 日志配置
│   └── WebConfig.cs          # Web服务端口配置
├── core/                     # 业务核心调度
│   └── OpcAeClientRun.cs     # OPC订阅、分发核心逻辑
├── discoverServer/           # OPC服务自动发现
│   ├── AeServerSelectTest.cs
│   └── DiscoverServer.cs
├── view/                     # Nancy Web静态前端
│   ├── index.html
│   └── static/
│       ├── app.js
│       └── style.css
├── logs/                     # 本地简易日志目录
└── packages/                 # NuGet依赖包
```

## 快速部署
### 1. 克隆代码
```bash
git clone https://github.com/forget-the-bright/opc_ae_relay
```

### 2. 修改配置文件 `App.config`
填写四组核心配置：
1. OPC UA 服务地址/发现地址；
2. SQL Server 连接字符串；
3. MQTT Broker 地址、主题；
4. Web服务监听端口。

### 3. 告警过滤配置 `AlarmRule.xml`
可配置需要过滤的点位、告警等级、事件类型，不符合规则不会入库/发MQ。

### 4. 编译运行
1. Visual Studio 2019/2022 打开解决方案；
2. 编译 Release；
3. 直接运行 `opcLearn.exe`；
4. 浏览器访问配置端口打开告警预览页面。

## Web界面说明
内置极简静态Web页面，功能限制：
- 仅展示近期告警列表，**不会加载数据库全量历史**；
- 仅用于现场快速预览、查看OPC连接在线状态；
- 完整告警查询、历史检索请直接查询SQL Server或消费MQTT。

## 数据说明
1. **本地logs日志**：仅打印连接状态、异常、少量告警摘要，不存储完整事件；
2. **SQL Server**：每条OPC Event完整字段入库（EventId、节点ID、时间、告警文本、等级等全部原始数据）；
3. **MQTT消息**：推送完整JSON结构化告警，包含OPCEvent所有原生字段；
4. OpcEvent处理：内置反射工具读取底层`DataStore`原始事件字段，完整解析NodeId、EventId、告警描述等。

## 关键依赖包
- Opc.UaFx.Advanced 2.54.0 （OPC UA客户端）
- Nancy.Hosting.Self （内置Web服务）
- Serilog （本地日志）
- Newtonsoft.Json （MQ告警序列化）
- Portable.BouncyCastle （OPC证书加密）

## 许可证
MIT License，可自由修改、商用二次开发。

## 开发说明
- 适配场景：冶金、化工横河DCS系统告警采集；
- 拓展方向：可新增MySQL/InfluxDB存储、增加Web历史查询页、告警短信/钉钉推送；
- 不依赖第三方Web服务，单exe开箱即用。

## 问题反馈
如有Bug、功能需求，提交GitHub Issue。