# MCP runtime

Use the SDK MCP runtime surface to inspect the servers visible to a thread, read resources, call tools, start OAuth, or reload MCP configuration. This is a control API for configured MCP servers; it does not define a Runtime Dynamic Tool in your SDK process.

## Understand server scope

An MCP server can come from workspace configuration, a plugin, a thread, or an App Binding. Status entries include an `origin` that identifies the source and, where applicable, the plugin, thread, or binding that owns it.

Pass a thread ID when the visible server set depends on thread configuration or bindings. The runtime `name` is the identifier to pass to resource, tool, and OAuth calls; `declaredName` is the name from its source configuration.

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

```python [Python]
status = await dotcraft.mcp_runtime.list_status(
    thread_id=thread.id,
    detail="full",
)

for server in status.data or []:
    origin = server.origin.kind if server.origin else None
    print(server.name, origin, server.startup_state, server.auth_status)
```

:::

Use `nextCursor` / `next_cursor` with the next request when the result is paginated.

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

```python [Python]
resource = await dotcraft.mcp_runtime.read_resource(
    "docs",
    "docs://getting-started",
    thread_id=thread.id,
)
print(resource.contents)
```

:::

## Call a tool

Call tools only after checking that the server is enabled, started, and exposes the requested tool. Tool arguments are validated by the MCP server.

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

```python [Python]
result = await dotcraft.mcp_runtime.call_tool(
    thread.id,
    "docs",
    "search",
    {"query": "thread lifecycle"},
)
if result.is_error:
    raise RuntimeError("MCP tool call failed")
print(result.structured_content or result.content)
```

:::

This control call executes immediately. To let an agent choose and invoke an MCP tool during a run, configure the MCP server for the thread and start the run normally.

## Authenticate and reload

When status reports that OAuth is required, start login for that runtime name. Open the returned authorization URL in the user's browser and keep consuming connection notifications until login completes or fails.

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

```python [Python]
login = await dotcraft.mcp_runtime.login_oauth(
    name="docs",
    thread_id=thread.id,
    scopes=["read"],
    timeout_secs=60,
)
print(login.authorization_url)

await dotcraft.mcp_runtime.reload()
```

:::

Reload re-reads MCP configuration. It does not create a server definition and should not be used as a retry loop for a failing server. Inspect `failureReason` / `failure_reason` and `lastError` / `last_error` first.

## Choose the right tool surface

| Need | Surface |
| --- | --- |
| Expose an application callback as a tool for one thread | [Runtime Dynamic Tools](./tools#runtime-dynamic-tools) |
| Inspect or directly control a configured MCP server | MCP runtime API on this page |
| Connect a product integration with thread-scoped authority | [App Binding](../integrations/app-binding) |

## Related docs

- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [Build an App](../integrations/build-an-app)
- [AppServer Protocol](../protocols/appserver-protocol)
