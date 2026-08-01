# DotCraft Specifications

This directory contains DotCraft's normative and design specifications. Specs
are grouped by document purpose so readers can distinguish foundational
architecture, feature designs, public protocols, first-party clients, and SDK
bindings.

## Core Reading Path

1. [Session Core](architecture/session-core.md) for the Thread -> Turn -> Item domain model.
2. [Context Compaction](architecture/context-compaction.md) for compaction backend selection, replacement domains, and recovery.
3. [Tool Architecture](architecture/tools-architecture.md) for tool identity, authority, execution, result audiences, and presentation boundaries.
4. [AppServer Protocol](protocols/appserver-protocol.md) for the JSON-RPC projection used by clients and SDKs.
5. [Plugin Architecture](architecture/plugin-architecture.md), [Skill 2.0](architecture/skill-2.0.md), and [Plugin Registry](architecture/plugin-registry.md) for extension loading, skill resolution, plugin boundaries, and public plugin distribution.
6. [Desktop Client](clients/desktop-client.md) and [Desktop DESIGN.md](architecture/DESIGN.md) for first-party client behavior and shared Desktop visual rules.
7. [SDK](sdk/sdk.md) for cross-language client binding expectations and [AppServer Protocol Contracts and SDK Generation](sdk/protocol-contract-generation.md) for the executable wire contract and generated low-level bindings.

## Categories

| Directory | Purpose |
|-----------|---------|
| [architecture](architecture/) | Foundational models, subsystem architecture, contribution systems, runtime architecture, design systems, and long-lived structural contracts. |
| [features](features/) | Standalone product or platform capabilities with their own UX, lifecycle, or service contract. |
| [protocols](protocols/) | Wire, API, and app-facing contracts consumed across processes, clients, SDKs, or external adapters. |
| [clients](clients/) | First-party client behavior specs. Shared visual design lives in `architecture/DESIGN.md`. |
| [sdk](sdk/) | Shared SDK contract plus language binding specifications. |

## Files By Category

- Architecture: [session-core.md](architecture/session-core.md), [context-compaction.md](architecture/context-compaction.md), [tools-architecture.md](architecture/tools-architecture.md), [prompt-composition.md](architecture/prompt-composition.md), [prompt-cache.md](architecture/prompt-cache.md), [responses-provider-history.md](architecture/responses-provider-history.md), [model-runtime.md](architecture/model-runtime.md), [hub-architecture.md](architecture/hub-architecture.md), [plugin-architecture.md](architecture/plugin-architecture.md), [plugin-registry.md](architecture/plugin-registry.md), [skill-2.0.md](architecture/skill-2.0.md), [lsp-plugin.md](architecture/lsp-plugin.md), [DESIGN.md](architecture/DESIGN.md), [openai-subscription-auth.md](architecture/openai-subscription-auth.md)
- Features: [agent-profiles.md](features/agent-profiles.md), [agent-teams.md](features/agent-teams.md), [external-cli-subagent.md](features/external-cli-subagent.md), [goal.md](features/goal.md), [memory-consolidation.md](features/memory-consolidation.md), [dreams.md](features/dreams.md), [lifecycle-hooks.md](features/lifecycle-hooks.md), [context-export-cli.md](features/context-export-cli.md), [automations-lifecycle.md](features/automations-lifecycle.md), [default-chat-workspace.md](features/default-chat-workspace.md), [model-options.md](features/model-options.md), [multi-folder-projects.md](features/multi-folder-projects.md), [remote-server-management.md](features/remote-server-management.md), [chrome-browser-runtime.md](features/chrome-browser-runtime.md), [desktop-inapp-browser.md](features/desktop-inapp-browser.md)
- Protocols: [appserver-protocol.md](protocols/appserver-protocol.md), [external-channel-adapter.md](protocols/external-channel-adapter.md), [app-binding.md](protocols/app-binding.md)
- Clients: [desktop-client.md](clients/desktop-client.md)
- SDK: [sdk.md](sdk/sdk.md), [protocol-contract-generation.md](sdk/protocol-contract-generation.md), [typescript.md](sdk/typescript.md), [dotnet.md](sdk/dotnet.md), [python.md](sdk/python.md)
