# DotCraft Channel Messaging and Commands

| Field | Value |
|-------|-------|
| **Version** | 0.8.10 |
| **Status** | Living |
| **Date** | 2026-10-18 |
| **Parent Specs** | [External Channel Adapter](../protocols/external-channel-adapter.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: Define how an agent speaks in social channels served by external channel adapters: one
model tool sends text and attachments to a chat, the channel's capabilities and
the conversation's place in it reach the model as context, and nothing else the agent writes is
projected into the chat. It also defines the in-chat command set and its semantics.

## 1. Scope

This specification owns:

- the chat binding of a thread and the chat address format;
- the semantics of channel delivery capabilities;
- the `SendChannelMessage` tool;
- the channel state the model receives;
- what an adapter shows in the chat while a Turn runs;
- how automations and App Binding deliver through the tool;
- the in-chat command set, its scopes, busy behavior, authority and platform presentation.

Wire shapes belong to [AppServer Protocol](../protocols/appserver-protocol.md) (§3.2, §7, §11.2,
§19). Adapter process, connection, thread mapping and approval obligations belong to
[External Channel Adapter](../protocols/external-channel-adapter.md). Automation storage and
scheduling belong to [Automations lifecycle](automations-lifecycle.md). Session Core stays
channel-agnostic: nothing in this specification adds channel behavior to
[Session Core](../architecture/session-core.md).

## 2. Design intent

- The chat sees only what the agent chooses to send. Reasoning, progress narration, tool activity,
  and the assistant's ordinary replies stay in the thread, where Desktop and other clients still show
  them.
- Not sending is a valid outcome. The runtime does not detect, recover or replace a Turn that sent
  nothing; it gives the model the facts it needs and reports delivery results back to it.
- One tool covers every channel. Channel differences are capabilities the adapter declares, not
  per-platform tools.
- Scheduled and background work uses the same tool, so "notify only when there is something to say"
  needs no silence token or importance score.

## 3. Non-goals

- Reactions, message edits, recalls, polls, buttons, @-mentions and quoted replies sent by the model.
  They may become capability bits and optional tool fields later.
- A directory or search tool for chats and contacts.
- Sending from a thread that has no chat binding, such as an unbound Desktop thread.
- Delivery-recovery Turns, silence tokens, or automatic fallback delivery of the final reply.
- Platform operations that are not messaging, such as document or calendar access; those remain
  adapter `channelTools`.

## 4. Chat binding

### 4.1 Definition

A chat binding is the chat a thread speaks to by default. It has a channel name, a delivery target
accepted by that channel's `ext/channel/send`, a chat kind (`direct`, `group` or `topic`), an
optional display label, and a membership (`shared` or `sender`).

A root thread has a chat binding when one of these holds:

| Thread | Binding |
|--------|---------|
| Created by an inbound channel | The adapter's chat for the thread. Its target is the thread's `channelContext`. |
| Bound by App Binding | The verified channel binding target ([App Binding](../protocols/app-binding.md) §7). |
| An automation run whose definition has a channel origin | The origin's channel and delivery target, with the origin's chat kind and label when it has them; without a kind the chat is unspecified. A thread-mode run into a thread that already has a binding uses that thread's binding. |

SubAgent threads, Workflow children and other non-root threads have no chat binding.

An App Binding binding binds its chat in every state that keeps a verified target until it is
revoked. Only an `active` binding can deliver; any other state, such as `offline`, keeps the binding
but fails delivery (§10).

### 4.2 Membership

Thread granularity is an adapter policy. An adapter may map one chat to one shared thread or give
each sender in a group their own thread.

- `shared`: every forwarded member's messages arrive in the thread, each with the sender's name.
- `sender`: the thread holds one sender's messages in that chat; other members' messages are not
  in it.

In both cases the binding's target is the chat the messages came from, never the sender's direct
chat. The adapter states the membership when it starts or resumes the thread.

### 4.3 Addresses

A chat address is `<channelName>:<target>`, split at the first colon. `channelName` is the
configured channel name and contains no colon. `target` is that adapter's `ext/channel/send` target
syntax, which the adapter owns, for example `group:123456` or `user:10001`, giving
`example:group:123456`.

The model learns addresses from context: the bound chat's address (§6.1) and the sender's direct
address when the adapter reports one (§6.2). Other addresses come from the conversation, for example
a group number a user gives.

## 5. Channel capabilities

An adapter declares its delivery capabilities during `initialize`
([AppServer Protocol](../protocols/appserver-protocol.md) §3.2). Every adapter serves
`ext/channel/send`; there is no delivery-less adapter.

| Capability | Meaning |
|------------|---------|
| `displayName` | The platform's product name, shown to the model and in Desktop. |
| `text.format` | `markdown` when the chat renders formatting the adapter converts from Markdown, otherwise `plain`. The adapter owns the conversion to its platform's dialect and splits text too long for one platform message. |
| `media.<kind>` | Accepted attachment kinds (`image`, `file`, `audio`, `video`) with size, type and caption limits. An absent kind is unsupported. |
| `targets.group` | Whether the adapter can send to a group other than the bound chat. |
| `targets.direct` | `any`, `knownOnly` (only to users who have messaged the bot) or `none`: whether it can send to a direct chat other than the bound chat. |

Capabilities are fixed for the lifetime of an adapter connection.

## 6. Model context

### 6.1 Channel section

A root thread with a chat binding carries a channel section in its
[World State](../architecture/world-state.md). The channel runtime contributes the section, not
Session Core. Its snapshot holds identity and status only: the channel name and display name, and
the bound chat's address, kind, label and membership, text format, accepted attachment kinds and
reachable targets (§5). The section is sent again only when that snapshot changes. It states:

- the platform, chat kind, label and address of the bound chat;
- that people in the chat see only what is sent with `SendChannelMessage`, and that the rest of the
  conversation stays in the thread;
- the membership (§4.2);
- the text format and accepted attachment kinds;
- which other chats the adapter can reach (§5 `targets`).

Shape, with values substituted:

```text
## Channel
This conversation is connected to the {displayName} {chat kind} "{label}" ({address}).
People in the chat see only what you send with SendChannelMessage. Everything else you write stays in this conversation.
{membership: "Messages from every member arrive here with the sender's name." | "This conversation holds only one member's messages in that {chat kind}; other members' messages are not here."}
The chat renders {text format} and accepts {attachment kinds}.
{targets: "You can send to other {displayName} groups and direct chats by address." | omitted when neither is reachable}
```

While the binding exists but its facts are unavailable, the section takes no snapshot and sends
nothing, so the model keeps what it was last told. This covers a channel that is not connected, a
chat whose details the adapter has not yet stated on its current connection, and an App Binding
binding that is not `active`. A non-root thread takes no snapshot either, so a SubAgent neither
restates nor retracts a section it inherited.

When the thread has no binding, the section follows the capability rules of
[World State](../architecture/world-state.md) §2.1: a thread that was never bound hears nothing, and
a thread whose binding is gone, for example after revocation, gets one notice that it is no longer
connected to a chat.

The section names facts only. It does not tell the model when it must send.

### 6.2 Turn facts

The per-turn runtime reminder ([Prompt Composition](../architecture/prompt-composition.md) §9) keeps
its Request Source lines and adds:

- `SenderAddress`, when the adapter reports the sender's direct address;
- `Trigger`, naming the automation for a Turn an automation started.

Turns started by task notifications or goal continuation keep their existing input; they need no
additional channel line.

## 7. `SendChannelMessage`

### 7.1 Availability

The tool is published on a root thread that has a chat binding (§4.1). It is a host-implemented
tool of the channel runtime, not an adapter `channelTool`. Its schema is identical for every thread
and channel, so it does not change with capabilities, connection state or mode. Exposure is
`DirectModelOnly` ([Tool Architecture](../architecture/tools-architecture.md) §5.4): the model sends
in its own words rather than from a script. It is runtime-managed, so Agent Profile tool lists do
not hide it from a bound root thread.

### 7.2 Schema

```json
{
  "type": "object",
  "properties": {
    "text": { "type": "string" },
    "attachments": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "path": { "type": "string", "description": "Path of a file on this computer." },
          "kind": { "type": "string", "enum": ["image", "file", "audio", "video"] },
          "name": { "type": "string" }
        },
        "required": ["path"]
      }
    },
    "to": { "type": "string" }
  }
}
```

- At least one of `text` and `attachments` is present.
- `path` names a file on the host running DotCraft that the Turn's file access policy lets it read,
  such as a workspace path or a generated image's saved path. A file in a
  [Remote Tool Host](../architecture/remote-tool-host.md) workspace must first be downloaded to the
  local workspace; while the thread has a remote route, an attachment not found locally fails with
  `invalidArguments` and says so.
- `kind`, when omitted, is inferred from the file's media type: images become `image`, anything else
  `file`. `audio` and `video` are sent as voice and video messages where the channel supports them.
- `name` is the file name shown in the chat; it defaults to the file's name.
- `to` is a chat address (§4.3). When omitted, the message goes to the bound chat.

### 7.3 Delivery

One call delivers its parts in order through `ext/channel/send`: text first, then each attachment.
When the call has exactly one attachment and the target kind supports captions, the text is that
attachment's caption instead of a separate message.

The host validates the whole call before sending anything:

- the target channel is connected, an App Binding binding is `active`, and, for a target other than
  the bound chat, its `targets` capability allows the target's kind;
- every attachment exists, is readable under the Turn's file access policy (its workspace roots,
  outside-workspace rule and path blacklist, as for file tools), and fits the target's `media`
  limits.

