# Runtime Specifications

Runtime specs define host services and operational behavior that sit around
Session Core and AppServer. They cover process coordination, background work,
provider/runtime integrations, and execution settings.

| File | Purpose |
|------|---------|
| [hub-architecture.md](hub-architecture.md) | Local Hub coordinator, managed AppServer, registry, locks, health, and client bootstrap. |
| [automations-lifecycle.md](automations-lifecycle.md) | Automation task identity, AppServer surface, local task files, and dispatch rules. |
| [chrome-browser-runtime.md](chrome-browser-runtime.md) | Chrome browser runtime sessions, transport, command lifecycle, diagnostics, and recovery. |
| [desktop-browser-parity.md](desktop-browser-parity.md) | Desktop embedded browser behavior contract. |
| [openai-subscription-auth.md](openai-subscription-auth.md) | Sign in with ChatGPT auth, credential layout, routing, telemetry, and UX. |
| [prompt-cache.md](prompt-cache.md) | Prompt cache stability strategy, measurement contract, and runtime guardrails. |
| [reasoning-settings.md](reasoning-settings.md) | Reasoning configuration model, AppServer changes, provider semantics, and client UX. |
| [remote-server-management.md](remote-server-management.md) | Desktop-owned SSH manager for remote DotCraft Docker stacks: settings schema, allow-listed Compose operations, tunnel-first connection, and the Servers surface. |

Related domains: [Core](../core/), [Protocols](../protocols/), [Clients](../clients/).

