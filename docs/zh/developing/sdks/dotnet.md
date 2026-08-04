# .NET SDK 参考

`DotCraft.Sdk` 是面向 AppServer 应用的 .NET client。安装和首次运行见[快速开始](./quickstart)。

## 包

| 字段 | 值 |
| --- | --- |
| 包 | `DotCraft.Sdk`（NuGet） |
| 目标框架 | `net10.0` |
| 序列化 | `System.Text.Json` 与 `DotCraftJson.Options` |

只安装 `DotCraft.Sdk`。该包包含 `DotCraft.Sdk.dll` 和 `DotCraft.Protocol.dll`。

## 命名空间

| 命名空间 | 公共接口 |
| --- | --- |
| `DotCraft.Sdk` | `DotCraftClient`、thread 和 run API、回调、MCP runtime 和高层异常。 |
| `DotCraft.Protocol` | 协议基础类型、RPC descriptor 和 JSON 选项。 |
| `DotCraft.Protocol.AppServer` | AppServer DTO、payload、result 和 notification。 |
| `DotCraft.Sdk.Wire` | `DotCraftWireClient`、transport、连接状态、超时和 JSON-RPC 错误。 |
| `DotCraft.Sdk.Hub` | Hub 发现、AppServer 管理、进程策略、事件和结构化错误。 |
| `DotCraft.Sdk.DynamicTools` | 基于 attribute 的 Runtime Dynamic Tool authoring。 |
| `DotCraft.Sdk.AppBinding` | App Binding handoff 和结果 helper。 |

Contracts 是独立程序集和逻辑层，不是独立 NuGet 包。

## 高层 API

| 任务 | API |
| --- | --- |
| 连接 | `ConnectLocalAsync()`、`ConnectLocalChatAsync()`、`ConnectRemoteAsync()`、`ConnectAsync()` |
| 关闭 | `DisposeAsync()` / `await using` |
| Thread | `Threads.StartAsync()`、`ResumeAsync()`、`ListAsync()`、`ReadAsync()` |
| Run | `RunAsync()`、`RunStreamedAsync()`、`EnqueueAsync()`、`InterruptAsync()` |
| Thread 状态 | `Snapshot`、`RefreshAsync()`、`SubscribeAsync()`、`UnsubscribeAsync()`、`SetModeAsync()`、`ArchiveAsync()`、`DeleteAsync()` |
| Provider 和模型 | `Providers.ListAsync()`、`Models.GetCatalogAsync()` |
| 模型配置 | `Threads.ReadModelConfigurationAsync()`、`UpdateModelConfigurationAsync()` |
| MCP runtime | `McpRuntime.ListStatusAsync()`、`ReadResourceAsync()`、`CallToolAsync()`、`LoginOAuthAsync()`、`ReloadAsync()` |
| App Binding | `AppBindings` |
| 运行时工具 | `OnToolCall()` 或 `RegisterDynamicToolHandler()` |

在连接选项中配置 `ApprovalHandler` 和 `UserInputHandler`。任务流程见[线程与运行](./runs)和[工具与审批](./tools)。

## 连接

| 方法 | 必需参数 | 连接所有权 |
| --- | --- | --- |
| `ConnectLocalAsync(workspacePath, options?, cancellationToken)` | 工作区路径 | 通过 Hub 确保工作区 AppServer 可用，再连接到它。 |
| `ConnectLocalChatAsync(options?, cancellationToken)` | 无 | 通过 Hub 确保默认 Chat 工作区 AppServer 可用。 |
| `ConnectRemoteAsync(appServerUrl, options?, cancellationToken)` | WebSocket URL；`DotCraftRemoteOptions` 中可选 token | 直接连接现有 AppServer。 |
| `ConnectAsync(transport, options?, cancellationToken)` | `IJsonRpcTransport` | 使用应用拥有的自定义 transport。 |

`DotCraftClientOptions` 控制 client identity、capability、callback、streaming、配置变更通知和重连行为。`DotCraftLocalOptions` 另外包含可执行文件、Hub lock、user profile 和启动 timeout 设置。

共享选项字段为 `AutoReconnect`、`ClientName`、`ClientTitle`、`ClientVersion`、`ApprovalSupport`、`StreamingSupport`、`RequestUserInputSupport`、`ConfigChange`、`ExtraCapabilities`、`ApprovalHandler` 和 `UserInputHandler`。`DotCraftRemoteOptions` 另外包含 `Token`。

## Thread 与 Run

`DotCraftThreadClient` 使用类型化 contract 参数：

```csharp
Task<DotCraftThread> StartAsync(ThreadStartParams parameters, CancellationToken cancellationToken = default);
Task<DotCraftThread> ResumeAsync(ThreadResumeParams parameters, CancellationToken cancellationToken = default);
Task<ThreadListResult> ListAsync(ThreadListParams parameters, CancellationToken cancellationToken = default);
Task<ThreadReadResult> ReadAsync(ThreadReadParams parameters, CancellationToken cancellationToken = default);
```

`DotCraftThread` 的 `RunAsync()` 和 `RunStreamedAsync()` 接受文本或 `IReadOnlyList<InputPart>`。`RunOptions` 控制 sender context、raw event 收集、busy 时排队，以及失败终态是否抛出异常。Cancellation 会在获知活动 turn ID 后中断它。

结果类型为 `DotCraftRunResult`；流式事件为 `DotCraftRunEvent` 或 `DotCraftRunEvent<TParams>`。Thread 控制方法使用 handle 的 `Id`，低层 `Turns` 接口则接受显式协议参数。

## Provider、模型、MCP 与 App Binding

