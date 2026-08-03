# .NET SDK 参考

`DotCraft.Sdk` 提供生成契约、纯 JSON-RPC 访问、高层 Thread 与 Run API，以及 Hub 管理。安装和首次运行请从[快速开始](./quickstart)开始。

## 包信息

| | |
|---|---|
| 包 | `DotCraft.Sdk`（NuGet） |
| 目标框架 | `net10.0` |
| 序列化 | `System.Text.Json` 与 camelCase Web 默认值（`DotCraftJson.Options`） |

```bash
dotnet add package DotCraft.Sdk
```

本参考描述当前仓库中的接口。NuGet 最新版本可能会按正常发布节奏晚于仓库源码。

## 层级与命名空间

| 层级 | 公共接口 |
|------|----------|
| Contracts | `DotCraft.Protocol.Contracts` 程序集中 `DotCraft.Protocol` 的协议基础类型，以及 `DotCraft.Protocol.AppServer` 的 DTO、payload 和 RPC descriptor。 |
| Wire | `DotCraft.Sdk.Wire.DotCraftWireClient`、stdio/WebSocket 传输、连接状态和 JSON-RPC 错误。 |
| High-level | `DotCraft.Sdk.DotCraftClient`、`DotCraftThread`、Run API、强类型回调和异常。 |
| Dynamic Tools | `DotCraft.Sdk.DynamicTools` 的 Runtime Dynamic Tool authoring API。 |
| Hub | `DotCraft.Sdk.Hub.HubClient`、Hub DTO、进程策略、事件和结构化 `HubClientException`。 |
| App Binding | `DotCraft.Sdk.AppBinding.DotCraftAppBindingClient`、handoff 模型和错误辅助方法。 |

Core 与 SDK 使用同一个 `DotCraft.Protocol.Contracts` 程序集；SDK 不维护第二份 Wire DTO。Core 的领域和持久化模型保持独立，并在 AppServer 边界显式映射。

Thread start/resume/read/list、turn start/enqueue、provider/model/MCP/App Binding 操作、回调、snapshot 与 Run 终止结果都直接暴露这些 Contracts DTO。`SessionItem.Payload` 为保持开放世界兼容性而继续使用 `JsonElement`；对于 16 种 canonical payload，请使用 `SessionItemPayloadParser.Parse(item)` 与 `TryGet<TPayload>`，未知 kind 仍可通过 `Raw` 保留。

`DotCraft.Sdk` NuGet 包同时包含 `DotCraft.Sdk.dll` 和 `DotCraft.Protocol.Contracts.dll`。只需安装 `DotCraft.Sdk`；Contracts 是独立的逻辑层和程序集，不是独立的包。

## Typed 与 raw Wire API

`DotCraftWireClient.RequestAsync` 和 `NotifyAsync` 接受生成的 descriptor，由 descriptor 决定参数与结果类型。强类型通知和服务端请求 handler 使用同一套契约。

生成的 `DotCraftWireClient.XxxAsync` 与 `RegisterXxxHandler` 扩展覆盖全部已登记方法。高层 SDK 客户端内部也使用这些绑定；已知 AppServer 方法不再走 raw 调用。

未知或第三方扩展使用独立的 `RequestRawAsync`、`NotifyRawAsync` 和 raw handler 注册。Typed 方法不提供任意字符串 overload。

`DotCraftClient` 是常规应用入口。`DotCraftWireClient` 只管理 JSON-RPC 和连接会话；它不会自动批准请求、回答用户输入，也不会恢复应用资源。

## 连接生命周期

Wire Client 会报告 `Connecting`、`Initializing`、`Ready`、`Disconnected`、`Reconnecting`、`ReconnectError` 和 `Closed`。

- Raw Wire 连接默认不重连；本地和远程高层连接默认启用重连。设置 `DotCraftClientOptions.AutoReconnect` 可覆盖入口默认值。
- 默认 RPC 超时为 30 秒，包含重连队列等待时间。
- 重连使用 1 到 30 秒的指数退避和抖动，最多按调用顺序排队 1024 个新请求。
- 断线时进行中的请求会失败且不会重放。Client 会重新发送 `initialize` 和 `initialized`，之后才释放排队请求。
- 已注册 handler 会保留；thread subscription 和运行时动态工具不会自动重建。活动 Run 会以 `RunDisconnectedException` 失败，SDK 绝不会重放 `turn/start`。

`ConnectLocalAsync` 通过 Hub 确保工作区 AppServer。当宿主已解析特定 DotCraft 二进制时，设置 `DotCraftLocalOptions.Executable`。`ConnectRemoteAsync` 接受 `DotCraftRemoteOptions` 并连接已知的 AppServer WebSocket，`ConnectAsync` 接受自定义 `IJsonRpcTransport`。

## Hub API

`HubClient` 支持 lock/default chat、活动 Hub 查询和确保、工作区 AppServer 解析、ensure/restart/stop/list、状态、事件和关闭操作。

Hub 异常保留 `Code`、`Message` 和 `Details`。直接启动 Hub 时可设置 expected executable 和 binary match policy：

- `Ignore`
- `RestartIfMismatch`
- `ErrorIfMismatch`

未提供 expected executable 时，默认策略是 `Ignore`。

## 验证

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```

## 相关文档

- [快速开始](./quickstart)
- [Thread 与 Run](./runs)
- [工具与审批](./tools)
- [构建应用](../integrations/build-an-app)
- [AppServer 协议](../protocols/appserver-protocol)
