# Add tools and handle approvals

Harness can compose application-owned tools into the Agentic Loop. Tool implementations stay in your process and can use the same dependency injection container as the rest of the application.

## Define a tool source

Derive from `AIFunctionToolSource` to expose .NET methods as model-callable functions.

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

`CreateFunctions` receives immutable planning context for the current Thread and Turn. Use it when a tool should only be available for a particular workspace, mode, or provider capability.

## Register the source

Register the source in the same service collection as Harness:

```csharp
builder.Services.AddSingleton<IToolSource, ClockToolSource>();
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

Harness collects all `IToolSource` registrations when it builds the tool plan. Keep source IDs stable and tool names descriptive because they become part of the model-visible tool contract.

> [!TIP]
> Inject application services into the tool source constructor. This keeps credentials, databases, and UI state out of static helpers and makes tools straightforward to test.

## Process tool events

`SessionEventHandler` converts the session event stream into focused callbacks:

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
| `AcceptAlways` | Allow and persist the approval for future sessions. |
| `Reject` | Reject the requested operation. |
| `CancelTurn` | Reject the operation and cancel the active Turn. |

Only offer persistent approval when the application intentionally allows Harness to store workspace approval state and the user understands the scope. Prefer `AcceptOnce` for unfamiliar or high-impact operations.

> [!CAUTION]
> Do not approve tools automatically based only on their display name. Present the operation, arguments, affected resources, and approval scope to the user.

## Related docs

- [Harness overview](./)
- [Threads and Turns](./threads-turns)
- [Configuration and paths](./configuration-paths)
