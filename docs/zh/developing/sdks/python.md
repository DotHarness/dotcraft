# Python SDK 参考

`dotcraft` 的包标识与语言特定细节。如何使用请从[快速开始](./quickstart)入手。

## 包

| | |
|---|---|
| 包名 | `dotcraft`（PyPI） |
| 运行时基线 | Python 3.10+，asyncio 原生 |
| 依赖 | `pydantic` v2、`websockets` |

```bash
pip install dotcraft
```

## 公开面

| 分组 | 符号 |
|------|------|
| 高层客户端 | `DotCraft`、`Thread`、`ThreadManager`、`RunResult`、`RunEvent`、`LocalOptions`、`RemoteOptions` |
| Contracts | `dotcraft.contracts` 生成的 Pydantic 模型与协议元数据 |
| Wire 客户端 | `DotCraftWireClient`、`JsonRpcMessage` |
| AppServer 客户端 | `DotCraftAppServerClient` 提供面向业务的 Thread、Turn、MCP 与事件辅助 API |
| 传输 | `Transport`、`StdioTransport`、`WebSocketTransport` |
| Hub | `HubClient`、`HubLockInfo`、`HubError` |
| App Binding | `AppBindingManager`、`AppBindingHandoff`、`app_binding_tool_error`、`APP_BINDING_ERROR_CODES` |
| 渠道适配器 | `ChannelAdapter` |
| 输入 part | `text_part`、`image_url_part`、`local_image_part`、`skill_ref_part`、`command_ref_part`、`file_ref_part` |
| 错误 | `DotCraftError`、`TurnInProgressError`、`TurnFailedError`、`TurnCancelledError`、`ThreadNotFoundError` 等 |

`DotCraft.connect_local` / `connect_remote` 返回高层客户端。`DotCraftWireClient` 与传输保留给协议级场景；未知扩展必须使用名称明确的 raw API。

## 验证

```bash
cd sdk/python
python -m pytest
```

## 参见

- [快速开始](./quickstart) · [线程与运行](./runs) · [工具与审批](./tools) · [渠道适配器](./channels)
- Python 绑定规范：`specs/sdk/python.md`
