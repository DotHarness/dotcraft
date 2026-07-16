# App Binding

App Binding connects one installed app to one DotCraft thread. The binding owns connection authority; tools and interactive UI use a binding-scoped MCP session.

## What a binding grants

After you select **Enable**, DotCraft creates a ten-minute handoff. The app authenticates as its app principal and activates the binding with a Streamable HTTP MCP endpoint and a one-time bearer token. DotCraft then reads the MCP tool snapshot.

- The first valid snapshot is approved by the original Enable click.
- Narrower changes are accepted automatically.
- Expanded schema, visibility, risk, UI, CSP, domain, or permission authority requires confirmation.
- Offline bindings retain stable tool schemas but calls fail with `AppBindingOffline`.
- Revoking a binding removes its MCP session, calls, views, and model-visible tools immediately.

Each binding has its own MCP session and credential. Remote endpoints must use HTTPS; HTTP is allowed only on loopback. Restarted bindings remain offline until the same app principal rebinds them with a new bearer.

## Social channels

Conversation bindings use the social binding methods, but their tools are native plugin tools rather than MCP tools. DotCraft injects the bound delivery target on the server. Channel tools cannot declare or pass `target`, `chatId`, `groupId`, `conversationId`, `deliveryTarget`, or aliases of those fields.

## Security boundary

An authenticated app connection can call only App Binding app-role methods. It cannot read threads, start turns, inspect the workspace, or control another app. DotCraft persists only salted credential verifiers and non-sensitive normalized capability snapshots; principal credentials, binding bearers, live MCP clients, and UI resource bodies are not stored.

For implementation details, see [Build an App](./build-an-app) and the [AppServer protocol](../protocols/appserver-protocol).
