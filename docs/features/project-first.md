# Project Workspace

DotCraft anchors every AI workflow to a real project directory. When you open a project, its conversation history, skills, long-term memory, automations, plugins, and model selection live in a single `.craft/` directory that is backed up, shared, and restored together with the repository — never scattered across a cloud account, a system prompt, or a single editor window.

## Key Concepts

| Concept | Meaning |
|---|---|
| **Workspace** | Project root that contains a `.craft/` directory. Desktop, TUI, ACP, bots, and automations all act on the same workspace. |
| **Bootstrap files** | `.craft/AGENTS.md`, `.craft/SOUL.md`, `.craft/USER.md` — agent rules, personality, and user profile. |
| **Memory & History** | `.craft/MEMORY.md` and `.craft/HISTORY.md` — long-term memory and history maintained by the agent and readable by humans. |
| **Skills / Plugins** | `.craft/skills/` and `.craft/plugins/` — capability bundles that travel with the workspace. |
| **Sessions** | `.craft/sessions/` and `.craft/threads/` — conversation archives shared by every entry point. |
| **Config** | `.craft/config.json` — workspace-level config layered on top of global `~/.craft/config.json`. |

## Why Organized This Way

![DotCraft workspace topology](/workspace-topology.svg)

Every entry point reads the workspace **before doing anything**. That means:

- Switching entry points never loses context: a session opened in Desktop continues in TUI.
- Restoring on another machine costs nothing: sync the project directory.
- Teams share one baseline: commit the shareable parts of `.craft/` (skills, commands, AGENTS.md) and every teammate's Desktop boots with the same agent constraints.

## Global vs Workspace Config

DotCraft layers two configuration files:

| Layer | Path | Purpose |
|---|---|---|
| Global | `~/.craft/config.json` | Provider credentials, endpoints, personal preferences — not committed |
| Workspace | `<workspace>/.craft/config.json` | Model selection, entry-point switches, automations, security — committable |

When in doubt: **put providers globally and let the workspace override only the project-specific choices**. Provider fields, entry switches, memory, skills, automations, and security options all live in the [Configuration Reference](../developing/configuration).

## Bootstrap Trio

The three Markdown files under `.craft/` decide "who this agent is in this project and what rules it follows":

- `AGENTS.md` — role responsibilities, behavior boundaries, answer rules
- `SOUL.md` — personality, tone, expression style
- `USER.md` — user profile, audience background, communication preferences

They are plain Markdown with no special syntax. DotCraft injects them into the system prompt at every session start.

## First Entry

| Step | Command or action |
|---|---|
| Open in Desktop | Launch Desktop → choose project folder → follow setup |
| Init from terminal | `cd <project>` → `dotcraft setup` and answer prompts |
| Verify | In Desktop, ask "Read README and docs/index.md and tell me how to start the project" |

Full walkthrough: [Getting Started](../getting-started).

## When to Enable / Disable

Treat entry switches (ACP and external channels) and behavior switches (Memory / Skills / Automations) as separate axes. Keep personal credentials global, keep shared project policy in the workspace, and use Dashboard Settings when you need to inspect the merged result.

Full switch names, defaults, and JSON examples are in the [Configuration Reference](../developing/configuration).

## Related

- [Getting Started](../getting-started)
- [Workspace Handoff](../developing/workflow/workspace-handoff)
- [Memory & Dreams](./agent-system/memory)
- [Unified Session Core](../developing/architecture/session-core)
- [Security & Sandbox](./self-hosted/security)
- [Configuration Reference](../developing/configuration)
