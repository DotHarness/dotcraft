# DotCraft Plugin JSON Samples

## Skill-only plugin

```json
{
  "schemaVersion": 1,
  "id": "my-plugin",
  "version": "0.1.0",
  "displayName": "My Plugin",
  "description": "Describe what this plugin contributes.",
  "capabilities": ["skill"],
  "skills": "./skills/",
  "interface": {
    "displayName": "My Plugin",
    "shortDescription": "One-line user-facing summary.",
    "longDescription": "A concise description for plugin detail views.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill"],
    "defaultPrompt": "Use my plugin.",
    "brandColor": "#2563EB"
  }
}
```

## MCP plugin

```json
{
  "schemaVersion": 1,
  "id": "review-tools",
  "version": "0.1.0",
  "displayName": "Review Tools",
  "description": "Review instructions and MCP tools.",
  "capabilities": ["skill", "mcp"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review workflows and MCP tools.",
    "longDescription": "A plugin that contributes review guidance and MCP server configuration.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill", "MCP"],
    "defaultPrompt": "Review this change.",
    "brandColor": "#2563EB"
  }
}
```

Matching `.mcp.json`:

```json
{
  "mcpServers": {
    "review": {
      "transport": "stdio",
      "command": "node",
      "arguments": ["./mcp-server/index.js"],
      "cwd": "./"
    }
  }
}
```

## Hooks plugin

```json
{
  "schemaVersion": 1,
  "id": "audit-hooks",
  "version": "0.1.0",
  "displayName": "Audit Hooks",
  "description": "Lifecycle hooks for workspace audit logging.",
  "capabilities": ["hooks"],
  "hooks": "./hooks/hooks.json",
  "interface": {
    "displayName": "Audit Hooks",
    "shortDescription": "Hook-based audit logging.",
    "longDescription": "A plugin that contributes lifecycle hook commands.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Hooks"],
    "defaultPrompt": "Use audit hooks.",
    "brandColor": "#2563EB"
  }
}
```

Matching `hooks/hooks.json`:

```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "",
        "hooks": [
          {
            "type": "command",
            "command": "node \"${DOTCRAFT_PLUGIN_ROOT}/hooks/session-start.js\"",
            "timeout": 30
          }
        ]
      }
    ]
  }
}
```

Plugin hook commands run from the workspace root. DotCraft expands `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` in commands and injects the same names as environment variables. Users must trust plugin hooks before they run.

## Interface-only plugin

```json
{
  "schemaVersion": 1,
  "id": "team-workflows",
  "version": "0.1.0",
  "displayName": "Team Workflows",
  "description": "Catalog metadata for team workflows.",
  "capabilities": ["metadata"],
  "interface": {
    "displayName": "Team Workflows",
    "shortDescription": "Team-specific workflow entry points.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Metadata"],
    "defaultPrompt": "Use the team workflow.",
    "brandColor": "#2563EB"
  }
}
```

## Desktop Plugin

```json
{
  "schemaVersion": 1,
  "id": "project-board",
  "version": "0.1.0",
  "displayName": "Project Board",
  "description": "Adds a Desktop board surface.",
  "capabilities": ["desktop"],
  "desktop": {
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  },
  "interface": {
    "displayName": "Project Board",
    "shortDescription": "Desktop board for project state.",
    "developerName": "DotCraft",
    "category": "Productivity",
    "capabilities": ["Desktop"],
    "defaultPrompt": "Open the project board.",
    "brandColor": "#2563EB"
  }
}
```

## Rules

- `schemaVersion` must be `1`.
- `id` must contain only ASCII letters, digits, `.`, `_`, `-`, or `:`.
- `displayName` is required.
- At least one supported contribution is required: `dotnet`, `skills`, `mcpServers` or default root `.mcp.json`, `hooks` or default root `hooks/hooks.json`, `desktop`, or `interface`.
- Plugin-bundled MCP servers use the same schema as workspace `McpServers`.
- If `mcpServers` is omitted, DotCraft looks for `.mcp.json` in the plugin root.
- Plugin hooks use the same schema as workspace `.craft/hooks.json`.
- If `hooks` is omitted, DotCraft looks for `hooks/hooks.json` in the plugin root.
- Manifest paths must start with `./`, must not contain `..`, and must stay inside the plugin root.
- `desktop.entry` must name an `.mjs` file inside `./desktop/dist/`; each `desktop.styles` entry must name a `.css` file in the same output tree.
- Desktop Plugin bundles are trusted local modules loaded after the plugin is installed and enabled.
- `tools`, `functions`, and `processes` are unsupported manifest fields. Managed plugins contribute native Tools from their C# implementation.
