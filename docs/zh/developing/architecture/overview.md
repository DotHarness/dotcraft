# 架构总览

DotCraft 是使用 .NET 10 / C# 构建的 Agent Harness。它按程序集划分职责：与模型提供商无关的 Agent 基础、产品内核、可复用的宿主能力、对外协议，以及官方应用的组合根。本页为集成方和贡献者定义这些边界。

![DotCraft 运行时架构拓扑图](/runtime-architecture-topology.svg)

## 程序集边界

上层组件只依赖其下方的基础层：

```text
DotCraft.App（官方组合根）
  |-- DotCraft.Runtime
  |     `-- DotCraft.Core
  |           `-- DotCraft.Agents
  |-- DotCraft.AppServer
  |     |-- DotCraft.Core
  |     `-- DotCraft.Protocol
  |-- 模型提供商
  `-- 可选功能
```

| 组件 | 职责 |
|---|---|
| **`DotCraft.Agents`** | 与模型提供商无关的 Agent API、提供商契约、公共中间件、工具循环与提示缓存选择 |
| **`DotCraft.Core`** | 产品内核，负责会话、Agent 编排、工具、上下文、记忆、技能、插件、安全、配置、模块与工作区语义 |
| **`DotCraft.Runtime`** | 面向工作区的可复用依赖注入注册与 Generic Host 生命周期 |
| **`DotCraft.Protocol`** | AppServer 与协议客户端共用的传输契约 |
| **`DotCraft.AppServer`** | JSON-RPC 请求处理、契约映射、连接状态，以及 stdio 和 WebSocket 传输 |
| **`DotCraft.App`** | 官方组合根，负责进程入口、模型提供商、可选功能、日志与进程策略 |
| **功能程序集** | 基于 Core 契约实现 Automations、Dynamic Workflows、渠道等功能行为 |

Core 构建在与模型提供商无关的 Agents 基础之上。Runtime 与功能程序集构建在 Core 之上。AppServer 把 Core 的领域能力映射为 Protocol 契约。官方 App 挑选这些组件，并在组合边界接上各功能专用的协议适配器。Runtime 与 AppServer 共用宿主持有的同一个 `ISessionService`。

## 模块发现与能力接口

`DotCraft.Generators` 在编译期发现实现 `IDotCraftModule` 的模块。基础契约定义模块标识、配置检查与依赖注入注册。模块按自己提供的能力实现对应的接口：

| 能力接口 | 提供的能力 |
|---|---|
| **`IToolSourceModule`** | 向 Agent Runtime 提供工具源 |
| **`IChannelServiceModule`** | 提供托管的渠道服务 |
| **`ISessionChannelModule`** | 提供渠道所暴露的会话来源 |

`DotCraft.App` 负责宿主选择与进程组合。宿主工厂通过 `IModuleHostComposition` 决定每个官方宿主的服务图包含哪些模块。

## Session Core

Session Core 定义 `Thread → Turn → Item` 模型。`ISessionService` 是进程内的中心 API，负责 Thread 生命周期、输入提交、事件、审批和用户输入请求。

CLI、ACP、Automations 与渠道适配器使用同一个 Session Core 和持久化 Thread 模型。传输边界只投影该模型，不改变其领域语义。模型与生命周期详见[统一会话核心](./session-core)。

## AppServer

AppServer 是 Session Core 之上可选的协议与传输边界，由宿主持有。它通过 stdio 和 WebSocket 上的 JSON-RPC 2.0 投影 `ISessionService`，把 Core 领域模型映射为 `DotCraft.Protocol` 契约，并管理连接级资源。

Desktop、CLI、ACP、外部渠道适配器和 [SDK](../sdks/) 客户端都可以使用这个进程外边界。详见 [AppServer 协议](../protocols/appserver-protocol)与 [AppServer 模式](../lifecycle/appserver)。

## Hub

每个用户在本机有一个 [Hub](../lifecycle/hub)。Hub 为每个工作区启动或复用一个 AppServer，并在 `~/.craft/hub/` 下维护发现信息与锁。Desktop 与 CLI 默认使用 Hub。远程、CI、机器人和协议调试场景可以直接管理 AppServer。

## 官方宿主中的配置

官方 `DotCraft.App` 宿主默认加载以下配置层：

| 层级 | 路径 | 作用 |
|---|---|---|
| **全局** | `~/.craft/config.json` | 模型提供商凭据、端点与个人偏好 |
| **工作区** | `<workspace>/.craft/config.json` | 模型选择、入口开关、自动化与安全策略 |

配置策略由宿主负责。`DotCraft.App` 合并全局与工作区两层，在组合 Runtime 时提供最终的 `AppConfig`，Core 与 Runtime 都使用这份配置。模块用 `[ConfigSection("Key")]` 声明自己的配置节，源生成器把这些配置节纳入合并后的配置模式。

字段说明见[配置参考](../configuration)，配置何时生效见[设置生效层级](../lifecycle/settings-lifecycle)。
