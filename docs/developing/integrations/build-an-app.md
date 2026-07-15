# Build an App

App Binding uses AppServer for authority and a binding-scoped Streamable HTTP MCP server for tools.

## Connect the app principal

1. A trusted DotCraft client calls `app/connection/start` with `appId`.
2. The app reads the handoff with `app/connection/request/get`.
3. The app calls `app/connection/connect` and stores the returned credential. It is returned once.
4. On later AppServer connections, call `app/connection/authenticate`. Use `app/connection/refresh` to rotate the credential.

The principal credential lasts 30 days by default. Rotation invalidates the old credential immediately.

## Activate a thread binding

After the user enables the app with `thread/appBindings/enable`, inspect the request with `app/binding/request/get`, then call:

```json
{
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

See [App Binding](./app-binding) for the user model.
