---
name: dotcraft-guide
description: Answer questions about DotCraft itself, where "you", "this app", or "this agent" means DotCraft, and change this installation's settings: providers and models, .craft/config.json, channels, skills, plugins, MCP servers, hooks, automations, permissions, the dotcraft CLI, and the official docs on dotcraft.net.
---

# DotCraft Guide

DotCraft is the harness you are running inside. When the user says "you", "this app", "this agent", or "the harness" and means the product rather than the model or the project checked out in this workspace, they mean DotCraft. Your running version and workspace paths are in the identity section of your system prompt. When behavior observed in this session contradicts a document, trust the session and say which one you followed.

## Source order

Stop at the first source that answers the question.

1. `Exec("dotcraft config schema --json")` for any field name, type, default, sensitive flag, or reload tier. `Exec("dotcraft config show --json")` for the current merged config, with sensitive values masked. Compiled truth outranks every document.
2. If the workspace contains `docs/.vitepress/config.mts`, it is the dotcraft repository itself. Use `ReadFile` and `GrepFiles` under `docs/` and skip the network.
3. `WebFetch("https://www.dotcraft.net/llms.txt", extractMode: "markdown")` to choose a page, then fetch that one page. Chinese is `/zh/` plus the same path.
4. `https://github.com/DotHarness/dotcraft`, branch `main`, for what the site does not cover.

Cite only `www.dotcraft.net` and `github.com/DotHarness/dotcraft`. Never invent a config key, CLI flag, field, or URL; say you could not find it. If `Exec` or the web tools are unavailable, answer from the local `.craft/` tree and this skill's references, and say the documentation site was unreachable. A tool you lack is not a request to install anything.

## Which layer does the change belong to

| The user wants | It belongs in |
|---|---|
| Something done once, right now | Just do it. Nothing to persist |
| A convention for everyone working in this repository | `AGENTS.md` |
| A fact about the user or project to recall later | `.craft/memory/MEMORY.md` — `$memory` |
| A setting for this project | `<workspace>/.craft/config.json` |
| A personal default, credentials, or endpoints | `~/.craft/config.json` |
| A skill or tool turned off here | `Skills` or `EnabledTools` in the workspace config, or Desktop Settings > Skills to take effect at once |
| A procedure worth repeating | A skill — `$skill-authoring` to write one, `$skill-installer` to install one |
| A bundle of skills, tools, hooks, MCP servers, or UI | A plugin — `$plugin-creator` |
| External data or actions from another program | `McpServers` |
| "From now on, whenever X happens, do Y", mechanically | A hook — `$create-hooks`. Not memory, and not prose in a file |
| Something on a schedule | `$cron`, or an Automation |
| Parallel orchestration across many items | `$workflow-authoring` |
| A chat bot on QQ, WeCom, Feishu, Telegram, or Weixin | Desktop Settings > Channels, and `/features/channels/` for setup |

Split a mixed request and place each part separately.

## Editing configuration

1. Start with `dotcraft config show --json` to see the merged state. Do not `ReadFile` a config file merely to look; that pulls plaintext keys into the transcript. Workspace overrides personal, objects merge recursively, arrays and scalars replace wholesale, `ProviderPreferences` replaces per provider id, and keys match case-insensitively.
2. Open only the file you are changing, edit only the keys the user asked about, and keep the JSON valid. Credentials and endpoints belong to the personal file's `Providers`; the project's model selection belongs to the workspace file. An unknown property under `McpServers` or `LspServers` fails the entire config load.
3. Never write a literal secret. Write `"ApiKey": "${OPENAI_API_KEY}"` and tell the user which variable to set. `$VAR` and `${VAR}` expand when the config loads; an unset variable keeps the placeholder unchanged.
4. **A file edit is not live.** The running host does not watch these files, and `AppConfig` is a snapshot taken at startup, so the change applies at the next AppServer restart and the Desktop settings pages will not show it before then. Say "restart to apply" every time. If the user wants it live now, point them at the matching Desktop Settings panel instead. The `hot` reload tier in the schema describes changes made through Desktop Settings or AppServer RPC, never a hand-edited file.
5. For provider sign-in, prefer the product surface: Desktop Settings > Models, or `dotcraft auth openai login`. Do not hand-write OAuth credentials.

## References

Read the one that matches the task. They sit next to this file, in the directory the skills catalog gives as this skill's `<location>` — normally `.craft/skills/dotcraft-guide/`.

- `references/config-map.md` — the two config files, merge rules, the section index, provider protocols, and a worked provider switch.
- `references/surfaces.md` — every path under `.craft/` and `~/.craft/`, what is safe to edit, and skill precedence.
- `references/docs-map.md` — how dotcraft.net is addressed, and which page answers which question.
- `references/cli.md` — the `dotcraft` command surface and where to find the binary.
- `references/capabilities.md` — answering "what can you do" from this session before reaching for the product catalog.

## Hand off

- A failure that needs logs, rollouts, or `state.db` — `dotcraft-doctor`, in the bundled `dotcraft` plugin. If it is not installed, say so and point to Desktop > Plugins rather than guessing at a cause. An error already visible in this conversation you can simply answer.
- Writing or installing a skill — `$skill-authoring`, `$skill-installer`. Plugins — `$plugin-creator`. Hooks — `$create-hooks`. Schedules — `$cron`. Workflows — `$workflow-authoring`. Charts and interactive views — `$visualize`.
- How memory retrieval and consolidation work — `$memory`. The `Memory` and `Dreams` settings stay here.
- Building an application on the SDKs, in-process Harness, or AppServer protocol — `$dotcraft-api`.
- Editing DotCraft's own source or documentation — `dotcraft-dev-guide`, `dotcraft-docs-guide`.