| Client | 操作 |
| --- | --- |
| `Providers` | `ListAsync()` 列出已配置 provider。 |
| `Models` | `GetCatalogAsync(providerId?)` 返回模型及类型化 capability。 |
| `Threads` | `ReadModelConfigurationAsync()` 和 `UpdateModelConfigurationAsync()` 会保留无关的 thread 配置字段。 |
| `McpRuntime` | `ListStatusAsync()`、`ReadResourceAsync()`、`CallToolAsync()`、`LoginOAuthAsync()`、`ReloadAsync()`。 |
| `AppBindings` | 类型化连接、surface、thread binding 和 principal 操作。 |

任务流程见 [MCP 运行时](./mcp-runtime)和[构建应用](../integrations/build-an-app)。

MCP client 接受生成的 Contracts DTO，并返回生成的 result DTO：

```csharp
Task<McpServerStatusListResult> ListStatusAsync(McpServerStatusListParams? parameters = null, CancellationToken cancellationToken = default);
Task<McpServerResourceReadResult> ReadResourceAsync(McpServerResourceReadParams parameters, CancellationToken cancellationToken = default);
Task<McpServerToolCallResult> CallToolAsync(McpServerToolCallParams parameters, CancellationToken cancellationToken = default);
Task<McpServerOAuthLoginResult> LoginOAuthAsync(McpServerOAuthLoginParams parameters, CancellationToken cancellationToken = default);
Task<McpServerReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
```

## 回调与运行时动态工具

```csharp
delegate Task<ApprovalResponseResult> ApprovalHandler(
    ApprovalRequestParams request, CancellationToken cancellationToken);
delegate Task<UserInputResponseResult> UserInputHandler(
    UserInputRequestParams request, CancellationToken cancellationToken);

IDisposable OnToolCall(
    string? @namespace,
    string toolName,
    Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler);
```

`DotCraftThread.OnToolCall()` 把 handler 限定到一个 thread。`DotCraftClient.RegisterDynamicToolHandler()` 还支持 catch-all handler 或显式 thread/namespace/tool key。请在所属 scope 结束时 dispose 注册。

## Contracts 与 payload

高层方法直接返回 `DotCraft.Protocol.AppServer` contract，不创建重复的 SDK DTO。`SessionItem.Payload` 保持为开放的 `JsonElement`，让旧 client 也能保留未知 item kind。

已知 payload 使用 `SessionItemPayloadParser.Parse(item)` 或 `TryGet<TPayload>`。应用不识别未来 payload kind 时应保留 `Raw`。

## Typed 与 raw Wire API

已登记的操作使用生成 descriptor 或生成的 `XxxAsync` 扩展方法：

```csharp
var result = await client.Wire.ThreadListAsync(parameters, cancellationToken);
```

只有第三方或尚未进入目录的扩展才使用 `RequestRawAsync`、`NotifyRawAsync` 和 raw handler。Typed 方法不接受任意方法字符串。

`DotCraftWireClient` 只负责 JSON-RPC 和连接状态，不会自动审批、回答用户输入，也不会重建 thread 和工具资源。

## 连接生命周期

Wire 状态包括 `Connecting`、`Initializing`、`Ready`、`Disconnected`、`Reconnecting`、`ReconnectError` 和 `Closed`。

- Raw Wire 连接默认不重连。
- 本地和远程高层连接默认启用重连。用 `DotCraftClientOptions.AutoReconnect` 覆盖默认值。
- 普通请求默认超时为 30 秒。
- 重连使用指数退避，最多排队 1024 个新调用。
- 进行中的调用会失败且绝不重放。
- 初始化完成后才会释放排队调用。
- Handler 注册会跨重连保留；thread subscription 和运行时工具资源不会保留。
- 活动 run 会以 `RunDisconnectedException` 失败；`turn/start` 绝不重放。

Dispose 本地高层 client 只关闭 AppServer 连接，不会停止 Hub 或由 Hub 管理的 AppServer。

## 错误

SDK 异常派生自 `DotCraftException`，并带有稳定的 `Code`。

| 异常 | 条件 |
| --- | --- |
| `JsonRpcException` | AppServer 返回 JSON-RPC 错误；保留 `RpcCode` 和 `ErrorData`。 |
| `InitializationFailedException` | 连接初始化失败。 |
| `ProtocolViolationException` | 已知消息不符合其 contract。 |
| `TurnInProgressException` | Thread 已有活动 turn。 |
| `ThreadNotFoundException` / `ThreadNotActiveException` | 目标 thread 不存在或无法运行。 |
| `TurnFailedException` / `TurnCancelledException` | Buffered run 到达失败或取消终态。 |
| `RunDisconnectedException` | Wire session 在活动 run 期间结束。 |
| `ApprovalTimeoutException` | AppServer 报告审批超时。 |
| `RequestTimeoutException` | Wire 请求超过超时时间。 |
| `ReconnectQueueFullException` | 重连队列达到容量上限。 |

`HubClientException` 保留 Hub `Code`、message 和 `Details`。

## Hub API

`HubClient` 发现或启动 Hub、验证本地 lock、解析工作区 AppServer，并支持 ensure、restart、stop、list、status、events 和 shutdown。

不要记录 Hub token、App Binding credential 或包含 token 的完整 WebSocket URL。

## 相关文档

- [SDK 快速开始](./quickstart)
- [线程与运行](./runs)
- [工具与审批](./tools)
- [MCP 运行时](./mcp-runtime)
- [构建应用](../integrations/build-an-app)
- [AppServer 协议](../protocols/appserver-protocol)
