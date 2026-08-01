# Tools & approvals

Extend a turn with your own **runtime dynamic tools**, and answer **approval** and **user-input** prompts with callbacks. All three are part of every SDK.

## Runtime dynamic tools

Declare tools when you start (or resume) a thread. The tool **spec** is sent over the wire; the **handler** runs in your process and is never serialized. The agent calls a tool, your handler returns a result.

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
using DotCraft.Sdk.AppServer;
using DotCraft.Sdk.Tools;

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
    new DotCraftThreadStartRequest(
        new SessionIdentity("my-app", Environment.UserName),
        DynamicTools: declarations));

using var registration = thread.OnToolCall("myapp", "GetIssue", async (call, ct) =>
{
    var outcome = await registry.InvokeAsync(
        call.Namespace!,
        call.Tool,
        call.Arguments,
        ct);

    if (!outcome.Ok)
    {
        return new DynamicToolResult(
            false,
            ErrorCode: outcome.Code,
            ErrorMessage: outcome.Message);
    }

    var issue = (Issue)outcome.Data!;
    return new DynamicToolResult(
        true,
        [new ToolContentItem("text", $"Loaded issue {issue.Id}.")],
        issue);
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

A handler returns a success result (`success: true` with at least one useful text item in `contentItems`, plus optional `structuredContent`) or a failure (`success: false` with `errorCode` / `errorMessage`). The .NET registry generates closed JSON Schemas from typed arguments and rejects undeclared properties. Pass the same declarations to `thread/resume` when rebinding a thread. If no handler is registered, the SDK returns `UnsupportedTool`; if a handler throws, it returns `AdapterToolCallFailed`. Tool handlers still own app-level authorization.

> [!TIP]
> Runtime Dynamic callbacks can use the shared error helpers. App Binding tools use standard MCP results from their binding-scoped server. See [Build an App](../integrations/build-an-app).

## Approvals

When the agent requests approval for a sensitive action, the SDK routes it to your handler, which returns a decision (`accept`, `acceptForSession`, `acceptAlways`, `decline`, `cancel`). A client cannot advertise approval support without registering this handler first.

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
    new DotCraftLocalClientOptions
    {
        ClientName = "my-app",
        ApprovalHandler = async (request, ct) =>
            await ConfirmWithUserAsync(request, ct) ? ApprovalDecision.Accept : ApprovalDecision.Decline,
    });
```

```python [Python]
dotcraft = await DotCraft.connect_local(LocalOptions(
    workspace_path="/path/to/workspace",
    approval_handler=lambda request: "accept" if confirm_with_user(request) else "decline",
))
```

:::

## User input

Plan Mode and some tools ask the user a structured question. Provide a user-input handler that returns answers. The high-level client advertises user-input support only when a handler is registered. If a caller explicitly enables the capability without providing its handler, initialization fails with a stable configuration error instead of inventing an answer.

::: code-group

```ts [TypeScript]
const dotcraft = await DotCraft.local({
  workspacePath: "/path/to/workspace",
  userInputHandler: async (request) => ({ answers: await askUser(request) }),
});
```

```csharp [.NET]
var options = new DotCraftLocalClientOptions
{
    ClientName = "my-app",
    UserInputHandler = async (request, ct) =>
        new UserInputResponse(await AskUserAsync(request, ct)),
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
- [Build an App](../integrations/build-an-app) — App Binding tools from an external native app.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
