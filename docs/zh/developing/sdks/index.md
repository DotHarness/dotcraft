# DotCraft SDK

DotCraft SDK 是同一套 AppServer 协议之上的语言绑定。应用、原生 App、工具和外部渠道可以通过 SDK 连接工作区、复用持久化线程、流式接收对话事件，并参与审批流，而不需要自己重写 Session Core。

所有 SDK 共享同一套 AppServer 与 Hub 模型。各语言页面会说明对应包结构、运行时行为和可用 helper。

## 共同模型

所有 SDK 都基于同一组层次：

| 层 | 作用 |
|----|------|
| Hub bootstrap | 在 local 模式下发现或启动本机 Hub，并确保目标工作区的 AppServer。 |
| AppServer JSON-RPC | 承载 `initialize`、线程/轮次方法、通知、服务端请求和 raw escape hatch。 |
| Session Core | 提供持久化的 `Thread -> Turn -> Item` 模型、审批、事件顺序和历史记录。 |
| SDK binding | 提供符合语言习惯的客户端、辅助方法、回调、流 reducer 和可用的 typed wrapper。 |

需要客户端库时使用 SDK。需要实现新传输、调试 wire protocol，或完全控制 JSON-RPC 消息时，直接使用 [AppServer 协议](../protocols/appserver-protocol)。

## 事件拓扑

SDK 客户端会消费 AppServer 通知，有时也需要回答服务端主动发起的请求。通知没有 JSON-RPC `id`；服务端请求带有 `id`，客户端必须返回响应。

![DotCraft SDK event topology](/sdk-event-topology.svg)

TypeScript 的 `runStreamed()` 会把常见 wire 通知归一成 `DotCraftRunEvent`，未知通知保留为 `raw`。.NET 当前暴露 raw notification stream，需要最终文本合并或 terminal run 处理时由应用自行实现 reducer。

## SDK 选择

| SDK | 包 | 适用场景 | 说明 |
|-----|----|----------|------|
| [TypeScript](./typescript) | `@dotcraft/sdk` | Node.js 应用、一方 TypeScript 渠道模块、高层 run helper。 | 当前高层应用侧能力最完整，包含 `run()` / `runStreamed()`、回调处理、Hub helper 和 channel runtime 组件。 |
| [.NET](./dotnet) | `DotCraft.Sdk` | 原生 App、App Binding 集成、C# 工具和 typed AppServer client。 | 提供 local/remote 连接、线程/轮次/模型 wrapper、Runtime Dynamic Tools、App Binding helper 和 raw notification 访问。 |
| [Python](./python) | `dotcraft_wire` | Python 外部渠道适配器和 wire protocol 客户端。 | 保留为 Python adapter/wire SDK，支持 stdio/WebSocket、审批、投递、channel tools，并包含 Telegram 参考适配器。 |

## 能力快照

| 能力 | TypeScript | .NET |
|------|------------|------|
| 本地 Hub-managed 连接 | Typed `DotCraft.local()` | Typed `DotCraftClient.ConnectLocalAsync()` |
| 远程 WebSocket 连接 | Typed `DotCraft.remote()` | Typed `DotCraftClient.ConnectRemoteAsync()` |
| Raw AppServer request | `request()` / wire client | `RequestAsync()` / wire client |
| 流式通知 | 归一化 run events + raw messages | Raw `AppServerNotification` stream |
| 高层单轮运行 API | `run()` / `runStreamed()` | 暂未提供 |
| Runtime Dynamic Tools | 声明 + typed callbacks | 声明 + `RegisterDynamicToolHandler` |
| 审批和用户输入回调 | 高层 handler | 当前通过低层 server request handler 注册 |
| App Binding helper | Typed/generic helper | 原生 App 场景的一等 helper surface |
| Channel adapter runtime | 一方 TypeScript channel runtime | .NET 暂不提供 |

SDK 不应该复制服务端权威逻辑。线程状态、队列行为、审批、模型目录解析和持久化仍由 AppServer 负责。

## 继续阅读

- [TypeScript SDK](./typescript)
- [.NET SDK](./dotnet)
- [Python SDK](./python)
- [AppServer 协议](../protocols/appserver-protocol)
- [Hub 本地协调](../lifecycle/hub)
