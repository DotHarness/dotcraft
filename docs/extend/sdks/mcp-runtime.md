# MCP runtime

Use the SDK MCP runtime surface to inspect the servers visible to a thread, read resources, call tools, start OAuth, or reload MCP configuration. This is a control API for configured MCP servers; it does not define a Runtime Dynamic Tool in your SDK process.

## Understand server scope

An MCP server can come from workspace configuration, a plugin, a thread, or an App Binding. Status entries include an `origin` that identifies the source and, where applicable, the plugin, thread, or binding that owns it.

Each thread owns its MCP connections, including servers inherited from workspace or plugin configuration. Turns in the same loaded thread reuse those sessions; other threads establish separate sessions.

Pass the thread ID to inspect or control that thread's MCP runtime. Omitting it on supported management calls selects the workspace management runtime, not the current conversation. The runtime `name` is the identifier to pass to resource, tool, and OAuth calls; `declaredName` is the name from its source configuration.

Unloading or closing the thread's runtime releases its connections. Loading it again creates new sessions, without restoring server memory state. Connection isolation does not imply separate credentials or application state: a server may still use a shared account or browser profile.

## Inspect runtime status

Request `detail: "full"` when you need tool and resource descriptors; this is also the default when `detail` is omitted. Use `detail: "toolsAndAuthOnly"` for a reduced status view focused on tools and authentication.

::: code-group

```ts [TypeScript]
const status = await dotcraft.mcpRuntime.listStatus({
  threadId: thread.id,
  detail: "full",
});

for (const server of status.data ?? []) {
  console.log(server.name, server.origin?.kind, server.startupState, server.authStatus);
}
```

```csharp [.NET]
using DotCraft.Protocol.AppServer;

var status = await client.McpRuntime.ListStatusAsync(new McpServerStatusListParams
{
    ThreadId = thread.Id,
    Detail = "full"
});

foreach (var server in status.Data.Value ?? [])
    Console.WriteLine($"{server.Name.Value} {server.Origin.Value?.Kind.Value} {server.StartupState.Value}");
```

:::

When the result is paginated, pass `nextCursor` back as `cursor` on the next request.

## Read a resource

Use the runtime name returned by the status call. The URI must be one advertised by that server or one of its resource templates.

::: code-group

```ts [TypeScript]
const resource = await dotcraft.mcpRuntime.readResource({
  threadId: thread.id,
  server: "docs",
  uri: "docs://getting-started",
});
console.log(resource.contents);
```

```csharp [.NET]
var resource = await client.McpRuntime.ReadResourceAsync(new McpServerResourceReadParams
{
    ThreadId = thread.Id,
    Server = "docs",
    Uri = "docs://getting-started"
});
Console.WriteLine(resource.Contents.Value);
```

:::

## Call a tool

Check that the server is enabled, started, and exposes the requested tool before calling it. The call runs through the same dispatcher as a model call, so authority checks, schema validation, approval policy, and result limits all still apply. `threadId` is required — it selects the effective server snapshot.

::: code-group

```ts [TypeScript]
const result = await dotcraft.mcpRuntime.callTool({
  threadId: thread.id,
  server: "docs",
  tool: "search",
  arguments: { query: "thread lifecycle" },
});

if (result.isError) throw new Error("MCP tool call failed");
console.log(result.structuredContent ?? result.content);
```

```csharp [.NET]
using System.Text.Json;

var result = await client.McpRuntime.CallToolAsync(new McpServerToolCallParams
{
    ThreadId = thread.Id,
    Server = "docs",
    Tool = "search",
    Arguments = new Dictionary<string, JsonElement>
    {
        ["query"] = JsonSerializer.SerializeToElement("thread lifecycle")
    }
});
Console.WriteLine(result.StructuredContent.Value ?? result.Content.Value);
```

:::

This control call executes immediately and never reaches the model. To let an agent choose and invoke an MCP tool during a run, configure the MCP server for the thread and start the run normally.

## Answer supplementary input

An MCP tool can ask the user for a form or URL confirmation while it runs. Declare `capabilities.mcpElicitation: true`, attach to the thread, and handle `mcpServer/elicitation/request`. Each request includes its originating `threadId` and a nullable `turnId`; return `accept`, `decline`, or `cancel`, with form content only on acceptance.

The TypeScript wire handler receives an `AbortSignal` as its third argument. The .NET wire handler receives a request-scoped `CancellationToken`. Dismiss the prompt when that signal or token is cancelled: another client may have answered, the invocation may have ended, or the connection may have closed. These inputs are not replayed after reconnect. A client implementing the raw protocol handles [`serverRequest/resolved`](../protocols/appserver#mcp-supplementary-input) using the original JSON-RPC request ID.

## Authenticate and reload

Login is accepted only while a server reports `authStatus: "notLoggedIn"`, and any other state rejects the request. Start login for that runtime name, open the returned authorization URL in the user's browser, then wait for the `mcpServer/oauthLogin/completed` notification to report success or failure.

::: code-group

```ts [TypeScript]
const login = await dotcraft.mcpRuntime.loginOAuth({
  name: "docs",
  threadId: thread.id,
  scopes: ["read"],
  timeoutSecs: 60,
});
console.log(login.authorizationUrl);

await dotcraft.mcpRuntime.reload();
```

```csharp [.NET]
var login = await client.McpRuntime.LoginOAuthAsync(new McpServerOAuthLoginParams
{
    Name = "docs",
    ThreadId = thread.Id,
    Scopes = new[] { "read" },
    TimeoutSecs = 60
});
Console.WriteLine(login.AuthorizationUrl.Value);

await client.McpRuntime.ReloadAsync();
```

:::

Reload refreshes configuration and the next thread tool snapshot, retaining unchanged connections. Connection input changes replace only affected servers; tool timeout and approval changes preserve the session.

## Choose the right tool surface

| Need | Surface |
| --- | --- |
| Expose an application callback as a tool for one thread | [Runtime Dynamic Tools](./tools#runtime-dynamic-tools) |
| Inspect or directly control a configured MCP server | MCP runtime API on this page |
| Connect a product integration with thread-scoped authority | [DotCraft App](../app-binding) |

## Related docs

- [Threads & runs](./runs) — the thread lifecycle these control calls hang off.
- [AppServer Protocol](../protocols/appserver) — the JSON-RPC method groups and capability negotiation these calls sit inside.
