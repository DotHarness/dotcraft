# App Binding

This page targets app integrators and client authors. App Binding separates a workspace app connection from the per-thread authority that exposes an app's tools.

For the Desktop workflow, see [Connected Apps](../../features/agent-system/connected-apps).

## Connection and binding

| Scope | Purpose | Control plane |
|---|---|---|
| **App connection** | Authenticates one app principal for the workspace | `app/connection/*` |
| **Thread binding** | Grants one thread access to that app | `thread/appBindings/*`, `app/binding/*` |

An app can have one workspace connection and multiple thread bindings. Turning the app off in one thread revokes only that binding. Disconnecting the app principal revokes every binding owned by that connection.

## Connect the app principal

1. A trusted client calls `app/connection/start` with the app ID.
2. The app reads the handoff with `app/connection/request/get`.
3. The app calls `app/connection/connect`.
4. The server returns the principal credential once.
5. The app immediately calls `app/connection/authenticate` on its initialized AppServer connection.
6. Later connections authenticate with the stored credential; `app/connection/refresh` rotates it.

The principal credential expires after 30 days. Rotation invalidates the previous credential immediately.

`app/connection/revoke` removes the workspace connection and revokes all of its thread bindings.

## Activate a thread binding

A trusted client starts the binding with `thread/appBindings/enable`. The server creates a ten-minute binding request.

An online app principal receives `app/binding/requested` with the `bindingRequestId`. If the app is offline, the trusted client receives a request-specific handoff from `thread/appBindings/enable` and passes it to the app. The authenticated app reads the request with `app/binding/request/get`, then activates it:

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

The endpoint must expose a Streamable HTTP MCP server. DotCraft creates a binding-scoped MCP session and reads its tool snapshot before the binding becomes ready.

`thread/appBindings/revoke` removes the binding from one thread without disconnecting the app principal.

### New-thread selection

A client may stage app selections before creating a thread. After `thread/start`, enable the selected apps and wait for them to become ready before submitting the first turn.

## Capability changes

The first valid tool snapshot is approved by the original enable action.

- Narrower capability changes are accepted automatically.
- Expanded tool schema, visibility, risk, UI, CSP, domain, or permission authority requires confirmation.
- A trusted client calls `thread/appBindings/confirmCapabilities` to retain the previous approved baseline or accept the new one.

Accepting the expansion makes the new baseline active. Retaining the previous baseline rejects the expansion, removes the live MCP session, and leaves the binding offline until the app rebinds with a compatible capability set.

## Offline and rebind behavior

An offline binding retains stable tool schemas, but tool calls fail with `AppBindingOffline`.

After a process restart, the authenticated app calls `app/bindings/list`, then `app/binding/rebind` with:

- the current `authorityRevision`;
- a trusted endpoint;
- a new bearer.

Each binding keeps its own MCP session and bearer. Live MCP clients and binding bearer values are not persisted.

## Endpoint rules

- Only Streamable HTTP endpoints are accepted.
- Remote endpoints must use HTTPS.
- Loopback endpoints may use HTTP.
- App Binding does not accept command, arguments, environment, working-directory, or stdio configuration.
- Redirects or trust-boundary changes require activation again.

## Social channels

Social conversation bindings use the social binding methods and native plugin tools instead of MCP tools. DotCraft injects the bound delivery target on the server.

Channel tools must not declare `target`, `chatId`, `groupId`, `conversationId`, `deliveryTarget`, or aliases of those fields.

## Security boundary

An authenticated app connection can call only App Binding app-role methods. It cannot read threads, start turns, inspect the workspace, or control another app.

DotCraft persists salted credential verifiers and normalized non-sensitive capability snapshots. It does not persist principal credentials, binding bearers, live MCP clients, or UI resource bodies.

## Related docs

- [Connected Apps](../../features/agent-system/connected-apps)
- [Build an App](./build-an-app)
- [MCP Apps](./mcp-apps)
- [AppServer Protocol](../protocols/appserver-protocol)
- [Security & Sandbox](../../features/self-hosted/security)
