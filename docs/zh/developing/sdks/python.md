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
| Thread | `threads.get_or_create()`、`start()`、`resume()`、`list()`、`read()` |
| Run | `run()`、`run_streamed()`、`enqueue()`、`interrupt()` |
| Thread 状态 | `snapshot`、`refresh()`、`subscribe()`、`unsubscribe()`、`set_mode()`、`archive()`、`delete()` |
| 模型 | `models.list()` |
| MCP runtime | `mcp_runtime.list_status()`、`read_resource()`、`call_tool()`、`login_oauth()`、`reload()` |
| App Binding | `app_bindings` |
| 运行时工具 | `on_tool_call()` |

在本地或远程连接选项中配置 `approval_handler` 和 `user_input_handler`。Python 不会为 `run()` 添加自动 abort 选项；请使用活动 turn ID 调用 `interrupt()`。

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
- Handler 注册会跨重连保留；thread subscription、活动 run 和运行时工具资源不会保留。

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
- [渠道适配器](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
