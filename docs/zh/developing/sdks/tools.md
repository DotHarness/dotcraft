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

处理器返回成功结果（`success: true`，`contentItems` 至少包含一条有用的文本，并可附带 `structuredContent`）或失败（`success: false`，带 `errorCode` / `errorMessage`）。.NET registry 会从强类型参数生成 closed JSON Schema，并拒绝未声明字段；恢复线程时把同一组 declarations 传给 `thread/resume` 以重新绑定。若未注册处理器，SDK 返回 `UnsupportedTool`；若处理器抛错，返回 `AdapterToolCallFailed`。工具处理器仍负责应用级授权。

> [!TIP]
> 对于 App Binding 应用，请使用共享的 App Binding 错误形态（`appBindingToolError` / `DotCraftAppBindingClient.ToolError` / `app_binding_tool_error`），而非通用失败。参见 [构建应用](../integrations/build-an-app)。

## 审批

当 Agent 为敏感操作请求审批时，SDK 会把它路由到你的处理器，由处理器返回决策（`accept`、`acceptForSession`、`acceptAlways`、`decline`、`cancel`）。客户端必须先注册该处理器，才能声明支持审批。

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

## 用户输入

Plan Mode 和某些工具会向用户提出结构化问题。提供用户输入 handler 返回答案。高层 client 只有在注册 handler 后才会声明支持用户输入；如果调用方显式开启 capability 却没有提供 handler，初始化会立即返回稳定的配置错误，而不会虚构答案。

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
    user_input_handler=lambda request: ask_user(request),  # 返回 answers dict
))
```

:::

## 相关文档

- [线程与运行](./runs)——这些回调触发其间的运行循环。
- [构建应用](../integrations/build-an-app)——来自外部原生应用的 App Binding 工具。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
