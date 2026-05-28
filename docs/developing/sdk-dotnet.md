# DotCraft .NET SDK

`DotCraft.Sdk` is the .NET SDK for Hub discovery, AppServer JSON-RPC clients, Runtime Dynamic Tools, and App Binding integrations.

Use it when you are building a C# tool, a native app that accepts DotCraft App Binding handoffs, or an advanced client that wants typed wrappers plus raw AppServer access.

The .NET SDK follows the shared AppServer and Hub model; AppServer wire semantics are described in [AppServer Protocol](./appserver-protocol.md).

## Package Shape

| Namespace | Purpose |
|-----------|---------|
| `DotCraft.Sdk.AppServer` | `DotCraftClient`, thread/turn/model wrappers, Runtime Dynamic Tool models. |
| `DotCraft.Sdk.AppBinding` | Handoff parsing, binding request helpers, tool attachment, standard app-bound tool errors. |
| `DotCraft.Sdk.Hub` | Hub lock discovery, health probing, AppServer lookup and ensure. |
| `DotCraft.Sdk.Wire` | JSON-RPC transports, wire client, JSON options, JSON-RPC exceptions. |

The SDK targets `.NET 10` and uses `System.Text.Json` with camelCase web defaults.

## Local Workspace Quickstart

`ConnectLocalAsync` discovers or starts the local Hub, asks Hub to ensure an AppServer for the workspace, then performs the AppServer `initialize` / `initialized` handshake.

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

## Remote AppServer

Use `ConnectRemoteAsync` when another component already knows the AppServer WebSocket endpoint.

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

Treat Hub tokens, AppServer WebSocket tokens, and App Binding handoff tokens as secrets. Do not log full token-bearing URLs.

## Threads, Turns, And Notifications

The high-level client exposes grouped clients for current typed wrappers:

```csharp
var thread = await client.Threads.ReadAsync(threadId, cancellationToken: ct);
var turn = await client.Turns.EnqueueAsync(threadId, input, cancellationToken: ct);
var models = await client.Models.ListAsync(providerId: null, cancellationToken: ct);
```

Notifications are exposed as raw AppServer messages:

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

The .NET SDK does not currently provide a high-level run helper, normalized event reducer, or final-text merge. Applications that need those behaviors should subscribe to the thread, read raw notifications, and use `thread/read` as the recovery snapshot when needed.

## Runtime Dynamic Tools

Runtime Dynamic Tools are declared on `thread/start` or `thread/resume`. The SDK routes server-initiated `item/tool/call` requests to registered handlers.

```csharp
var tools = new[]
{
    new DynamicToolSpec(
        Namespace: "myapp",
        Name: "GetIssue",
        Description: "Read an issue from MyApp.",
        InputSchema: inputSchema)
};

var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(
        new SessionIdentity("myapp", Environment.UserName, workspacePath),
        DisplayName: "MyApp bound thread",
        DynamicTools: tools),
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

For reconnecting clients, call `thread/resume` with replacement `DynamicTools` when `client.Capabilities.DynamicToolRebind` is true.

## App Binding

Native apps can parse a handoff URL, inspect the connection or binding request, accept it, attach app-owned tools, and keep the connection alive by draining notifications.

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

When an app-bound tool cannot run, return a standard App Binding error shape:

```csharp
return DotCraftAppBindingClient.ToolError(
    AppBindingErrorCodes.Offline,
    "MyApp is not connected.");
```

## Hub And Raw JSON-RPC

Most applications should use `DotCraftClient.ConnectLocalAsync`. Use `HubClient` directly only when you need Hub metadata without opening an AppServer connection.

```csharp
var hub = new HubClient(new DotCraftHubClientOptions
{
    StartHubIfMissing = false
});

var appServer = await hub.GetAppServerByWorkspaceAsync(workspacePath, ct);
```

The high-level client keeps a raw escape hatch for AppServer methods that do not yet have typed wrappers:

```csharp
var raw = await client.RequestAsync(
    "thread/read",
    new { threadId, includeTurns = true },
    ct);
```

For tests or custom hosts, use `DotCraftWireClient` with your own `IJsonRpcTransport`.

## Current Gaps

- No high-level `RunAsync` / `RunStreamingAsync` abstraction yet.
- No normalized event reducer or final text merge yet.
- Approval and user-input server requests require low-level handler registration today.
- Many management APIs remain raw-only until typed wrappers are added.

## Validation

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```
