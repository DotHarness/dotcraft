# SDK 快速开始

DotCraft 为 **TypeScript**、**.NET**、**Python** 提供 SDK。

## 安装

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

## 1. 连接

`local` 会发现或启动本地 Hub，并为你的工作区确保一个 AppServer。若要连接已知的 AppServer WebSocket，则用 `remote`。

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

## 2. 开启线程

线程是一段持久化对话。可以新建一个，或用 `getOrCreate` / `get_or_create` 复用某个身份已有的线程。

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

## 下一步

- [线程与运行](./runs)——线程生命周期、运行选项、归一化事件模型。
- [工具与审批](./tools)——运行时动态工具、审批与用户输入回调。
- [渠道适配器](./channels)——构建外部渠道（TypeScript 与 Python）。
- 参考卡片：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
