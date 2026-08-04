# Threads and runs

A thread is a durable conversation. A run starts one turn on that thread and either returns the final result or streams events as work progresses.

## Manage threads

Start a new thread, resume one by ID, or list threads for an identity.

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({ userId: "me" });
const resumed = await dotcraft.threads.resume(threadId);
const threads = await dotcraft.threads.list({ userId: "me" });
const snapshot = await dotcraft.threads.read(threadId, { includeTurns: true });
```

```csharp [.NET]
var identity = new SessionIdentity { ChannelName = "my-app", UserId = Environment.UserName };
var thread = await client.Threads.StartAsync(new ThreadStartParams { Identity = identity });
var resumed = await client.Threads.ResumeAsync(new ThreadResumeParams { ThreadId = threadId });
var threads = await client.Threads.ListAsync(new ThreadListParams { Identity = identity });
var snapshot = await client.Threads.ReadAsync(threadId, includeTurns: true);
```

```python [Python]
thread = await dotcraft.threads.start(user_id="me")
resumed = await dotcraft.threads.resume(thread_id)
threads = await dotcraft.threads.list(user_id="me")
snapshot = await dotcraft.threads.read(thread_id, include_turns=True)
```

:::

TypeScript and Python also provide `getOrCreate` / `get_or_create`. It reuses an active or paused thread for the identity before starting a new one.

## Choose a model

Discover the catalog before presenting a model picker or validating saved configuration.

::: code-group

```ts [TypeScript]
const models = await dotcraft.models.list();
for (const model of models) console.log(model.id);
const configuration = (await dotcraft.threads.read(thread.id)).configuration;
```

```csharp [.NET]
var catalog = await client.Models.GetCatalogAsync();
foreach (var model in catalog.Models.Value ?? [])
    Console.WriteLine(model.Id.Value);
var currentConfiguration = await client.Threads.ReadModelConfigurationAsync(thread.Id);
```

```python [Python]
models = await dotcraft.models.list()
for model in models:
    print(model.id)
configuration = (await dotcraft.threads.read(thread.id)).configuration
```

:::

All three high-level clients return the current `ThreadConfiguration` through a thread read. The .NET client also provides a safe read-modify-write helper for model fields. It preserves unrelated and unknown thread configuration fields:

```csharp
var configuration = await client.Threads.UpdateModelConfigurationAsync(
    thread.Id,
    providerId: "<provider-id>",
    model: "<model-id>",
    reasoning: new ReasoningConfig { Enabled = true, Effort = "high" },
    speed: null,
    contextWindow: null);
```

TypeScript and Python currently expose model discovery at the high level but not this configuration helper. Applications using their typed Wire layer must update the complete `ThreadConfiguration` and preserve fields they do not own. Do not infer model IDs or reasoning options across providers; use the catalog returned by the connected AppServer.

## Build input

Pass a string for plain text. Use input parts for files, images, skills, or commands.

::: code-group

```ts [TypeScript]
import { fileRefPart, textPart } from "@dotcraft/sdk";

const result = await thread.run([
  textPart("Review this file."),
  fileRefPart("src/app.ts"),
]);
```

```csharp [.NET]
using DotCraft.Protocol.AppServer;

var result = await thread.RunAsync([
    new InputPart { Type = "text", Text = "Review this file." },
    new InputPart { Type = "fileRef", Path = "src/App.cs" },
]);
```

```python [Python]
from dotcraft import file_ref_part, text_part

result = await thread.run([
    text_part("Review this file."),
    file_ref_part("src/app.py"),
])
```

:::

| Part | Purpose | TypeScript / Python helper |
| --- | --- | --- |
| `text` | Literal user text | `textPart` / `text_part` |
| `fileRef` | Workspace or local file reference | `fileRefPart` / `file_ref_part` |
| `image` | Base64 `data:image/...` URL | `imageDataUrlPart` / `image_data_url_part` |
| `localImage` | Image path readable by AppServer | `localImagePart` / `local_image_part` |
| `skillRef` | Skill reference | `skillRefPart` / `skill_ref_part` |
| `commandRef` | Custom command reference | `commandRefPart` / `command_ref_part` |

.NET constructs the generated `InputPart` contract directly. High-level clients do not convert leading `/command`, `$skill`, or `@file` text into structured parts.

Remote image URLs are not accepted as `image` parts. Download the image first, then send a data URL or a `localImage` path readable by AppServer.

## Run a turn

Use the buffered form for a final result. Use the streaming form for live progress.

::: code-group

```ts [TypeScript]
const result = await thread.run("Run the tests and summarize failures.");
console.log(result.text);

