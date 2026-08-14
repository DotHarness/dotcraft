# Channel adapters

A channel adapter bridges an external messaging platform (Telegram, Feishu, QQ, …) to DotCraft as a first-class channel. The adapter resolves a thread per user, runs turns, and delivers replies back to the platform.

> [!NOTE]
> The channel adapter is a language-specific profile, available in **TypeScript and Python**. The .NET SDK does not ship a channel adapter.

Subclass the adapter base class when you want the built-in Channel policy: per-identity message queues, thread resolution, slash-command routing, turn-stream reduction, server-request handlers, and heartbeat.

## Minimal adapter

::: code-group

```ts [TypeScript]
import { ChannelAdapter } from "@dotcraft/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    return await platform.requestApproval(request);
  }

  protected async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    content: string,
    _isFinal: boolean,
    target: string,
  ): Promise<boolean> {
    return await this.onDeliver(target, content, {});
  }
}
```

```python [Python]
from dotcraft.channel import ChannelAdapter
from dotcraft.wire import StdioTransport

class MyChannel(ChannelAdapter):
    def __init__(self, client_version: str):
        super().__init__(
            transport=StdioTransport(),
            channel_name="my-channel",
            client_name="my-adapter",
            client_version=client_version,
        )

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        await platform_send(target, content)
        return True

    async def on_approval_request(self, request: dict) -> str:
        return await platform_request_approval(request)
```

:::

## Lifecycle and recovery

- Call `start()` before accepting platform events. It connects the Wire client, registers Channel handlers, and then advertises the Channel capabilities during `initialize`. Call `stop()` during shutdown; Python also cancels its per-identity worker tasks.
- Forward each platform event with `handleMessage` / `handle_message`. The call accepts the event into an in-memory queue; it does not mean the turn or platform delivery has completed.
- Queue identity is the combination of user id and channel context. Messages for one identity run serially; different identities can run concurrently. A slash command may bypass the queue when the adapter already knows the thread so commands such as stop can affect an active turn.
- The adapter resumes a paused thread, replaces a stale or inactive thread, retries against that replacement, and requeues an input when the server reports another turn is already running.
- The Wire client reconnects and repeats initialization. It does not persist or replay external platform events or completed delivery calls. Keep the platform receiver alive, provide platform-side deduplication or retry where needed, and do not treat reconnect as delivery recovery.

## Handler rules

| Hook | Contract |
|------|----------|
| `onDeliver` / `on_deliver` | Required. Deliver plain text to the platform target and report success. The default structured-send handler delegates text messages here. |
| `onApprovalRequest` / `on_approval_request` | Required. Return a valid approval decision. If the hook throws, the adapter answers `cancel`. |
| `onSend` / `on_send` | Optional. Override for structured delivery and advertise matching delivery capabilities. The default accepts text and rejects other kinds with `UnsupportedDeliveryKind`. |
| `getChannelTools` + `onToolCall` / `get_channel_tools` + `on_tool_call` | Optional. Advertise only tools the call hook implements; the default call hook returns `UnsupportedTool`. |
| `onReplyProgress` | TypeScript-only optional observer for ordered AgentMessage text while a Turn is running. It does not mark text as delivered; coalesce platform updates and use `onSegmentCompleted` or `onTurnCompleted` for delivery fallback. |
| turn and segment hooks | Override for platform formatting, progressive delivery, and failed/cancelled notifications. |

TypeScript also handles user-input requests through `onUserInputRequest`; its default returns an empty answer set. Python does not advertise that callback. Heartbeat replies are registered by the base adapter in both languages and should not be implemented by the platform subclass.

Progressive delivery differs slightly by language. In TypeScript, return `false` from `onSegmentCompleted` when a segment was not delivered; any other return marks it delivered, and the default `onTurnCompleted` avoids sending the full reply again. In Python, if `on_segment_completed` sends segments, also override `on_turn_completed` and track whether a segment was sent so the full reply is not duplicated.

## First-party channels

TypeScript Channel authoring is provided by the private `@dotcraft/channel` package. Import adapter and module authoring APIs from its root, queues and routing from `/runtime`, media helpers from `/media`, conformance helpers from `/testing`, and Channel contract metadata from `/meta`.

TypeScript ships hosted channel modules for several platforms. Each module depends on `@dotcraft/channel`, which in turn depends on `@dotcraft/sdk`. Their setup and behavior are documented per platform:

- [QQ](../channels/qq) · [WeCom](../channels/wecom) · [Feishu](../channels/feishu) · [Telegram](../channels/telegram) · [Weixin](../channels/weixin)

Python ships a Telegram reference adapter:

- [Telegram (Python)](../channels/python-telegram)

## Related docs

- [AppServer Protocol](../protocols/appserver-protocol) — the underlying JSON-RPC contract.
- Reference: [TypeScript](./typescript) · [Python](./python).
