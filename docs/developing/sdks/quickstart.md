# SDK quickstart

Connect to a workspace and run a turn with TypeScript, .NET, or Python.

## Install

::: code-group

```bash [TypeScript]
DOTCRAFT_RELEASE_TAG="replace-with-release-tag"
git clone --branch "$DOTCRAFT_RELEASE_TAG" https://github.com/DotHarness/dotcraft.git
npm --prefix ./dotcraft/sdk/typescript install
npm --prefix ./dotcraft/sdk/typescript run build
npm install /absolute/path/to/dotcraft/sdk/typescript
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

```bash [Python]
DOTCRAFT_RELEASE_TAG="replace-with-release-tag"
git clone --branch "$DOTCRAFT_RELEASE_TAG" https://github.com/DotHarness/dotcraft.git
python -m pip install -e /absolute/path/to/dotcraft/sdk/python
```

:::

`DotCraft.Sdk` is published on NuGet. TypeScript and Python are source previews and are not published to npm or PyPI. For source installs, use the release tag that matches these docs instead of following `main`.

## 1. Connect

Connect to a workspace through the local [Hub](../lifecycle/hub):

::: code-group

```ts [TypeScript]
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({ workspacePath: "/path/to/workspace" });
```

```csharp [.NET]
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;

var client = await DotCraftClient.ConnectLocalAsync(
    "/path/to/workspace",
    new DotCraftLocalOptions { ClientName = "my-app", ClientVersion = "dev" });
```

```python [Python]
from dotcraft import DotCraft, LocalOptions

dotcraft = await DotCraft.connect_local(
    LocalOptions(workspace_path="/path/to/workspace")
)
```

:::

Use `localChat` / `ConnectLocalChatAsync` / `connect_local_chat` when your application targets the default Chat workspace.

## 2. Start a thread

A thread is a durable conversation.

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

`run` waits for the terminal turn and returns the merged assistant reply.

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

Use the streaming form when your application needs progress before the turn ends.

::: code-group

```ts [TypeScript]
for await (const event of thread.runStreamed("List the open questions.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
}
```

```csharp [.NET]
await foreach (var runEvent in thread.RunStreamedAsync("List the open questions."))
{
    if (runEvent is DotCraftRunEvent<ItemDeltaNotification> delta &&
        runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta)
        Console.Write(delta.Params.Delta);
}
```

```python [Python]
async for event in thread.run_streamed("List the open questions."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## 5. Close the client

Close the SDK connection when your application finishes. This does not stop a Hub-managed AppServer.

::: code-group

```ts [TypeScript]
await dotcraft.close();
```

```csharp [.NET]
await client.DisposeAsync();
```

```python [Python]
await dotcraft.close()
```

:::

## Connect remotely

Start AppServer in WebSocket mode, then connect to its `/ws` endpoint. Pass tokens separately so they are not copied into logs with the URL.

::: code-group

```ts [TypeScript]
const dotcraft = await DotCraft.remote({
  url: "wss://server.example/ws",
  token: process.env.DOTCRAFT_TOKEN,
});
```

```csharp [.NET]
var client = await DotCraftClient.ConnectRemoteAsync(
    "wss://server.example/ws",
    new DotCraftRemoteOptions
    {
        Token = Environment.GetEnvironmentVariable("DOTCRAFT_TOKEN"),
    });
```

```python [Python]
import os
from dotcraft import RemoteOptions

dotcraft = await DotCraft.connect_remote(RemoteOptions(
    url="wss://server.example/ws",
    token=os.getenv("DOTCRAFT_TOKEN"),
))
```

:::

See [AppServer mode](../lifecycle/appserver) for server startup, `/ws`, TLS, and token requirements.

## Run complete examples

- [TypeScript application example](https://github.com/DotHarness/dotcraft/tree/main/sdk/typescript/examples)
- [Python Run-profile example](https://github.com/DotHarness/dotcraft/tree/main/sdk/python/examples)

## Related docs

- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [Channel adapters](./channels)
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)
