# TypeScript SDK 参考

`@dotcraft/sdk` 是面向 AppServer 应用的 Node.js SDK。安装和首次运行见[快速开始](./quickstart)。

## 包

| 字段 | 值 |
| --- | --- |
| 包 | `@dotcraft/sdk`（源码预览） |
| 模块格式 | ESM |
| 运行时 | Node.js 20+ |

该包是 private package，尚未发布到 npm。运行时入口应放在 Node.js 或 Electron Main，而不是浏览器或 Electron Renderer 中。

## 入口点

| 入口点 | 公共接口 |
| --- | --- |
| `@dotcraft/sdk` | `DotCraft`、thread、run、回调、输入 helper、审批决策和高层错误。 |
| `@dotcraft/sdk/contracts` | 生成的 DTO、方法映射、注册表和协议元数据。 |
| `@dotcraft/sdk/wire` | `DotCraftWireClient`、transport、生命周期状态、超时、typed 方法和 raw 扩展 API。 |
| `@dotcraft/sdk/hub` | Hub 发现、AppServer 管理、进程策略、事件和结构化错误。 |
| `@dotcraft/sdk/app-binding` | App Binding handoff 和结果 helper。 |
| `@dotcraft/sdk/dynamic-tools` | Runtime Dynamic Tool authoring helper。 |
| `@dotcraft/sdk/testing` | Transport 测试 helper。 |
| `@dotcraft/sdk/meta` | SDK、contract、protocol 和 contract hash 元数据。 |

Contracts 不依赖 Node.js、WebSocket 或运行时 I/O，因此 Renderer 代码可以导入它获取类型。

## 高层 API

| 任务 | API |
| --- | --- |
| 连接 | `DotCraft.local()`、`DotCraft.localChat()`、`DotCraft.remote()` |
| 关闭 | `dotcraft.close()` |
| Thread | `threads.getOrCreate()`、`start()`、`resume()`、`list()`、`listPage()`、`read()` |
| Run | `run()`、`runStreamed()`、`enqueue()`、`interrupt()` |
| Thread 状态 | `snapshot()`、`refresh()`、`subscribe()`、`unsubscribe()`、`setMode()`、`archive()`、`delete()` |
| 模型 | `models.list()` |
| MCP runtime | `mcpRuntime.listStatus()`、`readResource()`、`callTool()`、`loginOAuth()`、`reload()` |
| App Binding | `appBindings` |
| 运行时工具 | `onToolCall()` |

在本地或远程连接选项中配置 `approvalHandler` 和 `userInputHandler`。任务流程见[线程与运行](./runs)和[工具与审批](./tools)。

## Typed 与 raw Wire API

已登记的 AppServer 方法使用 typed 方法映射：

```ts
const result = await wire.request("thread/list", params);
const dispose = wire.on("thread/started", ({ thread }) => console.log(thread.id));
```

只有第三方或尚未进入目录的扩展才使用 raw API：

```ts
const value = await wire.requestRaw("ext/example/read", { id: "42" });
const dispose = wire.onRaw("ext/example/changed", console.log);
```

`DotCraftWireClient` 只负责 JSON-RPC 和连接状态，不会自动审批、回答用户输入，也不会重建 thread 和工具资源。

## 连接生命周期

Wire 状态包括 `connecting`、`initializing`、`ready`、`disconnected`、`reconnecting`、`reconnectError` 和 `closed`。

- Raw Wire 连接只有启用 `autoReconnect` 后才会重连。
- 普通请求默认超时为 30 秒。
- 重连使用指数退避，最多排队 1024 个新调用。
- 进行中的调用会失败且绝不重放。
- 初始化完成后才会释放排队调用。
- Handler 注册会跨重连保留；thread subscription、活动 run 和运行时工具资源不会保留。

关闭本地高层 client 只关闭其 WebSocket 连接，不会停止 Hub 或由 Hub 管理的 AppServer。

## 错误

所有 SDK 错误都派生自 `DotCraftError`，并带有稳定的 `code`。

| 错误 | 条件 |
| --- | --- |
| `JsonRpcError` | AppServer 返回 JSON-RPC 错误；保留 `rpcCode` 和 data。 |
| `InitializationError` | 连接初始化失败。 |
| `TurnInProgressError` | Thread 已有活动 turn。 |
| `ThreadNotFoundError` / `ThreadNotActiveError` | 目标 thread 不存在或无法运行。 |
| `TurnFailedError` / `TurnCancelledError` | Buffered run 到达失败或取消终态。 |
| `ApprovalTimeoutError` | AppServer 报告审批超时。 |
| `ProtocolViolationError` | 已知消息不符合其 contract。 |

Wire 入口还导出 `TransportError`、`TransportClosed`、`RequestTimeoutError` 和 `ReconnectQueueFullError`。

## Hub API

`HubClient` 发现或启动 Hub、验证本地 lock、解析工作区 AppServer，并支持 ensure、restart、stop、list、status、events 和 shutdown。

Hub 错误会保留 `code`、`message` 和 `details`。不要记录 Hub token 或包含 token 的完整 WebSocket URL。

## 故障排查

| 现象 | 检查项 |
| --- | --- |
| npm 无法解析 `@dotcraft/sdk` | 该包目前是源码预览。按 [Quickstart](./quickstart) 从仓库 checkout 构建。 |
| 本地连接要求工作区 | 传入 `workspacePath`，或使用 `DotCraft.localChat()` 连接默认 Chat 工作区。 |
| 远程初始化失败 | 确认 AppServer WebSocket URL 以 `/ws` 结尾，且 token 与该 AppServer 匹配。不要把任一值输出到日志。 |
| Run 在重连期间结束 | 进行中的工作不会重放。继续前应读取或恢复 thread、重新订阅，并重新注册 runtime tool handler。 |

## 相关文档

- [SDK 快速开始](./quickstart)
- [线程与运行](./runs)
- [工具与审批](./tools)
- [渠道适配器](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
