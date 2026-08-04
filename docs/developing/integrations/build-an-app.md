# Build an App

App Binding uses AppServer for authority and a binding-scoped Streamable HTTP MCP server for tools.

## Use the typed SDK from a trusted client

A trusted DotCraft client can discover an app, start its connection handoff, inspect connection state, and manage thread bindings through the high-level SDK. Keep `requestToken`, principal credentials, and binding bearers out of logs.

::: code-group

```ts [TypeScript]
const apps = await dotcraft.appBindings.listApps({ threadId: thread.id });
const app = await dotcraft.appBindings.viewApp(appId, { threadId: thread.id });
const handoff = await dotcraft.appBindings.startConnection(appId);

// The app principal completes the handoff described below.
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

// The app principal completes the handoff described below.
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

# The app principal completes the handoff described below.
connection = await dotcraft.app_bindings.connection_status(app_id)
enabled = await dotcraft.app_bindings.enable(thread.id, app_id)
bindings = await dotcraft.app_bindings.list_thread_bindings(thread.id)
await dotcraft.app_bindings.revoke_thread_binding(
    thread.id, binding_id, "user disconnected app"
)
```

:::

`startConnection` / `StartConnectionAsync` / `start_connection` starts the request but does not authenticate the app. Enable a thread binding only after the app connection is ready. Use the returned handoff in your UI; do not send its token through an agent prompt.

## Connect the app principal

1. A trusted DotCraft client calls `app/connection/start` with `appId`.
2. The app reads the handoff with `app/connection/request/get`.
3. The app calls `app/connection/connect` and stores the returned credential. It is returned once.
4. Immediately call `app/connection/authenticate` on the initialized AppServer connection.
5. Authenticate later connections with the stored credential. Use `app/connection/refresh` to rotate it.

The principal credential expires after 30 days. Rotation invalidates the old credential immediately.

## Activate a thread binding

After the user enables the app with `thread/appBindings/enable`, obtain the `bindingRequestId` from `app/binding/requested` while online. If the app is offline, use the request-specific handoff returned to the trusted client. Inspect the request with `app/binding/request/get`, then call:

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

Expose tools through that MCP server. Associate interactive results with stable MCP Apps `ui://` resources. App descriptors contain identity, installation, connection UX, branding, and security links only.

After a process restart, call `app/bindings/list`, then `app/binding/rebind` with the current `authorityRevision`, a trusted endpoint, and a new bearer. Use `thread/appBindings/confirmCapabilities` only from a trusted DotCraft client.

## Endpoint rules

- Streamable HTTP only.
- Remote HTTPS or loopback HTTP only.
- No command, arguments, environment, working directory, or stdio configuration.
- Redirects or trust-boundary changes require activation again.

See [Connected Apps](../../features/agent-system/connected-apps) for the user workflow and [App Binding](./app-binding) for the protocol model.

## Related docs

- [Connected Apps](../../features/agent-system/connected-apps)
- [App Binding](./app-binding)
- [AppServer Protocol](../protocols/appserver-protocol)
- [MCP Apps](./mcp-apps)
- [SDK reference](../sdks/)
