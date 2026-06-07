# SDK Quickstart

Connect to a workspace and run your first turn with the DotCraft SDK. This tutorial covers all three SDKs — **TypeScript**, **.NET**, and **Python** — and takes you from install to a streamed reply. Pick your language in each tab.

## Install

::: code-group

```bash [TypeScript]
npm install @dotcraft/sdk
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

```bash [Python]
pip install dotcraft
```

:::

## 1. Connect

`local` discovers or starts the local [Hub](../lifecycle/hub) and ensures an [AppServer](../protocols/appserver-protocol) for your workspace — pass the workspace path. Use `remote` instead to connect to a known AppServer WebSocket URL (`ws://host:port/...`) when the workspace runs elsewhere.

::: code-group

```ts [TypeScript]
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({ workspacePath: "/path/to/workspace" });
```

```csharp [.NET]
using DotCraft.Sdk.AppServer;

await using var client = await DotCraftClient.ConnectLocalAsync(
    "/path/to/workspace",
    new DotCraftLocalClientOptions { ClientName = "my-app", ClientVersion = "0.1.0" });
```

```python [Python]
from dotcraft import DotCraft, LocalOptions

dotcraft = await DotCraft.connect_local(LocalOptions(workspace_path="/path/to/workspace"))
```

:::

## 2. Start a thread

A thread is a persistent conversation. Start a fresh one, or reuse an existing one for an identity with `getOrCreate` / `get_or_create`.

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({ userId: "me" });
```

```csharp [.NET]
var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(new SessionIdentity("my-app", Environment.UserName)));
```

```python [Python]
thread = await dotcraft.threads.start(user_id="me")
```

:::

## 3. Run a turn

`run` submits input and waits for the turn to finish, returning the merged assistant reply.

::: code-group

```ts [TypeScript]
const result = await thread.run("Summarize this project.");
console.log(result.text);
```

```csharp [.NET]
var result = await thread.RunAsync("Summarize this project.");
Console.WriteLine(result.Text);
```

```python [Python]
result = await thread.run("Summarize this project.")
print(result.text)
```

:::

## 4. Stream events

`runStreamed` yields normalized events as they arrive — text deltas, item lifecycle, and the terminal turn.

::: code-group

```ts [TypeScript]
for await (const event of thread.runStreamed("And list the open questions.")) {
  if (event.type === "agent_message_delta") {
    process.stdout.write(event.delta ?? "");
  }
}
```

```csharp [.NET]
await foreach (var runEvent in thread.RunStreamedAsync("And list the open questions."))
{
    if (runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta)
    {
        Console.Write(runEvent.Params.GetProperty("delta").GetString());
    }
}
```

```python [Python]
async for event in thread.run_streamed("And list the open questions."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## Next steps

- [Threads & runs](./runs) — thread lifecycle, run options, and the normalized event model.
- [Tools & approvals](./tools) — runtime dynamic tools and approval / user-input callbacks.
- [Channel adapters](./channels) — build external channels (TypeScript and Python).
- Reference cards: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
