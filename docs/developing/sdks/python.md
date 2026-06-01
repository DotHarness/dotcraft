# DotCraft Python SDK

A Python client library for the DotCraft AppServer Wire Protocol (JSON-RPC 2.0). Build external channel adapters in Python — Telegram, Discord, Slack, and more — that integrate with DotCraft as first-class channels with full support for thread management, streaming events, and the approval flow.

## Overview

DotCraft exposes a language-neutral JSON-RPC 2.0 wire protocol over stdio and WebSocket that allows out-of-process clients to create threads, submit turns, stream events, and participate in approval flows. This SDK wraps that protocol into a clean Python API.

There are two abstraction levels:

| Class | Use when |
|-------|----------|
| `DotCraftClient` | You want raw access to all wire protocol methods. |
| `ChannelAdapter` | You are building a social channel adapter (Telegram, Discord, etc.). |

**Key capabilities**:

- stdio and WebSocket transports (subprocess and standalone deployment)
- Full thread and turn lifecycle (`thread/start`, `thread/resume`, `turn/start`, `turn/interrupt`, …)
- Streaming event dispatch (`item/agentMessage/delta`, `turn/completed`, …)
- Bidirectional approval flow (`item/approval/request` ↔ JSON-RPC response)
- Delivery requests from server (`ext/channel/deliver`, `ext/channel/send`)
- Runtime channel tool calls from server (`ext/channel/toolCall`)
- Automatic reconnection with exponential backoff (WebSocket mode)

## Installation

```bash
pip install -e sdk/python          # from the DotCraft repo root
# or
pip install dotcraft-wire          # when published to PyPI
```

### Dependencies

```
websockets>=12.0
```

The SDK has no other mandatory runtime dependencies. Install `python-telegram-bot` separately if you are building the Telegram adapter example.

## Quick Start

### Subprocess mode (adapter launched by DotCraft)

When DotCraft runs your adapter as a subprocess, it communicates over stdin/stdout. Your adapter starts, performs the initialize handshake, and waits for messages:

```python
import asyncio
from dotcraft_wire import DotCraftClient, StdioTransport

async def main():
    transport = StdioTransport()
    client = DotCraftClient(transport)

    await client.initialize(
        client_name="my-adapter",
        client_version="1.0.0",
    )

    # Create a thread (omit workspace_path; AppServer uses the host workspace root)
    thread = await client.thread_start(
        channel_name="my-channel",
        user_id="user-123",
    )

    # Submit a turn and stream events
    turn = await client.turn_start(thread.id, [{"type": "text", "text": "Hello!"}])

    async for event in client.stream_events(thread.id):
        if event.method == "item/agentMessage/delta":
            print(event.params["delta"], end="", flush=True)
        elif event.method == "turn/completed":
            break

asyncio.run(main())
```

### WebSocket mode (adapter connects independently)

```python
import asyncio
from dotcraft_wire import DotCraftClient, WebSocketTransport

async def main():
    transport = WebSocketTransport("ws://127.0.0.1:9100/ws")
    client = DotCraftClient(transport)

    await client.connect()
    await client.initialize(client_name="my-adapter", client_version="1.0.0")

    # ... same API as stdio mode

asyncio.run(main())
```

### Building a channel adapter with `ChannelAdapter`

For social platforms, use the `ChannelAdapter` base class. Override the abstract methods to connect platform events to DotCraft:

