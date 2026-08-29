# Tools & approvals

Add application-owned tools to a thread, then handle the approval and user-input requests AppServer sends back.

## Runtime dynamic tools

Declare tools when you start or resume a thread. The declaration crosses the Wire boundary. The handler stays in your application process.

::: code-group

```ts [TypeScript]
const thread = await dotcraft.threads.start({
  userId: "me",
  dynamicTools: [
    {
      namespace: "myapp",
      name: "GetIssue",
      description: "Read an issue from MyApp.",
      inputSchema: { type: "object", properties: { id: { type: "string" } }, required: ["id"] },
      handler: async (call) => ({
        success: true,
        contentItems: [{ type: "text", text: "Issue loaded." }],
        structuredContent: await getIssue(call.arguments.id as string),
      }),
    },
  ],
});
```

```csharp [.NET]
using System.ComponentModel;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.DynamicTools;

public sealed class GetIssueArgs
{
    [Description("Issue id to read.")]
    public required string Id { get; init; }
}

public sealed class IssueTools(IssueStore issues)
{
    [DynamicTool("GetIssue", "Read an issue from MyApp.")]
    public Task<Issue> GetIssueAsync(GetIssueArgs args, CancellationToken ct) =>
        issues.GetIssueAsync(args.Id, ct);
}

var registry = new DynamicToolRegistry();
registry.Register(new IssueTools(issueStore), "myapp");

var declarations = RuntimeDynamicToolDeclarationBuilder.Build(
    registry.ListDescriptors(),
    new Dictionary<string, string> { ["myapp"] = "MyApp issue tools." });

var thread = await client.Threads.StartAsync(
    new ThreadStartParams
    {
        Identity = new SessionIdentity { ChannelName = "my-app", UserId = Environment.UserName },
        DynamicTools = declarations,
    });

using var registration = thread.OnToolCall("myapp", "GetIssue", async (call, ct) =>
{
    var outcome = await registry.InvokeAsync(
        call.Namespace!,
        call.Tool,
        call.Arguments,
        ct);

    if (!outcome.Ok)
    {
        return new DynamicToolCallResult
        {
            Success = false,
            ErrorCode = outcome.Code,
            ErrorMessage = outcome.Message,
        };
    }

    var issue = (Issue)outcome.Data!;
    return new DynamicToolCallResult
    {
        Success = true,
        ContentItems = [new DynamicToolContentItem { Type = "text", Text = $"Loaded issue {issue.Id}." }],
        StructuredContent = JsonSerializer.SerializeToElement(issue, AppServerContractJson.Options),
    };
});
```

:::

A handler returns either:

- Success: `success: true` plus non-empty `contentItems` — that is what the model sees. `structuredContent` is optional, client-only, and never enters the model context.
- Failure: `success: false`, `errorCode`, and `errorMessage`.

If no handler matches, the SDK returns `UnsupportedTool`. If the handler throws, it returns `AdapterToolCallFailed`. The .NET registry generates closed JSON Schemas from typed arguments and rejects undeclared properties.

> [!CAUTION]
> Runtime Dynamic Tool handlers are not sandboxed. They run with your application's permissions. Validate arguments and enforce application-level authorization in every handler.

Pass the same declarations when you resume a thread. A Wire reconnect replays initialization but does not restore thread, run, or dynamic-tool state, so refresh or resume the thread and rebind its runtime tools before relying on them again.

App Binding tools take a different path. They use their binding-scoped MCP session and App Binding error helpers — see [DotCraft App](../integrations/app-binding).

## Approvals

When the agent requests approval, the SDK calls your handler. Return `accept`, `acceptForSession`, `acceptAlways`, `decline`, or `cancel`.

::: code-group

```ts [TypeScript]
const dotcraft = await DotCraft.local({
  workspacePath: "/path/to/workspace",
  approvalHandler: async (request) => {
    return confirmWithUser(request) ? "accept" : "decline";
  },
});
```

```csharp [.NET]
await using var client = await DotCraftClient.ConnectLocalAsync(
    "/path/to/workspace",
    new DotCraftLocalOptions
    {
        ClientName = "my-app",
        ApprovalHandler = async (request, ct) =>
            await ConfirmWithUserAsync(request, ct) ? ApprovalResponses.Accept : ApprovalResponses.Decline,
    });
```

:::

Register an approval handler in every production client. A high-level client advertises approval support only when a handler is registered, and asking for `approvalSupport` in `capabilities` without one fails initialization instead of inventing a decision.

## User input

Plan Mode and some tools ask structured questions. Register a user-input handler that returns the answers. The rule matches approvals: the capability is advertised only when the handler is registered, and asking for `requestUserInputSupport` alone fails initialization.

::: code-group

```ts [TypeScript]
const dotcraft = await DotCraft.local({
  workspacePath: "/path/to/workspace",
  userInputHandler: async (request) => ({ answers: await askUser(request) }),
});
```

```csharp [.NET]
var options = new DotCraftLocalOptions
{
    ClientName = "my-app",
    UserInputHandler = (request, ct) => AskUserAsync(request, ct),
};
```

:::

## Related docs

- [Threads & runs](./runs) — the run loop these callbacks fire during.
- [MCP runtime](./mcp-runtime) — the other tool path: inspect and directly control configured MCP servers.
- Full signatures per language: [TypeScript](./typescript) · [.NET](./dotnet).
