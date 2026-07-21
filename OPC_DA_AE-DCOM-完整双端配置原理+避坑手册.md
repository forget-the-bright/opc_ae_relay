# OPC DA/AE DCOM 完整配置原理 + 避坑手册（客户端 + 服务端）

## 前言：核心底层原理（所有问题的根源）

老式 OPC（DA1.0/AE/历史）基于 **原生 DCOM/RPC（Windows 系统层）** 实现，完全区别于 TCP 自定义协议：

1. **OPC 业务代码不做任何权限校验、加密、认证**，所有安全校验全部由 Windows 底层 DCOM 子系统接管；
2. DCOM 是**双向通信**：
        - OPC DA：单向（客户端主动请求读/写数据）
        - OPC AE：双向（客户端订阅 + 服务端主动反向回调推送报警事件）
3. DCOM 校验是**两端双向校验**：客户端有配置、服务端也有配置，任意一端严格不匹配，直接失败/静默拦截；
4. Windows 新版补丁默认开启 **DCOM 加固策略**（KB5004442），专门拦截老旧无加密的 OPC 协议，是绝大多数兼容问题的核心。

> **防火墙提醒**：DCOM 依赖 **TCP 135 端口** 以及 **动态分配的高位端口**（通常在 1024–65535 范围内）。若配置后仍无法连接，请检查防火墙是否放行 135 端口及 OPC 服务端程序的入站规则，或临时关闭防火墙进行测试。

---

## 一、DCOM 安全校验的完整链路（理解后续所有问题的前提）

OPC AE 从发起连接到收到回调，要经过 **四层校验**，任何一层失败都会导致连接失败或回调丢失：

```
客户端程序发起连接
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│ 第1层：客户端操作系统 DCOM 环境（注册表控制）                    │
│   - DCOM 加固开关是否关闭                                      │
│   - 全局认证级别是否宽松                                       │
│   - 全局模拟级别是否正确                                       │
│   → 不配：客户端所有 DCOM 调用在系统层就被拦截                   │
├──────────────────────────────────────────────────────────────┤
│ 第2层：客户端代码级 DCOM 安全参数（SDK 控制）  ← 本文重点新增    │
│   - 程序发起 RPC 调用时声明的认证/模拟/授权等级                   │
│   - 此参数决定了「客户端以什么安全身份」与每个服务端交互           │
│   → 不配：SDK 默认参数过严，部分服务端回调被静默丢弃              │
├──────────────────────────────────────────────────────────────┤
│ 第3层：网络层 NTLM 账户鉴权 + 服务端 DCOM 组件权限              │
│   - 工作组同名同密码账户                                        │
│   - 服务端 dcomcnfg 中 OPC 组件的远程权限                       │
│   → 不配：拒绝访问 0x80070005                                   │
├──────────────────────────────────────────────────────────────┤
│ 第4层：客户端本机 DCOM 回调权限（dcomcnfg 控制）                 │
│   - 允许外部机器远程激活/访问本机 COM 对象                       │
│   → 不配：正向连接正常，但 AE 回调永远收不到                     │
└──────────────────────────────────────────────────────────────┘
    │
    ▼
服务端收到连接 → 按客户端声明的安全等级 + 服务端自身策略 决定是否接受
    │
    ▼
服务端反向回调客户端 → 按第2层声明的等级 + 第4层权限 决定是否放行
```

> **关键认知**：第1层是「操作系统全局开关」，第2层是「每个程序每次连接的局部策略」。即使全局开关已放宽（第1层通过），程序自身以高等级安全策略发起调用（第2层未配），Windows 仍会按程序**实际声明的等级**执行校验。

---

## 二、三大核心注册表参数：原理 + 作用 + 不配故障 + 主客配置规则（第1层）

这三项是 OPC 兼容老设备的核心，**客户端、服务端必须同步宽松配置**，不能只改一端。

### 1. DCOM 加固开关：`RequireIntegrityActivationAuthenticationLevel`

- **注册表路径**：`HKLM\SOFTWARE\Microsoft\Ole\AppCompat`
- **标准正常值**：`0`（关闭加固）

#### ✅ 原理
Windows 后期安全补丁（KB5004442）新增的 DCOM 强制校验机制：
- 默认无此项/值=1：**开启 DCOM 加固**，要求所有 COM 远程调用必须带「完整性校验、加密校验」；
- 老式恒河/传统 OPC DA/AE 完全不支持加密和完整性校验；
- 加固开启时，Windows 底层直接拦截 OPC 连接，业务层无报错，直接拒绝。

