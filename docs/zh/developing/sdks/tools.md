# 工具与审批

用你自己的**运行时动态工具**扩展一轮，并用回调回答**审批**与**用户输入**提问。这三者都是每个 SDK 的一部分。

## 运行时动态工具

在开启（或恢复）线程时声明工具。工具**规格**会通过 wire 发送；**处理器**在你的进程内运行，绝不会被序列化。Agent 调用工具，你的处理器返回结果。

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

处理器返回成功结果（`success: true`，带 `contentItems` / `structuredResult`）或失败（`success: false`，带 `errorCode` / `errorMessage`）。若未注册处理器，SDK 返回 `UnsupportedTool`；若处理器抛错，返回 `AdapterToolCallFailed`。工具处理器负责参数校验与应用级授权。

> [!TIP]
> 对于 App Binding 应用，请使用共享的 App Binding 错误形态（`appBindingToolError` / `DotCraftAppBindingClient.ToolError` / `app_binding_tool_error`），而非通用失败。参见 [构建应用](../integrations/build-an-app)。

## 审批

当 Agent 为敏感操作请求审批时，SDK 会把它路由到你的处理器，由处理器返回决策（`accept`、`acceptForSession`、`acceptAlways`、`decline`、`cancel`）。没有处理器时 SDK 会自动接受——生产环境请提供显式处理器。

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

## 用户输入

Plan Mode 和某些工具会向用户提出结构化问题。提供一个用户输入处理器返回答案。没有处理器时，SDK 返回空答案，从而让非交互式客户端不会阻塞。

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
    user_input_handler=lambda request: ask_user(request),  # 返回 answers dict
))
```

:::

## 参见

- [线程与运行](./runs)——这些回调触发其间的运行循环。
- [构建应用](../integrations/build-an-app)——来自外部原生应用的 App Binding 工具。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
