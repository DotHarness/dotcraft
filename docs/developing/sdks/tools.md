# Tools & approvals

Add application-owned tools to a thread, then handle approval and user-input requests from AppServer.

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

```python [Python]
tools = [
    {
        "namespace": "myapp",
        "name": "GetIssue",
        "description": "Read an issue from MyApp.",
        "inputSchema": {"type": "object", "properties": {"id": {"type": "string"}}, "required": ["id"]},
    }
]

thread = await dotcraft.threads.start(user_id="me", dynamic_tools=tools)

thread.on_tool_call("myapp", "GetIssue", lambda call: {
    "success": True,
    "contentItems": [{"type": "text", "text": "Issue loaded."}],
    "structuredContent": get_issue(call["arguments"]["id"]),
})
```

:::

A handler returns either:

- Success: `success: true`, useful `contentItems`, and optional client-only `structuredContent`.
- Failure: `success: false`, `errorCode`, and `errorMessage`.

If no handler matches, the SDK returns `UnsupportedTool`. If the handler throws, it returns `AdapterToolCallFailed`. The .NET registry generates closed JSON Schemas from typed arguments and rejects undeclared properties.

> [!CAUTION]
> Runtime Dynamic Tool handlers are not sandboxed. They run with your application's permissions. Validate arguments and enforce application-level authorization in every handler.

Pass the same declarations when you resume a thread. After reconnect, refresh or resume the thread and rebind its runtime tools before relying on them again.

> [!TIP]
> App Binding tools use their binding-scoped MCP session and App Binding error helpers. See [Build an app](../integrations/build-an-app).

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

```python [Python]
dotcraft = await DotCraft.connect_local(LocalOptions(
    workspace_path="/path/to/workspace",
    approval_handler=lambda request: "accept" if confirm_with_user(request) else "decline",
))
```

:::

Production clients should always provide an explicit approval handler. A high-level client cannot advertise approval support without one; initialization fails instead of inventing a decision.

## User input

Plan Mode and some tools ask structured questions. Provide a user-input handler that returns the answers. A high-level client advertises this capability only when the handler is registered.

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

```python [Python]
dotcraft = await DotCraft.connect_local(LocalOptions(
    workspace_path="/path/to/workspace",
    user_input_handler=lambda request: ask_user(request),  # returns an answers dict
))
```

:::

## Related docs

- [Threads & runs](./runs) — the run loop these callbacks fire during.
- [MCP runtime](./mcp-runtime) — inspect and control configured MCP servers.
- [Build an App](../integrations/build-an-app) — App Binding tools from an external native app.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
