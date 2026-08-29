# 线程与运行

Thread 是持久化的对话。Run 在该 thread 上启动一个 turn，并返回最终结果或持续输出执行事件。下面的示例都从一个已连接的 client 继续，连接步骤见 [SDK 快速开始](./quickstart)。

## 管理 thread

启动新 thread、按 ID 恢复 thread，或列出某个 identity 的 thread。

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

TypeScript 还提供 `getOrCreate`。它返回该 identity 下第一个 active 或 paused 的 thread（paused 的会先恢复），两者都不存在时才启动新 thread。

`read` 返回当前 Thread 头部和 runtime 状态，不包含对话历史。通过有界的 Turn 和 Item 分页读取历史：

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

Turn 页只包含元数据，不包含 Item。Item 页会带上每个 Item 所属的 Turn ID，并可跨整个 Thread 或限定到一个 Turn。使用相同的 Thread、scope、可选 Turn 和方向继续传入 `nextCursor` / `NextCursor` 读取下一页。请把 cursor 视为 opaque token。

## 选择模型

在展示模型选择器或验证已保存配置前，先发现模型目录。

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

两种高层 client 都会通过 thread read 返回当前 `ThreadConfiguration`。.NET client 另外提供针对模型字段的 read-modify-write helper，它会保留无关和未知的配置字段：

```csharp
var configuration = await client.Threads.UpdateModelConfigurationAsync(
    thread.Id,
    providerId: "<provider-id>",
    model: "<model-id>",
    reasoning: new ReasoningConfig { Enabled = true, Effort = "high" },
    speed: null,
    contextWindow: null);
```

TypeScript 在高层接口提供模型发现，但没有这个配置 helper。使用类型化 Wire 层的应用必须更新完整 `ThreadConfiguration`，并保留不归自己所有的字段。不要跨 provider 推断模型 ID 或 reasoning 选项，只使用所连接 AppServer 返回的目录。

## 构造输入

纯文本直接传入字符串。文件、图片、Skill 或 Command 使用 input part。

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

| Part | 用途 | TypeScript helper |
| --- | --- | --- |
| `text` | 原样用户文本 | `textPart` |
| `fileRef` | 工作区或本地文件引用 | `fileRefPart` |
| `image` | Base64 `data:image/...` URL | `imageDataUrlPart` |
| `localImage` | AppServer 可读取的图片路径 | `localImagePart` |
| `skillRef` | Skill 引用 | `skillRefPart` |
| `commandRef` | 自定义 Command 引用 | `commandRefPart` |

.NET 直接构造生成的 `InputPart` contract。高层 client 不会把以 `/command`、`$skill` 或 `@file` 开头的文本自动转换为结构化 part。

`image` part 不接受远程图片 URL。先下载图片，再发送 data URL，或发送 AppServer 可读取的 `localImage` 路径。

## 运行 turn

需要最终结果时使用 buffered 形式，需要实时进度时使用 streamed 形式。

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

## 读取结果

| 值 | TypeScript | .NET |
| --- | --- | --- |
| 合并回复 | `result.text` | `result.Text` |
| Thread ID | `result.thread.id` | `result.ThreadId` |
| Turn ID | `result.turn?.id` | `result.TurnId` |
| 终止 turn | `result.turn` | `result.Turn` |
| Item 和 usage | `result.items`、`result.usage` | `result.Turn?.Items`、`result.Turn?.TokenUsage` |
| Raw event | `result.rawEvents` | `result.RawEvents` |

只有启用各语言的 `collectRawEvents` / `CollectRawEvents` 选项时，SDK 才收集 raw event。

## Run 选项

| 行为 | TypeScript | .NET |
| --- | --- | --- |
| Sender context | `sender` | `RunOptions.Sender` |
| Busy 时排队 | `enqueueIfBusy` | `RunOptions.EnqueueIfBusy` |
| 收集 raw event | `collectRawEvents` | `RunOptions.CollectRawEvents` |
| 返回失败终态 | 不支持 | `RunOptions.ThrowOnFailure = false` |
| 通过取消中断 | `AbortSignal` | `CancellationToken` |

未启用 busy 选项时，启动第二个 turn 会抛出 `TurnInProgressError` 或 `TurnInProgressException`。启用后，SDK 会把输入排队，并返回不含 turn ID 的 queued result。

## 控制 thread

| 任务 | TypeScript | .NET |
| --- | --- | --- |
| 最新 snapshot | `snapshot()` | `Snapshot` |
| 重新读取状态 | `refresh()` | `RefreshAsync()` |
| 订阅 | `subscribe()` | `SubscribeAsync()` |
| 取消订阅 | `unsubscribe()` | `UnsubscribeAsync()` |
| 排队输入 | `enqueue()` | `EnqueueAsync()` |
| 中断 turn | `interrupt()` | `InterruptAsync()` |
| 切换模式 | `setMode()` | `SetModeAsync()` |
| 归档 | `archive()` | `ArchiveAsync()` |
| 删除 | `delete()` | `DeleteAsync()` |

`subscribe({ replayRecent: true })` 及各语言对应形式只回放近期事件，不返回完整的当前状态。调用 `refresh` 或 `read` 获取权威头部状态，通过历史分页方法获取持久化的 Turn 和 Item。

## 读取流式事件

TypeScript 会规范化事件名称。.NET 在 `DotCraftRunEvent.Type` 中使用 Wire 方法名，并通过 `DotCraftRunEvent<TParams>.Params` 暴露已知参数。

| TypeScript 类型 | Wire 方法 |
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
| `raw` | 未知的已订阅通知 |

每个事件都保留原始通知。请及时消费事件流：client 跟不上时缓冲只到一个上限，超出后 AppServer 会断开连接。

停止迭代不一定会中断服务端工作。要真正停下这个 turn，TypeScript 中止传入的 `AbortSignal`，.NET 取消 `CancellationToken`。

## 断线后恢复

重连会恢复 Wire 传输、重新初始化并保留本地 handler 注册。它不会重放进行中的请求或 `turn/start`，也不会重建 thread subscription 或运行时工具绑定。

重连后：

1. 应用需要 thread 事件时重新订阅。
2. 读取或刷新 Thread 头部。
3. 如果应用展示历史，重新读取最新的 Turn 和 Item 页，不复用上一个连接的 cursor。
4. 恢复 thread 时重新绑定运行时工具。
5. 从服务端状态启动下一项操作。

活动的 .NET run 会以 `RunDisconnectedException` 失败。不要因为请求响应丢失就假定 AppServer 从未收到请求。

## 处理 Run 错误

| 情形 | TypeScript | .NET |
| --- | --- | --- |
| Turn 失败 | `TurnFailedError` | `TurnFailedException` |
| Turn 取消 | `TurnCancelledError` | `TurnCancelledException` |
| 已有 turn 在运行 | `TurnInProgressError` | `TurnInProgressException` |

按错误类型或稳定的 `code` 分支，message 只用于诊断。初始化、transport、timeout、JSON-RPC 和协议错误见各语言参考。

## 相关文档

- [工具与审批](./tools)——运行时工具，以及 run 触发的审批回调。
- [AppServer 协议](../protocols/appserver-protocol)——这些事件与错误码背后的 wire 方法。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet)——各语言的完整 client 接口。
