# 构建应用

App Binding 使用 AppServer 管理授权，并使用 binding-scoped Streamable HTTP MCP server 提供工具。

## 在可信 client 中使用类型化 SDK

可信 DotCraft client 可以通过高层 SDK 发现 app、启动连接 handoff、检查连接状态并管理 thread binding。不要在日志中记录 `requestToken`、principal credential 或 binding bearer。

::: code-group

```ts [TypeScript]
const apps = await dotcraft.appBindings.listApps({ threadId: thread.id });
const app = await dotcraft.appBindings.viewApp(appId, { threadId: thread.id });
const handoff = await dotcraft.appBindings.startConnection(appId);

// App principal 按下文流程完成 handoff。
const connection = await dotcraft.appBindings.connectionStatus(appId);
const enabled = await dotcraft.appBindings.enable(thread.id, appId);
const bindings = await dotcraft.appBindings.listThreadBindings(thread.id);
await dotcraft.appBindings.revokeThreadBinding(thread.id, bindingId, "user disconnected app");
```

```csharp [.NET]
using DotCraft.Protocol.AppServer;

var apps = await client.AppBindings.ListAppsAsync(new AppListParams { ThreadId = thread.Id });
var app = await client.AppBindings.ViewAppAsync(new AppViewParams { AppId = appId, ThreadId = thread.Id });
var handoff = await client.AppBindings.StartConnectionAsync(new AppConnectionStartParams { AppId = appId });

// App principal 按下文流程完成 handoff。
var connection = await client.AppBindings.GetConnectionStatusAsync(new AppConnectionStatusParams { AppId = appId });
var enabled = await client.AppBindings.EnableBindingAsync(new ThreadAppBindingEnableParams { ThreadId = thread.Id, AppId = appId });
var bindings = await client.AppBindings.ListThreadBindingsAsync(new ThreadAppBindingsListParams { ThreadId = thread.Id });
await client.AppBindings.RevokeThreadBindingAsync(new ThreadAppBindingRevokeParams
{
    ThreadId = thread.Id,
    BindingId = bindingId,
    Reason = "user disconnected app"
});
```

```python [Python]
apps = await dotcraft.app_bindings.list_apps(thread_id=thread.id)
app = await dotcraft.app_bindings.view_app(app_id, thread_id=thread.id)
handoff = await dotcraft.app_bindings.start_connection(app_id)

# App principal 按下文流程完成 handoff。
connection = await dotcraft.app_bindings.connection_status(app_id)
enabled = await dotcraft.app_bindings.enable(thread.id, app_id)
bindings = await dotcraft.app_bindings.list_thread_bindings(thread.id)
await dotcraft.app_bindings.revoke_thread_binding(
    thread.id, binding_id, "user disconnected app"
)
```

:::

`startConnection` / `StartConnectionAsync` / `start_connection` 只启动请求，并不会认证 app。App 连接 ready 后才能启用 thread binding。请在 UI 中使用返回的 handoff，不要把其中的 token 发送到 agent prompt。

## 连接 app principal

1. 可信 DotCraft 客户端用 `appId` 调用 `app/connection/start`。
2. 应用通过 `app/connection/request/get` 读取 handoff。
3. 应用调用 `app/connection/connect` 并保存返回的 credential；该 credential 只返回一次。
4. 立即在已经 initialized 的 AppServer 连接上调用 `app/connection/authenticate`。
5. 后续连接使用已保存的 credential 认证；通过 `app/connection/refresh` 轮换 credential。

Principal credential 会在 30 天后过期，轮换后旧 credential 立即失效。

## 激活线程 binding

用户通过 `thread/appBindings/enable` 启用应用后，在线 App 从 `app/binding/requested` 获取 `bindingRequestId`；如果 App 离线，则使用可信 client 收到的请求专用 handoff。应用先用 `app/binding/request/get` 检查请求，再调用：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "app/binding/activate",
  "params": {
    "bindingRequestId": "appbindreq_...",
    "endpoint": "https://app.example/mcp/binding/123",
    "bearer": "one-time-binding-secret"
  }
}
```

工具由该 MCP server 暴露；交互结果使用稳定 MCP Apps `ui://` resource。App descriptor 只包含产品身份、安装与连接体验、品牌和安全链接信息。

进程重启后，调用 `app/bindings/list`，再用当前 `authorityRevision`、可信 endpoint 与新 bearer 调用 `app/binding/rebind`。`thread/appBindings/confirmCapabilities` 只能由可信 DotCraft 客户端调用。

## Endpoint 规则

- 只允许 Streamable HTTP。
- 只允许远程 HTTPS 或 loopback HTTP。
- 不接受 command、arguments、environment、working directory 或 stdio 配置。
- redirect 或信任边界变化后必须重新激活。

用户操作流程见 [Connected Apps](../../features/agent-system/connected-apps)，协议模型见 [App Binding](./app-binding)。

## 相关文档

- [Connected Apps](../../features/agent-system/connected-apps)
- [App Binding](./app-binding)
- [AppServer 协议](../protocols/appserver-protocol)
- [MCP Apps](./mcp-apps)
- [SDK 参考](../sdks/)
