# SDK quickstart

Connect to a workspace and run a turn with TypeScript or .NET.

## Install

::: code-group

```bash [TypeScript]
npm install @dotcraft/sdk
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

:::

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
:::

Use `localChat` / `ConnectLocalChatAsync` when your application targets the default Chat workspace.

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
:::

See [AppServer mode](../lifecycle/appserver) for server startup, `/ws`, TLS, and token requirements.

## Run complete examples

- [TypeScript application samples](https://github.com/DotHarness/dotcraft/tree/main/sdk/typescript/samples/applications)
- [.NET agent profile and thread sample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/AgentProfileThreadSample)
- [.NET interactive tool sample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/InteractiveToolSample)

## Related docs

- [Threads & runs](./runs) — thread management, input parts, streaming, and recovery after a disconnect.
- [Tools & approvals](./tools) — expose your own tools to a run and answer its interactive callbacks.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) — the complete client surface per language.
