# dotcraft-dotnet

.NET SDK for DotCraft Hub discovery, AppServer JSON-RPC clients,
runtime dynamic tools, and App Binding integration.

This SDK is implemented entirely against the
[DotCraft .NET SDK Specification](https://github.com/DotHarness/dotcraft/blob/main/specs/sdk/dotnet.md).
Any SDK behavior, public API, protocol wrapper, or compatibility change must be
specified there first, then implemented here.

## Connect To A Local Workspace AppServer

`ConnectLocalAsync` discovers or starts the local DotCraft Hub, asks Hub to
ensure an AppServer for the workspace, then performs the AppServer
`initialize` / `initialized` handshake.

```csharp
var workspacePath = @"E:\examples\my-workspace";

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
        HistoryMode: "server",
        Config: new
        {
            mode = "agent",
            approvalPolicy = "interrupt"
        }),
    ct);

await client.Threads.SubscribeAsync(thread.Id, cancellationToken: ct);

var turn = await client.Turns.StartAsync(
    thread.Id,
    [new TurnInputPart("text", "Summarize this workspace.")],
    cancellationToken: ct);

await foreach (var notification in client.ReadNotificationsAsync(ct))
{
    Console.WriteLine($"{notification.Method}: {notification.Params}");

    if (notification.Method is "turn/completed" or "turn/failed" or "turn/cancelled")
    {
        break;
    }
}
```

## Connect Directly To An AppServer

Use `ConnectRemoteAsync` when another component already knows the AppServer
WebSocket endpoint.

```csharp
await using var client = await DotCraftClient.ConnectRemoteAsync(
    "ws://127.0.0.1:9100/ws",
    token: appServerToken,
    options: new DotCraftClientOptions
    {
        ClientName = "my-dotnet-app",
        ClientVersion = "0.1.0",
        ApprovalSupport = true,
        StreamingSupport = true
    },
    cancellationToken: ct);
```

Do not log AppServer or Hub bearer tokens. Treat endpoint URLs with embedded
`token=` query values as secrets.

## Register Runtime Dynamic Tools

Runtime dynamic tools are declared on `thread/start` or `thread/resume`. The
SDK handles server-initiated `item/tool/call` requests and routes them to your
registered handler.

```csharp
var inputSchema = JsonSerializer.SerializeToElement(new
{
    type = "object",
    properties = new
    {
        issueId = new
        {
            type = "string",
            description = "Issue id to read."
        }
    },
    required = new[] { "issueId" }
}, DotCraftJson.Options);

var tools = new[]
{
    new DynamicToolSpec(
        Namespace: "myapp",
        Name: "GetIssue",
        Description: "Read an issue from MyApp.",
        InputSchema: inputSchema,
        DeferLoading: true,
        Approval: new ToolApprovalDescriptor(
            Kind: "tool",
            TargetArgument: "issueId"))
};

var thread = await client.Threads.StartAsync(
    new DotCraftThreadStartRequest(
        new SessionIdentity("myapp", Environment.UserName, workspacePath),
        DisplayName: "MyApp bound thread",
        DynamicTools: tools),
    ct);

using var toolRegistration = client.RegisterDynamicToolHandler(
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
            StructuredResult: new
            {
                issue.Id,
                issue.Title,
                issue.Status
            });
    });
```

For reconnecting clients, call `thread/resume` with replacement `DynamicTools`
when `client.Capabilities.DynamicToolRebind` is true.

```csharp
if (client.Capabilities.DynamicToolRebind)
{
    await client.Threads.ResumeAsync(
        new DotCraftThreadResumeRequest(threadId, tools),
        ct);
}
```

## App Binding Handoff

Native apps can parse a DotCraft App Binding handoff URL, inspect the binding
request, accept it, attach app-owned dynamic tools, and keep the connection
alive by draining notifications.

```csharp
var handoff = AppBindingHandoff.Parse(
    handoffUrl,
    expectedScheme: "myapp",
    expectedAppId: "com.example.myapp");

if (!string.Equals(handoff.Operation, "bind", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Unsupported handoff operation: {handoff.Operation}");
}

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

var threadId = request.GetProperty("threadId").GetString()!;
var grantId = "grant_" + Guid.NewGuid().ToString("N");

var accepted = await client.AppBindings.AcceptBindingAsync<JsonElement>(new
{
    bindingRequestId = handoff.RequestId,
    requestToken = handoff.RequestToken,
    grantId,
    grantedScopes = new[] { "issues:read" },
    approvalMode = "appAccepted",
    approvedBy = Environment.UserName
}, ct);

var bindingId = accepted
    .GetProperty("binding")
    .GetProperty("bindingId")
    .GetString()!;

await client.AppBindings.AttachToolsAsync<JsonElement>(new
{
    bindingId,
    threadId,
    appId = handoff.AppId,
    grantId,
    tools
}, ct);

await client.AppBindings.KeepAliveAsync(
    onNotification: (notification, cancellationToken) =>
    {
        Console.WriteLine(notification.Method);
        return Task.CompletedTask;
    },
    cancellationToken: ct);
```

When an app-bound tool cannot run, return a standard App Binding error shape:

```csharp
return DotCraftAppBindingClient.ToolError(
    AppBindingErrorCodes.Offline,
    "MyApp is not connected.");
```

## Query Hub Directly

Most applications should use `DotCraftClient.ConnectLocalAsync`. Use `HubClient`
directly only when you need Hub metadata without opening an AppServer
connection.

```csharp
var hub = new HubClient(new DotCraftHubClientOptions
{
    HubLockPath = @"C:\Users\me\.craft\hub\hub.lock",
    StartHubIfMissing = false
});

var appServer = await hub.GetAppServerByWorkspaceAsync(workspacePath, ct);

if (appServer?.State == HubAppServerStates.Running &&
    appServer.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl))
{
    // wsUrl may contain a token query parameter. Use it to connect, but do not log it.
    await using var client = await DotCraftClient.ConnectRemoteAsync(wsUrl, cancellationToken: ct);
}
```

## Low-Level JSON-RPC

The high-level client exposes `RequestAsync` for methods that do not yet have a
typed wrapper.

```csharp
var raw = await client.RequestAsync(
    "thread/read",
    new
    {
        threadId,
        includeTurns = true
    },
    ct);
```

For tests or custom transports, use `DotCraftWireClient` with your own
`IJsonRpcTransport`.

## Release

Packages are published through GitHub Actions only. Do not publish from a local
developer machine unless the spec and maintainers explicitly approve an
emergency exception.

The normal preview flow is:

1. Run the `Publish NuGet` workflow with `target=dry-run` and a preview version
   such as `0.1.0-preview.1`.
2. Inspect the uploaded `.nupkg` and `.snupkg` artifacts.
3. Re-run the same workflow with `target=nuget-org` from `main`.
4. Set `confirm` to exactly:

```text
publish DotCraft.Sdk 0.1.0-preview.1 to nuget.org
```

The nuget.org Trusted Publishing policy should be configured for:

| Field | Value |
|-------|-------|
| Repository owner | `DotHarness` |
| Repository | `dotcraft-dotnet` |
| Workflow file | `publish-nuget.yml` |
| Environment | `nuget-production` |

Configure `nuget-production` as a protected GitHub Environment and set the
`NUGET_USER` GitHub Actions variable to the nuget.org profile or organization
name used for Trusted Publishing.

## Development

```powershell
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```
