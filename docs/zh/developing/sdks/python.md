# Python SDK 参考

`dotcraft` 是面向 AppServer 应用和 Channel Adapter 的 asyncio SDK。安装和首次运行见[快速开始](./quickstart)。

## 包

| 字段 | 值 |
| --- | --- |
| 包 | `dotcraft`（源码预览） |
| 运行时 | Python 3.10+ |
| 依赖 | Pydantic v2、`websockets` |

该包尚未发布到 PyPI。请从本仓库安装。

## 模块

| 模块 | 公共接口 |
| --- | --- |
| `dotcraft` | `DotCraft`、thread、run、回调、输入 helper、审批决策和高层错误。 |
| `dotcraft.contracts` | 生成的 Pydantic contract、注册表和协议元数据。 |
| `dotcraft.wire` | `DotCraftWireClient`、transport、生命周期错误，以及 typed/raw Wire API。 |
| `dotcraft.hub` | Hub 发现、AppServer 管理、进程策略、事件和结构化错误。 |
| `dotcraft.app_binding` | App Binding handoff 和结果 helper。 |
| `dotcraft.dynamic_tools` | Runtime Dynamic Tool 模型和 helper。 |
| `dotcraft.channel` | Channel Adapter profile。 |

## 高层 API

| 任务 | API |
| --- | --- |
| 连接 | `connect_local()`、`connect_local_chat()`、`connect_remote()` |
| 关闭 | `close()` 或 `async with` |
| Thread | `threads.get_or_create()`、`start()`、`resume()`、`list()`、`read()`、`list_turns()`、`list_items()` |
| Run | `run()`、`run_streamed()`、`enqueue()`、`interrupt()` |
| Thread 状态 | `snapshot`、`refresh()`、`subscribe()`、`unsubscribe()`、`set_mode()`、`archive()`、`delete()` |
| 模型 | `models.list()` |
| MCP runtime | `mcp_runtime.list_status()`、`read_resource()`、`call_tool()`、`login_oauth()`、`reload()` |
| App Binding | `app_bindings` |
| 运行时工具 | `on_tool_call()` |

在本地或远程连接选项中配置 `approval_handler` 和 `user_input_handler`。Python 不会为 `run()` 添加自动 abort 选项。请使用活动 turn ID 调用 `interrupt()`。

## 连接

| 方法 | 选项 | 连接所有权 |
| --- | --- | --- |
| `DotCraft.connect_local(LocalOptions(...))` | 必须提供 `workspace_path`。 | 通过 Hub 确保工作区 AppServer 可用，再连接到它。 |
| `DotCraft.connect_local_chat(LocalChatOptions(...))` | 所有字段都可选。 | 通过 Hub 确保默认 Chat 工作区 AppServer 可用。 |
| `DotCraft.connect_remote(RemoteOptions(...))` | 必须提供 `url`，`token` 可选。 | 直接连接现有 AppServer WebSocket。 |

选项 dataclass 还包含 client identity、callback、capability 和本地 Hub 覆盖。所有连接方法都是异步方法。

| 选项类型 | 字段 |
| --- | --- |
| `LocalOptions` | 必需 `workspace_path`，可选 client identity、`executable`、`home_dir`、`hub_startup_timeout`、handler 和 `capabilities`。 |
| `LocalChatOptions` | 除 `workspace_path` 外的本地字段。 |
| `RemoteOptions` | 必需 `url`，可选 `token`、client identity、handler 和 `capabilities`。 |

## Thread 与 Run

`ThreadManager` 提供 `get_or_create()`、`start()`、`resume(thread_id)`、`list()`、`read(thread_id)`、`list_turns()` 和 `list_items()`。基于 identity 的方法接受 `user_id`、`channel_name` 和 `channel_context`。`start()` 还接受工作区路径、显示名称和运行时动态工具。

`read()` 和 `DotCraftThread.refresh()` 返回当前 Thread 头部，不包含持久化的 Turn 或 Item。`list_turns()` 读取 Turn 元数据，`list_items()` 跨 Thread 或按可选 `turn_id` 读取 Item。Manager 和 Thread handle 两种形式都接受 opaque `cursor`、`limit` 和 `sort_direction`，并返回 `data` 与 `next_cursor`。

`run()` 和 `run_streamed()` 接受文本、input-part 列表或 `{input, sender}` 形式的字典。Buffered run 选项为 `sender`、`collect_raw_events`、`enqueue_if_busy` 和 `throw_on_failure`。streaming 接受 `sender` 和 `enqueue_if_busy`。`RunResult` 包含 `thread_id`、可选 `turn_id`、合并后的 `text`、可选终止 `turn` 和可选 raw event。

`DotCraftThread` 还提供 `list_turns()`、`list_items()`、`enqueue()`、`interrupt()`、`subscribe()`、`unsubscribe()`、`set_mode()`、`archive()`、`delete()`、`refresh()` 和 `on_tool_call()`。