#### ❌ 不配会出现的现象
- 客户端直接连不上服务端，报错：COM 端口拒绝、远程调用失败、拒绝访问；
- 135 端口通畅、网络无问题，但 DCOM 握手直接失败；
- 新电脑必现，老旧工控机默认关闭加固所以能通（7号机正常的核心原因）。

#### 🎯 配置规则
**客户端、服务端必须全部设置为 0**，缺一不可。

---

### 2. 全局认证级别：`LegacyAuthenticationLevel`

- **注册表路径**：`HKLM\SOFTWARE\Microsoft\Ole`
- **标准正常值**：`1`（Default 无严格认证）

#### ✅ 原理
控制 DCOM 远程连接的**身份认证严格等级**，数值越高校验越严格：
- `1`：最宽松，兼容所有老式无加密 COM 组件（适配老 OPC）；
- `2`（系统默认）：Connect 级别，仅连接时认证，会校验会话安全；
- `≥3`：更高加密级别，老式 OPC 不支持，直接拦截。

DCOM 连接是双向校验：客户端认证等级、服务端认证等级必须互相兼容。

#### ❌ 不配会出现的现象
- 客户端等级高、服务端等级低：握手失败，连不上；
- 客户端等级低、服务端等级高：服务端主动拒绝低安全等级的客户端连接；
- 特殊现象：**能连接、能读 DA 数据，但 AE 回调间歇性失败**。

#### 🎯 配置规则
**客户端、服务端全部设为 1**，统一最宽松兼容模式。

---

### 3. 全局模拟级别：`LegacyImpersonationLevel`

- **注册表路径**：`HKLM\SOFTWARE\Microsoft\Ole`
- **标准正常值**：`2`（Identify 身份识别）

#### ✅ 原理
控制「远程机器模拟本机身份执行操作」的权限等级，**OPC AE 回调核心依赖项**：
- `2`：仅识别身份，适合工控 OPC 双向回调场景；
- `3`（系统默认）：模拟权限过高，老 OPC 权限不匹配，回调校验失败；
- DCOM 反向回调（AE 报警推送）必须依赖合法的模拟级别。

#### ❌ 不配会出现的现象（对应 8 号机故障）
- 正向连接完全正常，OPC 程序显示已连接、注册回调无报错；
- TCP 流量正常互通，但**永远收不到 AE 报警事件**；
- 无任何报错，属于 Windows 底层静默拦截，业务层感知不到。

#### 🎯 配置规则
**客户端、服务端统一设为 2**，是 OPC AE 双向回调的必备条件。

---

## 三、工作组账户密码校验：原理 + 故障 + 解决方案（第3层核心大坑）

### ✅ 原理
纯工作组（非域环境）下，DCOM 远程认证依赖 **NTLM 本地账户哈希校验**，规则极其死板：
1. 客户端用「当前登录 Windows 账户」发起连接；
2. 服务端本地账户库检索**同名账户**，比对密码哈希；
3. **用户名不同 / 密码不同 → 直接鉴权失败**；
4. 7号机正常的原因：**刚好和服务端账户名 + 密码完全一致**，侥幸通关。

### ❌ 不配会出现的现象
- 直接报错：拒绝访问、`0x80070005`；
- 135 端口通、注册表全对，依旧连不上；
- 部分机器通、部分机器不通，无规律。

### ✅ 两种解决方式（二选一）
1. **最优兼容法**：所有客户端、服务端，新建一模一样的本地账户（同名同密码）；
2. **免统一密码法（服务端单独配置）**：  
   修改注册表关闭 UAC 远程账户过滤，允许密码不一致的同名账户访问：
   ```cmd
   reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f
   ```

---

## 四、服务端专属配置（客户端再完美，服务端不配依旧失效）

很多人只改客户端，忽略服务端双向校验，导致反复踩坑。

### 1. 服务端同步三套宽松注册表
原理：服务端接收远程连接时，会二次校验客户端安全等级，服务端加固开启、认证级别过高，会直接拦截宽松客户端。  
→ 请将第二章的三项注册表在服务端同样设置。

### 2. 服务端 OPC 组件独立 DCOM 权限
- 运行 `dcomcnfg` → 组件服务 → 计算机 → 我的电脑 → DCOM 配置；
- 找到**恒河 OPC 服务**（如 `OPCEnum`、具体 OPC Server 名称）；
- 右键属性 → 安全：
        - **启动和激活权限**：添加 `Everyone`，允许「本地启动」「远程启动」「本地激活」「远程激活」；
        - **访问权限**：添加 `Everyone`，允许「本地访问」「远程访问」；
