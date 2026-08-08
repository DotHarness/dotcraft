# MCP 运行时

使用 SDK 的 MCP 运行时接口，可以检查 thread 可见的 server、读取 resource、调用 tool、启动 OAuth 或重新加载 MCP 配置。这是已配置 MCP server 的控制 API；它不会在 SDK 进程中定义运行时动态工具。

## 理解 server 作用域

MCP server 可以来自工作区配置、插件、thread 或 App Binding。状态条目的 `origin` 会标识来源，并在适用时给出拥有它的插件、thread 或 binding。

当可见 server 集合取决于 thread 配置或 binding 时，传入 thread ID。运行时 `name` 是 resource、tool 和 OAuth 调用要使用的标识符；`declaredName` 是来源配置中的名称。

## 检查运行时状态

需要 tool 和 resource descriptor 时，请求 `detail: "full"`；省略 `detail` 时这也是默认行为。若只需要 tool 和认证相关的精简状态，请使用 `detail: "toolsAndAuthOnly"`。

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

结果分页时，在下一次请求中使用 `nextCursor` / `next_cursor`。

## 读取 resource

使用状态调用返回的运行时名称。URI 必须是该 server 公布的 resource，或匹配它的 resource template。

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

## 调用 tool

只有在确认 server 已启用、已启动并公开目标 tool 后才调用。Tool 参数由 MCP server 验证。

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

这个控制调用会立即执行。若要让 agent 在 run 中选择并调用 MCP tool，请为 thread 配置 MCP server，再正常启动 run。

## 认证与重新加载

当状态表明需要 OAuth 时，使用运行时名称启动登录。在用户浏览器中打开返回的授权 URL，并持续消费连接通知，直到登录完成或失败。

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

Reload 会重新读取 MCP 配置。它不会创建 server 定义，也不应该被当作失败 server 的重试循环。应先检查 `failureReason` / `failure_reason` 和 `lastError` / `last_error`。

## 选择正确的工具接口

| 需求 | 接口 |
| --- | --- |
| 把应用回调作为某个 thread 的工具公开 | [运行时动态工具](./tools#运行时动态工具) |
| 检查或直接控制已配置的 MCP server | 本页的 MCP 运行时 API |
| 连接具有 thread 级权限的产品集成 | [DotCraft App](../integrations/app-binding) |

## 相关文档

- [线程与运行](./runs)
- [工具与审批](./tools)
- [DotCraft App](../integrations/app-binding)
- [AppServer 协议](../protocols/appserver-protocol)
