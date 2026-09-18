# Add tools and handle approvals

Harness composes application-owned tools into the Agentic Loop. Tool implementations stay in your process and share the same dependency injection container as the rest of the application.

## Define a tool source

Write ordinary typed methods with `[GeneratedTool]`, then expose their generated wrappers through `AIFunctionToolSource`. The Harness NuGet package includes the generator; no separate analyzer reference is needed. This example assumes the application assembly is named `MyApp`.

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

`CreateFunctions` receives immutable planning context for the current Thread and Turn. Use it to decide whether to emit a function that belongs only to a particular workspace, mode, or provider capability.

The generator owns schema generation and typed argument binding. Describe each model parameter with `[Description]`; C# defaults become optional arguments. `[Tool]` uses the same generator and adds built-in catalog/presentation metadata. Plugins normally use `[GeneratedTool]`, which is not catalog-visible by default.

### Access live invocation identity only when needed

Most tools need only typed parameters, constructor-injected services, and an optional `CancellationToken`. Request `ToolInvocationContext` only when the method needs the live calling identity, and return `ToolExecutionResult` only when it needs explicit success or failure semantics. See [Write typed tools](../integrations/dotnet-plugins#write-typed-tools) for both contracts.

## Register the source

Register the source in the same service collection as Harness:

```csharp
builder.Services.AddSingleton<IToolSource, ClockToolSource>();
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

Harness collects all `IToolSource` registrations when it builds the tool plan. Keep source IDs stable and tool names descriptive. Both become part of the model-visible tool contract.

> [!TIP]
> Inject application services into the tool source constructor. This keeps credentials, databases, and UI state out of static helpers and makes tools straightforward to test.

## Process tool events

`SessionEventHandler` converts the [session event stream](./threads-turns) into focused callbacks:

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

The handler waits for `OnApprovalRequested` and sends its decision back to Session Core before execution resumes.

## Choose an approval decision

| Decision | Effect |
| --- | --- |
| `AcceptOnce` | Allow this request only. |
| `AcceptForSession` | Allow the request and remember it for the current Thread. |
| `AcceptAlways` | Allow the request and write the approval into workspace approval state. Neither the current Thread nor later sessions ask again. |
| `Reject` | Reject the operation and let the Turn continue. |
| `CancelTurn` | Reject the operation and cancel the active Turn. |

Prefer `AcceptOnce` for unfamiliar or high-impact operations. Offer `AcceptAlways` only where the user understands the scope of a permanent approval.

Approval requests expire. Without a decision within five minutes, Session Core resolves the request as `Reject` and the Turn continues. Adjust the window per Thread with `ThreadConfiguration.ApprovalTimeoutSeconds`.

> [!CAUTION]
> Do not approve tools automatically based only on their display name. Present the operation, arguments, affected resources, and approval scope to the user.

## Present shell command approvals

A shell request carries `request.Shell` with what the safety check found: `Risk` is `Dangerous`, `OutsideWorkspace`, or `Rule`, and `Reasons` explains the prompt. Show the command and its directory as they arrive in `operation` and `target`.

`RememberedPrefixes` and `RemembersExactCommand` describe what `AcceptAlways` will store: an allow rule per prefix in the workspace's `shell-rules.json`, the exact command key, or both. State that scope in the `AcceptAlways` option itself so the user sees it before choosing. `AcceptForSession` always keys on the exact command, shell, and directory.

## Related docs

- [Configuration and paths](./configuration-paths) — the workspace data directory where `AcceptAlways` state is written.
- [Session Core](../architecture/session-core) — how one approval event is presented across entry points.
