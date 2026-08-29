# 工具与审批

把应用自有的工具接入 thread，并处理 AppServer 发来的审批与用户输入请求。

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

:::

Handler 返回以下一种结果：

- 成功：`success: true`，加上非空的 `contentItems`，也就是模型能看到的内容。`structuredContent` 可选，只给客户端使用，不会进入模型上下文。
- 失败：`success: false`、`errorCode` 和 `errorMessage`。

没有匹配的 handler 时，SDK 返回 `UnsupportedTool`。handler 抛出异常时，返回 `AdapterToolCallFailed`。.NET registry 会从强类型参数生成 closed JSON Schema，并拒绝未声明字段。

> [!CAUTION]
> 运行时动态工具的 handler 不受 sandbox 保护，以应用自身的权限运行。每个 handler 都要自行校验参数并执行应用级授权。

恢复 thread 时传入同一组声明。Wire 重连会重放初始化，但不会恢复 thread、run 或动态工具状态，所以重连后要先刷新或恢复 thread 并重新绑定运行时工具，才能继续依赖它们。

App Binding 工具走的是另一条路径。它们使用 binding 作用域的 MCP session 和 App Binding 错误 helper，见 [DotCraft App](../integrations/app-binding)。

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

:::

生产环境的 client 都要注册 approval handler。注册了 handler，高层 client 才会声明支持审批。在 `capabilities` 里要求 `approvalSupport` 却不提供 handler，初始化会直接失败，而不会替你做出决策。

## 用户输入

Plan Mode 和部分工具会提出结构化问题。注册 user-input handler 返回答案。这里的规则与审批一致，注册了 handler 才会声明该能力，在 `capabilities` 里单独要求 `requestUserInputSupport` 同样会导致初始化失败。

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

## 相关文档

- [线程与运行](./runs)——这些回调触发其间的运行循环。
- [MCP 运行时](./mcp-runtime)——另一条工具路径，检查并直接控制已配置的 MCP server。
- 各语言的完整签名：[TypeScript](./typescript) · [.NET](./dotnet)。
