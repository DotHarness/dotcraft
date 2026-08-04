# Python SDK reference

`dotcraft` is the asyncio SDK for AppServer applications and Channel adapters. Start with the [Quickstart](./quickstart) for installation and a first run.

## Package

| Field | Value |
| --- | --- |
| Package | `dotcraft` (source preview) |
| Runtime | Python 3.10+ |
| Dependencies | Pydantic v2, `websockets` |

The package is not published to PyPI. Install it from this repository.

## Modules

| Module | Public surface |
| --- | --- |
| `dotcraft` | `DotCraft`, threads, runs, callbacks, input helpers, approval decisions, and high-level errors. |
| `dotcraft.contracts` | Generated Pydantic contracts, registries, and protocol metadata. |
| `dotcraft.wire` | `DotCraftWireClient`, transports, lifecycle errors, and typed and raw Wire APIs. |
| `dotcraft.hub` | Hub discovery, AppServer management, process policy, events, and structured errors. |
| `dotcraft.app_binding` | App Binding handoff and result helpers. |
| `dotcraft.dynamic_tools` | Runtime Dynamic Tool models and helpers. |
| `dotcraft.channel` | Channel Adapter profile. |

## High-level API

| Task | API |
| --- | --- |
| Connect | `connect_local()`, `connect_local_chat()`, `connect_remote()` |
| Close | `close()` or `async with` |
| Threads | `threads.get_or_create()`, `start()`, `resume()`, `list()`, `read()` |
| Run | `run()`, `run_streamed()`, `enqueue()`, `interrupt()` |
| Thread state | `snapshot`, `refresh()`, `subscribe()`, `unsubscribe()`, `set_mode()`, `archive()`, `delete()` |
| Models | `models.list()` |
| MCP runtime | `mcp_runtime.list_status()`, `read_resource()`, `call_tool()`, `login_oauth()`, `reload()` |
| App Binding | `app_bindings` |
| Runtime tools | `on_tool_call()` |

Configure `approval_handler` and `user_input_handler` in local or remote connection options. Python does not add an automatic abort option to `run()`; call `interrupt()` with the active turn ID.

## Connect

| Method | Options | Connection ownership |
| --- | --- | --- |
| `DotCraft.connect_local(LocalOptions(...))` | `workspace_path` is required. | Uses Hub to ensure the workspace AppServer, then connects to it. |
| `DotCraft.connect_local_chat(LocalChatOptions(...))` | All fields are optional. | Uses Hub to ensure the default Chat workspace AppServer. |
| `DotCraft.connect_remote(RemoteOptions(...))` | `url` is required; `token` is optional. | Connects directly to an existing AppServer WebSocket. |

The option dataclasses also carry client identity, callbacks, capabilities, and local Hub overrides. Every connection method is async.

| Option type | Fields |
| --- | --- |
| `LocalOptions` | Required `workspace_path`; optional client identity, `executable`, `home_dir`, `hub_startup_timeout`, handlers, and `capabilities`. |
| `LocalChatOptions` | The local fields except `workspace_path`. |
| `RemoteOptions` | Required `url`; optional `token`, client identity, handlers, and `capabilities`. |

## Threads and runs

`ThreadManager` provides `get_or_create()`, `start()`, `resume(thread_id)`, `list()`, and `read(thread_id, include_turns=False)`. Identity-based methods accept `user_id`, `channel_name`, and `channel_context`; `start()` also accepts workspace path, display name, and Runtime Dynamic Tools.

`run()` and `run_streamed()` accept text, input-part lists, or `{input, sender}`-style dictionaries. Buffered run options are `sender`, `collect_raw_events`, `enqueue_if_busy`, and `throw_on_failure`; streaming accepts `sender` and `enqueue_if_busy`. `RunResult` contains `thread_id`, optional `turn_id`, merged `text`, optional terminal `turn`, and optional raw events.

`DotCraftThread` also exposes `enqueue()`, `interrupt()`, `subscribe()`, `unsubscribe()`, `set_mode()`, `archive()`, `delete()`, `refresh()`, and `on_tool_call()`.

