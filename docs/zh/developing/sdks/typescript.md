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
| Thread | `threads.getOrCreate()`、`start()`、`resume()`、`list()`、`listPage()`、`read()`、`listTurns()`、`listItems()` |
| Run | `run()`、`runStreamed()`、`enqueue()`、`interrupt()` |
| Thread 状态 | `snapshot()`、`refresh()`、`subscribe()`、`unsubscribe()`、`setMode()`、`archive()`、`delete()` |
| 模型 | `models.list()` |
| MCP runtime | `mcpRuntime.listStatus()`、`readResource()`、`callTool()`、`loginOAuth()`、`reload()` |
| App Binding | `appBindings` |
| 运行时工具 | `onToolCall()` |

在本地或远程连接选项中配置 `approvalHandler` 和 `userInputHandler`。任务流程见[线程与运行](./runs)和[工具与审批](./tools)。

## 连接

| 方法 | 必需选项 | 连接所有权 |
| --- | --- | --- |
| `DotCraft.local(options)` | `workspacePath` | 通过 Hub 确保工作区 AppServer 可用，再连接到它。 |
| `DotCraft.localChat(options?)` | 无 | 通过 Hub 确保默认 Chat 工作区 AppServer 可用。 |
| `DotCraft.remote(options)` | `url`，可选 `token` | 直接连接现有 AppServer WebSocket。 |

三种选项类型都支持 client identity、审批和用户输入 handler，以及额外 capability。本地选项还支持可执行文件选择、二进制匹配策略、Hub timeout 和 home directory 覆盖。

| 选项类型 | 字段 |
| --- | --- |
| `DotCraftLocalOptions` | 必需 `workspacePath`，可选 `clientName`、`clientVersion`、`clientTitle`、`executable`、`expectedExecutable`、`binaryMatchPolicy`、`hubStartupTimeoutMs`、`homeDir`、handler 和 `capabilities`。 |
| `DotCraftLocalChatOptions` | 除 `workspacePath` 外的本地字段。 |
| `DotCraftRemoteOptions` | 必需 `url`，可选 `token`、client identity、handler 和 `capabilities`。 |

## Thread 与 Run

`ThreadManager` 的精确高层操作如下：

```ts
getOrCreate(options?: GetOrCreateThreadOptions): Promise<DotCraftThread>;
start(options?: StartThreadOptions): Promise<DotCraftThread>;
resume(threadId: string, options?: ResumeThreadOptions): Promise<DotCraftThread>;
list(options?: ListThreadOptions): Promise<ThreadSummary[]>;
listPage(options?: ListThreadOptions): Promise<ThreadListResult>;
read(threadId: string): Promise<SessionThread>;
listTurns(threadId: string, options?: ThreadHistoryPageOptions): Promise<ThreadTurnsListResult>;
listItems(threadId: string, options?: ThreadItemPageOptions): Promise<ThreadItemsListResult>;
```

Start 选项包含 identity 字段、显示名称、history mode、配置、运行时动态工具和额外上下文。Resume 选项只重新绑定动态工具和额外上下文。List 选项还包含 identity/workspace scope、归档过滤、文本查询、limit 和 cursor。

`read()` 和 Thread handle 的 `refresh()` 返回当前 Thread 头部，不包含持久化的 Turn 或 Item。`listTurns()` 读取 Turn 元数据，`listItems()` 跨 Thread 或按可选 `turnId` 读取 Item。两者都接受 `cursor`、`limit` 和 `sortDirection`，并返回 `data` 与 opaque `nextCursor`。Thread handle 也提供相同的两个分页方法，但不需要 `threadId` 参数。

`run()` 和 `runStreamed()` 接受文本、`InputPart[]` 或 `{ input, sender }`。Run 选项为 `sender`、`collectRawEvents`、`abortSignal` 和 `enqueueIfBusy`。Buffered 结果包含 `thread`、可选终止 `turn`、合并后的 `text`、`items`、可选 `usage`、可选 raw event 和 queued-input 结果。

## 模型、MCP 与 App Binding

| Manager | 操作 |
| --- | --- |
| `models` | `list()` 返回当前 AppServer 可见的模型目录。 |
| `mcpRuntime` | `listStatus()`、`readResource()`、`callTool()`、`loginOAuth()`、`reload()`。 |
| `appBindings` | App 发现、连接、surface、thread binding、social binding 和 principal 操作。 |

TypeScript 高层接口可以列出模型，但目前没有模型配置便利方法。应用必须修改完整 thread 配置时，使用类型化 Wire request map 调用 `thread/config/update`，并保留不归自己所有的字段。任务流程见 [MCP 运行时](./mcp-runtime)和 [DotCraft App](../integrations/app-binding)。

MCP manager 的签名如下：

```ts
listStatus(params?: McpServerStatusListParams): Promise<McpServerStatusListResult>;
readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult>;
callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult>;
loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult>;
reload(): Promise<McpServerReloadResult>;
```

## 回调与运行时动态工具

```ts
type ApprovalHandler =
  (request: Record<string, unknown>) => Promise<ApprovalDecision> | ApprovalDecision;
type UserInputHandler =
  (request: Record<string, unknown>) => Promise<Record<string, unknown>> | Record<string, unknown>;
type DynamicToolHandler =
  (request: DynamicToolCallRequest) => Promise<DynamicToolCallResult> | DynamicToolCallResult;

thread.onToolCall(namespace: string | null, name: string, handler: DynamicToolHandler): Unsubscribe;
```

Handler 在应用进程中执行。请在启动可能调用工具的工作前注册 handler，在 handler 中验证参数，并在其所属 scope 结束时 dispose 注册。

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
- Handler 注册会跨重连保留，但 thread subscription、活动 run 和运行时工具资源不会保留。

关闭本地高层 client 只关闭其 WebSocket 连接，不会停止 Hub 或由 Hub 管理的 AppServer。

## 错误

所有 SDK 错误都派生自 `DotCraftError`，并带有稳定的 `code`。

| 错误 | 条件 |
| --- | --- |
| `JsonRpcError` | AppServer 返回 JSON-RPC 错误，并保留 `rpcCode` 和 data。 |
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
- [MCP 运行时](./mcp-runtime)
- [渠道适配器](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
