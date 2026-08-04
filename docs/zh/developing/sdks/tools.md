# 工具与审批

把应用自有工具添加到 thread，并处理 AppServer 发出的审批和用户输入请求。

## 运行时动态工具

启动或恢复 thread 时声明工具。声明会跨过 Wire 边界，handler 始终留在应用进程中。

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

Handler 返回以下一种结果：

- 成功：`success: true`、有用的 `contentItems`，以及可选的客户端 `structuredContent`。
- 失败：`success: false`、`errorCode` 和 `errorMessage`。

没有匹配的 handler 时，SDK 返回 `UnsupportedTool`；handler 抛出异常时，返回 `AdapterToolCallFailed`。.NET registry 会从强类型参数生成 closed JSON Schema，并拒绝未声明字段。

> [!CAUTION]
> Runtime Dynamic Tool handler 不受 sandbox 保护，并以应用权限运行。每个 handler 都必须验证参数并执行应用级授权。

恢复 thread 时传入同一组声明。重连后先刷新或恢复 thread，并重新绑定运行时工具，再继续依赖这些工具。

> [!TIP]
> App Binding 工具使用 binding-scoped MCP session 和 App Binding 错误 helper。参见[构建应用](../integrations/build-an-app)。

## 审批

Agent 请求审批时，SDK 会调用你的 handler。返回 `accept`、`acceptForSession`、`acceptAlways`、`decline` 或 `cancel`。

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

生产 client 应始终提供显式 approval handler。高层 client 无法在缺少 handler 时声明支持审批；初始化会失败，而不会虚构决策。

## 用户输入

Plan Mode 和某些工具会提出结构化问题。提供 user-input handler 返回答案。高层 client 只有在注册该 handler 后才会声明支持此能力。

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
- [MCP 运行时](./mcp-runtime)——检查和控制已配置的 MCP server。
- [构建应用](../integrations/build-an-app)——来自外部原生应用的 App Binding 工具。
- 参考：[TypeScript](./typescript) · [.NET](./dotnet) · [Python](./python)。