## Models, MCP, and App Binding

| Manager | Operations |
| --- | --- |
| `models` | `list()` returns the model catalog visible to this AppServer. |
| `mcp_runtime` | `list_status()`, `read_resource()`, `call_tool()`, `login_oauth()`, `reload()`. |
| `app_bindings` | App discovery, connection, surfaces, thread bindings, and principal operations. |

The Python high-level surface lists models but does not currently provide a model-configuration convenience method. Use the generated Wire method for `thread/config/update` when an application must change the complete configuration, and preserve fields it does not own. See [MCP runtime](./mcp-runtime) and [Build an App](../integrations/build-an-app) for task-oriented flows.

The async MCP manager signatures are:

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

## Callbacks and Runtime Dynamic Tools

Approval handlers are `Callable[[dict], Awaitable[str] | str]`; user-input handlers are `Callable[[dict], Awaitable[dict] | dict]`. Register a thread-scoped Runtime Dynamic Tool with:

```python
def on_tool_call(
    namespace: str | None,
    name: str,
    handler: Callable,
) -> Callable[[], None]: ...
```

Handlers execute in the application process. Register them before starting work that can call the tool, validate arguments in the handler, and invoke the returned disposer when their owning scope ends.

## Close the client

Use the async context manager when one scope owns the connection:

```python
async with await DotCraft.connect_local(options) as dotcraft:
    thread = await dotcraft.threads.start(user_id="me")
    result = await thread.run("Summarize this project.")
```

Closing the client does not stop a Hub-managed AppServer.

## Contract models

Generated models use snake_case attributes and camelCase JSON aliases. Serialize Wire payloads with aliases and preserve missing fields:

```python
payload = model.model_dump(by_alias=True, exclude_unset=True)
```

Unknown fields are accepted for forward compatibility.

## Typed and raw Wire API

Use generated methods for cataloged AppServer operations. Use raw methods only for third-party or not-yet-cataloged extensions:

```python
value = await wire.request_raw("ext/example/read", {"id": "42"})
await wire.notify_raw("ext/example/changed", {"id": "42"})
dispose = wire.register_notification_raw("ext/example/event", handle_event)
```

`DotCraftWireClient` owns JSON-RPC and connection state. Approval, user input, runs, tools, and Channel policy belong to higher layers.

## Connection lifecycle

Wire state is `connecting`, `initializing`, `ready`, `disconnected`, `reconnecting`, `reconnectError`, or `closed`.

- Raw Wire connections do not reconnect unless enabled.
- Ordinary requests default to a 30-second timeout.
- Reconnect uses exponential backoff and queues at most 1024 new calls.
- In-flight calls fail and are never replayed.
- Initialization completes before queued calls are released.
- Handler registrations survive reconnect. Thread subscriptions, active runs, and runtime tool resources do not.

## Errors

High-level errors derive from `DotCraftError` and carry a stable `code`.

| Error | Condition |
| --- | --- |
| `JsonRpcError` | AppServer returned a JSON-RPC error. |
| `InitializationError` | Connection initialization failed. |
| `TurnInProgressError` | The thread already has an active turn. |
| `ThreadNotFoundError` / `ThreadNotActiveError` | The target thread is missing or cannot run. |
| `TurnFailedError` / `TurnCancelledError` | A buffered run reached a failed or cancelled terminal state. |
| `ApprovalTimeoutError` | AppServer reports approval timeout. |
| `ProtocolViolationError` | A known message does not match its contract. |

`dotcraft.wire` also exports `TransportError`, `TransportClosed`, `RequestTimeoutError`, and `ReconnectQueueFullError`.

## Hub API

`HubClient` discovers or starts Hub, validates the local lock, resolves a workspace AppServer, and supports ensure, restart, stop, list, status, events, and shutdown operations.

Hub errors preserve `code`, `message`, and `details`. Do not log Hub tokens or full token-bearing WebSocket URLs.

## Related docs

- [SDK quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [MCP runtime](./mcp-runtime)
- [Channel adapters](./channels)
- [AppServer Protocol](../protocols/appserver-protocol)
