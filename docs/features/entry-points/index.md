# Entry Points Overview

There's more than one way to open a DotCraft workspace. Whichever you choose, you're working with the same agent: it reads the same `.craft/` and shares the same [session core](../../developing/architecture/session-core) and long-term memory. The only thing that changes is **the surface you talk to it through**.

## Surface Shapes

![DotCraft entry points topology](/entry-points-topology.svg)

| Entry | Surface | Best for |
|---|---|---|
| [Desktop](./desktop) | Graphical desktop app | First-time use / long-running collaboration / complex diffs & approvals |
| [CLI](../../getting-started) | One-shot command | Scripts, SSH, CI, lightweight tasks |
| [IDE / Editor (ACP)](./editors) | Inside JetBrains / Obsidian / Unity / etc. | Read unsaved buffers, run via editor-managed terminal |
| [Channels / Bots](./channels) | QQ / WeCom / Feishu / Telegram / WeChat | Group chats, knowledge bots, support bots |

## Decision Matrix

| I want to… | Recommended |
|---|---|
| Start using DotCraft for the first time | [Desktop](./desktop) |
| Work over SSH on a remote server | [`dotcraft exec`](../../getting-started) or [AppServer + remote client](../../developing/lifecycle/appserver) |
| Let the IDE feed unsaved buffers to the agent | [ACP](./editors) |
| Bring an agent assistant into a Discord / QQ group | [Channels](./channels) |
| Share one workspace across multiple clients | [AppServer Mode](../../developing/lifecycle/appserver) + any of the above |
| Build a bot or custom client | [SDK overview](../../developing/sdks/python) + [AppServer Protocol](../../developing/protocols/appserver-protocol) |
| Run scheduled / CI automations | [Automations](../agent-system/automations) + any entry to handle approvals |

## Cross-Entry Sharing Principles

- **One AppServer per workspace**: locally coordinated by [Hub](../../developing/lifecycle/hub); you do not manage it by hand.
- **Threads resume across entries**: a Thread opened in Desktop can continue in ACP or another AppServer client; approvals render natively per platform.
- **Single source of truth for config**: model, security, automations all in `.craft/config.json` and `~/.craft/config.json`; every entry reads the same file.
- **Entry switches are independent**: ACP, Dashboard, Automations, and external channels are enabled per workspace.

## First-Time Picking

If you have not started yet:

1. Follow [Getting Started](../../getting-started) to install Desktop.
2. Walk through "select workspace + configure a model + first chat" inside Desktop.
3. Add a second entry (CLI / ACP / Channels) only when a real need shows up.

## Related docs

- [Unified Session Core](../../developing/architecture/session-core) — the Thread / Turn / Item model behind cross-entry sharing
- [AppServer Mode](../../developing/lifecycle/appserver) — remote, multi-client, custom integrations
- [Hub Local Coordination](../../developing/lifecycle/hub) — why "open and it just works" works
