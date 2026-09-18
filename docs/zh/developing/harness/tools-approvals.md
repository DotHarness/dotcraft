# 添加工具并处理审批

Harness 把应用自有的工具组合进 Agentic Loop。工具实现留在应用进程内，和应用其余部分共用同一个依赖注入容器。

## 定义工具来源

用 `[GeneratedTool]` 标记普通强类型方法，再通过 `AIFunctionToolSource` 暴露生成的包装器。Harness NuGet 包已包含生成器，无需另加 analyzer 引用。下面假设应用程序集名为 `MyApp`。

```csharp
using System.ComponentModel;
using DotCraft.GeneratedTools.MyApp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

public sealed class ClockToolSource : AIFunctionToolSource
{
    public override string SourceId => "sample.clock";

    protected override IEnumerable<AIFunction> CreateFunctions(
        ToolPlanningContext context)
    {
        yield return GeneratedToolFunctions.ClockToolSource_GetUtcTime(this);
    }

    [GeneratedTool]
    [Description("Return the current UTC time.")]
    public DateTimeOffset GetUtcTime() => DateTimeOffset.UtcNow;
}
```

`CreateFunctions` 收到当前 Thread 与 Turn 的不可变规划上下文。工具只在特定 workspace、模式或 Provider 能力下可用时，用它决定这次是否产出这个函数。

生成器负责 schema 生成与强类型参数绑定。为每个模型参数添加 `[Description]`。C# 默认值对应可选参数。`[Tool]` 使用同一个生成器，并增加内置目录和呈现元数据。插件通常使用默认不进入内置目录的 `[GeneratedTool]`。

### 按需获取真实调用身份

多数工具只需要强类型参数、构造函数注入的服务，以及可选的 `CancellationToken`。只有方法需要真实调用身份时才请求 `ToolInvocationContext`，需要显式表达成功或失败时才返回 `ToolExecutionResult`。两项契约见[编写强类型工具](../integrations/dotnet-plugins#write-typed-tools)。

## 注册工具来源

在注册 Harness 的同一个服务集合中注册工具来源：

```csharp
builder.Services.AddSingleton<IToolSource, ClockToolSource>();
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

Harness 构建工具规划时会收集全部 `IToolSource` 注册。保持来源 ID 稳定，工具名清晰。两者都会进入模型可见的工具契约。

> [!TIP]
> 通过工具来源的构造函数注入应用服务。这样可以避免把凭据、数据库与 UI 状态塞进静态辅助方法，也便于测试工具。

## 处理工具事件

`SessionEventHandler` 把[会话事件流](./threads-turns)转换为职责明确的回调：

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

处理器等待 `OnApprovalRequested` 返回，把决策传回 Session Core，然后恢复执行。

## 选择审批决策

| 决策 | 效果 |
| --- | --- |
| `AcceptOnce` | 只允许当前这次请求。 |
| `AcceptForSession` | 允许请求，并在当前 Thread 内记住它。 |
| `AcceptAlways` | 允许请求，并把审批写入 workspace 审批状态。当前 Thread 与后续会话都不再询问。 |
| `Reject` | 拒绝这次操作，Turn 继续执行。 |
| `CancelTurn` | 拒绝操作并取消活动 Turn。 |

面对陌生或影响较大的操作，优先给出 `AcceptOnce`。只有在用户清楚永久审批的范围时，才把 `AcceptAlways` 摆到界面上。

审批请求会超时。默认 5 分钟内没有决策就按 `Reject` 处理，Turn 随后继续。用 `ThreadConfiguration.ApprovalTimeoutSeconds` 可以按 Thread 调整这个窗口。

> [!CAUTION]
> 不要只根据工具展示名称自动批准操作。请向用户展示具体操作、参数、受影响资源与审批范围。

## 呈现 Shell 命令审批

Shell 请求带有 `request.Shell`，记录安全检查的结果：`Risk` 取 `Dangerous`、`OutsideWorkspace` 或 `Rule`，`Reasons` 解释为何询问。命令与目录按 `operation` 和 `target` 原样展示。

`RememberedPrefixes` 与 `RemembersExactCommand` 描述 `AcceptAlways` 会记住什么：按前缀写入工作区 `shell-rules.json` 的放行规则、精确的命令键，或两者都有。把这个范围写进 `AcceptAlways` 选项本身，让用户在选择前看到。`AcceptForSession` 始终按精确的命令、shell 和目录记忆。

## 相关文档

- [配置与路径](./configuration-paths)——`AcceptAlways` 写入的审批状态落在 workspace 数据目录里。
- [Session Core](../architecture/session-core)——同一个审批事件在不同入口如何呈现给用户。
