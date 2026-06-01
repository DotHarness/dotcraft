# DotCraft .NET SDK

`DotCraft.Sdk` 是用于 Hub 发现、AppServer JSON-RPC 客户端、Runtime Dynamic Tools 和 App Binding 集成的 .NET SDK。

当你在构建 C# 工具、接收 DotCraft App Binding handoff 的原生 App，或需要 typed wrapper + raw AppServer 访问能力的高级客户端时，使用它。

.NET SDK 遵循共享的 AppServer 与 Hub 模型；AppServer wire 语义见 [AppServer 协议](../protocols/appserver-protocol)。

## 包结构

| Namespace | 作用 |
|-----------|------|
| `DotCraft.Sdk.AppServer` | `DotCraftClient`、线程/轮次/模型 wrapper、Runtime Dynamic Tool 模型。 |
| `DotCraft.Sdk.AppBinding` | Handoff 解析、binding request helper、工具附加、标准 app-bound tool error。 |
| `DotCraft.Sdk.Hub` | Hub lock 发现、健康检查、AppServer lookup 和 ensure。 |
| `DotCraft.Sdk.Wire` | JSON-RPC transport、wire client、JSON options、JSON-RPC exception。 |

SDK 目标框架是 `.NET 10`，序列化使用 `System.Text.Json` 的 camelCase web 默认值。

## 本地工作区快速开始

`ConnectLocalAsync` 会发现或启动本机 Hub，要求 Hub 为工作区确保 AppServer，然后完成 AppServer `initialize` / `initialized` 握手。

```csharp
var workspacePath = @"E:\Git\my-workspace";

await using var client = await DotCraftClient.ConnectLocalAsync(
    workspacePath,
    new DotCraftLocalClientOptions
    {
        ClientName = "my-dotnet-app",
        ClientTitle = "My .NET App",
        ClientVersion = "0.1.0"
    },
    ct);

var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(
        new SessionIdentity(
            ChannelName: "my-dotnet-app",
            UserId: Environment.UserName,
            WorkspacePath: workspacePath),
        DisplayName: "SDK smoke test",
        HistoryMode: "server"),
    ct);

await client.Threads.SubscribeAsync(thread.Id, cancellationToken: ct);

await client.Turns.StartAsync(
    thread.Id,
    [new TurnInputPart("text", "Summarize this workspace.")],
    cancellationToken: ct);
```

## 远程 AppServer

当其他组件已经知道 AppServer WebSocket endpoint 时，使用 `ConnectRemoteAsync`。

```csharp
await using var client = await DotCraftClient.ConnectRemoteAsync(
    "ws://127.0.0.1:9100/ws",
    token: appServerToken,
    options: new DotCraftClientOptions
    {
        ClientName = "my-dotnet-app",
        ClientVersion = "0.1.0",
        ApprovalSupport = false,
        StreamingSupport = true
    },
    cancellationToken: ct);
```

Hub token、AppServer WebSocket token 和 App Binding handoff token 都是 secret。不要记录带 token 的完整 URL。

## 线程、轮次与通知

高层客户端为当前 typed wrapper 暴露了分组 client：

```csharp
var thread = await client.Threads.ReadAsync(threadId, cancellationToken: ct);
var turn = await client.Turns.EnqueueAsync(threadId, input, cancellationToken: ct);
var models = await client.Models.ListAsync(providerId: null, cancellationToken: ct);
```

通知以 raw AppServer message 暴露：

```csharp
await foreach (var notification in client.ReadNotificationsAsync(ct))
{
    Console.WriteLine($"{notification.Method}: {notification.Params}");

    if (notification.Method is "turn/completed" or "turn/failed" or "turn/cancelled")
    {
        break;
    }
}
```

.NET SDK 当前还没有高层 run helper、normalized event reducer 或最终文本合并。需要这些能力的应用应订阅线程、读取 raw notification，并在需要恢复状态时使用 `thread/read` 作为快照。

## Runtime Dynamic Tools

Runtime Dynamic Tools 在 `thread/start` 或 `thread/resume` 上声明。SDK 会把服务端发起的 `item/tool/call` 请求路由到注册的 handler。

