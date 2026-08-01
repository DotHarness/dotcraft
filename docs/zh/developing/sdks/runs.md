# 线程与运行

线程是一段持久化对话（`Thread -> Turn -> Item`）。线程管理器负责开启、恢复、列出线程；每次调用都返回一个**活动的线程句柄**，你在其上运行轮次。

## 线程

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({ userId: "me" });
const resumed = await dotcraft.threads.resume(threadId);
const threads = await dotcraft.threads.list({ userId: "me" });

// 复用某身份下活动或暂停的线程，否则新建：
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

# 复用某身份下活动或暂停的线程，否则新建：
reused = await dotcraft.threads.get_or_create(user_id="me")
```

:::

线程句柄还提供 `subscribe` / `unsubscribe`、`setMode`、`archive`、`delete`、`enqueue` 与 `interrupt`。

## 运行轮次

`run` 等待终止轮次并返回合并后的回复；`runStreamed` 随到随发地产出归一化事件。两者接受相同的输入——字符串、输入 part 数组，或 `{ input, sender }`。

::: code-group

```ts [TypeScript]
// 缓冲式
const result = await thread.run("Run the tests and summarize failures.");
console.log(result.text);

// 流式
for await (const event of thread.runStreamed("Now fix them.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
}
```

```csharp [.NET]
// 缓冲式
var result = await thread.RunAsync("Run the tests and summarize failures.");
Console.WriteLine(result.Text);

// 流式
await foreach (var e in thread.RunStreamedAsync("Now fix them."))
{
    if (e.Type == DotCraftRunEventTypes.AgentMessageDelta)
        Console.Write(e.Params.GetProperty("delta").GetString());
}
```

```python [Python]
# 缓冲式
result = await thread.run("Run the tests and summarize failures.")
print(result.text)

# 流式
async for event in thread.run_streamed("Now fix them."):
    if event.type == "agent_message_delta":
        print(event.params["delta"], end="", flush=True)
```

:::

## 运行选项

| 选项 | 含义 |
|------|------|
| `sender` | 每轮的发送者上下文（群聊时有用）。 |
| `enqueueIfBusy` / `enqueue_if_busy` | 已有轮次在运行时，把输入排队而非抛错。 |
| abort / 取消 | 轮次开始后取消会中断正在运行的轮次。 |

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

## 事件模型

`runStreamed` 产出**归一化**事件。`type` 在每个 SDK 中都是同一个字符串：

| `type` | wire 来源 |
|--------|-----------|
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
| `raw` | 任何未被归一化的订阅通知 |

每个事件都在 `raw` 上保留原始通知，不丢信息。缓冲式 `run` 复用同一条流，并把 agent-message 增量与最终快照合并，因此 `result.text` 不会重复。

## 重连边界

重连会恢复 Wire 传输、重新执行 `initialize`，并保留本地 handler 注册。它不会替你重建 thread subscription、恢复活动 Run 或重新绑定运行时动态工具。

应把因断线中断的 Run 视为已经中断的应用工作。连接再次 ready 后，显式读取或恢复 thread 以取得当前服务端状态；需要时重新订阅，再从该状态发起下一项操作。不要假定已经发送的请求会被重放。

## 错误

失败或取消的轮次会让 `run` 抛出 typed error（在 `runStreamed` 中表现为 `failed` / `cancelled` 事件）：

| 情形 | TypeScript | .NET | Python |
|------|-----------|------|--------|
| Agent 执行失败 | `TurnFailedError` | `TurnFailedError` | `TurnFailedError` |
| 轮次被取消 | `TurnCancelledError` | `TurnCancelledError` | `TurnCancelledError` |
| 已有轮次在运行 | `TurnInProgressError` | `TurnInProgressError` | `TurnInProgressError` |

传入 `enqueueIfBusy` 可把最后一种情形从抛错变成排队。

## 相关文档

- [工具与审批](./tools)——用你自己的工具与审批处理扩展一轮。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