An `audio` or `video` attachment the target does not support is sent as a `file` when files are
supported; the result reports the downgrade. Any other unsupported kind fails the call before
delivery.

Each part carries a deterministic delivery id derived from the Turn, the tool call and the part's
index. The adapter treats a repeated delivery id as already delivered, so a retried call does not
post twice.

### 7.4 Approval

A call to the bound chat needs no approval. A call with `to` naming any other chat requests approval
with `approvalType = "channelMessage"`, `operation = "send"`, and the address as `target`.
`acceptForSession` covers that address for the rest of the session. Workspace approval policy applies
as for other approval types. An automation run follows its approval policy: `fullAuto` approves other
targets, and `workspaceScope` refuses them.

### 7.5 Result

On success the result lists each delivered part with its kind, the platform message id when the
adapter returns one, and any downgrade. On failure the tool returns an error with one of these codes
and the parts delivered before the failure:

| Code | When |
|------|------|
| `invalidArguments` | No content, unreadable attachment, or malformed address. |
| `unsupportedTarget` | The target channel cannot reach that kind of chat. |
| `unsupportedKind` | An attachment kind the target cannot accept, even as a file. |
| `tooLarge` | An attachment exceeds the target's limit. |
| `channelUnavailable` | The target channel is not connected, or the App Binding binding is not `active`. |
| `deliveryFailed` | The platform rejected or failed a part. |
| `declined` | The approval was declined. |

