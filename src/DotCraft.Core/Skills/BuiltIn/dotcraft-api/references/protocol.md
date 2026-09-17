# AppServer protocol

Use AppServer directly for a custom client, unsupported language, or protocol-level debugging. Prefer a public SDK when it supports the application.

The current protocol reference is `https://www.dotcraft.net/developing/protocols/appserver-protocol`. Hub discovery and ownership are documented at `https://www.dotcraft.net/developing/protocols/hub-protocol` and `https://www.dotcraft.net/developing/lifecycle/hub`.

## Connection invariants

- AppServer uses JSON-RPC 2.0 over stdio or WebSocket.
- `initialize` is the first request on each connection.
- Send the `initialized` notification after a successful initialization response.
- Read the returned capabilities before exposing optional features.
- Keep consuming notifications and server-initiated requests while ordinary requests are in flight.
- Hub coordinates local processes; an SDK connects directly to the workspace AppServer after discovery.

Use the latest official protocol page for method names, request shapes, notifications, errors, and transport behavior. Generated TypeScript declarations and the public `DotCraft.Protocol` types can confirm shapes when the external project already uses an official SDK.

Do not infer a notification from a similarly named request or assume a field is optional because a sample omits it. If the official reference and public types do not establish a detail, report the gap rather than inventing a wire contract.
