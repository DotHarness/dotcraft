# 使用 Thread 与 Turn

Thread 是持久化对话。提交输入会启动一个 Turn，并返回一条事件流，其中包含文本生成、工具活动、审批请求，以及 Turn 的最终结果。

![Thread 与 Turn 生命周期：Thread 由身份创建后进入活动状态，可以暂停后恢复，也可以归档后取消归档。活动期间每次提交输入运行一个 Turn，Turn 的事件流承载每个 Item 的开始、增量与完成，审批请求会阻塞 Turn 直到应用作出决策，Turn 以完成或失败结束，而 Thread 保持活动。](/thread-turn-lifecycle.svg)

## 解析会话服务

[Host](./hosting-lifecycle) 启动后，解析由它持有的 `ISessionService`：

```csharp
using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;

var sessions = host.Services.GetRequiredService<ISessionService>();
```

在 Host 运行期间复用这个服务。它是 Thread 与 Turn 操作的核心 API。

## 创建 Thread

每个 Thread 都由 `SessionIdentity` 开始。这个身份说明对话属于哪个应用入口、用户、上下文与 workspace。

```csharp
var identity = new SessionIdentity
{
    ChannelName = "my-app",
    UserId = currentUser.Id,
    ChannelContext = activeDocument.Id,
    WorkspacePath = workspacePath
};

var thread = await sessions.CreateThreadAsync(
    identity,
    displayName: "Workspace review",
    ct: cancellationToken);
```

请选择稳定的身份值。查找现有 Thread 时也会使用这些值：

```csharp
var recentThreads = await sessions.FindThreadsAsync(
    identity,
    includeArchived: false,
    ct: cancellationToken);
```

## 提交输入

文本输入使用字符串重载。读取返回的事件流，直到 Turn 结束。

```csharp
await foreach (var sessionEvent in sessions.SubmitInputAsync(
    thread.Id,
    "Summarize the current workspace.",
    ct: cancellationToken))
{
    if (sessionEvent.DeltaPayload?.TextDelta is { } text)
        Console.Write(text);
}
```

图片等富输入改用 `Microsoft.Extensions.AI` 提供的 `IList<AIContent>` 重载。

一个 Thread 同时只运行一个 Turn。上一个 Turn 结束前再次调用 `SubmitInputAsync` 会失败，改用 `EnqueueTurnInputAsync` 可以让输入排队，等当前 Turn 成功结束后自动开始下一个。

事件的 `EventType` 取自 `SessionEventType`，其中应用最常处理的是这几个：

| 事件 | 含义 |
| --- | --- |
| `ItemDelta` | 收到流式文本或推理内容。 |
| `ItemStarted` | 工具调用等 Item 已开始。 |
| `ItemCompleted` | Item 已完成，并可能带有结果。 |
| `ApprovalRequested` | Turn 正在等待应用决策。 |
| `TurnCompleted` | Turn 已成功完成。 |
| `TurnFailed` | Turn 因错误停止。 |

> [!TIP]
> 将事件流视为活动 Turn 的事实来源。增量更新 UI，并保存 Thread ID，以便后续恢复会话或读取历史。

## 恢复与暂停

继续一个不在内存中的已知对话前，先恢复对应 Thread。恢复会从持久化历史重建 Agent 会话，并把 Thread 转回活动状态：

```csharp
var resumed = await sessions.ResumeThreadAsync(threadId, cancellationToken);

await foreach (var sessionEvent in sessions.SubmitInputAsync(
    resumed.Id,
    "Continue from the previous result.",
    ct: cancellationToken))
{
    // 将事件映射到应用 UI。
}
```

暂停会把 Thread 转为 Paused。对话仍然完整持久化，但在恢复之前不能开始新的 Turn：

```csharp
await sessions.PauseThreadAsync(threadId, cancellationToken);
```

## 归档对话

归档后的 Thread 仍然持久化，但恢复前保持只读。

```csharp
await sessions.ArchiveThreadAsync(threadId, cancellationToken);
await sessions.UnarchiveThreadAsync(threadId, cancellationToken);
```

`ResetConversationAsync` 归档该身份下可复用的 Thread，并创建一个全新的 Thread。

## 相关文档

- [工具与审批](./tools-approvals)——处理这条事件流里的审批请求，并把应用自有工具接进来。
- [Session Core](../architecture/session-core)——Thread、Turn、Item 这套模型在引擎侧的样子。
