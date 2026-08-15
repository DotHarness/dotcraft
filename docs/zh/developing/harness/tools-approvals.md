# 添加工具并处理审批

Harness 可以将应用自有工具组合进 Agentic Loop。工具实现在应用进程内运行，并可使用与应用其他部分相同的依赖注入容器。

## 定义工具来源

继承 `AIFunctionToolSource`，将 .NET 方法公开为模型可调用的函数。

```csharp
using DotCraft.Tools;
using Microsoft.Extensions.AI;

public sealed class ClockToolSource : AIFunctionToolSource
{
    public override string SourceId => "sample.clock";

    protected override IEnumerable<AIFunction> CreateFunctions(
        ToolPlanningContext context)
    {
        yield return AIFunctionFactory.Create(
            () => DateTimeOffset.UtcNow,
            name: "GetUtcTime",
            description: "Return the current UTC time.");
    }
}
```

`CreateFunctions` 会收到当前 Thread 与 Turn 的不可变规划上下文。当工具只应在特定 workspace、模式或 Provider 能力下可用时，可以使用这个上下文进行判断。

## 注册工具来源

在注册 Harness 的同一个服务集合中注册工具来源：

```csharp
builder.Services.AddSingleton<IToolSource, ClockToolSource>();
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

Harness 构建工具规划时会收集全部 `IToolSource` 注册。请保持来源 ID 稳定，并使用清晰的工具名称，因为它们会成为模型可见工具契约的一部分。

::: tip
通过工具来源的构造函数注入应用服务。这样可以避免将凭据、数据库与 UI 状态放进静态辅助方法，也便于测试工具。
:::

## 处理工具事件

`SessionEventHandler` 可以把会话事件流转换为职责明确的回调：

```csharp
var handler = new SessionEventHandler
{
    OnTextDelta = text => ui.AppendTextAsync(text),
    OnToolStarted = (name, icon, display, callId) =>
        ui.ShowToolStartedAsync(name, display),
    OnToolCompleted = (callId, result) =>
        ui.ShowToolResultAsync(callId, result),
    OnApprovalRequested = request =>
        approvalDialog.RequestDecisionAsync(request),
    OnTurnCompleted = usage => ui.CompleteTurnAsync(usage)
};

await handler.ProcessAsync(
    sessions.SubmitInputAsync(thread.Id, prompt, ct: cancellationToken),
    (threadId, turnId, requestId, decision) =>
        sessions.ResolveApprovalAsync(
            threadId,
            turnId,
            requestId,
            decision,
            cancellationToken),
    cancellationToken);
```

处理器会等待 `OnApprovalRequested` 返回，并将决策传回 Session Core，然后继续执行。

## 选择审批决策

| 决策 | 效果 |
| --- | --- |
| `AcceptOnce` | 只允许当前请求。 |
| `AcceptForSession` | 允许当前请求，并在当前 Thread 内记住它。 |
| `AcceptAlways` | 允许请求，并为后续会话持久化审批。 |
| `Reject` | 拒绝请求的操作。 |
| `CancelTurn` | 拒绝操作并取消活动 Turn。 |

只有应用明确允许 Harness 保存 workspace 审批状态，并且用户理解持久化范围时，才应提供永久审批。面对陌生或影响较大的操作时，优先使用 `AcceptOnce`。

::: warning
不要只根据工具展示名称自动批准操作。请向用户展示具体操作、参数、受影响资源与审批范围。
:::

## 相关文档

- [Harness 总览](./)
- [线程与轮次](./threads-turns)
- [配置与路径](./configuration-paths)
