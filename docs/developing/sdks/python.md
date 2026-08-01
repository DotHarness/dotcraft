# Python SDK reference

Package identity and language-specific details for `dotcraft`. For how-to, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `dotcraft` (PyPI) |
| Runtime baseline | Python 3.10+, asyncio-native |
| Dependencies | `pydantic` v2, `websockets` |

```bash
pip install dotcraft
```

## Public surface

| Group | Symbols |
|-------|---------|
| High-level client | `DotCraft`, `Thread`, `ThreadManager`, `RunResult`, `RunEvent`, `LocalOptions`, `RemoteOptions` |
| Contracts | `dotcraft.contracts` generated Pydantic models and protocol metadata |
| Wire client | `DotCraftWireClient`, `JsonRpcMessage` |
| AppServer client | `DotCraftAppServerClient` business-oriented Thread, Turn, MCP, and event helpers |
| Transports | `Transport`, `StdioTransport`, `WebSocketTransport` |
| Hub | `HubClient`, `HubLockInfo`, `HubError` |
| App Binding | `AppBindingManager`, `AppBindingHandoff`, `app_binding_tool_error`, `APP_BINDING_ERROR_CODES` |
| Channel adapter | `ChannelAdapter` |
| Input parts | `text_part`, `image_url_part`, `local_image_part`, `skill_ref_part`, `command_ref_part`, `file_ref_part` |
| Errors | `DotCraftError`, `TurnInProgressError`, `TurnFailedError`, `TurnCancelledError`, `ThreadNotFoundError`, … |

`DotCraft.connect_local` / `connect_remote` return the high-level client. `DotCraftWireClient` and the transports remain available for protocol-level use; unknown extensions require the explicitly named raw APIs.

## Validation

```bash
cd sdk/python
python -m pytest
```

## See also

- [Quickstart](./quickstart) · [Threads & runs](./runs) · [Tools & approvals](./tools) · [Channel adapters](./channels)
- Python binding spec: `specs/sdk/python.md`