## 模型、MCP 与 App Binding

| Manager | 操作 |
| --- | --- |
| `models` | `list()` 返回当前 AppServer 可见的模型目录。 |
| `mcp_runtime` | `list_status()`、`read_resource()`、`call_tool()`、`login_oauth()`、`reload()`。 |
| `app_bindings` | App 发现、连接、surface、thread binding 和 principal 操作。 |

Python 高层接口可以列出模型，但目前没有模型配置便利方法。应用必须修改完整配置时，使用生成的 Wire 方法调用 `thread/config/update`，并保留不归自己所有的字段。任务流程见 [MCP 运行时](./mcp-runtime)和 [DotCraft App](../integrations/app-binding)。

异步 MCP manager 的签名如下：

```python
async def list_status(**kwargs: Any) -> McpServerStatusListResult: ...
async def read_resource(server: str, uri: str, thread_id: str | None = None) -> McpServerResourceReadResult: ...
async def call_tool(
    thread_id: str,
    server: str,
    tool: str,
    arguments: dict | None = None,
    meta: Any = None,
) -> McpServerToolCallResult: ...
async def login_oauth(**kwargs: Any) -> McpServerOAuthLoginResult: ...
async def reload() -> McpServerReloadResult: ...
```

## 回调与运行时动态工具

审批 handler 类型为 `Callable[[dict], Awaitable[str] | str]`，用户输入 handler 类型为 `Callable[[dict], Awaitable[dict] | dict]`。使用下列接口注册 thread scoped 运行时动态工具：

```python
def on_tool_call(
    namespace: str | None,
    name: str,
    handler: Callable,
) -> Callable[[], None]: ...
```

Handler 在应用进程中执行。请在启动可能调用工具的工作前注册 handler，在 handler 中验证参数，并在其所属 scope 结束时调用返回的 disposer。

## 关闭 client

连接由单个作用域持有时，使用 async context manager：

```python
async with await DotCraft.connect_local(options) as dotcraft:
    thread = await dotcraft.threads.start(user_id="me")
    result = await thread.run("Summarize this project.")
```

关闭 client 不会停止由 Hub 管理的 AppServer。

## Contract 模型

生成模型使用 snake_case 属性和 camelCase JSON alias。序列化 Wire payload 时使用 alias，并保留缺失字段：

```python
payload = model.model_dump(by_alias=True, exclude_unset=True)
```

模型会接受未知字段以保持前向兼容。

## Typed 与 raw Wire API

已登记的 AppServer 操作使用生成方法。只有第三方或尚未进入目录的扩展才使用 raw 方法：

```python
value = await wire.request_raw("ext/example/read", {"id": "42"})
await wire.notify_raw("ext/example/changed", {"id": "42"})
dispose = wire.register_notification_raw("ext/example/event", handle_event)
```

`DotCraftWireClient` 只负责 JSON-RPC 和连接状态。审批、用户输入、run、工具和 Channel 策略属于更高层。

## 连接生命周期

Wire 状态包括 `connecting`、`initializing`、`ready`、`disconnected`、`reconnecting`、`reconnectError` 和 `closed`。

- Raw Wire 连接只有显式启用后才会重连。
- 普通请求默认超时为 30 秒。
- 重连使用指数退避，最多排队 1024 个新调用。
- 进行中的调用会失败且绝不重放。
- 初始化完成后才会释放排队调用。
- Handler 注册会跨重连保留，但 thread subscription、活动 run 和运行时工具资源不会保留。

## 错误

高层错误派生自 `DotCraftError`，并带有稳定的 `code`。

| 错误 | 条件 |
| --- | --- |
| `JsonRpcError` | AppServer 返回 JSON-RPC 错误。 |
| `InitializationError` | 连接初始化失败。 |
| `TurnInProgressError` | Thread 已有活动 turn。 |
| `ThreadNotFoundError` / `ThreadNotActiveError` | 目标 thread 不存在或无法运行。 |
| `TurnFailedError` / `TurnCancelledError` | Buffered run 到达失败或取消终态。 |
| `ApprovalTimeoutError` | AppServer 报告审批超时。 |
| `ProtocolViolationError` | 已知消息不符合其 contract。 |

`dotcraft.wire` 还导出 `TransportError`、`TransportClosed`、`RequestTimeoutError` 和 `ReconnectQueueFullError`。

## Hub API

`HubClient` 发现或启动 Hub、验证本地 lock、解析工作区 AppServer，并支持 ensure、restart、stop、list、status、events 和 shutdown。

Hub 错误会保留 `code`、`message` 和 `details`。不要记录 Hub token 或包含 token 的完整 WebSocket URL。

## 相关文档

- [SDK 快速开始](./quickstart)
- [线程与运行](./runs)
- [工具与审批](./tools)
- [MCP 运行时](./mcp-runtime)
- [渠道适配器](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