### 7.6 Presentation

The tool call carries a `core.channel` presentation. Desktop renders it as one row naming where the
message went, for example "Sent to {displayName} · {label}", with the text and attachments available on
expand. A failed send shows the error in the same row.

## 8. Inbound messages and chat activity

- An inbound message is still ordinary user input in the bound thread, with its `SenderContext`.
- When the thread has an active regular Turn, the adapter adds the message to it with `turn/steer`.
  When the Turn cannot accept steering, the adapter queues it with `turn/enqueue`. When no Turn is
  active, it starts one with `turn/start`. The adapter does not retry `turn/start` while a Turn runs.
- The adapter does not project `agentMessage` text, reasoning, plans, generated images or any other
  Item into the chat.
- The adapter may show activity it owns, which the model is not told about:
  - an acknowledgement reaction once the message is accepted;
  - a typing indicator while the Turn runs;
  - a status card, posted when a Turn the adapter started is still running after 20 seconds and
    patched silently through `working`, `retrying`, `failed`, `stopped` and `done`. A Turn that fails
    before then posts the card directly as `failed`. The card is never withdrawn and settles on the
    elapsed time.
- An adapter without a status card sends one short generic notice when a Turn it started fails. The
  notice carries no diagnostic detail.
- Approval and user-input requests keep their platform presentation
  ([External Channel Adapter](../protocols/external-channel-adapter.md) §11).

