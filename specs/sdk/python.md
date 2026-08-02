# DotCraft Python SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Living |
| **Date** | 2026-08-01 |
| **Related Specs** | [Unified SDK Specification](sdk.md), [AppServer Protocol](../protocols/appserver-protocol.md), [AppServer Protocol Contracts and SDK Generation](protocol-contract-generation.md), [Hub Architecture](../architecture/hub-architecture.md), [App Binding](../protocols/app-binding.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Session Core](../architecture/session-core.md), [TypeScript SDK Binding](typescript.md), [.NET SDK Binding](dotnet.md) |

Purpose: Define the Python binding — package identity, runtime baseline, full general-purpose client surface, channel adapter, and compatibility strategy — for the DotCraft Python SDK.

Shared SDK behavior is defined by [Unified SDK Specification](sdk.md). This language binding adds Python-specific package structure and async idioms without redefining shared SDK semantics.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Design Principles](#2-design-principles)
- [3. Current Implementation Snapshot](#3-current-implementation-snapshot)
- [4. Package Contract](#4-package-contract)
- [5. Runtime Requirements](#5-runtime-requirements)
- [6. Connection Model](#6-connection-model)
- [7. Hub Client](#7-hub-client)
- [8. Wire Client](#8-wire-client)
- [9. High-Level Application API](#9-high-level-application-api)
- [10. Thread API](#10-thread-api)
- [11. Run API](#11-run-api)
- [12. Input Model](#12-input-model)
- [13. Streaming Event Model](#13-streaming-event-model)
- [14. Callback Capabilities](#14-callback-capabilities)
- [15. App Binding API](#15-app-binding-api)
- [16. Channel Adapter](#16-channel-adapter)
- [17. Error Model](#17-error-model)
- [18. Documentation and Examples](#18-documentation-and-examples)
- [19. Testing and Conformance](#19-testing-and-conformance)
- [20. Security](#20-security)
- [21. Versioning and Compatibility](#21-versioning-and-compatibility)
- [22. Acceptance Contract](#22-acceptance-contract)
- [23. Future Work](#23-future-work)

---

## 1. Scope

### 1.1 What This Spec Defines

This specification defines the Python SDK that application developers, channel adapter authors, and advanced protocol clients use to integrate with DotCraft.

It defines:

- The package identity and public module surface.
- The Python runtime baseline.
- The local Hub-managed connection flow.
- The remote AppServer WebSocket connection flow.
- The low-level AppServer JSON-RPC client surface.
- The high-level `DotCraft` / `Thread` / run API.
- Streaming event normalization.
- Approval, user-input, and runtime dynamic tool callback contracts.
- App Binding helpers.
- The channel adapter surface.
- Documentation, examples, testing, security, and compatibility requirements.

### 1.2 What This Spec Does Not Define

This specification does not define:

- The AppServer JSON-RPC wire protocol. See [AppServer Protocol](../protocols/appserver-protocol.md).
- Hub HTTP endpoint semantics. See [Hub Architecture](../architecture/hub-architecture.md).
- App Binding product semantics. See [App Binding](../protocols/app-binding.md).
- External channel wire extensions. See [External Channel Adapter](../protocols/external-channel-adapter.md).
- Session persistence, turn lifecycle semantics, item payload semantics, or agent execution internals. See [Session Core](../architecture/session-core.md).
- A public PyPI publishing process. The SDK may remain repository-local until a publishing phase is approved.

### 1.3 Primary Audiences

| Audience | Need | SDK Surface |
|----------|------|-------------|
| Application developers | Connect to a local or remote DotCraft AppServer and run agent work against persistent threads from Python. | `dotcraft` |
| Channel authors | Build external channels that bridge messaging platforms to DotCraft. | `dotcraft` channel adapter |
| Advanced protocol clients | Access raw AppServer JSON-RPC methods and notifications. | `dotcraft` wire client |
| Native app authors | Complete principal handoffs and activate binding-scoped MCP sessions from Python. | `dotcraft` App Binding helpers |

---

## 2. Design Principles

The Python binding follows the shared design principles of [Unified SDK Specification §2](sdk.md#2-design-principles). The points below are Python-specific applications of those principles.

### 2.1 Parallel Surface, Idiomatic Casing

The Python SDK exposes the same entry points, verbs, event model, and callbacks as the TypeScript and .NET SDKs, using Python casing and async idioms. `DotCraft.connect_local()`, `thread.run()`, `thread.run_streamed()`, `approval_handler`, and `dotcraft.app_bindings` are the Python spellings of the canonical surface in [Unified SDK Specification §2.1](sdk.md#21-parallel-surface-idiomatic-casing).

### 2.2 Hub Is the Default Local Bootstrap

`DotCraft.connect_local()` discovers or starts the local Hub and ensures a workspace AppServer, exactly as the TypeScript and .NET local connectors do. Python applications must not need to allocate AppServer ports or manage workspace runtimes manually.

### 2.3 High-Level First, Raw Escape Hatch Always Available

The primary developer experience is the high-level `DotCraft` / `Thread` API. A public `request()` escape hatch on the high-level client and the wire client lets advanced callers reach AppServer methods that the SDK does not yet wrap.

### 2.4 Thread Is the Core User Concept

`Thread` is an active handle, not a passive dataclass. Run, enqueue, interrupt, subscribe, mode, archive, and delete operations live on the thread handle returned by the thread manager.

### 2.5 Streaming Must Be Structured

`run_streamed()` yields normalized `RunEvent` objects and retains the raw JSON-RPC message on each event for advanced rendering or diagnostics.

### 2.6 Channel Adapter Logic Is Shared

The Python `ChannelAdapter` remains a first-class surface and reuses the SDK's thread resolution, message queueing, stream reduction, and dispatch building blocks rather than reimplementing them per platform.

### 2.7 Protocol Changes Are Spec-First

If the Python implementation discovers a required change to AppServer Protocol, Hub Architecture, App Binding, External Channel Adapter, or Session Core, the owning protocol spec is updated before server behavior or SDK wrappers are treated as stable.

---

## 3. Current Implementation Snapshot

The canonical and only package is `dotcraft` (`sdk/python/dotcraft/`). Its public surface is split into generated contracts, the pure `DotCraftWireClient`, the business-oriented `DotCraftAppServerClient`, the `DotCraft` / active `Thread` API, Hub helpers, App Binding helpers, and the channel adapter.

Current surface:

| Area | Current symbol | Status |
|------|----------------|--------|
| Transports | `StdioTransport`, `WebSocketTransport` | Present |
| Wire client | `DotCraftWireClient(transport)` with typed RPC mixin and explicit raw APIs | Present, protocol-only |
| AppServer client | `DotCraftAppServerClient(transport)` with Thread, Turn, command, MCP, event stream, and Dynamic Tool operations | Present, high-level |
| Notifications | Generated registry plus explicit raw handlers | Present |
| Approval callback | `approval_handler` on high-level options | Present |
| Channel adapter | `ChannelAdapter` | Present |
| Input parts | `text_part`, `image_data_url_part`, `local_image_part` | Present |
| Reply merge helpers | `turn_reply.merge_reply_text_from_delta_and_snapshot` | Integrated by Run and Channel profiles |

---

## 4. Package Contract

### 4.1 Package Name

The canonical Python SDK package is:

```text
dotcraft         # distribution and import name
```

`dotcraft` is the only supported distribution and import root. No compatibility package or re-export package exists.

### 4.2 Public Module Surface

The package exposes a curated public API through `dotcraft/__init__.py`. Logical groupings:

| Group | Public symbols |
|-------|----------------|
| High-level client | `DotCraft`, `Thread`, `ThreadManager`, `RunResult`, `RunEvent` |
| AppServer client | `DotCraftAppServerClient` business-oriented protocol helpers |
| Connection options | `LocalOptions`, `RemoteOptions` |
| Contracts | `dotcraft.contracts` Pydantic models, registries, method groups, protocol info |
| Wire client | `dotcraft.wire.DotCraftWireClient`, `JsonRpcMessage` |
| Transports | `Transport`, `StdioTransport`, `WebSocketTransport` |
| Hub | `HubClient`, `HubLockInfo`, `HubError` |
| App Binding | `AppBindingManager`, `AppBindingHandoff`, `app_binding_tool_error`, `APP_BINDING_ERROR_CODES` |
| Channel adapter | `ChannelAdapter` |
| Input parts | `text_part`, `image_data_url_part`, `local_image_part`, `skill_ref_part`, `command_ref_part`, `file_ref_part` |
| Constants | `DECISION_ACCEPT`, `DECISION_ACCEPT_FOR_SESSION`, `DECISION_ACCEPT_ALWAYS`, `DECISION_DECLINE`, `DECISION_CANCEL`, `ERR_*` |
| Errors | `DotCraftError`, `TransportError`, `TransportClosed`, `InitializationError`, `TurnInProgressError`, `ThreadNotFoundError`, `ThreadNotActiveError`, `TurnFailedError`, `TurnCancelledError`, `ApprovalTimeoutError` |
| Version | `__version__`, `sdk_contract_version` |

The low-level wire client and transports remain importable for advanced clients. The high-level `DotCraft` API is the default surface.

---

## 5. Runtime Requirements

### 5.1 Python Version

The SDK targets Python 3.10 or newer.

### 5.2 Async Model

The SDK is asyncio-native. All connection, thread, turn, run, and callback methods are coroutines. Synchronous convenience wrappers are out of scope for this spec.

### 5.3 Dependencies

The SDK may depend on:

- `websockets` for the WebSocket transport.
- Python standard library for asyncio, subprocess, JSON, HTTP (`urllib`/`http.client`), and path handling.

The high-level SDK avoids heavy framework dependencies. Channel adapter subclasses may depend on platform SDKs.

### 5.4 Single Source of Version Truth

`__version__` in `dotcraft/__init__.py` and the `pyproject.toml` version must match. CI or packaging validation should fail when they diverge.

---

## 6. Connection Model

### 6.1 Local Mode

`DotCraft.connect_local()` establishes a Hub-managed local connection.

```python
dotcraft = await DotCraft.connect_local(LocalOptions(workspace_path="/path/to/workspace"))
```

Options:

```python
@dataclass
class LocalOptions:
    workspace_path: str
    client_name: str = "dotcraft-python"
    client_version: str = "..."
    client_title: str | None = None
    executable: str | None = None
    hub_startup_timeout: float = 30.0
    approval_handler: ApprovalHandler | None = None
    user_input_handler: UserInputHandler | None = None
    capabilities: dict | None = None
```

Behavior mirrors [Unified SDK Specification §3.2](sdk.md#32-hub-bootstrap-profile): validate `workspace_path`; discover a live Hub from the user's Hub lock; start `dotcraft hub` if needed; wait for readiness; `POST /v1/appservers/ensure`; read `endpoints.appServerWebSocket`; connect; `initialize`; `initialized`.

`connect_local()` must not stop the Hub-managed AppServer when the SDK client closes.

`DotCraft.connect_local_chat()` follows the same flow after resolving and initializing the default Chat workspace (`~/.craft/workspaces/chats`). It calls the existing Hub AppServer ensure endpoint with that concrete `workspace_path`; it must not use an empty path or a separate Hub endpoint.

### 6.2 Remote Mode

`DotCraft.connect_remote()` connects directly to an AppServer WebSocket endpoint.

```python
dotcraft = await DotCraft.connect_remote(RemoteOptions(url="ws://127.0.0.1:PORT/...", token=None))
```

When `token` is provided separately, the transport appends it as the `token` query parameter unless the URL already carries one.

### 6.3 Custom Transport Mode

The low-level `DotCraftClient(transport)` remains available for tests, in-memory conformance fixtures, and embedded hosts that supply their own `Transport`.

### 6.4 Initialize Capabilities

High-level clients send:

```json
{
  "approvalSupport": true,
  "requestUserInputSupport": true,
  "streamingSupport": true,
  "configChange": true
}
```

If `approvalSupport` or `requestUserInputSupport` is explicitly advertised, the matching handler must be configured before initialization or connection fails with a stable configuration error. Channel adapters additionally send `capabilities.channelAdapter`.

---

## 7. Hub Client

### 7.1 Responsibilities

`HubClient` provides Hub lock path resolution, lock JSON parsing, process liveness checks, loopback URL validation, status probing, Hub startup, live discovery, workspace lookup, AppServer ensure/restart/stop/list, status, SSE events, and Hub shutdown. It preserves complete runtime-tool fields and exposes structured `code`, `message`, and `details` errors. Behavior follows [Unified SDK Specification §3.2](sdk.md#32-hub-bootstrap-profile).

### 7.2 Hub Lock

The SDK reads `~/.craft/hub/hub.lock` and trusts it only after it parses, the pid is live, `apiBaseUrl` is loopback HTTP, and `GET /v1/status` succeeds.

```python
@dataclass
class HubLockInfo:
    pid: int
    api_base_url: str
    token: str
    started_at: str | None = None
    version: str | None = None
    binary_path: str | None = None
```

### 7.3 Hub Startup

When Hub is not live, the SDK starts `dotcraft hub` (or `dotnet <executable> hub` when `executable` is a `.dll`). The child process is detached and not connected to the parent stdio streams.

### 7.4 AppServer Ensure

`HubClient.ensure_app_server(workspace_path, ...)` calls `POST /v1/appservers/ensure` and requires `endpoints.appServerWebSocket` in the response. A missing endpoint raises `HubError`.

`HubClient.ensure_default_chat_app_server(...)` resolves and initializes the default Chat workspace, then calls `ensure_app_server()` with that concrete path.

---

## 8. Wire Client

### 8.1 Responsibilities

`DotCraftWireClient` handles request id generation, JSON-RPC serialization, response correlation, error conversion, typed and raw notification/server-request dispatch, initialization gating, timeout, opt-in reconnect, transport close handling, and graceful shutdown. It contains no Thread, Run, callback, or Channel business policy.

### 8.2 Transport Interface

```python
class Transport(abc.ABC):
    async def read_message(self) -> dict | None: ...
    async def write_message(self, message: dict) -> None: ...
    async def close(self) -> None: ...
```

`StdioTransport` uses newline-delimited JSON; `WebSocketTransport` uses one JSON-RPC message per text frame and appends a bearer token query parameter when provided separately. A transport represents one connection; `DotCraftWireClient` owns optional reconnect and protocol reinitialization.

### 8.3 Raw Request Escape Hatch

Generated mixins expose catalog-typed known methods. Unknown extensions use explicit raw APIs:

```python
await client.request_raw("ext/vendor/method", params)
await client.notify_raw("ext/vendor/event", params)
```

The high-level `DotCraft` client exposes the same explicit raw escape hatch. Typed methods do not accept an arbitrary method string.

### 8.4 Typed Wire Methods

The generated wire mixin provides typed wrappers for every cataloged client request and notification. Unknown methods remain reachable through `request_raw()` and `notify_raw()` only.

---

## 9. High-Level Application API

### 9.1 `DotCraft`

`DotCraft` represents one initialized AppServer connection.

```python
class DotCraft:
    @classmethod
    async def connect_local(cls, options: LocalOptions) -> "DotCraft": ...
    @classmethod
    async def connect_local_chat(cls, options: LocalChatOptions | None = None) -> "DotCraft": ...
    @classmethod
    async def connect_remote(cls, options: RemoteOptions) -> "DotCraft": ...

    @property
    def server_info(self) -> ServerInfo: ...
    @property
    def capabilities(self) -> ServerCapabilities: ...
    @property
    def threads(self) -> ThreadManager: ...
    @property
    def app_bindings(self) -> AppBindingManager: ...

    async def request(self, method: str, params=None): ...
    def on(self, method: str, handler) -> Unsubscribe: ...
    async def close(self) -> None: ...
```

### 9.2 Thread Manager

```python
class ThreadManager:
    async def get_or_create(self, *, user_id=None, channel_name=None, channel_context=None, **opts) -> Thread: ...
    async def start(self, **opts) -> Thread: ...
    async def resume(self, thread_id: str, **opts) -> Thread: ...
    async def list(self, **opts) -> list[ThreadInfo]: ...
    async def read(self, thread_id: str, **opts) -> ThreadInfo: ...
```

`get_or_create()` reuses an active or paused thread for the identity before creating a new one; paused threads are resumed before use.

### 9.3 Default Identity

| Field | Default |
|-------|---------|
| `channel_name` | `sdk` |
| `user_id` | current OS username when available, otherwise `local-user` |
| `channel_context` | omitted |

---

## 10. Thread API

`Thread` is an active server-backed handle.

```python
class Thread:
    id: str
    identity: SessionIdentity

    def snapshot(self) -> ThreadInfo: ...
    async def refresh(self, **opts) -> ThreadInfo: ...
    async def subscribe(self, **opts) -> None: ...
    async def unsubscribe(self) -> None: ...

    async def run(self, input, **opts) -> RunResult: ...
    def run_streamed(self, input, **opts) -> AsyncIterator[RunEvent]: ...
    async def enqueue(self, input, **opts) -> QueuedInput: ...
    async def interrupt(self, turn_id: str) -> None: ...

    async def set_mode(self, mode: str) -> None: ...
    async def archive(self) -> None: ...
    async def delete(self) -> None: ...

    def on_tool_call(self, namespace, name, handler) -> Unsubscribe: ...
```

The handle caches the latest thread snapshot and updates it on lifecycle notifications; `refresh()` forces a re-read. `run_streamed()` must avoid duplicate event delivery when an active subscription already exists for the thread, per the AppServer at-most-once rule.

---

## 11. Run API

### 11.1 Run Options

```python
async def run(self, input, *, sender=None, collect_raw_events=False,
              abort=None, enqueue_if_busy=False) -> RunResult: ...
```

`enqueue_if_busy` is explicit. When omitted or false, a server `TurnInProgress` response raises `TurnInProgressError`.

### 11.2 Run Result

```python
@dataclass
class RunResult:
    thread: ThreadInfo
    turn: TurnInfo
    text: str
    items: list
    usage: dict | None = None
    raw_events: list | None = None
```

`text` is the canonical merged assistant reply, using the same delta/snapshot merge as the channel stream reducer so callers never receive duplicated text.

### 11.3 `run()`

`run()` subscribes or prepares event capture before `turn/start`, starts the turn, consumes events until a terminal event, and returns `RunResult`. Terminal events are `turn/completed`, `turn/failed`, `turn/cancelled`. `turn/failed` raises `TurnFailedError` and `turn/cancelled` raises `TurnCancelledError` unless the caller opts into returning failed results.

### 11.4 `run_streamed()`

`run_streamed()` prepares event capture before `turn/start`, starts the turn, yields normalized events as they arrive, yields one terminal event, and unregisters client-side handlers if iteration stops early. It does not interrupt the server turn on early stop unless an abort is supplied or `interrupt()` is called.

### 11.5 Abort Semantics

When the supplied `abort` (an `asyncio.Event` or cancellation token) fires after `turn/start` succeeds, the SDK calls `turn/interrupt`. If it fires before `turn/start`, the SDK raises without sending a request.

---

## 12. Input Model

### 12.1 Accepted Input

`run()`, `run_streamed()`, and `enqueue()` accept a plain `str`, a list of input parts, or a `{"input": [...], "sender": {...}}` mapping.

### 12.2 Input Part Helpers

```python
text_part(text)
image_data_url_part(data_url)
local_image_part(path, *, mime_type=None)
skill_ref_part(name)
command_ref_part(raw_text)
file_ref_part(path, *, display_path=None)
```

Helpers produce the AppServer wire shape using camelCase fields.

### 12.3 Slash Commands

The high-level application API does not auto-interpret leading `/` text. Applications that want command semantics call `command/execute` through `request()` or the wire client. Channel adapters keep their existing slash-command behavior.

---

## 13. Streaming Event Model

### 13.1 Normalized Event Shape

```python
@dataclass
class RunEvent:
    type: str
    thread_id: str
    turn_id: str | None
    raw: JsonRpcMessage
    # optional payloads: delta, item, turn, result, error, queued_input
```

### 13.2 Event Types

The Python SDK normalizes the same notification set as [TypeScript SDK §13.2](typescript.md#132-event-types), with snake_case `type` values: `thread_started`, `thread_resumed`, `thread_status_changed`, `thread_runtime_changed`, `queue_updated`, `turn_started`, `item_started`, `item_completed`, `agent_message_delta`, `reasoning_delta`, `tool_arguments_delta`, `approval_resolved`, `usage_delta`, `subagent_progress`, `plan_updated`, `system_event`, `completed`, `failed`, `cancelled`, and `raw` for any subscribed notification not otherwise normalized.

### 13.3 Text Merging

The SDK accumulates agent-message deltas, prefers final snapshots when they are at least as complete as deltas, and never duplicates overlapping text. The high-level run and the channel adapter share the same reducer logic (`merge_reply_text_from_delta_and_snapshot`).

---

## 14. Callback Capabilities

### 14.1 Server-Initiated Request Dispatch

The SDK answers `item/approval/request`, `item/tool/call`, `item/tool/requestUserInput`, and `ext/channel/heartbeat`. Channel-specific requests are handled by the channel adapter.

### 14.2 Approval Handler

```python
ApprovalHandler = Callable[[ApprovalRequest], Awaitable[str] | str]
```

Allowed decisions: `DECISION_ACCEPT`, `DECISION_ACCEPT_FOR_SESSION`, `DECISION_ACCEPT_ALWAYS`, `DECISION_DECLINE`, `DECISION_CANCEL`. Initialization fails before connecting when approval support is declared without an approval handler; the Wire layer never invents a decision.

### 14.3 Dynamic Tool Handler

Runtime dynamic tools are declared on `thread.start`/`thread.resume` dynamic-tool options, and per-thread handlers register through `thread.on_tool_call()`. SDK-local handlers are not sent over the wire.

```python
DynamicToolHandler = Callable[[DynamicToolCall], Awaitable[DynamicToolResult] | DynamicToolResult]
```

Missing handler returns `UnsupportedTool`; a raised handler returns `AdapterToolCallFailed`. Handlers own argument validation and app-level authorization.

### 14.4 User Input Handler

```python
UserInputHandler = Callable[[UserInputRequest], Awaitable[UserInputResponse] | UserInputResponse]
```

The high-level client registers this server-request method only when a handler is configured. Without a handler, the Wire layer returns JSON-RPC `-32601`; it never invents an answer.

### 14.5 Heartbeat

The SDK answers `ext/channel/heartbeat` with `{}` when acting as a channel adapter, and may answer it for application clients if the server sends it unexpectedly.

---

## 15. App Binding API

The Python SDK provides the App Binding profile from [Unified SDK Specification §3.5](sdk.md#35-app-binding-profile).

### 15.1 Handoff Parsing

```python
AppBindingHandoff.parse(url, *, expected_scheme=None, expected_app_id=None) -> AppBindingHandoff
```

Result fields: `scheme`, `operation`, `app_id`, `request_id`, `request_token`, `app_server_url`. The accepted query keys are exactly `app`, `request`, `token`, and optional `endpoint`.

### 15.2 App Binding Manager

`dotcraft.app_bindings` exposes connection `start_connection` / `complete_connection` / `authenticate` / `refresh_credential` / `connection_status` / `revoke_connection`, surface `publish_surface` / `resolve_surface`, binding `enable` / `get_binding_request` / `activate` / `rebind` / `confirm_capabilities`, and thread binding list/revoke helpers. Surface publication requires an authenticated app-principal connection; resolution is for trusted AppServer clients.

### 15.3 Keep Alive

A `keep_alive()` coroutine drains control-plane notifications. The binding-scoped MCP session has an independent lifecycle and is not terminated when the control connection disconnects.

### 15.4 Standard Tool Errors

```python
APP_BINDING_ERROR_CODES  # Offline, Expired, Revoked, ScopeDenied, ToolUnavailable, ProtocolViolation
app_binding_tool_error(code, message, structured_result=None) -> DynamicToolResult
```

These match the shared App Binding error shapes used by the TypeScript and .NET SDKs.

---

## 16. Channel Adapter

`ChannelAdapter` remains a first-class Python surface for building external channels. It owns its `DotCraftClient`, performs the channel-adapter initialize handshake, and provides per-identity message queueing, thread resolution/recovery, slash-command routing, turn stream reduction with segment boundaries, and delivery/tool/approval/heartbeat dispatch.

Subclass hooks (abstract or overridable): `on_deliver`, `on_approval_request`, `on_send`, `on_tool_call`, `on_turn_completed`, `on_turn_failed`, `on_turn_cancelled`, `on_segment_completed`, `get_delivery_capabilities`, `get_channel_tools`. Channel identity key is `{user_id}:{channel_context}`; `SessionIdentity.channel_name` must match the adapter's declared channel name. The refactor must preserve existing adapter behavior, including the Telegram reference adapter.

---

## 17. Error Model

All SDK errors derive from `DotCraftError`. Required typed errors mirror [Unified SDK Specification §4.6](sdk.md#46-error-model):

| Error | Condition |
|-------|-----------|
| `DotCraftError` | Base; carries `code`, `message`, optional `data`. |
| `TransportError` | Transport read/write/open failure. |
| `TransportClosed` | Transport closed with pending reads or writes. |
| `InitializationError` | Initialize handshake failed. |
| `TurnInProgressError` | Server rejected turn start due to an active turn. |
| `ThreadNotFoundError` | Server reports thread missing. |
| `ThreadNotActiveError` | Server reports thread cannot accept turns. |
| `TurnFailedError` | Agent execution failed after `turn/start` succeeded. |
| `TurnCancelledError` | Turn was cancelled before successful completion. |
| `ApprovalTimeoutError` | Server reports approval timeout. |

Error `code` strings are stable API; message text may evolve.

---

## 18. Documentation and Examples

### 18.1 Documentation Locations

Python SDK docs live in VitePress and must be bilingual:

- `docs/developing/sdks/python.md` (English)
- `docs/zh/developing/sdks/python.md` (Chinese)

The page follows the shared SDK doc skeleton (see [§19.3](#193-documentation-validation)) so the three language pages stay parallel.

### 18.2 Examples

At least one runnable example demonstrates local and remote connect, `run_streamed()`, an approval handler, a dynamic tool handler, a user-input handler, and clean shutdown. The Telegram reference adapter remains as the channel example.

---

## 19. Testing and Conformance

### 19.1 Required Tests

- Hub lock parsing, dead-lock rejection, and loopback URL validation.
- stdio and WebSocket transport framing.
- JSON-RPC response correlation and error conversion.
- notification registration and server-request dispatch.
- initialize handshake and `request()` escape hatch.
- `run()` final result extraction and `run_streamed()` normalized event order.
- delta/snapshot text merge.
- explicit `turn/enqueue` and `TurnInProgressError`.
- approval, dynamic tool, and user-input callbacks.
- App Binding handoff parse, principal authentication, activation, and tool error shape.
- channel adapter queueing, thread resolution, and dispatch (existing coverage preserved).

### 19.2 Conformance Alignment

Run-profile event order, text merge, failure, cancellation, and abort tests must assert the same observable behavior as the TypeScript and .NET conformance suites described in [Unified SDK Specification §7](sdk.md#7-testing-and-conformance).

### 19.3 Documentation Validation

```bash
cd docs
npm run build
```

---

## 20. Security

The Python SDK follows [Unified SDK Specification §8](sdk.md#8-security): validate Hub locks before trusting them; treat Hub, AppServer WebSocket, and App Binding handoff tokens as secrets; avoid logging full token-bearing URLs; document explicit approval handlers for production; do not sandbox dynamic tool handlers; and preserve App Binding authority boundaries.

---

## 21. Versioning and Compatibility

### 21.1 SDK Contract Version

The SDK exposes `sdk_contract_version`, aligned with the TypeScript SDK contract version for cross-language conformance.

### 21.2 Protocol Compatibility

The SDK inspects `initialize` server info and capabilities and gates optional methods (dynamic tool rebind, command methods, workspace config, model list, App Binding) on the matching capability flags.

### 21.3 Package Identity

`dotcraft` is the canonical package and no alternate facade or re-export package exists. External package versioning is handled by the release workflow.

### 21.4 Stable and Unstable Surfaces

Stable: the high-level `DotCraft` / `Thread` API, the wire client and transports, the channel adapter hooks, error codes, and input part helpers. Less stable: internal reducer diagnostics and any not-yet-exported helper.

---

## 22. Acceptance Contract

A complete implementation of this specification satisfies:

- `dotcraft` is the sole canonical package.
- Python 3.10 is the documented runtime baseline; `__version__` and `pyproject.toml` agree.
- Local mode discovers or starts Hub and connects to the ensured AppServer.
- Remote mode connects directly to the AppServer WebSocket.
- High-level API exposes `DotCraft`, `ThreadManager`, `Thread`, `run()`, `run_streamed()`, and `enqueue()`.
- Streaming yields normalized `RunEvent` values and preserves raw messages.
- Final run results merge delta and snapshot text without duplication.
- Approval, dynamic tool, and user-input callbacks work.
- Explicit `request_raw()` and `notify_raw()` escape hatches are available on high-level and wire clients.
- App Binding handoff parse, connection, authentication, activation, rebind, confirmation, and tool error shape work.
- The channel adapter preserves existing behavior.
- AppServer Pydantic wire models, notification registry, RPC mixins, method groups, and protocol metadata are generated under `dotcraft/_generated/appserver/`; handwritten transports and high-level APIs retain raw fallbacks.
- Python SDK tests pass.
- Chinese and English Python SDK docs follow the shared skeleton.
- The Unified SDK capability matrix reflects Python's status in the same change.

---

## 23. Future Work

Future amendments may cover:

- Public PyPI publishing and provenance.
- Synchronous convenience wrappers over the async core.
- Pluggable logging and telemetry hooks.
