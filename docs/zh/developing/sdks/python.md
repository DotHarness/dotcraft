# Python SDK 参考

`dotcraft` 提供生成的 Pydantic 契约、asyncio JSON-RPC client、高层 Thread 与 Run API、Hub 管理和 Channel 宿主配置。安装和首次运行请从[快速开始](./quickstart)开始。

## 包信息

| | |
|---|---|
| 包 | `dotcraft`（源码预览） |
| 运行时基线 | Python 3.10+，原生 asyncio |
| 运行时依赖 | Pydantic v2 和 `websockets>=13` |

该包目前尚未发布到 PyPI。请按照[快速开始](./quickstart)以 editable 模式安装仓库目录。项目不提供单独的 Wire 兼容包；所有公共层级都从 `dotcraft` 或其公开子模块导入。

## 层级

| 层级 | 公共接口 |
|------|----------|
| Contracts | `dotcraft.contracts` 中生成的 Pydantic v2 模型、注册表和协议元数据。 |
| Wire | `DotCraftWireClient`、`Transport`、`StdioTransport`、`WebSocketTransport`、生命周期状态和 JSON-RPC 错误。 |
| High-level | `dotcraft` 导出的 `DotCraft`、`DotCraftThread`、`ThreadManager`、`RunResult`、`RunEvent`、回调和强类型错误。 |
| 辅助 API | Hub、App Binding、Runtime Dynamic Tools、Channel、测试、元数据和错误分别从对应命名子模块导入。 |

## 契约模型

生成模型使用符合 Python 风格的 snake_case 属性和 camelCase JSON alias。它们允许未知字段以保持前向兼容，也可以通过字段名或 alias 填充。序列化 Wire 值时应使用 alias，并保留 missing 与 null 的区别：

```python
payload = model.model_dump(by_alias=True, exclude_unset=True)
```

强类型 RPC mixin 和通知注册表将这些模型连接到已知协议方法。Required-nullable 字段与未提供的字段保持不同语义。

## Typed 与 raw Wire API

已知方法使用生成的 RPC 方法和 `register_notification`。只有第三方扩展或尚未进入目录的方法才使用显式命名的 raw 边界：

```python
value = await wire.request_raw("ext/example/read", {"id": "42"})
await wire.notify_raw("ext/example/changed", {"id": "42"})
wire.register_notification_raw("ext/example/event", handle_event)
```

`DotCraftWireClient` 只管理 JSON-RPC 和连接状态。审批、用户输入、运行时动态工具、Run 和 Channel 行为属于更高层。

## 连接生命周期

Wire Client 会报告 `connecting`、`initializing`、`ready`、`disconnected`、`reconnecting`、`reconnectError` 和 `closed`。

- Raw Wire 连接默认不重连；高层和 Channel 配置会显式启用。
- 默认 RPC 超时为 30 秒，包含重连队列等待时间。
- 重连使用 1 到 30 秒的指数退避和抖动，最多按调用顺序排队 1024 个新请求。
- 断线时进行中的请求会失败且不会重放。初始化完成后才释放排队请求。
- Handler 注册会保留；thread subscription、活动 Run 和运行时动态工具不会自动重建。

## Hub API

`HubClient` 支持 lock/default chat、活动 Hub 查询和确保、工作区 AppServer 解析、ensure/restart/stop/list、状态、事件和关闭操作。

`HubError` 保留结构化 `code`、`message` 和 `details`。启动参数接受 `expected_executable` 和 `binary_match_policy`，可选值为 `ignore`、`restartIfMismatch` 或 `errorIfMismatch`。未提供 expected executable 时，有效策略为 `ignore`。

## 其他公共 API

根包只导出高层 API、输入构造器、approval 常量和高层错误。Runtime Dynamic Tools 从 `dotcraft.dynamic_tools` 导入，App Binding 从 `dotcraft.app_binding` 导入，Channel authoring API 从 `dotcraft.channel` 导入，Transport 从 `dotcraft.wire` 导入。参见[工具与审批](./tools)和 [Channel Adapter](./channels)。

## 验证

```bash
cd sdk/python
python -m pytest
python -m pyright
```

仓库固定开发使用的 pyright 版本，以一致地校验生成绑定。

## 相关文档

- [快速开始](./quickstart)
- [Thread 与 Run](./runs)
- [工具与审批](./tools)
- [Channel Adapter](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
