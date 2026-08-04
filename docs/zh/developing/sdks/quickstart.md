# SDK 快速开始

使用 TypeScript、.NET 或 Python 连接工作区并运行一个 turn。

## 安装

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

`DotCraft.Sdk` 已发布到 NuGet。TypeScript 和 Python 仍是源码预览，尚未发布到 npm 或 PyPI。源码安装应使用与本文档匹配的 release tag，不要跟随 `main`。

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

```python [Python]
from dotcraft import DotCraft, LocalOptions

dotcraft = await DotCraft.connect_local(
    LocalOptions(workspace_path="/path/to/workspace")
)
```

:::

应用面向默认 Chat 工作区时，使用 `localChat` / `ConnectLocalChatAsync` / `connect_local_chat`。

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

```python [Python]
thread = await dotcraft.threads.start(user_id="me")
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

```python [Python]
result = await thread.run("Summarize this project.")
print(result.text)
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

```python [Python]
async for event in thread.run_streamed("List the open questions."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
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

```python [Python]
await dotcraft.close()
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

```python [Python]
import os
from dotcraft import RemoteOptions

dotcraft = await DotCraft.connect_remote(RemoteOptions(
    url="wss://server.example/ws",
    token=os.getenv("DOTCRAFT_TOKEN"),
))
```

:::

服务端启动、`/ws`、TLS 和 token 要求见 [AppServer 模式](../lifecycle/appserver)。

## 运行完整示例

- [TypeScript 应用示例](https://github.com/DotHarness/dotcraft/tree/main/sdk/typescript/examples)
- [Python Run-profile 示例](https://github.com/DotHarness/dotcraft/tree/main/sdk/python/examples)

## 相关文档

- [线程与运行](./runs)
- [工具与审批](./tools)
- [渠道适配器](./channels)
- 参考：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)
