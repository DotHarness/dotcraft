# 使用 Thread 与 Turn

Thread 是持久化对话。提交输入会启动一个 Turn，并返回事件流。事件流描述文本生成、工具活动、审批、完成与失败状态。

## 解析会话服务

Host 启动后，解析由 Host 持有的 `ISessionService`：

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

文本输入可以使用字符串重载。读取返回的事件流，直到 Turn 到达终止状态。

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

图片或其他富输入可以使用 `Microsoft.Extensions.AI` 提供的 `IList<AIContent>` 重载。

| 事件 | 含义 |
| --- | --- |
| `ItemDelta` | 收到流式文本或推理内容。 |
| `ItemStarted` | 工具调用等 Item 已开始。 |
| `ItemCompleted` | Item 已完成，并可能带有结果。 |
| `ApprovalRequested` | Turn 正在等待应用决策。 |
| `TurnCompleted` | Turn 已成功完成。 |
| `TurnFailed` | Turn 因错误停止。 |

::: tip
将事件流视为活动 Turn 的事实来源。增量更新 UI，并保存 Thread ID，以便后续恢复会话或读取历史。
:::

## 恢复与暂停

继续一个当前不在内存中的已知对话前，先恢复对应 Thread：

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

如果应用希望释放活动 Runtime 状态，同时保留持久化对话，可以暂停 Thread：

```csharp
await sessions.PauseThreadAsync(threadId, cancellationToken);
```

## 归档对话

归档后的 Thread 仍然持久化，但恢复前保持只读。

```csharp
await sessions.ArchiveThreadAsync(threadId, cancellationToken);
await sessions.UnarchiveThreadAsync(threadId, cancellationToken);
```

当应用需要为现有身份创建全新对话时，可以使用 `ResetConversationAsync`。

## 相关文档

- [Harness 总览](./)
- [托管与生命周期](./hosting-lifecycle)
- [工具与审批](./tools-approvals)
- [Session Core](../architecture/session-core)
