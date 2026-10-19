# 使用 Thread 与 Turn

Thread 是持久化对话。提交输入会启动一个 Turn，并返回一条事件流，其中包含文本生成、工具活动、审批请求，以及 Turn 的最终结果。

![Thread 与 Turn 生命周期：Thread 由身份创建后进入活动状态，可以归档后取消归档。活动期间每次提交输入运行一个 Turn，Turn 的事件流承载每个 Item 的开始、增量与完成，审批请求会阻塞 Turn 直到应用作出决策，Turn 以完成或失败结束，而 Thread 保持活动。](/thread-turn-lifecycle.svg)

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

## 挂载共享记忆库

每个 Thread 都有自己的记忆库。要让多个 Thread 共享知识，比如一个团队的所有 agent 都应该知道的内容，可以在 Thread 配置里在它旁边挂载最多八个记忆库。每个挂载项包含 `Id`、`Name`、供模型判断哪些内容该存进去的 `Description`，以及访问权限 `read` 或 `readWrite`：

```csharp
using DotCraft.Memory;

var thread = await sessions.CreateThreadAsync(
    identity,
    new ThreadConfiguration
    {
        MemoryStores =
        [
            new MemoryStoreAttachment("team", "Team", "What every agent of this team should know.", MemoryStoreAccess.ReadWrite),
            new MemoryStoreAttachment("policy", "Policy", "Rules maintained by administrators.", MemoryStoreAccess.Read)
        ]
    },
    ct: cancellationToken);
```

挂载的记忆库位于 `<data>/memory-stores/<id>/`，使用普通的记忆文件格式。它的内容归应用所有，应用可以随时替换其中的文件，运行中的 Thread 会在下一步看到变化。把带有 `MemoryStores` 的配置传给 `UpdateThreadConfigurationAsync`，会替换 Thread 的挂载项。

要在写入落盘前检查内容，可以注册 `IMemoryWriteGate`。agent 用普通的文件工具写挂载的记忆库，每次写入 `readWrite` 记忆库都必须得到所有 gate 的放行。被拒绝时，工具调用会带着你给出的原因失败，agent 可以改写这条记忆，或者改存到自己的记忆库：

```csharp
builder.Services.AddSingleton<IMemoryWriteGate, TeamMemoryGate>();

sealed class TeamMemoryGate : IMemoryWriteGate
{
    public ValueTask<MemoryWriteDecision> EvaluateAsync(MemoryWriteRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Content?.Contains('@') == true
            ? MemoryWriteDecision.Reject("Shared memories must not contain email addresses.")
            : MemoryWriteDecision.Admit);
}
```

请求里包含记忆库、文件在库内的路径、这次写入是新建、编辑还是删除文件，以及写入前后的内容。agent 把挂载的记忆写空即表示删除。完整约定见 [Memory 规范](https://github.com/DotHarness/dotcraft/blob/main/specs/features/memory.md)。

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

## 恢复对话

继续一个不在内存中的已知对话前，先恢复对应 Thread。恢复会从持久化历史重建 Agent 会话：

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

## 归档对话

归档后的 Thread 仍然持久化，但恢复前保持只读。

```csharp
await sessions.ArchiveThreadAsync(threadId, cancellationToken);
await sessions.UnarchiveThreadAsync(threadId, cancellationToken);
```

`ResetConversationAsync` 归档该身份下可复用的 Thread，并创建一个全新的 Thread。

## 相关文档

- [工具与审批](./tools-approvals)——处理这条事件流里的审批请求，并把应用自有工具接进来。
- [Session Core](../../contribute/architecture/session-core)——Thread、Turn、Item 这套模型在引擎侧的样子。