- **标识页**：改为「此用户」，输入管理员账号密码（禁止使用「交互式用户」，因为远程无桌面时会失效）。

### 3. 服务端本地安全策略（老 OPC 必备）
运行 `secpol.msc` 进行配置：
- **本地策略 → 安全选项**：
        - **网络访问：本地帐户的共享和安全模型** → 设置为 **“经典 - 对本地用户进行身份验证，不改变其本来身份”**；
        - **网络访问：让 Everyone 权限应用于匿名用户** → 设置为 **“已启用”**（兼容老式 OPC 匿名 RPC 调用）；
- 若组策略受限，也可通过注册表修改：
  ```cmd
  reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v forceguest /t REG_DWORD /d 0 /f
  reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v everyoneincludesanonymous /t REG_DWORD /d 1 /f
  ```

---

## 五、客户端本机 DCOM 回调权限（dcomcnfg）—— AE 收不到报警的专属原因（第4层）

### ✅ 核心原理（99%的人只懂单向，不懂双向）
- OPC DA：仅客户端出站请求，**只需要客户端向外权限**，对客户端本机入站权限无要求；
- OPC AE：服务端需要**反向主动连接客户端本机的 COM 回调对象**，必须客户端本机允许「外部机器远程激活本地 COM」。

### ❌ 不配会出现的现象（精准对应 8 号机）
- 客户端成功连接服务端，订阅回调注册成功，无任何报错；
- TCP 链路正常有流量；
- **永远收不到 AE 告警推送**（底层静默拦截，业务层无日志）。

### ✅ 客户端必须配置（dcomcnfg）
运行 `dcomcnfg` → 计算机 → 我的电脑右键 → 属性 → **默认安全**：
1. **默认访问权限**：添加 `Everyone`，允许【本地访问、远程访问】；
2. **默认启动和激活权限**：添加 `Everyone`，允许【本地启动、远程启动、本地激活、远程激活】。

**原理**：放开本机 COM 对象的远程调用权限，允许服务端反向回调。

---

## 六、客户端代码层 DCOM 安全参数配置（第2层 —— 解决“配置全对但部分机器/部分服务端仍不回调”的终极方案）

### ✅ 真实场景与问题
按照第一~五章配置完所有注册表、dcomcnfg、账户后，实际部署中仍然出现三种不同表现：

| 场景 | 客户端机器 | OPC1 服务端 | OPC2 服务端 | 表现 |
|------|-----------|------------|------------|------|
| 场景A | 8号机 | ✅ 能收到 AE 回调 | ❌ 收不到 AE 回调 | 同一台机器，有的行有的不行 |
| 场景B | 7号机 | ✅ 能收到 AE 回调 | ✅ 能收到 AE 回调 | 全部正常 |
| 场景C | 15号机 | ❌ 收不到 AE 回调 | ❌ 收不到 AE 回调 | 全部不行 |

三台机器的**注册表、dcomcnfg、账户密码配置完全一致**，为什么表现不同？

### 🔍 三种场景的根因分析

#### 场景C（15号机：全部不行）—— 第1层系统级拦截
**原因**：DCOM 加固未关闭或认证级别过高，操作系统层面直接拦截所有 DCOM 调用。  
这是最底层的问题，注册表没配好，**所有 DCOM 连接在系统层就被拦截**，程序根本走不到代码级参数这一步。  
**解决**：按第七章命令修复注册表。

#### 场景B（7号机：全部正常）—— 系统原生宽松 + 代码默认参数恰好兼容
**原因**：7号机是老旧工控机，Windows 原生 DCOM 安全基线就很宽松（未打严格补丁）。SDK 的默认代码级安全参数在这种宽松环境下**恰好能通过校验**，不需要额外设置。  
**本质**：不是7号机“配置更好”，而是它“要求更低”，SDK 默认参数就够用了。

#### 场景A（8号机：OPC1行、OPC2不行）—— 第2层代码级参数 + 服务端差异
**这是最复杂也最容易误判的场景。** 原因如下：
1. 8号机注册表已正确配置（第1层通过），dcomcnfg 也已放开（第4层通过），账户鉴权也通过（第3层通过）；
2. 但 OPC SDK 在创建 DCOM 连接时，会使用**自己的默认安全参数**（AuthenticationLevel、ImpersonationLevel 等）。这些默认值跟随 Windows SDK 标准策略，**不等于注册表中的宽松值**；
3. **OPC1 和 OPC2 是不同机器上的服务端**，它们的安全基线不同：
        - OPC1 所在机器：可能是老系统/宽松补丁，对客户端回调的安全等级要求低，SDK 默认参数就能通过；
        - OPC2 所在机器：可能是新系统/严格补丁，对客户端回调的安全等级要求高，SDK 默认参数过严，回调被 Windows 静默丢弃。
