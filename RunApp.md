# OPC AE 采集服务 — 部署与启动说明

## 1. 环境要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows Server / Windows 10+ |
| 运行时 | **.NET Framework 4.8**（需提前安装） |
| 数据库 | SQL Server 2016 及以上版本 |
| 消息队列 | RabbitMQ（默认端口 5672） |
| 网络 | 需能访问 OPC 服务器所在 IP 的 135 端口（DCOM） |

## 2. 目录结构

```
opc_ae_relay/
├── opc_ae_relay.exe          # 主程序
├── opc_ae_relay.exe.config   # 程序集绑定配置（勿删）
├── application.xml            # ★ 核心配置文件（需按实际环境修改）
├── lib/                       # 依赖 DLL 目录
├── logs/                      # 运行日志目录（自动创建）
└── view/                      # Web 前端静态文件
    ├── index.html
    ├── static/app.js
    └── static/style.css
```


> ⚠️ **请勿删除或移动 `opc_ae_relay.exe.config`**，程序依赖其中的 DLL 重定向配置来查找 `lib/` 目录下的依赖库。

## 3. 配置文件说明（application.xml）

部署时需根据实际环境修改 `application.xml`，主要配置项如下：

### 3.1 数据库连接

```xml
<Databases>
    <Database name="Default"
              connectionString="Data Source=数据库IP;Initial Catalog=OPC_AE_DATA;User ID=sa;Password=密码;TrustServerCertificate=True"/>
</Databases>
```


- `Data Source`：SQL Server 地址
- `Initial Catalog`：数据库名称
- 确保数据库已提前创建并授权

### 3.2 消息队列（RabbitMQ）

```xml
<MQ>
    <Host>127.0.0.1</Host>     <!-- RabbitMQ 地址 -->
    <Port>5672</Port>           <!-- 默认端口 -->
    <User>admin</User>
    <Password>123456</Password>
    <QueueName>mes_data_queue</QueueName>
</MQ>
```


### 3.3 OPC 服务器连接

```xml
<OPCServers>
    <Server name="服务器名称" progId="Yokogawa.ExaopcAECS1" ip="10.100.107.1" port="135"/>
</OPCServers>
```


- 可配置多个 `<Server>` 节点，程序会依次连接
- `progId`：OPC AE 服务器的 ProgID，需与现场 OPC 服务器注册信息一致
- `ip` / `port`：OPC 服务器地址和端口

### 3.4 Web 服务

```xml
<Web>
    <Host>localhost</Host>
    <Port>9000</Port>
</Web>
```


- 启动后可通过浏览器访问 `http://localhost:9000` 查看运行状态

## 4. 启动方式

### 方式一：直接运行（调试/测试）

双击 `opc_ae_relay.exe` 或在命令行中执行：

```
cd D:\path\to\opc_ae_relay
.\opc_ae_relay.exe
```


### 方式二：注册为 Windows 服务（推荐生产环境）

可使用 [NSSM](https://nssm.cc/) 注册为系统服务：

```powershell
# 安装 NSSM 后执行
nssm install OpcAeRelay "D:\path\to\opc_ae_relay\opc_ae_relay.exe"
nssm set OpcAeRelay AppDirectory "D:\path\to\opc_ae_relay"
nssm start OpcAeRelay
```


## 5. 停止服务

- **控制台运行**：按 `Ctrl + C` 优雅退出（程序会自动关闭 Web 服务和 OPC 连接）
- **Windows 服务**：`nssm stop OpcAeRelay` 或在服务管理器中停止

> ⚠️ 请勿直接杀进程，应使用上述方式让程序完成资源清理后再退出。

## 6. 日志

- 日志框架：Serilog
- 日志文件位于 `logs/` 目录下，按日期滚动
- 控制台也会同步输出日志

## 7. 启动后验证

1. 控制台输出 `服务已就绪，按 Ctrl+C 优雅退出` 表示启动成功
2. 浏览器访问 `http://localhost:9000` 可查看 Web 监控页面
3. 检查 `logs/` 目录下是否有报错日志
4. 确认数据库连接正常、OPC 服务器连接正常

## 8. 常见问题

| 问题 | 排查方向 |
|------|----------|
| 启动报"加载 application.xml 失败" | 检查 `application.xml` 是否存在于 exe 同级目录，XML 格式是否正确 |
| 数据库连接失败 | 检查连接字符串、数据库服务是否启动、网络是否可达 |
| OPC 连接失败 | 检查 OPC 服务器 IP/端口是否可达，ProgID 是否正确，DCOM 权限是否配置 |
| 端口 9000 被占用 | 检查是否有其他程序占用 9000 端口（`netstat -ano \| findstr 9000`） |
| 找不到依赖 DLL | 确认 `lib/` 目录完整，`opc_ae_relay.exe.config` 未被删除或修改 |