```python
from dotcraft_wire import ChannelAdapter, StdioTransport

class MyAdapter(ChannelAdapter):
    def __init__(self):
        super().__init__(
            transport=StdioTransport(),
            channel_name="my-channel",
            client_name="my-adapter",
            client_version="1.0.0",
        )

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        """Called when DotCraft asks the adapter to send a message to the platform."""
        print(f"Deliver to {target}: {content}")
        return True

    def get_delivery_capabilities(self) -> dict | None:
        return {
            "structuredDelivery": True,
            "media": {
                "file": {
                    "supportsHostPath": True,
                    "supportsUrl": True,
                    "supportsBase64": True,
                    "supportsCaption": True,
                }
            },
        }

    async def on_send(self, target: str, message: dict, metadata: dict) -> dict:
        kind = str(message.get("kind", ""))
        if kind == "text":
            return await super().on_send(target, message, metadata)
        if kind == "file":
            return {"delivered": True}
        return {
            "delivered": False,
            "errorCode": "UnsupportedDeliveryKind",
            "errorMessage": f"Unsupported kind: {kind}",
        }

    def get_channel_tools(self) -> list[dict] | None:
        return [
            {
                "name": "SendFileToCurrentChat",
                "description": "Send a file to the current chat.",
                "requiresChatContext": True,
                "display": {
                    "icon": "📎",
                    "title": "Send file to current chat",
                },
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "fileName": {"type": "string"},
                    },
                    "required": ["fileName"],
                },
            }
        ]

    async def on_tool_call(self, request: dict) -> dict:
        return {
            "success": True,
            "contentItems": [
                {
                    "type": "text",
                    "text": f"Sent {request['arguments']['fileName']}.",
                }
            ],
        }

    async def on_approval_request(self, request: dict) -> str:
        """Called when the agent needs user approval. Return a decision string."""
        # "accept" | "acceptForSession" | "acceptAlways" | "decline" | "cancel"
        return "accept"

    async def run(self):
        await self.start()
        # Your platform event loop goes here

import asyncio
asyncio.run(MyAdapter().run())
```

## Core API Reference

### `StdioTransport`

Reads from `sys.stdin` and writes to `sys.stdout` using newline-delimited JSON (JSONL). Used when DotCraft spawns your adapter as a subprocess.

```python
from dotcraft_wire import StdioTransport
transport = StdioTransport()
```

### `WebSocketTransport`

Connects to a DotCraft AppServer WebSocket endpoint. Each JSON-RPC message is a single WebSocket text frame.

```python
from dotcraft_wire import WebSocketTransport
transport = WebSocketTransport(
    url="ws://127.0.0.1:9100/ws",
    token="optional-auth-token",         # passed as ?token= query param
    reconnect=True,                       # auto-reconnect on disconnect
    reconnect_max_delay=30.0,
)
```

### `DotCraftClient`

Transport-agnostic JSON-RPC client. Handles request/response correlation, notification dispatch, and server-initiated request handling.

```python
client = DotCraftClient(transport)
```

#### Initialization

```python
result = await client.initialize(
    client_name="my-adapter",
    client_version="1.0.0",
    approval_support=True,
    streaming_support=True,
    opt_out_notifications=[],    # e.g. ["item/reasoning/delta", "subagent/progress"]
)
# result.server_info, result.capabilities
```

#### Thread methods

```python
thread = await client.thread_start(
    channel_name="telegram",
    user_id="12345",
    channel_context="group:67890",   # optional
    display_name="My Thread",        # optional
    history_mode="server",           # "server" | "client"
)

thread = await client.thread_resume(thread_id)
threads = await client.thread_list(channel_name="telegram", user_id="12345")
thread = await client.thread_read(thread_id, include_turns=False)
await client.thread_subscribe(thread_id)
await client.thread_unsubscribe(thread_id)
await client.thread_pause(thread_id)
await client.thread_archive(thread_id)
await client.thread_delete(thread_id)
await client.thread_set_mode(thread_id, mode="agent")
```

#### Turn methods

```python
turn = await client.turn_start(
    thread_id,
    input=[{"type": "text", "text": "Run the tests"}],
    sender={                          # optional, for group chats
        "senderId": "user-456",
        "senderName": "Alice",
    },
)

await client.turn_interrupt(thread_id, turn_id)
```

#### Event streaming

