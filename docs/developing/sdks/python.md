# Python SDK reference

`dotcraft` provides generated Pydantic contracts, an asyncio JSON-RPC client, high-level Thread and Run APIs, Hub management, and a Channel host profile. For installation and a first run, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `dotcraft` (source preview) |
| Runtime baseline | Python 3.10+, asyncio-native |
| Runtime dependencies | Pydantic v2 and `websockets>=13` |

The package is not currently published to PyPI. Install the repository directory in editable mode as described in the [Quickstart](./quickstart). There is no separate Wire compatibility package; import all public layers from `dotcraft` or its documented submodules.

## Layers

| Layer | Public surface |
|-------|----------------|
| Contracts | `dotcraft.contracts` generated Pydantic v2 models, registries, and protocol metadata. |
| Wire | `DotCraftWireClient`, `Transport`, `StdioTransport`, `WebSocketTransport`, lifecycle state, and JSON-RPC errors. |
| High-level | `DotCraft`, `DotCraftThread`, `ThreadManager`, `RunResult`, `RunEvent`, callbacks, and typed errors from `dotcraft`. |
| Supporting APIs | Hub, App Binding, Runtime Dynamic Tools, Channel, testing, metadata, and errors from their named submodules. |

## Contract models

Generated models use idiomatic snake-case attributes with camel-case JSON aliases. They accept unknown fields for forward compatibility and can be populated by either field name or alias. Serialize Wire values with aliases and preserve missing-versus-null semantics:

```python
payload = model.model_dump(by_alias=True, exclude_unset=True)
```

The typed RPC mixin and notification registry connect these models to known protocol methods. Required-nullable fields remain distinct from fields that were not provided.

## Typed and raw Wire APIs

Use generated RPC methods and `register_notification` for known methods. Use the explicitly named raw boundary only for third-party or not-yet-cataloged extensions:

```python
value = await wire.request_raw("ext/example/read", {"id": "42"})
await wire.notify_raw("ext/example/changed", {"id": "42"})
wire.register_notification_raw("ext/example/event", handle_event)
```

`DotCraftWireClient` manages JSON-RPC and connection state only. Approval, user-input, Runtime Dynamic Tool, Run, and Channel behavior belongs to the higher layers.

## Connection lifecycle

The Wire client reports `connecting`, `initializing`, `ready`, `disconnected`, `reconnecting`, `reconnectError`, and `closed`.

- Raw Wire connections do not reconnect unless enabled. High-level and Channel profiles opt in explicitly.
- The default RPC timeout is 30 seconds and includes reconnect queue time.
- Reconnect uses 1-to-30-second exponential backoff with jitter and queues at most 1024 new requests in call order.
- In-flight requests fail on disconnect and are not replayed. Initialization completes before queued calls are released.
- Handler registrations remain installed. Thread subscriptions, active Runs, and Runtime Dynamic Tools are not reconstructed automatically.

## Hub API

`HubClient` supports lock/default-chat helpers, live-Hub query and ensure, workspace AppServer resolution, ensure/restart/stop/list operations, status, events, and shutdown.

`HubError` preserves structured `code`, `message`, and `details`. Startup accepts `expected_executable` and `binary_match_policy` with `ignore`, `restartIfMismatch`, or `errorIfMismatch`. Without an expected executable, the effective policy is `ignore`.

## Other public APIs

The root package exports only the high-level API, input-part builders, approval constants, and high-level errors. Import Runtime Dynamic Tools from `dotcraft.dynamic_tools`, App Binding from `dotcraft.app_binding`, Channel authoring from `dotcraft.channel`, and transports from `dotcraft.wire`. See [Tools & approvals](./tools) and [Channel adapters](./channels).

## Validation

```bash
cd sdk/python
python -m pytest
python -m pyright
```

The repository pins the development version of pyright so generated bindings are checked consistently.

## Related docs

- [Quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [Channel adapters](./channels)
- [AppServer Protocol](../protocols/appserver-protocol)
