# DotCraft Python SDK — Architecture

This document explains how the SDK works internally. It is intended for contributors and adapter authors who want to understand the design rationale, not just the API surface.

---

## Table of Contents

- [1. Wire Protocol Overview](#1-wire-protocol-overview)
- [2. Transport Layer](#2-transport-layer)
- [3. Message Loop and Dispatch](#3-message-loop-and-dispatch)
- [4. ChannelAdapter Pattern](#4-channeladapter-pattern)
- [5. Approval Flow](#5-approval-flow)
- [6. Thread Management and Serialization](#6-thread-management-and-serialization)

---

## 1. Wire Protocol Overview

DotCraft uses **JSON-RPC 2.0** as its wire protocol. The full specification is at [specs/protocols/appserver-protocol.md](../../specs/protocols/appserver-protocol.md).

Three message kinds:

| Kind | Has `id` | Has `method` | Direction |
|------|----------|--------------|-----------|
| Request | yes | yes | either |
| Response | yes | no | reply to a request |
| Notification | no | yes | either |

For an external channel adapter, the relevant flows are:

```
Adapter                              DotCraft (AppServer)
  |  initialize (request)               |
  |------------------------------------>|
  |  (response)                         |
  |<------------------------------------|
  |  initialized (notification)         |
  |------------------------------------>|
  |                                     |
  |  thread/start (request)             |
  |------------------------------------>|
  |  (response: thread object)          |
  |<------------------------------------|
  |                                     |
  |  turn/start (request)               |
  |------------------------------------>|
  |  (response: turn object)            |
  |<------------------------------------|
  |                                     |
  |  item/agentMessage/delta (notif)    |   ← server pushes events
  |<------------------------------------|
  |  turn/completed (notif)             |
  |<------------------------------------|
  |                                     |
  |  item/approval/request (request)    |   ← server-initiated request
  |<------------------------------------|
  |  (response: { decision: "accept" }) |   ← adapter replies
  |------------------------------------>|
  |                                     |
  |  ext/channel/deliver (request)      |   ← server-initiated request
  |<------------------------------------|
  |  (response: { delivered: true })    |
  |------------------------------------>|
```

**External channel adapter extensions** (from [specs/protocols/external-channel-adapter.md](../../specs/protocols/external-channel-adapter.md)):

- The `initialize` request includes a `channelAdapter` capability object:
  ```json
  {
    "capabilities": {
      "channelAdapter": {
        "channelName": "telegram",
        "deliverySupport": true,
        "deliveryCapabilities": {
          "structuredDelivery": true
        },
        "channelTools": [
          {
            "name": "TelegramSendDocumentToCurrentChat",
            "description": "Send a document to the current Telegram chat.",
            "requiresChatContext": true,
            "display": {
              "icon": "📎",
              "title": "Send document to current Telegram chat"
            },
            "inputSchema": {
              "type": "object",
              "properties": {
                "fileName": { "type": "string" }
              },
              "required": ["fileName"]
            }
          }
        ]
      }
    }
  }
  ```
- The server routes the connection to `ExternalChannelHost` instead of a regular session client.
- `ext/channel/deliver`, `ext/channel/send`, `ext/channel/toolCall`, and `ext/channel/heartbeat` are server-to-adapter requests (the server sends them, the adapter must respond).
- Channel tool names should use PascalCase, and adapters should prefer declaring icons via `channelTools[].display.icon`.

---

## 2. Transport Layer

The transport abstraction (`ITransport`) provides three async operations:

```python
class ITransport:
    async def read_message(self) -> dict: ...   # blocks until next message
    async def write_message(self, msg: dict): ...
    async def close(self): ...
```

### StdioTransport

Used when DotCraft spawns the adapter as a subprocess (`transport: "subprocess"` in config). The adapter reads from `sys.stdin` (binary mode) and writes to `sys.stdout` (binary mode). Each JSON-RPC message occupies exactly one line (newline-delimited JSON, JSONL).

```
DotCraft → adapter stdin:   {"jsonrpc":"2.0","id":1,"method":"initialize",...}\n
adapter → DotCraft stdout:  {"jsonrpc":"2.0","id":1,"result":{...}}\n
```

Diagnostic output from the adapter goes to `sys.stderr` — DotCraft forwards stderr to its own diagnostic log stream.

### WebSocketTransport

Used when the adapter connects independently (`transport: "websocket"` in config). Each JSON-RPC message is a single WebSocket text frame. The transport manages:

- Initial HTTP upgrade to `ws://host:port/ws?token=...`
- Frame-level read/write (`recv()` / `send()`)
- Reconnection with exponential backoff (configurable `reconnect_max_delay`)

On reconnection, the adapter must redo the full `initialize` / `initialized` handshake because the server treats each WebSocket connection as a fresh session.

---

## 3. Message Loop and Dispatch

`DotCraftClient` runs a single background reader task (`_reader_loop`) that continuously reads messages from the transport and dispatches them:

```
┌─────────────────────────────────────────────────────┐
│  _reader_loop (background asyncio Task)             │
│                                                     │
│  read message                                       │
│     │                                               │
│     ├─ Is response (has id, no method)?             │
│     │    └─ Resolve pending Future in _pending      │
│     │                                               │
│     ├─ Is notification (no id, has method)?         │
│     │    └─ Call registered handlers in _handlers   │
│     │                                               │
│     └─ Is server request (has id AND method)?       │
│          └─ Call registered handler in              │
│             _request_handlers, then send response   │
└─────────────────────────────────────────────────────┘
```

### Request / Response correlation

When the adapter calls `client.thread_start(...)`, the client:

1. Assigns a unique integer `id`.
2. Creates an `asyncio.Future` and stores it in `_pending[id]`.
3. Sends the JSON-RPC request.
4. `await`s the Future.

When `_reader_loop` sees a response with a matching `id`, it resolves the Future with the result (or raises an exception for errors).

### Notification handlers

Registered with `client.on(method, callback)`. Multiple handlers can be registered for the same method. Handlers are called as `asyncio` tasks to avoid blocking the reader loop.

`client.stream_events(thread_id)` is implemented as an async generator backed by an `asyncio.Queue`. It registers a temporary handler for all `thread/*`, `turn/*`, `item/*` notifications filtered by `threadId`, yields events, and unregisters when the generator is closed or `turn/completed`/`turn/failed`/`turn/cancelled` is received.

### Server-initiated requests

`item/approval/request`, `ext/channel/deliver`, `ext/channel/send`, and `ext/channel/toolCall` are JSON-RPC requests sent by the *server* to the *adapter*. They have both `id` and `method`. The reader loop detects these, calls the appropriate registered handler, and sends back the JSON-RPC response.

```python
# Server sends:
{"jsonrpc":"2.0","id":100,"method":"item/approval/request","params":{...}}

# Adapter handler returns "accept", reader loop sends:
{"jsonrpc":"2.0","id":100,"result":{"decision":"accept"}}
```

---

## 4. ChannelAdapter Pattern

`ChannelAdapter` is a higher-level base class that wraps `DotCraftClient` and implements the full behavioral contract from [specs/protocols/external-channel-adapter.md §10](../../specs/protocols/external-channel-adapter.md):

```
Platform Events                 ChannelAdapter                DotCraftClient
      │                               │                              │
 on message from user                 │                              │
      │──────────────────────────────►│                              │
      │                         handle_message()                     │
      │                               │── thread_list()─────────────►│
      │                               │◄─────────────────────────────│
      │                               │   (find existing thread)     │
      │                               │── turn_start()──────────────►│
      │                               │◄─────────────────────────────│
      │                               │   (stream events)            │
      │                               │◄── item/agentMessage/delta ──│
      │                   accumulate reply                            │
      │                               │◄── turn/completed ───────────│
      │                     on_deliver called                         │
      │◄── send final message ────────│                              │
      │                               │                              │
 approval needed                      │                              │
      │                               │◄── item/approval/request ────│
      │                    on_approval_request called                 │
      │◄── show platform UI ──────────│                              │
      │──── user responds ───────────►│                              │
      │                         send JSON-RPC response               │
      │                               │──── response ───────────────►│
      │                               │                              │
 ext/channel/deliver                  │                              │
      │                               │◄── ext/channel/deliver ──────│
      │                     on_deliver called                         │
      │◄── send message ──────────────│                              │
      │                               │◄── ext/channel/send ─────────│
      │                     on_send called                            │
      │◄── send structured payload ───│                              │
      │                               │◄── ext/channel/toolCall ─────│
      │                   on_tool_call called                         │
      │◄── perform channel action ────│                              │
```

Handshake-to-hook mapping:

- `get_delivery_capabilities()` populates `initialize.capabilities.channelAdapter.deliveryCapabilities`
- `get_channel_tools()` populates `initialize.capabilities.channelAdapter.channelTools`
- `on_deliver()` handles `ext/channel/deliver`
- `on_send()` handles `ext/channel/send`
- `on_tool_call()` handles `ext/channel/toolCall`

### Thread-per-identity mapping

Each (channel_name, user_id, channel_context) triple maps to exactly one DotCraft thread. `ChannelAdapter` maintains a `_thread_map` dictionary:

```python
_thread_map: dict[str, str]  # identity_key -> thread_id
```

When `handle_message()` is called:

1. Compute `identity_key = f"{user_id}:{channel_context}"`.
2. Look up `_thread_map`. If found, use that `thread_id`.
3. If not found, call `thread/list` to find an existing active thread for this identity.
4. If still not found, call `thread/start` to create a new thread.
5. Store `thread_id` in `_thread_map`.

### Per-thread message serialization

The spec (§10.2) requires that the adapter not call `turn/start` on a thread that already has a running turn. `ChannelAdapter` enforces this with a per-thread `asyncio.Lock`:

```python
_thread_locks: dict[str, asyncio.Lock]  # thread_id -> lock
```

If a message arrives while a turn is running on its thread, the new message waits for the lock. When the running turn completes, the lock is released and the next message proceeds. This serializes concurrent messages from the same chat without dropping them.

---

## 5. Approval Flow

The approval flow is bidirectional: the server sends a JSON-RPC *request* mid-stream, and the adapter must respond before the agent can continue.

```
1. Server sends:  item/approval/request (id=100, params={...})
2. SDK reader loop: detects id + method, calls approval handler
3. Adapter: presents platform-native UI (e.g. Telegram inline keyboard)
4. User responds on platform
5. Adapter: calls send_approval_response(request_id=100, decision="accept")
6. SDK: sends {"jsonrpc":"2.0","id":100,"result":{"decision":"accept"}}
7. Server: continues agent execution
```

The approval handler registered via `ChannelAdapter.on_approval_request` is an async function. The SDK suspends the reader loop for that request ID until the handler returns. If the server's approval timeout fires before the adapter responds, the turn fails with error `-32020`.

Multiple approvals on different threads can be in flight simultaneously — each is tracked independently by its JSON-RPC request `id`.

---

## 6. Thread Management and Serialization

### Streaming reply accumulation

`ChannelAdapter` accumulates `item/agentMessage/delta` events into a buffer per turn:

```python
_reply_buffer: dict[str, list[str]]  # turn_id -> delta chunks
```

On `turn/completed`, the full accumulated text is sent to the platform via `on_deliver`. This avoids sending many small messages and allows the adapter to apply formatting (markdown conversion, message splitting for long replies) on the final assembled text.

Adapters can override `on_turn_completed(thread_id, turn_id, reply_text)` to customize delivery behavior, or opt into streaming delivery by sending each delta immediately.

### Workspace path

When `ChannelAdapter` calls `thread/start`, it uses the `workspace_path` from:

1. The `workspace_path` argument to `handle_message()` if provided.
2. The `DEFAULT_WORKSPACE_PATH` class attribute (defaults to empty string). The DotCraft AppServer then substitutes the host process workspace root when `identity.workspacePath` is omitted or empty.

