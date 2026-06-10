# DotCraft Specifications

This directory contains the normative and design specifications for DotCraft.
Specifications are grouped by system domain so readers can start from the
area they are changing.

## Core Reading Path

1. [Session Core](core/session-core.md) for the Thread -> Turn -> Item domain model.
2. [AppServer Protocol](protocols/appserver-protocol.md) for the JSON-RPC projection used by clients and SDKs.
3. [Plugin Architecture](extensions/plugin-architecture.md) for extension loading and plugin boundaries.
4. [Desktop Client](clients/desktop-client.md) and [TUI Client](clients/tui-client.md) for first-party client behavior.
5. [SDK](sdk/sdk.md) for cross-language client binding expectations.

## Domains

| Directory | Purpose |
|-----------|---------|
| [core](core/) | Session lifecycle, goals, memory, and durable agent-facing workspace state. |
| [protocols](protocols/) | AppServer wire surface, app binding, external channel contracts, and result presentation. |
| [clients](clients/) | Desktop and TUI client UX contracts, including shared Desktop visual rules. |
| [runtime](runtime/) | Host/runtime services such as Hub, automations, provider auth, prompt cache, and browser runtime. |
| [agents](agents/) | Multi-agent coordination and external subagent execution design. |
| [extensions](extensions/) | Plugin, skill, and LSP extension architecture. |
| [sdk](sdk/) | Shared SDK contract plus TypeScript and .NET binding specs. |

## Files By Domain

- Core: [session-core.md](core/session-core.md), [goal-design.md](core/goal-design.md), [memory-consolidation.md](core/memory-consolidation.md), [dreams.md](core/dreams.md), [context-export-cli.md](core/context-export-cli.md), [core-architecture-refactor.md](core/core-architecture-refactor.md)
- Protocols: [appserver-protocol.md](protocols/appserver-protocol.md), [external-channel-adapter.md](protocols/external-channel-adapter.md), [app-binding.md](protocols/app-binding.md), [tool-result-presentation.md](protocols/tool-result-presentation.md)
- Clients: [desktop-client.md](clients/desktop-client.md), [Desktop DESIGN.md](clients/DESIGN.md), [tui-client.md](clients/tui-client.md)
- Runtime: [hub-architecture.md](runtime/hub-architecture.md), [automations-lifecycle.md](runtime/automations-lifecycle.md), [chrome-browser-runtime.md](runtime/chrome-browser-runtime.md), [desktop-inapp-browser.md](runtime/desktop-inapp-browser.md), [openai-subscription-auth.md](runtime/openai-subscription-auth.md), [prompt-cache.md](runtime/prompt-cache.md), [reasoning-settings.md](runtime/reasoning-settings.md)
- Agents: [agent-teams.md](agents/agent-teams.md), [external-cli-subagent-design.md](agents/external-cli-subagent-design.md)
- Extensions: [plugin-architecture.md](extensions/plugin-architecture.md), [skill-2.0.md](extensions/skill-2.0.md), [lsp-plugin.md](extensions/lsp-plugin.md)
- SDK: [sdk.md](sdk/sdk.md), [typescript.md](sdk/typescript.md), [dotnet.md](sdk/dotnet.md)

