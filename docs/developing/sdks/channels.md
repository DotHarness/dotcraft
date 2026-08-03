# Channel adapters

A channel adapter bridges an external messaging platform (Telegram, Feishu, QQ, …) to DotCraft as a first-class channel. The adapter resolves a thread per user, runs turns, and delivers replies back to the platform.

> [!NOTE]
> The channel adapter is a language-specific profile, available in **TypeScript and Python**. The .NET SDK does not ship a channel adapter.

Subclass the adapter base class and implement the platform hooks — delivery, approval, and (optionally) channel tools. The profile uses the high-level AppServer client, enables reconnect and reinitialization, and owns per-identity message queueing, thread resolution, slash-command routing, turn-stream reduction, and heartbeat.

Reconnect restores the AppServer connection, but Channel policy still decides how to recover conversations and delivery. Heartbeat, approval handling, and platform delivery remain Channel behavior rather than general Wire behavior.

::: code-group

```ts [TypeScript]
import { ChannelAdapter } from "@dotcraft/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(): Promise<string> {
    return "accept";
  }
}
```

```python [Python]
from dotcraft.channel import ChannelAdapter
from dotcraft.wire import StdioTransport

class MyChannel(ChannelAdapter):
    def __init__(self):
        super().__init__(
            transport=StdioTransport(),
            channel_name="my-channel",
            client_name="my-adapter",
            client_version="1.0.0",
        )

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        await platform_send(target, content)
        return True

    async def on_approval_request(self, request: dict) -> str:
        return "accept"
```

:::

Forward platform messages into the adapter with `handleMessage` / `handle_message`; the adapter finds or creates the thread for that identity, serializes concurrent input, runs the turn, and calls your delivery hook with the reply.

## First-party channels

TypeScript Channel authoring is provided by the private `@dotcraft/channel` package. Import adapter and module authoring APIs from its root, queues and routing from `/runtime`, media helpers from `/media`, conformance helpers from `/testing`, and Channel contract metadata from `/meta`.

TypeScript ships hosted channel modules for several platforms. Each module depends on `@dotcraft/channel`, which in turn depends on `@dotcraft/sdk`. Their setup and behavior are documented per platform:

- [QQ](../channels/qq) · [WeCom](../channels/wecom) · [Feishu](../channels/feishu) · [Telegram (TypeScript)](../channels/telegram) · [Weixin](../channels/weixin)

Python ships a Telegram reference adapter:

- [Telegram (Python)](../channels/python-telegram)

## Related docs

- [AppServer Protocol](../protocols/appserver-protocol) — the underlying JSON-RPC contract.
- Reference: [TypeScript](./typescript) · [Python](./python).
