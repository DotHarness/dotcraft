# Client SDK

Use a client SDK when an application connects to a workspace owned by another DotCraft process. Use Harness when the application owns the runtime itself.

## Choose a layer

| Layer | Use for | Official documentation |
| --- | --- | --- |
| High-level client | Threads, runs, tools, approvals, models, MCP runtime, and App Binding | `https://www.dotcraft.net/developing/sdks/` |
| Typed Wire | Explicit JSON-RPC lifecycle, transport, and generated request maps | `https://www.dotcraft.net/developing/sdks/typescript` or `https://www.dotcraft.net/developing/sdks/dotnet` |
| Generated contracts | DTOs and method maps without transport ownership | The same language reference above |

Installation and the first connection live at `https://www.dotcraft.net/developing/sdks/quickstart`. Threads, streaming, and reconnect behavior live at `https://www.dotcraft.net/developing/sdks/runs`. Tool callbacks and approvals live at `https://www.dotcraft.net/developing/sdks/tools`.

## Confirm the current API

For TypeScript, read the exports and declarations installed with `@dotcraft/sdk`. For .NET, use the public members from `DotCraft.Sdk` and its bundled `DotCraft.Protocol` assembly. Package declarations take precedence when an external project has not yet upgraded to the latest documented API.

Never translate an API name mechanically between TypeScript and .NET. Confirm the spelling, async shape, option type, and event representation in the language being edited.

## Runtime checks

Use initialization capabilities to decide whether optional protocol-backed features are available. A successful connection does not imply that every management or extension capability is enabled.
