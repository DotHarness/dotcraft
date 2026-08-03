# SDK quickstart

Connect to a workspace and run your first turn with the DotCraft SDK. Pick TypeScript, .NET, or Python in each code group.

## Install

::: code-group

```bash [TypeScript]
git clone https://github.com/DotHarness/dotcraft.git
npm --prefix ./dotcraft/sdk/typescript install
npm --prefix ./dotcraft/sdk/typescript run build
# Run this last command from your application:
npm install /absolute/path/to/dotcraft/sdk/typescript
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

```bash [Python]
git clone https://github.com/DotHarness/dotcraft.git
python -m pip install -e /absolute/path/to/dotcraft/sdk/python
```

:::

> [!NOTE]
> `DotCraft.Sdk` is published on NuGet. The TypeScript and Python packages are source previews and are not currently published to npm or PyPI. Build or install them from a local checkout as shown above.

## 1. Connect

`local` discovers or starts the local [Hub](../lifecycle/hub) and ensures an [AppServer](../protocols/appserver-protocol) for your workspace — pass the workspace path. Use `remote` instead to connect to a known AppServer WebSocket URL (`ws://host:port/...`) when the workspace runs elsewhere.

::: code-group

```ts [TypeScript]
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({ workspacePath: "/path/to/workspace" });
```

```csharp [.NET]
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;

await using var client = await DotCraftClient.ConnectLocalAsync(
    "/path/to/workspace",
    new DotCraftLocalOptions { ClientName = "my-app", ClientVersion = "0.1.0" });
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
    new ThreadStartParams
    {
        Identity = new SessionIdentity
        {
            ChannelName = "my-app",
            UserId = Environment.UserName,
        },
    });
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
    if (runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta &&
        runEvent is DotCraftRunEvent<ItemDeltaNotification> delta)
    {
        Console.Write(delta.Params.Delta);
    }
}
```

```python [Python]
async for event in thread.run_streamed("And list the open questions."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## Related docs

- [Threads & runs](./runs) — thread lifecycle, run options, and the normalized event model.
- [Tools & approvals](./tools) — runtime dynamic tools and approval / user-input callbacks.
- [Channel adapters](./channels) — build external channels (TypeScript and Python).
- Reference cards: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
