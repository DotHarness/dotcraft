# Architecture Overview

DotCraft is a .NET 10 / C# Agent Harness. Its modular design lets CLI, editors, bots, automations, and GitHub workflows share one workspace and reuse the same sessions, memory, skills, and tools. This page targets **integrators and contributors** — it explains the code-level boundaries that matter for extension and troubleshooting.

## Top-Level Modules

![DotCraft runtime architecture topology](/runtime-architecture-topology.svg)

## Module Types & Discovery

Every interaction mode implements `IDotCraftModule` and is discovered by the `DotCraft.Generators` source generators. Three module kinds:

| Type | Description | Examples |
|---|---|---|
| **Host** | Standalone entry that owns the process | CLI (`DotCraft.App`, priority `0`) |
| **Channel** | Managed by Gateway | ACP, Automations, QQ / WeCom / Feishu / Telegram / WeChat |
| **Tool-only** | Provides tools without an entry point | Auxiliary toolsets |

> [!NOTE]
> **Host priority**: When multiple Hosts are enabled, the lower-priority Host owns the process; if none are enabled, Gateway owns the process and loads enabled Channels.

## Session Core

Session Core defines `Thread → Turn → Item` and uses `ISessionService` as the central API:

- Thread lifecycle (`thread/start`, `thread/resume`, `thread/list`, `thread/read`, `thread/archive`, `thread/delete`, `thread/pause`, `thread/setMode`)
- Input submission (`turn/start`, `turn/interrupt`)
- Streaming event subscription (`item/agentMessage/delta`, `turn/completed`, ...)
- Approval flow (`item/approval/request` ↔ JSON-RPC responses)

| Entry | Uses ISessionService |
|---|---|
| CLI, ACP, Automations, external channel adapters | Yes (persistent threads + cross-entry sharing) |

## AppServer

AppServer exposes `ISessionService` to external clients as JSON-RPC 2.0:

- **Transport**: stdio (JSONL, one message per line) and WebSocket (one message per frame)
- **Clients**: Desktop, CLI, ACP, external channel adapters, SDKs (TypeScript / .NET / Python)
- **Auth**: WebSocket `?token=` query string ([details](../lifecycle/appserver))

See [AppServer Protocol](../protocols/appserver-protocol) and [AppServer Mode](../lifecycle/appserver).

## Hub

Each user has one [Hub](../lifecycle/hub) on the machine. Hub starts/reuses one AppServer per workspace and maintains discovery info and locks under `~/.craft/hub/`. Desktop and CLI use Hub by default. Bypass Hub for remote, CI, bots, or protocol debugging — use [AppServer Mode](../lifecycle/appserver).

## Agents

`DotCraft.Core.Agents` builds on `Microsoft.Extensions.AI`:

- Agent factories and runners
- Tool injection and filtering (`EnabledTools`, SubAgent role allow/deny list, Hook interception)
- SubAgents: `native` and `cli-oneshot` runtimes (see [SubAgents](../../features/agent-system/subagents))
- Context compaction pipeline (auto + reactive + microcompaction, via `Compaction.*`)

## Configuration

DotCraft layers two configs:

| Layer | Path | Purpose |
|---|---|---|
| Global | `~/.craft/config.json` | Provider credentials, endpoints, personal preferences |
| Workspace | `<workspace>/.craft/config.json` | Model selection, entry switches, automations, security |

Modules declare config sections via `[ConfigSection("Key")]` inside their assembly. The source generator collects them. Adding a new module integrates it into the merged schema automatically.

Full field reference: [Configuration Reference](../configuration). How fields take effect (immediate / subsystem restart / AppServer restart): [Settings Lifecycle](../lifecycle/settings-lifecycle).

## Related

- [Configuration Reference](../configuration)
- [AppServer Protocol](../protocols/appserver-protocol) / [AppServer Mode](../lifecycle/appserver)
- [Hub Protocol](../protocols/hub-protocol) / [Hub Local Coordination](../lifecycle/hub)
- [SDK Overview](../sdks/) · [TypeScript SDK](../sdks/typescript) · [.NET SDK](../sdks/dotnet) · [Python SDK](../sdks/python)
