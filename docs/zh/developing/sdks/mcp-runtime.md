# MCP 运行时

使用 SDK 的 MCP 运行时接口，可以检查 thread 可见的 server、读取 resource、调用 tool、启动 OAuth 或重新加载 MCP 配置。这是已配置 MCP server 的控制 API，不会在 SDK 进程中定义运行时动态工具。

## 理解 server 作用域

MCP server 可以来自工作区配置、插件、thread 或 App Binding。状态条目的 `origin` 会标识来源，并在适用时给出拥有它的插件、thread 或 binding。

当可见 server 集合取决于 thread 配置或 binding 时，传入 thread ID。运行时 `name` 是 resource、tool 和 OAuth 调用要使用的标识符，`declaredName` 是来源配置中的名称。

## 检查运行时状态

需要 tool 和 resource descriptor 时，请求 `detail: "full"`。省略 `detail` 时这也是默认行为。若只需要 tool 和认证相关的精简状态，请使用 `detail: "toolsAndAuthOnly"`。

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

结果分页时，把 `nextCursor` 作为下一次请求的 `cursor` 传回去。

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

:::

## 调用 tool

先确认 server 已启用、已启动并公开了目标 tool，再发起调用。调用走的是与模型调用相同的 dispatcher，权限检查、schema 校验、审批策略和结果长度限制都照常生效。`threadId` 是必填项，它决定了生效的 server 快照。

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

这个控制调用会立刻执行，不经过模型。若要让 agent 自己在 run 中挑选并调用 MCP tool，为 thread 配置好 MCP server，正常启动 run 即可。

## 认证与重新加载

只有 `authStatus` 为 `notLoggedIn` 的 server 才接受登录，其他状态下登录请求会被拒绝。用运行时名称发起登录，在用户浏览器中打开返回的授权 URL，然后等待 `mcpServer/oauthLogin/completed` 通知报告成功或失败。

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

Reload 会重新读取 MCP 配置并重连生效的运行时。它不会创建 server 定义，也不是失败 server 的重试循环。server 起不来时，先看状态里的 `failureReason` 和 `lastError`。

## 选择正确的工具接口

| 需求 | 接口 |
| --- | --- |
| 把应用回调作为某个 thread 的工具公开 | [运行时动态工具](./tools#运行时动态工具) |
| 检查或直接控制已配置的 MCP server | 本页的 MCP 运行时 API |
| 连接具有 thread 级权限的产品集成 | [DotCraft App](../integrations/app-binding) |

## 相关文档

- [线程与运行](./runs)——这些控制调用所依附的 thread 生命周期。
- [AppServer 协议](../protocols/appserver-protocol)——这些调用所属的 JSON-RPC 方法分组与能力协商。