Each adapter uses whichever of these its platform offers; an adapter whose platform offers none shows
no activity.

## 9. Automations

- An automation created from a channel thread captures its origin with the delivery target, the
  chat kind and the chat label. Its runs have a chat binding to that chat (§4.1), so the run's agent
  sends with `SendChannelMessage` when it has something to tell the chat, and sends nothing
  otherwise.
- The host sends nothing to the chat after a successful run. When a run fails, times out or is
  interrupted, the host sends one fixed failure notice to the origin chat, because the agent can no
  longer send.
- The run's summary remains the last agent message and is shown in Desktop.
- Automations without a channel origin keep their existing Desktop result notification.

## 10. App Binding

A thread bound to a chat through App Binding speaks to it only through `SendChannelMessage`. The tool
stays published in every binding state that keeps a verified target, so a binding going offline does
not change the thread's tools ([App Binding](../protocols/app-binding.md) §6.3). The tool resolves
the binding revision and verified target at call time, and a call fails with `channelUnavailable`
unless the binding is `active` then. Revocation removes the binding: later calls fail, the tool is
no longer published, and the channel section retires once (§6.1). The assistant's final reply is not
delivered to the bound chat.

## 11. Adapter channel tools

Adapters declare no `channelTools` for sending text or media to a chat; every message goes through
`SendChannelMessage`. `channelTools` remain for platform operations that are not messaging.

## 12. Commands

### 12.1 Registry

In-chat commands are server commands from the command registry
([AppServer Protocol](../protocols/appserver-protocol.md) §19), executed with `command/execute` on the
conversation's current thread. Adapter-local commands are limited to platform setup: `/bind` and
adapter-owned account commands such as personal authorization.

- A message is a command only when its first word is a registered command name. Any other text that
  starts with `/`, such as a path, is ordinary input.
- Commands bypass the adapter's message queue and run immediately, whether or not a Turn is active
  and whether or not the adapter has already resolved the thread.
- Each command has a scope, a busy behavior and an authority, all enforced by the server:
  - busy behavior `run` executes while a Turn is active; `interruptFirst` cancels the active Turn,
    then executes; `reject` answers that the conversation is busy;
  - authority is everyone the adapter forwards, or admin only (`requiresAdmin`).

### 12.2 Command set

| Command | Semantics | Scope | Busy | Authority |
|---------|-----------|-------|------|-----------|
| `/new` | Archive the current thread and start a new one for this conversation. | conversation | interruptFirst | everyone |
| `/stop` | Cancel the active Turn. SubAgent Turns started under it are cancelled with it. Queued input and background shells and Workflows keep running. | current Turn | run | everyone |
| `/stop all` | Cancel the active Turn, remove queued input, and stop every background task the thread owns (shell, SubAgent, Workflow), recorded as stopped by the user. | current thread | run | everyone |
| `/tasks` | List the thread's background tasks with a number, kind, label, status and elapsed time. `/tasks stop <n or task id>` stops one. | current thread | run | everyone |
| `/status` | Show the thread's title, state (idle, running, waiting for approval or input), model, mode, context usage, queued input count, running task count and pending approvals. | current thread | run | everyone |
| `/model [model]` | Without an argument, list the models available for the thread's provider. With one, set the model for the thread's later Turns. | current thread | run | admin |
| `/mode [agent\|plan]` | Show or set the thread's mode. | current thread | run | everyone |
| `/sessions` | List this conversation's recent threads, including archived ones, most recently active first, with a number, title and last activity. | conversation | run | everyone |
| `/resume <n or thread id>` | Make that thread from `/sessions` the conversation's current thread: unarchive it and archive the current one. | conversation | interruptFirst | everyone |
| `/compact` | Compact the thread's context. | current thread | reject | everyone |
| `/automate list\|show\|pause\|resume\|run\|remove` | Unchanged ([Automations lifecycle](automations-lifecycle.md)). | automations created from this conversation | run | admin |
| `/help` | List the commands the sender may use. | none | run | everyone |
| `/init` | Unchanged. | workspace | reject | admin |
| `/debug` | Unchanged. | current thread | run | admin |

