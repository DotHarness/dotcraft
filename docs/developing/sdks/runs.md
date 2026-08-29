# Threads and runs

A thread is a durable conversation. A run starts one turn on that thread and either returns the final result or streams events as work progresses. The examples below continue from a connected client — see the [SDK quickstart](./quickstart) for the connection step.

## Manage threads

Start a new thread, resume one by ID, or list threads for an identity.

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({ userId: "me" });
const resumed = await dotcraft.threads.resume(threadId);
const threads = await dotcraft.threads.list({ userId: "me" });
const snapshot = await dotcraft.threads.read(threadId);
```

```csharp [.NET]
var identity = new SessionIdentity { ChannelName = "my-app", UserId = Environment.UserName };
var thread = await client.Threads.StartAsync(new ThreadStartParams { Identity = identity });
var resumed = await client.Threads.ResumeAsync(new ThreadResumeParams { ThreadId = threadId });
var threads = await client.Threads.ListAsync(new ThreadListParams { Identity = identity });
var snapshot = await client.Threads.ReadAsync(threadId);
```

:::

TypeScript also provides `getOrCreate`. It returns the identity's first active or paused thread — resuming the paused one — and starts a new thread only when neither exists.

`read` returns the current Thread header and runtime state without conversation history. Read history through bounded Turn and Item pages:

::: code-group

```ts [TypeScript]
const turns = await dotcraft.threads.listTurns(threadId, {
  limit: 20,
  sortDirection: "descending",
});
const items = await dotcraft.threads.listItems(threadId, {
  turnId: turns.data[0]?.id,
  limit: 100,
  sortDirection: "ascending",
});
```

```csharp [.NET]
var turns = await client.Threads.ListTurnsAsync(new ThreadTurnsListParams
{
    ThreadId = threadId,
    Limit = 20,
    SortDirection = "descending"
});
var items = await client.Threads.ListItemsAsync(new ThreadItemsListParams
{
    ThreadId = threadId,
    TurnId = turns.Data.FirstOrDefault()?.Id,
    Limit = 100,
    SortDirection = "ascending"
});
```

:::

Turn pages contain metadata without Items. Item pages include each Item's owning Turn ID and can span the whole Thread or one Turn. Follow `nextCursor` / `NextCursor` with the same Thread, scope, optional Turn, and direction to read another page. Treat cursors as opaque.

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

:::

Both high-level clients return the current `ThreadConfiguration` through a thread read. The .NET client adds a read-modify-write helper for the model fields that preserves unrelated and unknown configuration fields:

```csharp
var configuration = await client.Threads.UpdateModelConfigurationAsync(
    thread.Id,
    providerId: "<provider-id>",
    model: "<model-id>",
    reasoning: new ReasoningConfig { Enabled = true, Effort = "high" },
    speed: null,
    contextWindow: null);
```

TypeScript exposes model discovery at the high level but not this configuration helper. Applications using the typed Wire layer must update the complete `ThreadConfiguration` and preserve fields they do not own. Do not infer model IDs or reasoning options across providers — use the catalog returned by the connected AppServer.

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

:::

| Part | Purpose | TypeScript helper |
| --- | --- | --- |
| `text` | Literal user text | `textPart` |
| `fileRef` | Workspace or local file reference | `fileRefPart` |
| `image` | Base64 `data:image/...` URL | `imageDataUrlPart` |
| `localImage` | Image path readable by AppServer | `localImagePart` |
| `skillRef` | Skill reference | `skillRefPart` |
| `commandRef` | Custom command reference | `commandRefPart` |

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

:::

## Read the result

| Value | TypeScript | .NET |
| --- | --- | --- |
| Merged reply | `result.text` | `result.Text` |
| Thread ID | `result.thread.id` | `result.ThreadId` |
| Turn ID | `result.turn?.id` | `result.TurnId` |
| Terminal turn | `result.turn` | `result.Turn` |
| Items and usage | `result.items`, `result.usage` | `result.Turn?.Items`, `result.Turn?.TokenUsage` |
| Raw events | `result.rawEvents` | `result.RawEvents` |

Raw events are collected only when the language-specific `collectRawEvents` / `CollectRawEvents` option is enabled.

## Run options

| Behavior | TypeScript | .NET |
| --- | --- | --- |
| Sender context | `sender` | `RunOptions.Sender` |
| Queue when busy | `enqueueIfBusy` | `RunOptions.EnqueueIfBusy` |
| Collect raw events | `collectRawEvents` | `RunOptions.CollectRawEvents` |
| Return failed terminal turns | Not available | `RunOptions.ThrowOnFailure = false` |
| Interrupt through cancellation | `AbortSignal` | `CancellationToken` |

Without the busy option, starting a second turn raises `TurnInProgressError` or `TurnInProgressException`. With it, the SDK enqueues the input and returns a queued result without a turn ID.

## Control a thread

| Task | TypeScript | .NET |
| --- | --- | --- |
| Latest snapshot | `snapshot()` | `Snapshot` |
| Re-read state | `refresh()` | `RefreshAsync()` |
| Subscribe | `subscribe()` | `SubscribeAsync()` |
| Unsubscribe | `unsubscribe()` | `UnsubscribeAsync()` |
| Enqueue input | `enqueue()` | `EnqueueAsync()` |
| Interrupt a turn | `interrupt()` | `InterruptAsync()` |
| Change mode | `setMode()` | `SetModeAsync()` |
| Archive | `archive()` | `ArchiveAsync()` |
| Delete | `delete()` | `DeleteAsync()` |

`subscribe({ replayRecent: true })` and its language equivalents replay recent events, not a complete current-state snapshot. Call `refresh` or `read` for authoritative header state, and use the history page methods for persisted Turns and Items.

## Stream events

TypeScript normalizes event names. .NET uses the Wire method name in `DotCraftRunEvent.Type` and exposes known parameters through `DotCraftRunEvent<TParams>.Params`.

| TypeScript type | Wire method |
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

Every event preserves the original notification. Consume the stream promptly: a client that falls behind is buffered only up to a limit, after which AppServer drops the connection.

Stopping iteration does not reliably interrupt server work. To stop the turn, abort the supplied `AbortSignal` in TypeScript or cancel the `CancellationToken` in .NET.

## Recover after disconnect

Reconnect restores the Wire transport, repeats initialization, and preserves local handler registrations. It does not replay in-flight requests or `turn/start`. It also does not recreate thread subscriptions or runtime tool bindings.

After reconnect:

1. Resubscribe if the application needs thread events.
2. Read or refresh the Thread header.
3. Read the latest Turn and Item pages when the application displays history. Start from a new page instead of reusing a cursor from the previous connection.
4. Rebind runtime tools when resuming the thread.
5. Start the next operation from server state.

An active .NET run fails with `RunDisconnectedException`. Do not assume a request that lost its response was never received by AppServer.

## Handle run errors

| Condition | TypeScript | .NET |
| --- | --- | --- |
| Turn failed | `TurnFailedError` | `TurnFailedException` |
| Turn cancelled | `TurnCancelledError` | `TurnCancelledException` |
| Turn already running | `TurnInProgressError` | `TurnInProgressException` |

Branch on the error type or stable `code`. Treat the message as diagnostic text. See the language reference for initialization, transport, timeout, JSON-RPC, and protocol errors.

## Related docs

- [Tools & approvals](./tools) — runtime tools and the approval callbacks a run raises.
- [AppServer Protocol](../protocols/appserver-protocol) — the wire methods and error codes behind these events.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) — the complete client surface per language.
