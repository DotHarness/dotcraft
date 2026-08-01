# DotCraft SDK

DotCraft SDK 让应用、原生宿主、工具和外部渠道连接到同一套 AppServer 协议。一般应用应从高层 API 开始，只有在需要更多控制时才下沉到更底层。

## 选择层级

| 层级 | 适用场景 |
|------|----------|
| Contracts | 使用生成的 DTO、方法映射、注册表和协议元数据，不引入传输或运行时依赖。 |
| Wire | 使用强类型 JSON-RPC 请求、通知、服务端请求、连接生命周期和显式 raw 逃生口。 |
| High-level | 使用 `DotCraft`、Thread、Run、审批、用户输入和运行时动态工具。这是默认的应用 API。 |
| Host Adapter | 使用 Desktop 和 Channel 的工作区路由、重连策略、心跳和平台投递等宿主策略。这些策略不属于通用 Wire Client。 |

按照[快速开始](./quickstart)安装 SDK、连接工作区并运行一个 turn。只有在实现自定义传输、不受支持的语言或调试协议时，才直接使用 [AppServer 协议](../protocols/appserver-protocol)。

## 包可用性

| 语言 | 包 | 可用状态 |
|------|----|----------|
| TypeScript | `@dotcraft/sdk` | 源码预览；从当前仓库构建并安装。 |
| .NET | `DotCraft.Sdk` | 已发布到 NuGet。 |
| Python | `dotcraft` | 源码预览；从当前仓库安装。 |

安装命令统一维护在[快速开始](./quickstart)中，避免产生多个不一致的安装入口。

## 指南

- [快速开始](./quickstart) — 安装、连接、运行 turn 并流式读取事件。
- [Thread 与 Run](./runs) — thread 生命周期、run 选项、事件和重连边界。
- [工具与审批](./tools) — 运行时动态工具、审批和用户输入回调。
- [Channel Adapter](./channels) — 使用 TypeScript 或 Python 宿主配置构建外部渠道。

## 能力概览

| 能力 | TypeScript | .NET | Python |
|------|------------|------|--------|
| Hub 管理的本地连接 | `DotCraft.local()` | `DotCraftClient.ConnectLocalAsync()` | `DotCraft.connect_local()` |
| 远程 WebSocket 连接 | `DotCraft.remote()` | `DotCraftClient.ConnectRemoteAsync()` | `DotCraft.connect_remote()` |
| 强类型 Wire 请求 | `request()` | 使用 descriptor 的 `RequestAsync()` | 生成的强类型 RPC 方法 |
| Raw Wire 请求 | `requestRaw()` / `notifyRaw()` | `RequestRawAsync()` / `NotifyRawAsync()` | `request_raw()` / `notify_raw()` |
| 高层单次 Run | `thread.run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `thread.run()` / `run_streamed()` |
| 审批和用户输入回调 | 强类型 handler | 强类型 handler | 强类型 handler |
| 运行时动态工具 | 声明和强类型回调 | 声明和强类型回调 | 声明和强类型回调 |
| Channel Adapter 配置 | TypeScript runtime | 不适用 | Python adapter 基类 |

AppServer 仍是 thread 状态、队列行为、审批、模型解析和持久化的权威来源。SDK 只呈现这些能力，不创建第二套真源。

## 应用集成路径

SDK client 可以在活动连接上暴露运行时动态工具；原生应用也可以通过 App Binding 将应用工具授予一个 thread。运行时工具绑定到活动连接，App Binding 工具绑定到持久化的 thread 授权。

![DotCraft 应用集成路径：Wire Client 与 App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/app-integration.png)

## 事件拓扑

SDK client 消费通知，并可能回答服务端发起的请求。通知没有 JSON-RPC `id`；服务端请求有 `id`，client 必须返回响应。

![DotCraft SDK 事件拓扑](/sdk-event-topology.svg)

三个 SDK 都会把常见通知标准化为 Run 事件，并将未知通知保留为 raw 事件。Wire 层还为尚未进入生成契约的扩展提供显式 raw 通知监听器。

## 语言参考

- [TypeScript](./typescript) — `@dotcraft/sdk`
- [.NET](./dotnet) — `DotCraft.Sdk`
- [Python](./python) — `dotcraft`

## 相关文档

- [AppServer 协议](../protocols/appserver-protocol)
- [Hub 生命周期](../lifecycle/hub)
