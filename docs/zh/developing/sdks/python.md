# Python SDK 参考

`dotcraft` 的包标识与语言特定细节。如何使用请从[快速开始](./quickstart)入手。

## 包

| | |
|---|---|
| 包名 | `dotcraft`（PyPI） |
| 兼容别名 | `dotcraft_wire`（re-export wire 客户端、传输与渠道适配器） |
| 运行时基线 | Python 3.10+，asyncio 原生 |
| 依赖 | `websockets` |

```bash
pip install dotcraft
```

## 公开面

| 分组 | 符号 |
|------|------|
| 高层客户端 | `DotCraft`、`Thread`、`ThreadManager`、`RunResult`、`RunEvent`、`LocalOptions`、`RemoteOptions` |
| Wire 客户端 | `DotCraftClient`、`JsonRpcMessage` |
| 传输 | `Transport`、`StdioTransport`、`WebSocketTransport` |
| Hub | `HubClient`、`HubLockInfo`、`HubError` |
| App Binding | `AppBindingManager`、`AppBindingHandoff`、`app_binding_tool_error`、`APP_BINDING_ERROR_CODES` |
| 渠道适配器 | `ChannelAdapter` |
| 输入 part | `text_part`、`image_url_part`、`local_image_part`、`skill_ref_part`、`command_ref_part`、`file_ref_part` |
| 错误 | `DotCraftError`、`TurnInProgressError`、`TurnFailedError`、`TurnCancelledError`、`ThreadNotFoundError` 等 |

`DotCraft.connect_local` / `connect_remote` 返回高层客户端；低层 `DotCraftClient` 与传输仍可用于高级场景。

## 验证

```bash
cd sdk/python
python -m pytest
```

## 参见

- [快速开始](./quickstart) · [线程与运行](./runs) · [工具与审批](./tools) · [渠道适配器](./channels)
- Python 绑定规范：`specs/sdk/python.md`
