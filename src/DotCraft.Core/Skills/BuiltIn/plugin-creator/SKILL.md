---
name: plugin-creator
description: "Create, update, verify, or share a DotCraft plugin that bundles skills, MCP or LSP servers, hooks, workflows, apps, settings, Desktop UI, or .NET extensions, including contributing one to the official registry. Not for a standalone skill or for installing an existing plugin."
---

# Plugin Creator

A plugin is finished when it would look complete in the plugin catalog and has been shown to work. The runtime loads far less than that, so `dotcraft plugin verify` and a real run decide completion.

- To change an existing plugin, go to [Update](#update).
- To share a plugin, read `references/contribute.md`.

A standalone skill is enough for reusable instructions in one workspace or for one person. Use a plugin to bundle several contributions, run executable behavior, or hand a capability to other people.

## Plan

Start from what the conversation already shows, including a workflow the user just performed. Ask only about gaps that would change the result, and do not ask the user to write manifest fields.

Before writing files, state one sentence of purpose and the plan: plugin id, location, and contributions. Wait for the user only when the purpose or the contributions are ambiguous. If the id already exists among discovered plugins, update that plugin unless the user asks for a separate one.

Choose the contributions with the least execution authority that meet the need. A Desktop or .NET extension runs as trusted code, so it needs a reason a skill or MCP server cannot satisfy.

| The user needs | Manifest field and file | Start from | Then |
|---|---|---|---|
| Instructions the agent follows | `"skills": "./skills/"`, `skills/<name>/SKILL.md` | `assets/template-skill.md` | Follow `$skill-creator` for its content, and add its interface metadata |
| Tools or data from a service | `"mcpServers": "./.mcp.json"` | `assets/template-mcp.json` | For a remote server, use `"transport": "http"` and `"url"`. See `https://www.dotcraft.net/configuration/plugins#mcp-servers` |
| Automation on agent events | `"hooks": "./hooks/hooks.json"` | `assets/template-hooks.json`, `assets/template-hook-script.js` | Follow `$create-hooks` |
| A scripted multi-agent procedure | `"workflows": "./workflows"` | | Follow `$workflow-authoring` |
| Language intelligence | `"lspServers": "./.lsp.json"` | `assets/template-lsp.json` | |
| An application connected to threads | `"apps": "./apps.json"` | | See `https://www.dotcraft.net/extend/app-binding` |
| User options | `"settings": "./settings.schema.json"` | `assets/template-settings.json` | |
| A view or action in Desktop | `"desktop"` | `assets/template-desktop-*` | Read `references/desktop-authoring.md` |
| In-process tools or Host contribution points | `"dotnet"` | `assets/template-dotnet-extension.txt` | Read `references/dotnet-authoring.md` |

## Write

Choose the location:

- This workspace, by default: `<workspace>/.craft/plugins/<id>`
- Every workspace of this user, when asked: `~/.craft/plugins/<id>`
- A .NET extension: `<workspace>/.craft/plugin-projects/<id>`

Do not write into marketplace snapshots or a plugin directory that holds a `.builtin` marker.

Copy `assets/template-plugin.json` to `.craft-plugin/plugin.json`, add the manifest field for each contribution, and copy the templates you need. Create only the directories the plugin uses. The id is lowercase hyphen-case, at most 64 characters, and equals the directory name.

Manifest fields are documented at `https://www.dotcraft.net/extend/plugin-manifest`. Replace every `[TODO: ...]` with real content. The `.builtin` file in this skill's directory holds the running DotCraft version, which is the value for host-pinned fields.

Reference plugin files with plugin-relative paths or `${PLUGIN_ROOT}`, never absolute paths. Use `${PLUGIN_DATA}` for writable state shared by the plugin's hooks, MCP servers, and LSP processes. Plugin stdio MCP declarations expand these variables in `command`, `arguments`, `environmentVariables` values, and `cwd`, not HTTP URLs or headers. Keep secrets out of plugin files: reference environment variables and name them in the setup instructions.

Read `references/presentation.md` before writing the name, descriptions, default prompt, or icons. Every plugin needs them, including a .NET-only plugin.

## Verify

```powershell
dotcraft plugin verify --candidate "<plugin-root>" --json
```

Fix every error. Read each warning and fix it unless you can state why it does not apply. If `dotcraft` is not on PATH, look in `~/.craft/bin`, the DotCraft installation directory, or Desktop Settings > Connections. If it still cannot run, report the plugin as unverified.

## Exercise

Passing verification does not show that a plugin works. Make it active and exercise each contribution.

| Contribution | Evidence |
|---|---|
| Skills | The skill loads with `SkillView`, and a realistic request follows its workflow |
| MCP servers | The server starts, its tools are listed, and one harmless call succeeds |
| Hooks | After the user grants trust, the event fires and its effect is observed |
| Workflows | The workflow is listed and a bounded run completes |
| LSP servers | The server starts for a matching file and returns one result |
| Apps | The app appears and its connection flow starts |
| Desktop extension | The build succeeds, and the view opens or the action runs in Desktop |
| .NET extension | `CSharp.Build` succeeds, and a contributed tool runs on a later turn |
| Settings | The field appears and its value reaches the plugin |

Some steps belong to the user: refreshing Desktop > Plugins, sending `/reload-skills`, granting trust, or starting a new conversation. Name the exact step and mark the result unverified until you have evidence. Do not report success from file creation or installation alone.

When you finish, report the location, the contributions, the verification result, what was and was not exercised, and how to try it. You may mention in one line that the plugin can be shared.

## Update

Inspect the plugin first, then change the source that owns it:

| Plugin | Where to change it |
|---|---|
| Workspace-local or user-owned | In place |
| .NET under development | The project's `src/`, then `CSharp.Build` |
| Published from another repository | That repository, through its release process |
| Holds a `.builtin` marker | Not in place, because a refresh replaces it. Change its source, or copy it without the marker to `<workspace>/.craft/plugins/<id>`, which takes precedence |

Keep the `id` once anyone else has the plugin, because a new id is a new plugin. Keep existing metadata and contributions the request does not change. Increase `version` when a changed plugin is distributed: patch for fixes, minor for added behavior, and major for incompatible changes.

Rerun verification and exercise the affected contributions. A question about a plugin does not by itself call for an edit.
