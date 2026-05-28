# Protocol Specifications

Protocol specs define DotCraft's cross-process and app-facing contracts. These
documents own wire shapes, capability negotiation, compatibility rules, and
presentation contracts shared by clients, SDKs, and adapters.

| File | Purpose |
|------|---------|
| [appserver-protocol.md](appserver-protocol.md) | JSON-RPC 2.0 AppServer methods, events, transports, errors, and extension surfaces. |
| [external-channel-adapter.md](external-channel-adapter.md) | External channel adapter connection modes, protocol extensions, and behavioral contract. |
| [app-binding.md](app-binding.md) | App descriptor, binding, runtime tool exposure, lifecycle, security, and SDK integration expectations. |
| [tool-result-presentation.md](tool-result-presentation.md) | Declarative tool result renderer and action contract for trusted local clients. |

Related domains: [Core](../core/), [Clients](../clients/), [SDK](../sdk/).

