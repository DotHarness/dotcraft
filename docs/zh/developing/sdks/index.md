# DotCraft SDK

使用 DotCraft SDK 把应用连接到 AppServer。常规应用从高层 API 开始，用它处理 thread、run、工具、审批和用户输入。

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
| **Host adapter** | Desktop 或 Channel 的路由、heartbeat、平台投递和重连恢复策略。 |

只有在使用未支持的语言、自定义传输或调试协议时，才直接实现 [AppServer 协议](../protocols/appserver-protocol)。

## 包

| 语言 | 包 | 可用状态 |
| --- | --- | --- |
| TypeScript | `@dotcraft/sdk` | 源码预览，需要从本仓库构建。 |
| .NET | `DotCraft.Sdk` | 已发布到 NuGet。 |
| Python | `dotcraft` | 源码预览，需要从本仓库安装。 |

[快速开始](./quickstart)是安装命令的唯一来源。

## 常用能力

| 任务 | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| 连接工作区 | `DotCraft.local()` | `ConnectLocalAsync()` | `connect_local()` |
| 连接默认 Chat | `DotCraft.localChat()` | `ConnectLocalChatAsync()` | `connect_local_chat()` |
| 连接远程服务 | `DotCraft.remote()` | `ConnectRemoteAsync()` | `connect_remote()` |
| 运行 turn | `run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `run()` / `run_streamed()` |
| 读取历史分页 | `listTurns()` / `listItems()` | `ListTurnsAsync()` / `ListItemsAsync()` | `list_turns()` / `list_items()` |
| 列出模型 | `models.list()` | `Models.GetCatalogAsync()` | `models.list()` |
| 使用 MCP runtime | `mcpRuntime` | `McpRuntime` | `mcp_runtime` |
| 使用 App Binding | `appBindings` | `AppBindings` | `app_bindings` |

TypeScript 和 Python 还提供 Channel Adapter profile，.NET 不提供。

## 连接所有权

本地高层 client 先请求 [Hub](../lifecycle/hub) 确保工作区 AppServer 可用，再直接连接 AppServer。关闭 SDK 连接不会停止由 Hub 管理的 AppServer。

重连只恢复 Wire 传输和初始化，不会重放进行中的请求，也不会重建 thread subscription、活动 run 或运行时工具绑定。恢复步骤见[线程与运行](./runs)。

## 语言参考

- [TypeScript](./typescript)
- [.NET](./dotnet)
- [Python](./python)

## 相关文档

- [Hub 生命周期](../lifecycle/hub)
- [AppServer 模式](../lifecycle/appserver)
- [AppServer 协议](../protocols/appserver-protocol)
- [MCP 运行时](./mcp-runtime)
- [DotCraft App](../integrations/app-binding)
