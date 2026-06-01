# DotCraft Python SDK

用于 DotCraft AppServer Wire Protocol（JSON-RPC 2.0）的 Python 客户端库。使用 Python 构建外部渠道适配器——Telegram、Discord、Slack 等——作为 DotCraft 的一等公民渠道，完整支持线程管理、流式事件和审批流。

## 概述

DotCraft 通过 stdio 和 WebSocket 暴露了一套语言无关的 JSON-RPC 2.0 Wire Protocol，允许外部进程创建线程、提交对话轮次、流式接收事件、参与审批流。本 SDK 将该协议封装为简洁的 Python API。

SDK 提供两个抽象层级：

| 类 | 适用场景 |
|----|---------|
| `DotCraftClient` | 需要直接访问所有 Wire Protocol 方法时使用 |
| `ChannelAdapter` | 构建社交渠道适配器（Telegram、Discord 等）时使用 |

**核心能力**：

- stdio 和 WebSocket 传输（子进程模式与独立部署模式）
- 完整的线程和对话轮次生命周期（`thread/start`、`thread/resume`、`turn/start`、`turn/interrupt` 等）
- 流式事件分发（`item/agentMessage/delta`、`turn/completed` 等）
- 双向审批流（`item/approval/request` ↔ JSON-RPC 响应）
- 服务端消息投递请求（`ext/channel/deliver`、`ext/channel/send`）
- 服务端运行时 channel tool 调用（`ext/channel/toolCall`）
- 指数退避自动重连（WebSocket 模式）

## 安装

```bash
pip install -e sdk/python          # 在 DotCraft 仓库根目录执行
# 或
pip install dotcraft-wire          # 发布到 PyPI 后
```

### 依赖

```
websockets>=12.0
```

SDK 本身没有其他强制运行时依赖。如果你在构建 Telegram 适配器示例，请单独安装 `python-telegram-bot`。

## 快速开始

### 子进程模式（由 DotCraft 启动适配器）

当 DotCraft 以子进程方式运行你的适配器时，通过 stdin/stdout 进行通信。适配器启动后执行初始化握手，然后等待消息：

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

    # 创建线程（不传 workspace_path；由 AppServer 使用宿主工作区根目录）
    thread = await client.thread_start(
        channel_name="my-channel",
        user_id="user-123",
    )

    # 提交对话轮次并流式接收事件
    turn = await client.turn_start(thread.id, [{"type": "text", "text": "你好！"}])

    async for event in client.stream_events(thread.id):
        if event.method == "item/agentMessage/delta":
            print(event.params["delta"], end="", flush=True)
        elif event.method == "turn/completed":
            break

asyncio.run(main())
```

### WebSocket 模式（适配器独立连接）

```python
import asyncio
from dotcraft_wire import DotCraftClient, WebSocketTransport

async def main():
    transport = WebSocketTransport("ws://127.0.0.1:9100/ws")
    client = DotCraftClient(transport)

    await client.connect()
    await client.initialize(client_name="my-adapter", client_version="1.0.0")

    # ... 与 stdio 模式 API 相同

asyncio.run(main())
```

### 使用 `ChannelAdapter` 构建渠道适配器

对于社交平台，使用 `ChannelAdapter` 基类。重写抽象方法，将平台事件接入 DotCraft：

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
        """DotCraft 要求适配器向平台发送消息时调用。"""
        print(f"投递到 {target}: {content}")
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
            "errorMessage": f"不支持的类型: {kind}",
        }

    def get_channel_tools(self) -> list[dict] | None:
        return [
            {
                "name": "SendFileToCurrentChat",
                "description": "向当前聊天发送文件。",
                "requiresChatContext": True,
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
                    "text": f"已发送 {request['arguments']['fileName']}。",
                }
            ],
        }

    async def on_approval_request(self, request: dict) -> str:
        """Agent 需要用户审批时调用。返回决策字符串。"""
        # "accept" | "acceptForSession" | "acceptAlways" | "decline" | "cancel"
        return "accept"

    async def run(self):
        await self.start()
        # 在这里运行你的平台事件循环

import asyncio
asyncio.run(MyAdapter().run())
```

## 核心 API 参考

### `StdioTransport`

通过换行分隔的 JSON（JSONL）格式读写 `sys.stdin` / `sys.stdout`。在 DotCraft 以子进程方式启动适配器时使用。

```python
from dotcraft_wire import StdioTransport
transport = StdioTransport()
```

### `WebSocketTransport`

连接到 DotCraft AppServer 的 WebSocket 端点。每条 JSON-RPC 消息对应一个 WebSocket 文本帧。