A conversation is a thread identity: the current thread's origin channel, user id and channel
context. It has one current thread: the newest active thread for its identity. `/new` and
`/resume` keep that invariant by archiving the thread they replace. When a command result names a
thread, the adapter switches its current thread to it.

A chat bound to a thread through App Binding (§4.1) belongs to that thread. While the binding is not
revoked, `/new`, `/resume` and `/sessions` archive nothing and answer that the chat is bound to a
DotCraft conversation and must be unbound in DotCraft first. `/sessions` and `/tasks` number entries in creation order, so a number never moves when a newer
thread or task appears; `/resume` and `/tasks stop` accept that number or the entry's id, and the
choices they return carry the id. `/model`, `/mode`, `/sessions`
and `/tasks` return choices (§12.5) that select a model, a mode, a thread or a task to stop.

### 12.3 Authority

An adapter sets `senderRole = "admin"` only for senders on its configured admin list, and forwards
other permitted senders as `user`. An adapter whose platform lets only the bot's owner reach it has
no admin list and forwards every sender as `admin`. A sender the adapter does not permit is not
forwarded at all.

### 12.4 Approval replies

Approval prompts are not registry commands. In addition to buttons and the existing reply keywords,
every adapter accepts `/approve`, `/approve session` and `/deny` as replies to the sender's pending
approval. An approval is the pending approval of the sender who started the Turn that raised it, or of
the thread's only sender when the thread holds one sender's messages; these replies never answer
another member's approval.

An adapter handles an inbound message in this order: an approval reply, then a command, then an
answer to a pending question, then ordinary input. A command such as `/stop` therefore runs even
while a question waits for an answer.

### 12.5 Presentation

A command result may carry choices, each a label and the command that selects it, for example the
models `/model` lists.

- Slash text works on every platform.
- An adapter whose platform has a native command menu fills it from `command/list`, filtered by the
  sender's authority and, where the platform scopes menus, by chat kind.
- An adapter whose platform has interactive controls such as buttons or cards presents choices and
  approvals with them. Otherwise it shows choices as a numbered list with each choice's command, and
  approvals as reply keywords.
- `/help` and native menus list only the commands the sender may use.

## 13. Acceptance checklist

- [ ] An answer the agent writes without calling `SendChannelMessage` never appears in the chat, on
      every channel, including text written between tool calls.
- [ ] `SendChannelMessage` delivers text, an image and a file to the bound chat on every channel that
      supports them, and a captioned single attachment where captions are supported.
- [ ] An unsupported audio or video attachment is delivered as a file and the result says so; an
      unsupported kind with no file fallback fails before anything is sent.
- [ ] A retried call with the same Turn and call id does not post twice.
- [ ] `to` sends to another group and to a direct chat on the same channel, and to a chat on another
      connected channel; each asks for approval, `acceptForSession` covers later sends to the same
      address, and an address the target channel cannot reach fails with `unsupportedTarget`.
- [ ] The channel section names the platform, chat, address, membership, text format and
      attachments; it is not sent again when nothing changed or while the channel is disconnected,
      and a binding that is gone retires it once.
- [ ] A message sent while a Turn runs reaches that Turn as guidance.
- [ ] Each adapter shows the activity its platform offers (§8); a status card appears only for a Turn
      over 20 seconds and settles on `done` or `failed`.
- [ ] A channel-origin automation run that sends nothing leaves the chat silent; one that calls
      `SendChannelMessage` posts in the origin chat, with attachments; a failed run posts one fixed
      failure notice.
- [ ] An App Binding thread delivers only through the tool; an offline binding keeps the tool and
      fails the call with `channelUnavailable`, and a revoked binding removes the tool.
- [ ] No adapter declares a messaging `channelTool`; non-messaging `channelTools` still work.
- [ ] Every command in §12.2 behaves as specified on an adapter with interactive controls and on a
      text-only adapter, and `/stop` runs immediately during a Turn on every channel.
- [ ] `/stop` leaves a background shell running; `/stop all` stops it and clears queued input.
- [ ] `/new` then `/sessions` then `/resume 2` returns to the previous thread and keeps one current
      thread for the conversation.
- [ ] `/new` in a chat bound through App Binding is refused and archives nothing.
- [ ] A non-admin sender cannot run `/model`, and native menus hide it for them.
