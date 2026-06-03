# Python SDK reference

Package identity and language-specific details for `dotcraft`. For how-to, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `dotcraft` (PyPI) |
| Compatibility alias | `dotcraft_wire` (re-exports the wire client, transports, and channel adapter) |
| Runtime baseline | Python 3.10+, asyncio-native |
| Dependencies | `websockets` |

```bash
pip install dotcraft
```

## Public surface

| Group | Symbols |
|-------|---------|
| High-level client | `DotCraft`, `Thread`, `ThreadManager`, `RunResult`, `RunEvent`, `LocalOptions`, `RemoteOptions` |
| Wire client | `DotCraftClient`, `JsonRpcMessage` |
| Transports | `Transport`, `StdioTransport`, `WebSocketTransport` |
| Hub | `HubClient`, `HubLockInfo`, `HubError` |
| App Binding | `AppBindingManager`, `AppBindingHandoff`, `app_binding_tool_error`, `APP_BINDING_ERROR_CODES` |
| Channel adapter | `ChannelAdapter` |
| Input parts | `text_part`, `image_url_part`, `local_image_part`, `skill_ref_part`, `command_ref_part`, `file_ref_part` |
| Errors | `DotCraftError`, `TurnInProgressError`, `TurnFailedError`, `TurnCancelledError`, `ThreadNotFoundError`, … |

`DotCraft.connect_local` / `connect_remote` return the high-level client; the low-level `DotCraftClient` and transports remain available for advanced use.

## Validation

```bash
cd sdk/python
python -m pytest
```

## See also

- [Quickstart](./quickstart) · [Threads & runs](./runs) · [Tools & approvals](./tools) · [Channel adapters](./channels)
- Python binding spec: `specs/sdk/python.md`
