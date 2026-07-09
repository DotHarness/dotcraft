# DotCraft Specifications

This directory contains DotCraft's normative and design specifications. Specs
are grouped by document purpose so readers can distinguish foundational
architecture, feature designs, internal agent mechanics, public protocols,
first-party clients, and SDK bindings.

## Core Reading Path

1. [Session Core](architecture/session-core.md) for the Thread -> Turn -> Item domain model.
2. [AppServer Protocol](protocols/appserver-protocol.md) for the JSON-RPC projection used by clients and SDKs.
3. [Plugin Architecture](architecture/plugin-architecture.md), [Skill 2.0](architecture/skill-2.0.md), and [Plugin Registry](architecture/plugin-registry.md) for extension loading, skill resolution, plugin boundaries, and public plugin distribution.
4. [Desktop Client](clients/desktop-client.md), [TUI Client](clients/tui-client.md), and [Desktop DESIGN.md](architecture/DESIGN.md) for first-party client behavior and shared Desktop visual rules.
5. [SDK](sdk/sdk.md) for cross-language client binding expectations.

## Categories

| Directory | Purpose |
|-----------|---------|
| [architecture](architecture/) | Foundational models, subsystem architecture, contribution systems, runtime architecture, design systems, and long-lived structural contracts. |
| [features](features/) | Standalone product or platform capabilities with their own UX, lifecycle, or service contract. |
| [agents](agents/) | Internal agent execution, prompt/runtime mechanics, subagent orchestration, and model-context behavior. |
| [protocols](protocols/) | Wire, API, and app-facing contracts consumed across processes, clients, SDKs, or external adapters. |
| [clients](clients/) | First-party client behavior specs. Shared visual design lives in `architecture/DESIGN.md`. |
| [sdk](sdk/) | Shared SDK contract plus language binding specifications. |

## Files By Category

- Architecture: [session-core.md](architecture/session-core.md), [hub-architecture.md](architecture/hub-architecture.md), [plugin-architecture.md](architecture/plugin-architecture.md), [plugin-registry.md](architecture/plugin-registry.md), [skill-2.0.md](architecture/skill-2.0.md), [lsp-plugin.md](architecture/lsp-plugin.md), [DESIGN.md](architecture/DESIGN.md), [openai-subscription-auth.md](architecture/openai-subscription-auth.md)
- Features: [agent-profiles.md](features/agent-profiles.md), [agent-teams.md](features/agent-teams.md), [goal.md](features/goal.md), [memory-consolidation.md](features/memory-consolidation.md), [dreams.md](features/dreams.md), [lifecycle-hooks.md](features/lifecycle-hooks.md), [context-export-cli.md](features/context-export-cli.md), [automations-lifecycle.md](features/automations-lifecycle.md), [default-chat-workspace.md](features/default-chat-workspace.md), [reasoning-settings.md](features/reasoning-settings.md), [remote-server-management.md](features/remote-server-management.md), [chrome-browser-runtime.md](features/chrome-browser-runtime.md), [desktop-inapp-browser.md](features/desktop-inapp-browser.md)
- Agents: [prompt-composition.md](agents/prompt-composition.md), [prompt-cache.md](agents/prompt-cache.md), [external-cli-subagent.md](agents/external-cli-subagent.md)
- Protocols: [appserver-protocol.md](protocols/appserver-protocol.md), [external-channel-adapter.md](protocols/external-channel-adapter.md), [app-binding.md](protocols/app-binding.md), [tool-result-presentation.md](protocols/tool-result-presentation.md)
- Clients: [desktop-client.md](clients/desktop-client.md), [tui-client.md](clients/tui-client.md)
- SDK: [sdk.md](sdk/sdk.md), [typescript.md](sdk/typescript.md), [dotnet.md](sdk/dotnet.md), [python.md](sdk/python.md)