```python
from dotcraft_wire import WebSocketTransport
transport = WebSocketTransport(
    url="ws://127.0.0.1:9100/ws",
    token="optional-auth-token",         # 以 ?token= 查询参数传递
    reconnect=True,                       # 断线后自动重连
    reconnect_max_delay=30.0,
)
```

### `DotCraftClient`

传输层无关的 JSON-RPC 客户端。处理请求/响应关联、通知分发和服务端发起的请求处理。

```python
client = DotCraftClient(transport)
```

#### 初始化

```python
result = await client.initialize(
    client_name="my-adapter",
    client_version="1.0.0",
    approval_support=True,
    streaming_support=True,
    opt_out_notifications=[],    # 例如 ["item/reasoning/delta", "subagent/progress"]
)
# result.server_info, result.capabilities
```

#### 线程方法

```python
thread = await client.thread_start(
    channel_name="telegram",
    user_id="12345",
    channel_context="group:67890",   # 可选
    display_name="我的线程",          # 可选
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

#### 对话轮次方法

```python
turn = await client.turn_start(
    thread_id,
    input=[{"type": "text", "text": "运行测试"}],
    sender={                          # 可选，用于群聊场景
        "senderId": "user-456",
        "senderName": "Alice",
    },
)

await client.turn_interrupt(thread_id, turn_id)
```

#### 事件流

```python
# 订阅线程的所有事件并迭代：
async for event in client.stream_events(thread_id):
    print(event.method, event.params)

# 或注册回调：
@client.on("item/agentMessage/delta")
async def on_delta(params):
    print(params["delta"], end="")

@client.on("turn/completed")
async def on_done(params):
    print("\n完成。")
```

#### 审批响应

SDK 会自动将收到的 `item/approval/request` 服务端请求路由到你注册的处理器：

```python
@client.on_approval_request
async def handle_approval(request_id: str, params: dict) -> str:
    print(f"是否批准: {params['operation']}?")
    return "accept"   # 或 "decline"、"cancel"、"acceptForSession"、"acceptAlways"
```

### `ChannelAdapter`

构建社交渠道适配器的高级基类。处理完整的 Wire Protocol 生命周期，你只需实现平台相关逻辑。

```python
class ChannelAdapter:
    def __init__(
        self,
        transport,
        channel_name: str,
        client_name: str,
        client_version: str,
    ): ...

    # 需要重写的方法：
    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool: ...
    async def on_send(self, target: str, message: dict, metadata: dict) -> dict: ...
    async def on_approval_request(self, request: dict) -> str: ...
    def get_delivery_capabilities(self) -> dict | None: ...
    def get_channel_tools(self) -> list[dict] | None: ...
    async def on_tool_call(self, request: dict) -> dict: ...

    # 生命周期：
    async def start(self): ...   # 连接、初始化、启动消息循环
    async def stop(self): ...    # 优雅关闭

    # 平台事件处理器辅助方法：
    async def handle_message(
        self,
        user_id: str,
        user_name: str,
        text: str,
        channel_context: str = "",    # 群组/会话标识
        workspace_path: str = "",
    ) -> None: ...
    # 为该身份查找或创建线程，如果正在执行对话轮次则串行排队。
```

`ChannelAdapter` 与握手/协议方法的映射关系：

- `get_delivery_capabilities()` -> `initialize.capabilities.channelAdapter.deliveryCapabilities`
- `get_channel_tools()` -> `initialize.capabilities.channelAdapter.channelTools`
- `on_deliver()` -> `ext/channel/deliver`
- `on_send()` -> `ext/channel/send`
- `on_tool_call()` -> `ext/channel/toolCall`

## 配置

### 子进程模式（DotCraft 启动适配器）

在 DotCraft 的 `config.json` 中添加 `ExternalChannels` 配置节：

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

DotCraft 会启动 `python -m my_adapter`，通过 stdin/stdout 通信，并在进程崩溃时自动重启。

`ExternalChannels` 配置节只负责告诉 DotCraft 如何启动或接入适配器。structured delivery 能力和 `channelTools` 需要由适配器在 `initialize` 握手时自行声明，而不是写在 `config.json` 里。

### WebSocket 模式（适配器独立连接）

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

适配器连接到 `ws://127.0.0.1:9100/ws`，在 `initialize` 握手时提供 `channelAdapter.channelName = "my-channel"`。

## 示例

| 示例 | 说明 |
|------|------|
| [examples/telegram/](../channels/python-telegram) | 参考 Telegram 适配器，使用长轮询、内联键盘审批和消息投递支持。 |

