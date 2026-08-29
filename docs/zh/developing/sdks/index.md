# DotCraft SDK

使用 DotCraft SDK 把应用连接到 AppServer。从高层 API 开始，用它处理 thread、run、工具、审批和用户输入。

![DotCraft SDK 分层与连接归属：应用从高层 API 进入，其下是 Wire 层和生成的 Contracts 层。本地场景由 Hub 确保工作区 AppServer 就绪，SDK 直连该 AppServer 并抵达 session core](/sdk-layers-topology.svg)

## 从这里开始

- [快速开始](./quickstart)——连接并运行第一个 turn。
- [线程与运行](./runs)——管理 thread、输入、流式事件和恢复。
- [工具与审批](./tools)——添加运行时工具和交互回调。
- [MCP 运行时](./mcp-runtime)——检查已配置 server、resource、tool 和认证。
- [渠道适配器](./channels)——连接外部消息平台。

## 选择 API 层级

| 层级 | 适用场景 |
| --- | --- |
| **High-level** | 使用 `DotCraft`、thread、run、回调、模型、MCP runtime 或 App Binding 的应用。从这里开始。 |
| **Wire** | 强类型 JSON-RPC、连接状态、超时和显式 raw 扩展调用。 |
| **Contracts** | 不含传输 I/O 的生成 DTO、方法映射、注册表和协议元数据。 |

Host adapter 和 Channel runtime 建立在这些层级之上，补上特定运行环境的集成：工作区路由、heartbeat、平台投递和 UI 交互。

## 包

| 语言 | 包 | 可用状态 |
| --- | --- | --- |
| TypeScript | `@dotcraft/sdk` | 已发布到 npm。 |
| .NET | `DotCraft.Sdk` | 已发布到 NuGet。 |

[快速开始](./quickstart)是安装命令的唯一来源。

## 常用能力

| 任务 | TypeScript | .NET |
| --- | --- | --- |
| 连接工作区 | `DotCraft.local()` | `ConnectLocalAsync()` |
| 连接默认 Chat | `DotCraft.localChat()` | `ConnectLocalChatAsync()` |
| 连接远程服务 | `DotCraft.remote()` | `ConnectRemoteAsync()` |
| 运行 turn | `run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` |
| 读取历史分页 | `listTurns()` / `listItems()` | `ListTurnsAsync()` / `ListItemsAsync()` |
| 列出模型 | `models.list()` | `Models.GetCatalogAsync()` |
| 使用 MCP runtime | `mcpRuntime` | `McpRuntime` |
| 使用 App Binding | `appBindings` | `AppBindings` |

TypeScript 还提供 Channel Adapter profile，.NET 不提供。

## 连接所有权

本地高层 client 先请求 [Hub](../lifecycle/hub) 确保工作区 AppServer 可用，再直接连接 AppServer。关闭 SDK 连接不会停止由 Hub 管理的 AppServer。

重连只恢复 Wire 传输和初始化，不会重放进行中的请求，也不会重建 thread subscription、活动 run 或运行时工具绑定。恢复步骤见[线程与运行](./runs)。

## 语言参考

- [TypeScript](./typescript)
- [.NET](./dotnet)

## 相关文档

- [AppServer 模式](../lifecycle/appserver)——SDK 所连接的 AppServer 如何启动、加固并对外暴露 WebSocket 端点。
- [AppServer 协议](../protocols/appserver-protocol)——这些 client 实际收发的方法与通知。
- [DotCraft App](../integrations/app-binding)——App Binding，供应用向 thread 暴露自身能力。
