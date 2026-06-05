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
      "args": ["./mcp-server/index.js"],
      "cwd": "./"
    }
  }
}
```

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

## Desktop extension plugin

```json
{
  "schemaVersion": 1,
  "id": "project-board",
  "version": "0.1.0",
  "displayName": "Project Board",
  "description": "Adds a Desktop board surface.",
  "capabilities": ["desktopExtension"],
  "desktopExtensions": "./desktop-extensions.json",
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

Matching `desktop-extensions.json`:

```json
{
  "extensions": [
    {
      "id": "desktop",
      "displayName": "Project Board Desktop",
      "description": "Adds a trusted Desktop main view.",
      "entry": "./desktop/main-view.mjs",
      "styles": [],
      "surfaces": [
        {
          "type": "mainView",
          "viewId": "main",
          "label": "Project Board",
          "placement": "sidebar",
          "order": 80
        },
        {
          "type": "pluginDetail",
          "title": "Project Board Desktop",
          "description": "Adds a Desktop surface for this plugin."
        }
      ],
      "requiredAppIds": [],
      "connectOrigins": []
    }
  ]
}
```

## Rules

- `schemaVersion` must be `1`.
- `id` must contain only ASCII letters, digits, `.`, `_`, `-`, or `:`.
- `displayName` is required.
- At least one supported contribution is required: `skills`, `mcpServers` or default root `.mcp.json`, `desktopExtensions`, or `interface`.
- Plugin-bundled MCP servers use the same schema as workspace `McpServers`.
- If `mcpServers` is omitted, DotCraft looks for `.mcp.json` in the plugin root.
- Manifest paths must start with `./`, must not contain `..`, and must stay inside the plugin root. This includes `desktopExtensions`, Desktop extension `entry`, and Desktop extension `styles`.
- Desktop extension bundles are trusted local ESM loaded by Desktop after the plugin is installed and enabled.
- `tools`, `functions`, and `processes` are unsupported legacy native tool fields; new plugins must not use them.