4. **DCOM 回调的安全等级协商机制**：客户端连接服务端时，会声明“我接受什么等级的回调”。如果 SDK 默认声明了较高的认证/模拟等级，而 OPC2 服务端机器安全加固严格，两端等级不匹配，回调就会被丢弃。

> **一句话总结**：注册表控制的是“系统允不允许 DCOM”，代码参数控制的是“程序以什么身份做 DCOM”。不同服务端机器的安全基线不同，对客户端回调身份的容忍度也不同，所以同一台客户端连不同服务端表现不一致。

### 🔧 解决方案：在代码中显式设置 5 个 DCOM 安全参数
在 `OpcClient` 创建后、`Connect()` 调用前，**显式覆盖 SDK 默认值**，将客户端 DCOM 安全等级降到最低：

```csharp
var client = new OpcClient("opc.tcp://10.100.107.1:4840/");

// ========== DCOM 核心安全参数（必须在 Connect 前设置）==========

// 参数1：AuthenticationLevel = None
// 完全关闭 DCOM 身份加密校验
// SDK 默认值较高（如 Packet），加固系统会静默丢弃回调
client.Security.AuthenticationLevel = OpcClassicAuthenticationLevel.None;

// 参数2：ImpersonationLevel = Identify
// 最低权限，仅允许服务端读取程序身份
// SDK 默认 Impersonate，在加固系统触发权限拦截（事件日志 10016）
client.Security.ImpersonationLevel = OpcClassicImpersonationLevel.Identify;

// 参数3：AuthenticationService = WinNt
// 远程跨机器 DCOM 必须用 NTLM 协议
client.Security.AuthenticationService = OpcClassicAuthenticationService.WinNt;

// 参数4：AuthorizationService = None
// 关闭额外授权校验，老式 OPC AE 无自定义授权逻辑
client.Security.AuthorizationService = OpcClassicAuthorizationService.None;

// 参数5：ProxyCapabilities = None
// 关闭所有额外 DCOM 安全标记（如 MutualAuth 双向认证）
client.Security.ProxyCapabilities = OpcClassicProxyCapabilities.None;

// ========== OPC UA 参数（DCOM 场景不生效，建议显式关闭）==========
client.Security.AutoUpgradeEndpointPolicy = false;
client.Security.AutoAcceptUntrustedCertificates = true;
client.Security.UseOnlySecureEndpoints = false;

client.Connect();
```

### 📋 5 个参数详解

| 参数 | 对应底层 | 可选值 | **必须设为** | SDK 默认值 | 不设后果 |
|------|---------|--------|-------------|-----------|---------|
| **AuthenticationLevel** | `RPC_C_AUTHN_LEVEL`，控制远程 DCOM 调用身份校验/加密强度 | `None` / `Connect` / `Call` / `Packet` / `PacketIntegrity` / `PacketPrivacy` | **`None`** | 通常为 `Packet` 或 `Connect` | 加固系统会将服务端回调静默丢弃，业务层无报错 |
| **ImpersonationLevel** | 控制 OPC 服务端回调时能获取客户端身份的权限范围 | `Anonymous` / `Identify` / `Impersonate` / `Delegate` | **`Identify`** | 通常为 `Impersonate` | 加固系统直接禁止回调，事件日志出现 10016 |
| **AuthenticationService** | 指定 Windows 安全协议包 | `None` / `WinNt` / `SChannel` | **`WinNt`**（NTLM） | 可能为 `WinNt` 或 `None` | 远程 OPC Classic AE 依赖 NTLM，不指定可能导致协议不匹配 |
| **AuthorizationService** | DCOM 自定义授权校验开关 | `None` / `Dcom` | **`None`** | 通常为 `None` 或 `Dcom` | 老式 OPC AE 无自定义授权，开启会多余校验并拦截回调 |
| **ProxyCapabilities** | DCOM RPC 附加安全标记（如 MutualAuth） | `None` / `MutualAuth` 等 | **`None`** | 可能包含 `MutualAuth` | 开启增强标记，老式 OPC 服务不支持，直接拒绝 |

### 🎯 代码级参数与系统级配置的关系

