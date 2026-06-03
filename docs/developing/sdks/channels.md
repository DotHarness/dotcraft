# Channel adapters

A channel adapter bridges an external messaging platform (Telegram, Feishu, QQ, …) to DotCraft as a first-class channel. The adapter resolves a thread per user, runs turns, and delivers replies back to the platform.

> [!NOTE]
> The channel adapter is a language-specific profile, available in **TypeScript and Python**. The .NET SDK does not ship a channel adapter. The wire contract is defined by the [External Channel Adapter](https://github.com/DotHarness/dotcraft/blob/master/specs/protocols/external-channel-adapter.md) spec.

Subclass the adapter base class and implement the platform hooks — delivery, approval, and (optionally) channel tools. The SDK owns the rest: per-identity message queueing, thread resolution and recovery, slash-command routing, turn-stream reduction, and heartbeat.

::: code-group

```ts [TypeScript]
import { ChannelAdapter } from "@dotcraft/sdk/channel";

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
from dotcraft import ChannelAdapter, StdioTransport

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

TypeScript ships hosted channel modules for several platforms. Their setup and behavior are documented per platform:

- [QQ](../channels/qq) · [WeCom](../channels/wecom) · [Feishu](../channels/feishu) · [Telegram (TypeScript)](../channels/telegram) · [Weixin](../channels/weixin)

Python ships a Telegram reference adapter:

- [Telegram (Python)](../channels/python-telegram)

## See also

- [External Channel Adapter](https://github.com/DotHarness/dotcraft/blob/master/specs/protocols/external-channel-adapter.md) — the channel wire contract.
- Reference: [TypeScript](./typescript) · [Python](./python).
