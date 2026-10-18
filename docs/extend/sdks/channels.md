# Channel adapters

A channel adapter brings an external messaging platform into DotCraft as a first-class channel. It resolves a thread per conversation, forwards platform messages and commands to it, and serves the deliveries the agent sends with `SendChannelMessage`.

The adapter is the runtime abstraction. To make the same adapter discoverable and manageable by DotCraft hosts such as Desktop, package it as a [Channel module](../channel-modules). A module adds host metadata and lifecycle packaging; it does not replace the adapter.

> [!NOTE]
> The channel adapter is a language-specific profile, available in **TypeScript** only. The .NET SDK does not ship a channel adapter.

Subclass the adapter base class when you want the built-in Channel policy: per-identity message queues, thread resolution, command routing, routing input into a running turn, turn activity reduction, server-request handlers, and heartbeat.

The adapter never posts the agent's replies on its own. People in a chat see only what the agent sends with `SendChannelMessage`, which reaches the adapter as `ext/channel/send` requests and lands in `onSend`. Turn hooks report activity, not text, so the adapter can show that the agent is busy.

![One platform message through a channel adapter: queued per identity, routed to a thread or run as a command, started as a turn or steered into the running one, and only the messages the agent sends are delivered back to the chat](/channel-adapter-loop.svg)

## Minimal adapter

```ts
import {
  ChannelAdapter,
  type ChannelAdapterMessageOpts,
  type ChannelChatDescriptor,
  type ChannelDeliveryCapabilities,
} from "@dotcraft/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    return await platform.requestApproval(request);
  }

  protected getDisplayName(): string {
    return "My Chat";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return {
      text: { format: "plain" },
      targets: { group: false, direct: "none" },
    };
  }

  protected getChatDescriptor(opts: ChannelAdapterMessageOpts, _channelContext: string): ChannelChatDescriptor {
    return { kind: "direct", label: opts.userName, membership: "shared" };
  }
}
```

This adapter sends text only. Declare a `media` entry and override `onSend` to deliver attachments.

## Lifecycle and recovery

- Call `start()` before accepting platform events. It connects the Wire client, registers Channel handlers, and then advertises the Channel capabilities during `initialize`. Call `stop()` during shutdown.
- Forward each platform event with `handleMessage`. The call accepts the event into an in-memory queue; it does not mean the turn has started. Use the chat the message came from as `channelContext`, never the sender's direct chat for a group message, because it is where the agent's messages go by default. Pass `directTarget` when the adapter can reach the sender's direct chat, so the agent can message them there.
- Set `senderRole: "admin"` in `sender` only for senders on your configured admin list. Everyone else the platform lets through is forwarded as `user`, and admin-only commands reject them. Drop senders you do not permit before calling `handleMessage`.
- Queue identity is the combination of user id and channel context. Messages for one identity run serially; different identities can run concurrently.
- A message for a thread with a running turn joins that turn with `turn/steer`. When the turn cannot take it, the message is queued with `turn/enqueue`; otherwise it starts a new turn. The adapter replaces a stale or inactive thread and retries against that replacement.
- The Wire client reconnects and repeats initialization. It does not persist or replay platform events. Keep the platform receiver alive and add platform-side deduplication or retry where needed.

## Commands

A message is a command only when its first word is the name or alias of a command the server lists in `command/list`. Other text, including a path that starts with `/`, is ordinary input. Commands bypass the queue and run with `command/execute` as soon as they arrive, even while a turn is running, so `/stop` takes effect immediately.

The base adapter shows a command result's message through `onDeliver`, switches the conversation to the result's thread when it names one, and then calls `renderCommandChoices` for any choices. The default renders a numbered list showing each choice's command. Override it on a platform with buttons or cards, and call `executeCommandChoice` with the selected choice's command and the selecting sender's message options.

Keep adapter-local commands to platform setup, such as `/bind` and account authorization. If the platform has a native command menu, fill it from `listCommands()` and show commands with `requiresAdmin` only to admins.

Approvals answered by text accept `/approve`, `/approve session`, and `/deny`, in addition to the reply keywords `parseApprovalReplyDecision` recognizes.

## Handler rules

| Hook | Contract |
|------|----------|
| `onDeliver` | Required. Deliver plain text to the platform target and report success. Command results, notices, and the default text send use it. |
| `onApprovalRequest` | Required. Return a valid approval decision. If the hook throws, the adapter answers `cancel`. |
| `getDisplayName` | Required. Return the platform's product name, such as `Telegram`. The agent and Desktop show it. |
| `getDeliveryCapabilities` | Required. Declare how the platform renders text (`plain` or `markdown`), which other chats the adapter can reach (`targets`), and every attachment kind `onSend` delivers (`media`). Declare only targets the adapter can actually reach. |
| `getChatDescriptor` | Required. Describe the chat a message came from: its kind (`direct`, `group` or `topic`), label, and whether the thread holds every member's messages (`shared`) or one sender's (`sender`). It is sent when the adapter starts or resumes the thread. |
| `onSend` | Override to deliver every attachment kind `getDeliveryCapabilities` declares, and answer a target the adapter cannot reach with `UnsupportedTarget`. The default accepts text and rejects other kinds with `UnsupportedDeliveryKind`. The base answers a repeated delivery id with the first result, so a retried send does not post twice. |
| `getChannelTools` + `onToolCall` | Optional. Advertise only tools the call hook implements; the default call hook returns `UnsupportedTool`. Channel tools are for platform operations other than messaging, which `SendChannelMessage` already covers. |
| `onTurnStarted`, `onActivity`, `onTurnRetrying`, `onTurnEnded` | Optional. Show chat activity, such as a typing indicator or a status card, for a turn the adapter started. `onActivity` reports text, reasoning, and tool items starting and completing, without their content. `onTurnRetrying` fires while the server retries the model stream. `onTurnEnded` fires once with `completed`, `failed`, `cancelled`, or `disconnected`; the default sends one generic failure notice for `failed`. Override it when a status card already shows the failure. |
| `onCurrentThreadChanged` | Optional. Called when a command such as `/new` or `/resume` switches the conversation to another thread. |
| `renderCommandChoices` | Optional. Present a command result's choices. The default renders a numbered list through `onDeliver`. |

The adapter also handles user-input requests through `onUserInputRequest`; its default returns an empty answer set. The base adapter registers heartbeat replies itself, so a platform subclass never implements them.

## Package

TypeScript Channel authoring uses `@dotcraft/channel`. Import adapter authoring APIs from its root, queues and routing from `/runtime`, media helpers from `/media`, conformance helpers from `/testing`, and Channel contract metadata from `/meta`.

DotCraft uses the same base for its built-in hosted channels. Their setup and configuration are documented in the [channel configuration](../../configuration/channels).

## Related docs

- [Channel modules](../channel-modules) — package an adapter for host discovery and lifecycle management.
- [AppServer Protocol](../protocols/appserver#channel-adapters) — the JSON-RPC contract beneath the adapter.
- [TypeScript reference](./typescript) — the SDK surface used by the adapter.