```csharp
var tools = new[]
{
    new DynamicToolSpec(
        Namespace: "myapp",
        Name: "GetIssue",
        Description: "Read an issue from MyApp.",
        InputSchema: inputSchema)
};

var context = new Dictionary<string, RuntimeAdditionalContextEntry>
{
    ["myapp.threadGuidance"] = new(
        "When the user asks about MyApp issues, search for the relevant MyApp tool first.")
};

var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(
        new SessionIdentity("myapp", Environment.UserName, workspacePath),
        DisplayName: "MyApp bound thread",
        DynamicTools: tools,
        AdditionalContext: context),
    ct);

using var registration = client.RegisterDynamicToolHandler(
    thread.Id,
    "myapp",
    "GetIssue",
    async (call, cancellationToken) =>
    {
        var issueId = call.Arguments.GetProperty("issueId").GetString();
        var issue = await issueStore.GetIssueAsync(issueId!, cancellationToken);

        return new DynamicToolResult(
            Success: true,
            ContentItems: [new ToolContentItem("text", issue.Title)],
            StructuredResult: new { issue.Id, issue.Title, issue.Status });
    });
```

`additionalContext` 适合放简短 runtime guidance，尤其是在工具使用 deferred exposure 时。发送前先检查 `client.Capabilities.RuntimeAdditionalContext`。

重连客户端如果看到 `client.Capabilities.DynamicToolRebind` 为 true，应通过 `thread/resume` 重新发送 replacement `DynamicTools`；如果 `client.Capabilities.RuntimeAdditionalContext` 为 true，也可以同时发送 replacement `AdditionalContext`。

```csharp
await client.Threads.ResumeAsync(
    new DotCraftThreadResumeRequest(
        thread.Id,
        DynamicTools: tools,
        AdditionalContext: context),
    ct);

await client.Threads.ResumeAsync(
    new DotCraftThreadResumeRequest(
        thread.Id,
        AdditionalContext: new Dictionary<string, RuntimeAdditionalContextEntry>()),
    ct);
```

恢复线程时，省略 `AdditionalContext` 会保留当前 runtime context；传入空字典会清空它。

## App Binding

原生 App 可以解析 handoff URL、检查 connection 或 binding request、接受绑定、附加 App 自有工具，并通过持续 drain notification 保持连接存活。

```csharp
var handoff = AppBindingHandoff.Parse(
    handoffUrl,
    expectedScheme: "myapp",
    expectedAppId: "com.example.myapp");

await using var client = await DotCraftClient.ConnectRemoteAsync(
    handoff.AppServerUrl ?? throw new InvalidOperationException("Missing AppServer endpoint."),
    options: new DotCraftClientOptions
    {
        ClientName = "myapp",
        ClientVersion = "0.1.0"
    },
    cancellationToken: ct);

var request = await client.AppBindings.GetBindingRequestAsync<JsonElement>(new
{
    appId = handoff.AppId,
    bindingRequestId = handoff.RequestId,
    requestToken = handoff.RequestToken
}, ct);

await client.AppBindings.AcceptBindingAsync<JsonElement>(new
{
    bindingRequestId = handoff.RequestId,
    requestToken = handoff.RequestToken,
    grantId = "grant_" + Guid.NewGuid().ToString("N"),
    grantedScopes = new[] { "issues:read" },
    approvalMode = "appAccepted",
    approvedBy = Environment.UserName
}, ct);
```

当 app-bound tool 无法运行时，返回标准 App Binding error shape：

```csharp
return DotCraftAppBindingClient.ToolError(
    AppBindingErrorCodes.Offline,
    "MyApp is not connected.");
```

## Hub 与 Raw JSON-RPC

大多数应用应使用 `DotCraftClient.ConnectLocalAsync`。只有在不打开 AppServer 连接、但需要 Hub 元数据时，才直接使用 `HubClient`。

```csharp
var hub = new HubClient(new DotCraftHubClientOptions
{
    StartHubIfMissing = false
});

var appServer = await hub.GetAppServerByWorkspaceAsync(workspacePath, ct);
```

对于还没有 typed wrapper 的 AppServer 方法，高层 client 保留 raw escape hatch：

```csharp
var raw = await client.RequestAsync(
    "thread/read",
    new { threadId, includeTurns = true },
    ct);
```

测试或自定义 host 可以直接使用 `DotCraftWireClient` 和自定义 `IJsonRpcTransport`。

## 当前限制

- 暂无高层 `RunAsync` / `RunStreamingAsync`。
- 暂无 normalized event reducer 或最终文本合并。
- 审批和用户输入 server request 当前需要低层 handler 注册。
- 很多管理 API 在 typed wrapper 完成前仍只能 raw 调用。

## 验证

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```
