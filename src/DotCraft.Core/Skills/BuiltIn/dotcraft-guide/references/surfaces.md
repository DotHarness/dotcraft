# Workspace Surfaces

Everything DotCraft keeps on disk. Confirm a path exists before quoting it; a workspace only grows the directories its features have used.

## `<workspace>/.craft/`

| Path | Status | What it is |
|---|---|---|
| `config.json` | user-editable | This project's settings. See `config-map.md` |
| `hooks.json` | user-editable | Workspace lifecycle hooks. Use `$create-hooks` |
| `AGENTS.md` | user-editable | Instructions for agents working in this workspace |
| `commands/` | user-editable | Custom `/` commands. Not skills |
| `models.json` | user-editable | Model catalog overrides, including context-window entries. Sits beside a `config.json` |
| `model-thinking-adapters.json` | user-editable | Model reasoning-adapter overrides |
| `qq.json` `wecom.json` `feishu.json` `telegram.json` `weixin.json` | prefer the UI | One per bundled channel adapter. Configure these through Desktop Settings > Channels, which validates credentials and restarts the adapter |
| `skills/` | mixed | Installed and built-in skills. A `.builtin` marker file means the directory was deployed by the product |
| `plugins/` | mixed | Installed plugins, each with `.craft-plugin/plugin.json`. Install and enable through Desktop > Plugins |
| `agents/` | user-editable | Subagent definitions |
| `automations/` | user-editable | Saved automations |
| `memory/MEMORY.md` | user-editable | Long-term facts, always in context. Use `$memory` |
| `memory/HISTORY.md` | runtime-owned | Append-only event log, searched not read whole |
| `skill-variants/` | runtime-owned | Self-learning overlays on source skills |
| `threads/active/*.jsonl` `threads/archived/*.jsonl` | never touch | Authoritative thread rollouts |
| `state.db` (`-shm`, `-wal`) | never touch | Projections, runtime state, traces |
| `appserver.lock` | never touch | Workspace AppServer lock |
| `logs/` `tracing/` | read-only evidence | Operational logs and traces |
| `attachments/` `tool-results/` `runtime/` `cache/` | never touch | Payloads and runtime scratch |

Hard rules: never hand-edit `state.db`, `threads/*.jsonl`, or anything under `runtime/`. Deleting a rollout destroys the only authoritative copy of that thread.

## `~/.craft/`

| Path | Status | What it is |
|---|---|---|
| `config.json` | user-editable | Personal defaults, including the `Providers` registry |
| `hooks.json` | user-editable | Personal hooks, applied before workspace hooks |
| `skills/` | user-editable | User-global skills, available in every workspace |
| `plugins/` | mixed | User-global plugin container |
| `auth.json` `mcp-auth.json` | never touch | OAuth tokens written by `dotcraft auth openai login` and the MCP OAuth flow |
| `bin/` | runtime-owned | Default install directory of the `dotcraft` CLI from the install script |
| `hub/` | runtime-owned | Hub state: `hub.lock`, the AppServer registry, and runtime tool paths |
| `workspaces/chats/` | runtime-owned | Default chat workspaces the Hub creates |
| `logs/` | read-only evidence | Hub and global logs |

## Skill precedence

When two skills share a name, the first match wins:

1. Workspace skill the user owns — `<workspace>/.craft/skills/<name>/` with no `.builtin` marker
2. Skill from an enabled plugin
3. Built-in — `<workspace>/.craft/skills/<name>/` carrying a `.builtin` marker
4. User-global — `~/.craft/skills/<name>/`

Shadowing is silent. A workspace or plugin skill named `dotcraft-guide` replaces this one with no warning. Only the user-global pass skips names already taken; the earlier passes do not deduplicate, so the same name can appear more than once in a skill listing.

Built-in skills redeploy only when the product version changes. To pick up an edit to a built-in skill's source during local development, delete `<workspace>/.craft/skills/<name>/` and restart.

## Where writes should go

- Config and hooks: edit the file, then say "restart to apply", or send the user to the matching Desktop Settings panel for an immediate effect.
- Skills: `$skill-authoring` and `$skill-installer`. Never edit an installed skill's source directory by hand.
- Plugins: `$plugin-creator` to author, Desktop > Plugins to install and enable.
- Channels: Desktop Settings > Channels rather than the `<channel>.json` file.
