# SDK 快速开始

用 DotCraft SDK 连接工作区并运行第一个 turn。在每组代码中选择 TypeScript、.NET 或 Python。

## 安装

::: code-group

```bash [TypeScript]
git clone https://github.com/DotHarness/dotcraft.git
npm --prefix ./dotcraft/sdk/typescript install
npm --prefix ./dotcraft/sdk/typescript run build
# 最后一条命令在你的应用目录中执行：
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
> `DotCraft.Sdk` 已发布到 NuGet。TypeScript 和 Python 包目前是源码预览，尚未发布到 npm 或 PyPI；请按上面的方式从本地仓库构建或安装。

## 1. 连接

`local` 会发现或启动本地 [Hub](../lifecycle/hub)，并为你的工作区确保一个 [AppServer](../protocols/appserver-protocol)——传入工作区路径。当工作区运行在别处时，改用 `remote` 连接已知的 AppServer WebSocket URL（`ws://host:port/...`）。

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

## 2. 开启线程

线程是一段持久化对话。可以新建一个，或用 `getOrCreate` / `get_or_create` 复用某个身份已有的线程。

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

## 3. 运行一轮

`run` 提交输入并等待该轮结束，返回合并后的助手回复。

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

## 4. 流式接收事件

`runStreamed` 随到随发地产出归一化事件——文本增量、条目生命周期，以及终止轮次。

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

## 相关文档

- [线程与运行](./runs)——线程生命周期、运行选项、归一化事件模型。
- [工具与审批](./tools)——运行时动态工具、审批与用户输入回调。
- [渠道适配器](./channels)——构建外部渠道（TypeScript 与 Python）。
- 参考卡片：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