```python
# Subscribe to all events for a thread and iterate:
async for event in client.stream_events(thread_id):
    print(event.method, event.params)

# Or register callbacks:
@client.on("item/agentMessage/delta")
async def on_delta(params):
    print(params["delta"], end="")

@client.on("turn/completed")
async def on_done(params):
    print("\nDone.")
```

#### Approval response

The SDK automatically routes incoming `item/approval/request` server requests to your registered handler:

```python
@client.on_approval_request
async def handle_approval(request_id: str, params: dict) -> str:
    print(f"Approve: {params['operation']}?")
    return "accept"   # or "decline", "cancel", "acceptForSession", "acceptAlways"
```

### `ChannelAdapter`

High-level base class for building social channel adapters. Handles the full Wire Protocol lifecycle so you only implement platform-specific logic.

```python
class ChannelAdapter:
    def __init__(
        self,
        transport,
        channel_name: str,
        client_name: str,
        client_version: str,
    ): ...

    # Override these:
    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool: ...
    async def on_send(self, target: str, message: dict, metadata: dict) -> dict: ...
    async def on_approval_request(self, request: dict) -> str: ...
    def get_delivery_capabilities(self) -> dict | None: ...
    def get_channel_tools(self) -> list[dict] | None: ...
    async def on_tool_call(self, request: dict) -> dict: ...

    # Lifecycle:
    async def start(self): ...   # connect, initialize, start message loop
    async def stop(self): ...    # graceful shutdown

    # Helpers for platform event handlers:
    async def handle_message(
        self,
        user_id: str,
        user_name: str,
        text: str,
        channel_context: str = "",    # group/chat identifier
        workspace_path: str = "",
    ) -> None: ...
    # Finds or creates the thread for this identity, serializes if a turn is running.
```

`ChannelAdapter` handshake mapping:

- `get_delivery_capabilities()` -> `initialize.capabilities.channelAdapter.deliveryCapabilities`
- `get_channel_tools()` -> `initialize.capabilities.channelAdapter.channelTools`
- `on_deliver()` -> `ext/channel/deliver`
- `on_send()` -> `ext/channel/send`
- `on_tool_call()` -> `ext/channel/toolCall`

## Configuration

### Subprocess mode (DotCraft spawns the adapter)

Add an `ExternalChannels` section to DotCraft's `config.json`:

```json
{
  "ExternalChannels": {
    "my-channel": {
      "enabled": true,
      "transport": "subprocess",
      "command": "python",
      "args": ["-m", "my_adapter"],
      "workingDirectory": ".",
      "env": {
        "MY_TOKEN": "secret"
      }
    }
  }
}
```

DotCraft will spawn `python -m my_adapter`, communicate over stdin/stdout, and restart the process if it crashes.

The `ExternalChannels` section only tells DotCraft how to launch or accept the adapter connection. Structured delivery capabilities and `channelTools` are declared by the adapter itself during `initialize`, not in `config.json`.

Use PascalCase for channel tool names. For display metadata, prefer declaring `channelTools[].display.icon` with an emoji string; `display.title` and `display.subtitle` are optional UI hints.

### WebSocket mode (adapter connects independently)

```json
{
  "AppServer": {
    "Mode": "WebSocket",
    "WebSocket": { "Host": "127.0.0.1", "Port": 9100 }
  },
  "ExternalChannels": {
    "my-channel": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

The adapter connects to `ws://127.0.0.1:9100/ws` and presents `channelAdapter.channelName = "my-channel"` during the `initialize` handshake.

## Examples

| Example | Description |
|---------|-------------|
| [examples/telegram/](../channels/python-telegram) | Reference Telegram adapter using long polling, inline keyboard approvals, and delivery support. |

## Further Reading

- [ARCHITECTURE.md](https://github.com/DotHarness/dotcraft/blob/master/sdk/python/ARCHITECTURE.md) — How the SDK works internally.
- [AppServer Protocol](../protocols/appserver-protocol) — JSON-RPC client protocol overview.
