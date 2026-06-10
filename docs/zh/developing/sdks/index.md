# DotCraft SDK

DotCraft SDK 是同一套 AppServer 协议之上的语言绑定。应用、原生 App、工具和外部渠道可以通过 SDK 连接工作区、复用持久化线程、流式接收对话事件，并参与审批流，而不需要自己重写 Session Core。

## 开始

::: code-group

```bash [TypeScript]
npm install @dotcraft/sdk
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

```bash [Python]
pip install dotcraft
```

:::

随后跟随[快速开始](./quickstart)完成连接、开启线程并运行第一轮。

## 指南

- [快速开始](./quickstart)——安装、连接、运行一轮、流式接收事件。
- [线程与运行](./runs)——线程生命周期、运行选项、归一化事件模型。
- [工具与审批](./tools)——运行时动态工具、审批与用户输入回调。
- [渠道适配器](./channels)——构建外部渠道（TypeScript 与 Python）。

## 共同模型

所有 SDK 都基于同一组层次：

| 层 | 作用 |
|----|------|
| Hub bootstrap | 在 local 模式下发现或启动本机 Hub，并确保目标工作区的 AppServer。 |
| AppServer JSON-RPC | 承载 `initialize`、线程/轮次方法、通知、服务端请求和 raw escape hatch。 |
| Session Core | 提供持久化的 `Thread -> Turn -> Item` 模型、审批、事件顺序和历史记录。 |
| SDK binding | 提供符合语言习惯的客户端、辅助方法、回调、流 reducer 和 typed wrapper。 |

需要客户端库时使用 SDK。需要实现新传输、调试 wire protocol，或完全控制 JSON-RPC 消息时，直接使用 [AppServer 协议](../protocols/appserver-protocol)。

## 应用集成路径

SDK 客户端可以在活跃线程连接上直接提供运行时动态工具，也可以参与 App Binding，让原生应用把自有工具授权给某一个线程。运行时工具绑定在当前 Wire Client 连接上；App Binding 工具绑定在应用接受并可重新挂载的持久化线程授权上。

![DotCraft 应用集成路径：Wire Client 与 App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/app-integration.png)

## 能力快照

完整的跨语言 parity 矩阵见 [SDK 规范](https://github.com/DotHarness/dotcraft/blob/master/specs/sdk/sdk.md)。

| 能力 | TypeScript | .NET | Python |
|------|------------|------|--------|
| 本地 Hub-managed 连接 | `DotCraft.local()` | `DotCraftClient.ConnectLocalAsync()` | `DotCraft.connect_local()` |
| 远程 WebSocket 连接 | `DotCraft.remote()` | `DotCraftClient.ConnectRemoteAsync()` | `DotCraft.connect_remote()` |
| Raw AppServer request | `request()` | `RequestAsync()` | `request()` |
| 高层单轮运行 | `thread.run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `thread.run()` / `run_streamed()` |
| 归一化流式事件 | `DotCraftRunEvent` + raw | `DotCraftRunEvent` + raw | `RunEvent` + raw |
| 审批与用户输入回调 | typed handler | typed handler | typed handler |
| Runtime Dynamic Tools | 声明 + typed callbacks | 声明 + typed callbacks | 声明 + typed callbacks |
| App Binding helper | typed/generic + 交接解析 | typed/generic + 交接解析 | typed/generic + 交接解析 |
| Channel adapter runtime | 一方 TypeScript runtime | 不适用 | channel adapter 基类 |

线程状态、队列行为、审批、模型目录解析和持久化都以 AppServer 为准；SDK 是它之上的客户端，而非第二份权威。

## 事件拓扑

SDK 客户端会消费 AppServer 通知，有时也需要回答服务端主动发起的请求。通知没有 JSON-RPC `id`；服务端请求带有 `id`，客户端必须返回响应。

![DotCraft SDK event topology](/sdk-event-topology.svg)

三个 SDK 都会把常见 wire 通知归一成 run event（TypeScript 与 .NET 为 `DotCraftRunEvent`，Python 为 `RunEvent`），未知通知保留为 `raw`，同时仍向高级客户端暴露 raw notification stream。

## 参考

各语言的包细节——标识、导出/命名空间、运行时基线、版本和语言特定 profile：

- [TypeScript](./typescript)——`@dotcraft/sdk`
- [.NET](./dotnet)——`DotCraft.Sdk`
- [Python](./python)——`dotcraft`

## 继续阅读

- [AppServer 协议](../protocols/appserver-protocol)
- [Hub 本地协调](../lifecycle/hub)
