# Threads & runs

A thread is a durable conversation (`Thread -> Turn -> Item`). The thread manager starts, resumes, and lists threads; each call returns an **active thread handle** you run turns against.

## Threads

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({ userId: "me" });
const resumed = await dotcraft.threads.resume(threadId);
const threads = await dotcraft.threads.list({ userId: "me" });

// Reuse an active or paused thread for an identity, otherwise create one:
const reused = await dotcraft.threads.getOrCreate({ userId: "me" });
```

```csharp [.NET]
var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(new SessionIdentity("my-app", Environment.UserName)));
var resumed = await client.Threads.ResumeAsync(new DotCraftThreadResumeRequest(threadId));
var threads = await client.Threads.ListAsync();
```

```python [Python]
thread = await dotcraft.threads.start(user_id="me")
resumed = await dotcraft.threads.resume(thread_id)
threads = await dotcraft.threads.list(user_id="me")

# Reuse an active or paused thread for an identity, otherwise create one:
reused = await dotcraft.threads.get_or_create(user_id="me")
```

:::

The thread handle also exposes `subscribe` / `unsubscribe`, `setMode`, `archive`, `delete`, `enqueue`, and `interrupt`.

## Running turns

`run` waits for the terminal turn and returns the merged reply. `runStreamed` yields normalized events as they arrive. Both accept the same input — a string, an array of input parts, or `{ input, sender }`.

::: code-group

```ts [TypeScript]
// Buffered
const result = await thread.run("Run the tests and summarize failures.");
console.log(result.text);

// Streamed
for await (const event of thread.runStreamed("Now fix them.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
}
```

```csharp [.NET]
// Buffered
var result = await thread.RunAsync("Run the tests and summarize failures.");
Console.WriteLine(result.Text);

// Streamed
await foreach (var e in thread.RunStreamedAsync("Now fix them."))
{
    if (e.Type == DotCraftRunEventTypes.AgentMessageDelta)
        Console.Write(e.Params.GetProperty("delta").GetString());
}
```

```python [Python]
# Buffered
result = await thread.run("Run the tests and summarize failures.")
print(result.text)

# Streamed
async for event in thread.run_streamed("Now fix them."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## Run options

| Option | Meaning |
|--------|---------|
| `sender` | Per-turn sender context (useful in group chats). |
| `enqueueIfBusy` / `enqueue_if_busy` | When a turn is already running, enqueue the input instead of raising. |
| abort / cancellation | Cancelling after the turn starts interrupts the running turn. |

::: code-group

```ts [TypeScript]
const result = await thread.run("Later: deploy to staging.", { enqueueIfBusy: true });
```

```csharp [.NET]
var result = await thread.RunAsync(
    "Later: deploy to staging.",
    new RunOptions { EnqueueIfBusy = true });
```

```python [Python]
result = await thread.run("Later: deploy to staging.", enqueue_if_busy=True)
```

:::

## The event model

`runStreamed` emits **normalized** events. The `type` is the same string in every SDK:

| `type` | Wire source |
|--------|-------------|
| `turn_started` | `turn/started` |
| `item_started` | `item/started` |
| `agent_message_delta` | `item/agentMessage/delta` |
| `reasoning_delta` | `item/reasoning/delta` |
| `tool_arguments_delta` | `item/toolCall/argumentsDelta` |
| `item_completed` | `item/completed` |
| `approval_resolved` | `item/approval/resolved` |
| `usage_delta` | `item/usage/delta` |
| `plan_updated` / `subagent_progress` / `system_event` | `plan/updated` / `subagent/progress` / `system/event` |
| `completed` / `failed` / `cancelled` | `turn/completed` / `turn/failed` / `turn/cancelled` |
| `raw` | Any subscribed notification not otherwise normalized |

Every event keeps the original notification on `raw`, so nothing is lost. The buffered `run` reuses the same stream and merges agent-message deltas with the final snapshot, so `result.text` is never duplicated.

## Reconnect boundaries

Reconnect restores the Wire transport, repeats `initialize`, and preserves local handler registrations. It does not recreate a thread subscription or rebind Runtime Dynamic Tools on your behalf. An active .NET Run fails with `RunDisconnectedError`; no SDK binding replays `turn/start`.

Treat a Run interrupted by disconnection as interrupted application work. After the connection is ready again, explicitly read or resume the thread to obtain current server state, resubscribe if needed, and start the next operation from that state. Do not assume a request that was already sent will be replayed.

## Errors

A failed or cancelled turn raises a typed error from `run` (and surfaces as a `failed` / `cancelled` event in `runStreamed`):

| Condition | TypeScript | .NET | Python |
|-----------|-----------|------|--------|
| Agent execution failed | `TurnFailedError` | `TurnFailedError` | `TurnFailedError` |
| Turn cancelled | `TurnCancelledError` | `TurnCancelledError` | `TurnCancelledError` |
| A turn was already running | `TurnInProgressError` | `TurnInProgressError` | `TurnInProgressError` |

Pass `enqueueIfBusy` to turn the last case into an enqueue instead of an error.

## Related docs

- [Tools & approvals](./tools) — extend a turn with your own tools and approval handling.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