for await (const event of thread.runStreamed("Now fix them.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
}
```

```csharp [.NET]
var result = await thread.RunAsync("Run the tests and summarize failures.");
Console.WriteLine(result.Text);

await foreach (var runEvent in thread.RunStreamedAsync("Now fix them."))
{
    if (runEvent is DotCraftRunEvent<ItemDeltaNotification> delta &&
        runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta)
        Console.Write(delta.Params.Delta);
}
```

```python [Python]
result = await thread.run("Run the tests and summarize failures.")
print(result.text)

async for event in thread.run_streamed("Now fix them."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## Read the result

| Value | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| Merged reply | `result.text` | `result.Text` | `result.text` |
| Thread ID | `result.thread.id` | `result.ThreadId` | `result.thread_id` |
| Turn ID | `result.turn?.id` | `result.TurnId` | `result.turn_id` |
| Terminal turn | `result.turn` | `result.Turn` | `result.turn` |
| Items and usage | `result.items`, `result.usage` | `result.Turn?.Items`, `result.Turn?.TokenUsage` | Read from `result.turn` |
| Raw events | `result.rawEvents` | `result.RawEvents` | `result.raw_events` |

Raw events are collected only when the language-specific `collectRawEvents` / `CollectRawEvents` / `collect_raw_events` option is enabled.

## Run options

| Behavior | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| Sender context | `sender` | `RunOptions.Sender` | `sender` |
| Queue when busy | `enqueueIfBusy` | `RunOptions.EnqueueIfBusy` | `enqueue_if_busy` |
| Collect raw events | `collectRawEvents` | `RunOptions.CollectRawEvents` | `collect_raw_events` |
| Return failed terminal turns | Not available | `RunOptions.ThrowOnFailure = false` | `throw_on_failure=False` |
| Interrupt through cancellation | `AbortSignal` | `CancellationToken` | Call `interrupt()` explicitly |

Without the busy option, starting a second turn raises `TurnInProgressError` or `TurnInProgressException`. With it, the SDK enqueues the input and returns a queued result without a turn ID.

## Control a thread

| Task | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| Latest snapshot | `snapshot()` | `Snapshot` | `snapshot` |
| Re-read state | `refresh()` | `RefreshAsync()` | `refresh()` |
| Subscribe | `subscribe()` | `SubscribeAsync()` | `subscribe()` |
| Unsubscribe | `unsubscribe()` | `UnsubscribeAsync()` | `unsubscribe()` |
| Enqueue input | `enqueue()` | `EnqueueAsync()` | `enqueue()` |
| Interrupt a turn | `interrupt()` | `InterruptAsync()` | `interrupt()` |
| Change mode | `setMode()` | `SetModeAsync()` | `set_mode()` |
| Archive | `archive()` | `ArchiveAsync()` | `archive()` |
| Delete | `delete()` | `DeleteAsync()` | `delete()` |

`subscribe({ replayRecent: true })` and its language equivalents replay recent events, not a complete current-state snapshot. Call `refresh` or `read` when you need authoritative thread state.

## Stream events

TypeScript and Python normalize event names. .NET uses the Wire method name in `DotCraftRunEvent.Type` and exposes known parameters through `DotCraftRunEvent<TParams>.Params`.

| TypeScript / Python type | Wire method |
| --- | --- |
| `turn_started` | `turn/started` |
| `item_started` / `item_completed` | `item/started` / `item/completed` |
| `agent_message_delta` | `item/agentMessage/delta` |
| `reasoning_delta` | `item/reasoning/delta` |
| `tool_arguments_delta` | `item/toolCall/argumentsDelta` |
| `approval_resolved` | `item/approval/resolved` |
| `usage_delta` | `item/usage/delta` |
| `plan_updated` / `subagent_progress` / `system_event` | `plan/updated` / `subagent/progress` / `system/event` |
| `completed` / `failed` / `cancelled` | `turn/completed` / `turn/failed` / `turn/cancelled` |
| `raw` | Unknown subscribed notification |

Every event preserves the original notification. Consume the stream promptly; AppServer may disconnect a subscriber that cannot keep up.

Stopping iteration does not reliably interrupt server work. TypeScript callers should abort the supplied `AbortSignal`; .NET callers should cancel the `CancellationToken`; Python callers should read the turn ID from the stream and call `interrupt()`.

## Recover after disconnect

Reconnect restores the Wire transport, repeats initialization, and preserves local handler registrations. It does not replay in-flight requests or `turn/start`. It also does not recreate thread subscriptions or runtime tool bindings.

After reconnect:

1. Read or refresh the thread.
2. Resubscribe if the application needs thread events.
3. Rebind runtime tools when resuming the thread.
4. Start the next operation from server state.

An active .NET run fails with `RunDisconnectedException`. Do not assume a request that lost its response was never received by AppServer.

## Handle run errors

| Condition | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| Turn failed | `TurnFailedError` | `TurnFailedException` | `TurnFailedError` |
| Turn cancelled | `TurnCancelledError` | `TurnCancelledException` | `TurnCancelledError` |
| Turn already running | `TurnInProgressError` | `TurnInProgressException` | `TurnInProgressError` |

Branch on the error type or stable `code`. Treat the message as diagnostic text. See the language reference for initialization, transport, timeout, JSON-RPC, and protocol errors.

## Related docs

- [SDK quickstart](./quickstart)
- [Tools & approvals](./tools)
- [MCP runtime](./mcp-runtime)
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)
- [AppServer Protocol](../protocols/appserver-protocol)
