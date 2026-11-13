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

TypeScript also provides `getOrCreate`. It returns the identity's first active thread and starts a new thread only when none exists.

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
    speed: null);
```

TypeScript exposes model discovery at the high level but not this configuration helper. Applications using the typed Wire layer must update the complete `ThreadConfiguration` and preserve fields they do not own. Do not infer model IDs or reasoning options across providers — use the catalog returned by the connected AppServer.

## Build input

Pass a string for plain text. Use input parts for files, images, or skills.

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

.NET constructs the generated `InputPart` contract directly. High-level clients do not convert leading `$skill` or `@file` text into structured parts.

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
| Provider and model | `providerId`, `model` | `RunOptions.ProviderId`, `RunOptions.Model` |
| Reasoning and speed | `reasoning`, `speed` | `RunOptions.Reasoning`, `RunOptions.Speed` |
| Collect raw events | `collectRawEvents` | `RunOptions.CollectRawEvents` |
| Return failed terminal turns | Not available | `RunOptions.ThrowOnFailure = false` |
| Interrupt through cancellation | `AbortSignal` | `CancellationToken` |

A run starts a turn when idle or adds guidance to an active regular turn. Model settings travel with the input and become defaults for later turns. A new turn uses them immediately; adding guidance does not change the active turn's model. Maintenance or an execution that cannot accept guidance is rejected. Use `enqueue()` / `EnqueueAsync()` explicitly to request a separate future turn.

When a run joins an active turn, it returns that shared turn's full final reply, not just text generated after this input. Multiple calls can therefore return the same turn ID and result. Cancelling such a run interrupts the shared turn, not only its guidance message.

## Control a thread

| Task | TypeScript | .NET |
| --- | --- | --- |
| Latest snapshot | `snapshot()` | `Snapshot` |
| Re-read state | `refresh()` | `RefreshAsync()` |
| Unsubscribe | `unsubscribe()` | `UnsubscribeAsync()` |
| Enqueue input | `enqueue()` | `EnqueueAsync()` |
| Interrupt a turn | `interrupt()` | `InterruptAsync()` |
| Change mode | `setMode()` | `SetModeAsync()` |
| Archive | `archive()` | `ArchiveAsync()` |
| Delete | `delete()` | `DeleteAsync()` |

Starting, resuming, and forking a thread attach to events automatically. `unsubscribe` releases observation while execution continues. `refresh` and `read` read the header; Turn and Item pages provide history.

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

Reconnect restores the Wire transport, repeats initialization, and preserves local handler registrations. It does not replay in-flight requests or `turn/start`.

High-level handles resume before the next run with their original tool declarations. Raw Wire callers resume explicitly and apply the returned state before handling subsequent events and requests.

An active .NET run fails with `RunDisconnectedException`. Do not assume a request that lost its response was never received by AppServer.

## Handle run errors

| Condition | TypeScript | .NET |
| --- | --- | --- |
| Turn failed | `TurnFailedError` | `TurnFailedException` |
| Turn cancelled | `TurnCancelledError` | `TurnCancelledException` |
| Execution cannot accept guidance | `TurnInProgressError` | `TurnInProgressException` |

Branch on the error type or stable `code`. Treat the message as diagnostic text. See the language reference for initialization, transport, timeout, JSON-RPC, and protocol errors.

## Related docs

- [Tools & approvals](./tools) — runtime tools and the approval callbacks a run raises.
- [AppServer Protocol](../protocols/appserver) — the wire methods and error codes behind these events.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) — the complete client surface per language.