```
修复前（三种场景的表现差异）：

         7号机(宽松系统)     8号机(中等系统)     15号机(严格系统)
         ┌──────────┐      ┌──────────┐       ┌──────────┐
第1层注册表│ ✅ 通过    │      │ ✅ 通过    │       │ ❌ 拦截   │ ← 全部失败
         └──────────┘      └──────────┘       └──────────┘
                                │                   │
                                ▼                   ✗
                         ┌──────────┐
第2层代码参数│ SDK默认=宽松│      │ SDK默认=偏严│
         │ 恰好兼容    │      │ OPC1宽松=通过│
         │ 全部通过    │      │ OPC2严格=丢弃│ ← 部分失败
         └──────────┘      └──────────┘

修复后（代码显式设置最低安全等级）：

         7号机             8号机              15号机
         ┌──────────┐      ┌──────────┐       ┌──────────┐
第1层注册表│ ✅ 通过    │      │ ✅ 通过    │       │ ✅ 通过    │ ← 全部修复
         └──────────┘      └──────────┘       └──────────┘
                                │                   │
                                ▼                   ▼
                         ┌──────────┐       ┌──────────┐
第2层代码参数│ None=最低  │       │ None=最低  │       │ None=最低  │
         │ 全部通过    │       │ 全部通过    │       │ 全部通过    │
         └──────────┘       └──────────┘       └──────────┘
```

> **核心结论**：代码级参数设为最低安全等级后，客户端对所有服务端统一以“最宽松身份”发起回调协商，不再依赖 SDK 默认值和服务端安全基线的“巧合兼容”，从根本上消除了不同机器、不同服务端之间的表现差异。

---

## 七、最终总结：所有 DCOM 校验层级（从上到下拦截）

| 层级 | 校验项 | 控制方式 | 不配后果 | 典型故障机器 |
|------|--------|---------|---------|------------|
| 第1层 | DCOM 加固开关 | 注册表 | 直接拒绝所有连接 | 15号机（修复前） |
| 第1层 | 全局认证/模拟级别 | 注册表 | 握手失败/回调失效 | 15号机（修复前） |
| 第2层 | **代码级 DCOM 安全参数** | **代码显式设置** | **部分服务端回调静默丢弃** | **8号机（OPC2不回调）** |
| 第3层 | NTLM 账户密码 | 系统账户 | 拒绝访问 0x80070005 | — |
| 第3层 | 服务端 DCOM 组件权限 | dcomcnfg | 远程激活失败 | — |
| 第4层 | 客户端本机回调权限 | dcomcnfg | AE 无推送 | 8号机（修复前） |

---

## 八、一键标准修复命令（可直接批量部署）

**客户端全套修复（注册表 + 代码）**

```cmd
rem ===== 注册表修复（第1层） =====
reg add "HKLM\SOFTWARE\Microsoft\Ole\AppCompat" /v RequireIntegrityActivationAuthenticationLevel /t REG_DWORD /d 0 /f
reg add "HKLM\SOFTWARE\Microsoft\Ole" /v LegacyAuthenticationLevel /t REG_DWORD /d 1 /f
reg add "HKLM\SOFTWARE\Microsoft\Ole" /v LegacyImpersonationLevel /t REG_DWORD /d 2 /f
```

```csharp
// ===== 代码修复（第2层） =====
client.Security.AuthenticationLevel = OpcClassicAuthenticationLevel.None;
client.Security.ImpersonationLevel = OpcClassicImpersonationLevel.Identify;
client.Security.AuthenticationService = OpcClassicAuthenticationService.WinNt;
client.Security.AuthorizationService = OpcClassicAuthorizationService.None;
client.Security.ProxyCapabilities = OpcClassicProxyCapabilities.None;
```

**服务端额外必加修复（账户过滤 + 本地安全策略）**

```cmd
rem ===== 关闭 UAC 远程账户过滤 =====
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f

rem ===== 本地安全策略（经典模式 + 匿名兼容） =====
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v forceguest /t REG_DWORD /d 0 /f
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v everyoneincludesanonymous /t REG_DWORD /d 1 /f
```

**别忘了**：客户端和服务端都需在 `dcomcnfg` 中手动配置第4层（客户端回调权限）和第3层（服务端组件权限），这些无法通过命令行一键完成，请参照第四、五章操作。

---

> **最后提醒**：本文所述配置适用于 Windows 7/10/11 及 Windows Server 2008–2022 全系列。若仍遇问题，请检查 Windows 事件查看器中的“应用程序”和“系统”日志，重点关注来源为 `DCOM` 或 `Microsoft-Windows-DistributedCOM` 的错误（如 10016），可帮助定位具体是哪一层校验失败。
>
> （注：部分内容可能由 AI 生成，经人工校验后发布）