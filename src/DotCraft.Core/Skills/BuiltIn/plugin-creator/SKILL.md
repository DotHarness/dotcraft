---
name: plugin-creator
description: Create and scaffold DotCraft local plugin directories with `.craft-plugin/plugin.json`, plugin-contained skills, optional plugin-bundled MCP server config, and optional assets. Use when developing DotCraft plugins or creating a skill/MCP plugin bundle for `.craft/plugins` or `~/.craft/plugins`.
---

# Plugin Creator

Use this skill when the user wants to create, scaffold, or maintain a DotCraft plugin directory.

## Quick Start

Default to a workspace-local plugin under `<workspace>/.craft/plugins/<plugin-id>`:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin"
```

If reading the skill from the source tree instead of a deployed workspace skill, use the source-tree script path from the repo root:

```powershell
python src/DotCraft.Core/Skills/BuiltIn/plugin-creator/scripts/create_basic_plugin.py "My Plugin"
```

Use `--path` when the user asks for another parent directory, such as a user-global plugin container:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --path "$HOME/.craft/plugins"
```

## Defaults

- Normalize plugin ids to lowercase hyphen-case, max 64 characters.
- Create `<parent>/<plugin-id>/.craft-plugin/plugin.json`.
- Create a skill plugin by default with `skills: "./skills/"`.
- Create `skills/<skill-name>/SKILL.md`; `--skill-name` defaults to the plugin id.
- Add `--with-mcp` to create a plugin-bundled `.mcp.json` placeholder.
- Add `--with-assets` to create plugin-level icon/logo placeholders.

## Manifest Rules

DotCraft schema version `1` allows a plugin to contribute skills, MCP servers, interface metadata, or a combination of these.

- Skill-only plugins are valid when `skills` points to a plugin-contained skills directory.
- MCP-only plugins are valid when `mcpServers` points to a plugin-bundled MCP config or a root `.mcp.json` exists.
- Interface-only plugins are valid for catalog or UI metadata.
- Manifest fields `tools`, `functions`, and `processes` are unsupported legacy native tool fields and must not be generated for new plugins.
- Reusable executable capabilities should be exposed through MCP.
- Thread-scoped AppServer client callbacks should use Runtime Dynamic Tools, not plugin manifest fields.
- Manifest-relative paths must start with `./`, stay inside the plugin root, and never contain `..`.

For exact examples, read `references/plugin-json-spec.md`.

## MCP Plugin Template

Use this when creating a plugin that bundles MCP configuration:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py review-tools --with-mcp
```

After generation, replace placeholder descriptions and edit `.mcp.json` to point at the real MCP server command or HTTPS endpoint.

## Validation

After scaffolding:

1. Inspect `.craft-plugin/plugin.json` and replace TODO placeholders.
2. Confirm every manifest-relative path starts with `./`.
3. If the plugin has skills, confirm each child skill has `SKILL.md`.
4. If the plugin has MCP servers, confirm `.mcp.json` uses the same schema as workspace `McpServers`.
5. Run relevant DotCraft tests when changing the runtime, or start DotCraft and confirm `plugin/list` and `skills/list` show the plugin.
