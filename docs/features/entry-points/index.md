# Entry Points Overview

DotCraft offers several ways to "open the same workspace". Every entry point speaks to the same [Unified Session Core](../session-core.md), reads the same `.craft/`, and shares the same long-term memory. The only difference is **the surface you talk to the agent through**.

## Surface Shapes

![DotCraft entry points topology](/entry-points-topology.svg)

| Entry | Surface | Best for |
|---|---|---|
| [Desktop](./desktop.md) | Graphical desktop app | First-time use / long-running collaboration / complex diffs & approvals |
| [TUI](./tui.md) | Rust-native terminal UI | Terminal-native, SSH remote, lightweight |
| [IDE / Editor (ACP)](./editors.md) | Inside JetBrains / Obsidian / Unity / etc. | Read unsaved buffers, run via editor-managed terminal |
| [Channels / Bots](./channels.md) | QQ / WeCom / Feishu / Telegram / WeChat | Group chats, knowledge bots, support bots |

## Decision Matrix

| I want to… | Recommended |
|---|---|
| Start using DotCraft for the first time | [Desktop](./desktop.md) |
| Work over SSH on a remote server | [TUI](./tui.md) or [AppServer + remote TUI/Desktop](../../developing/appserver.md) |
| Let the IDE feed unsaved buffers to the agent | [ACP](./editors.md) |
| Bring an agent assistant into a Discord / QQ group | [Channels](./channels.md) |
| Share one workspace across multiple clients | [AppServer Mode](../../developing/appserver.md) + any of the above |
| Build a bot or custom client | [SDK overview](../../developing/sdk-python.md) + [AppServer Protocol](../../developing/appserver-protocol.md) |
| Run scheduled / CI automations | [Automations](../automations.md) + any entry to handle approvals |

## Cross-Entry Sharing Principles

- **One AppServer per workspace**: locally coordinated by [Hub](../../developing/hub.md); you do not manage it by hand.
- **Threads resume across entries**: a Thread opened in Desktop can continue in TUI / ACP; approvals render natively per platform.
- **Single source of truth for config**: model, security, automations all in `.craft/config.json` and `~/.craft/config.json`; every entry reads the same file.
- **Entry switches are independent**: ACP, Dashboard, Automations, and external channels are enabled per workspace.

## First-Time Picking

If you have not started yet:

1. Follow [Getting Started](../../getting-started.md) to install Desktop.
2. Walk through "select workspace + configure a model + first chat" inside Desktop.
3. Add a second entry (TUI / ACP / Channels) only when a real need shows up.

## Related

- [Unified Session Core](../session-core.md) — the Thread / Turn / Item model behind cross-entry sharing
- [AppServer Mode](../../developing/appserver.md) — remote, multi-client, custom integrations
- [Hub Local Coordination](../../developing/hub.md) — why "open and it just works" works
