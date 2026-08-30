# SDK 快速开始

使用 TypeScript 或 .NET 连接工作区并运行一个 turn。

## 安装

::: code-group

```bash [TypeScript]
npm install @dotcraft/sdk
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

:::

## 1. 连接

通过本地 [Hub](../lifecycle/hub) 连接工作区：

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

应用面向默认 Chat 工作区时，使用 `localChat` / `ConnectLocalChatAsync`。

## 2. 启动 thread

Thread 是持久化的对话。

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

## 3. 运行 turn

`run` 等待 turn 结束并返回合并后的助手回复。

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

## 4. 流式读取事件

应用需要在 turn 结束前显示进度时，使用流式形式。

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

## 5. 关闭 client

应用结束时关闭 SDK 连接。这不会停止由 Hub 管理的 AppServer。

::: code-group

```ts [TypeScript]
await dotcraft.close();
```

```csharp [.NET]
await client.DisposeAsync();
```
:::

## 连接远程 AppServer

以 WebSocket 模式启动 AppServer，然后连接它的 `/ws` 端点。单独传入 token，避免日志把 token 和 URL 一起记录。

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

服务端启动、`/ws`、TLS 和 token 要求见 [AppServer 模式](../lifecycle/appserver)。

## 运行完整示例

- [TypeScript 应用示例](https://github.com/DotHarness/dotcraft/tree/main/sdk/typescript/samples/applications)
- [.NET agent profile 与 thread 示例](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/AgentProfileThreadSample)
- [.NET interactive tool 示例](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/InteractiveToolSample)

## 相关文档

- [线程与运行](./runs)——thread 管理、input part、流式事件和断线恢复。
- [工具与审批](./tools)——向 run 暴露自定义工具，并响应交互回调。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet)——各语言的完整 client 接口。
