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
        structuredResult: await getIssue(call.arguments.id as string),
      }),
    },
  ],
});
```

```csharp [.NET]
var tools = new[]
{
    new DynamicToolSpec("myapp", "GetIssue", "Read an issue from MyApp.", inputSchema),
};

var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(
        new SessionIdentity("my-app", Environment.UserName),
        DynamicTools: tools));

using var registration = thread.OnToolCall("myapp", "GetIssue", async (call, ct) =>
{
    var id = call.Arguments.GetProperty("id").GetString();
    var issue = await GetIssueAsync(id!, ct);
    return new DynamicToolResult(Success: true, StructuredResult: issue);
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
    "structuredResult": get_issue(call["arguments"]["id"]),
})
```

:::

A handler returns a success result (`success: true` with `contentItems` / `structuredResult`) or a failure (`success: false` with `errorCode` / `errorMessage`). If no handler is registered, the SDK returns `UnsupportedTool`; if a handler throws, it returns `AdapterToolCallFailed`. Tool handlers own argument validation and app-level authorization.

> [!TIP]
> For App Binding apps, use the shared App Binding error shapes (`appBindingToolError` / `DotCraftAppBindingClient.ToolError` / `app_binding_tool_error`) instead of generic failures. See [Build an App](../integrations/build-an-app).

## Approvals

When the agent requests approval for a sensitive action, the SDK routes it to your handler, which returns a decision (`accept`, `acceptForSession`, `acceptAlways`, `decline`, `cancel`). Without a handler, the SDK auto-accepts — provide an explicit handler in production.

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

Plan Mode and some tools ask the user a structured question. Provide a user-input handler that returns answers. Without one, the SDK returns empty answers so non-interactive clients never block.

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

## See also

- [Threads & runs](./runs) — the run loop these callbacks fire during.
- [Build an App](../integrations/build-an-app) — App Binding tools from an external native app.
- Reference: [TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python).
