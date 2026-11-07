# Plugin manifest

This page targets plugin authors. It defines `.craft-plugin/plugin.json` and `dotcraft plugin verify`, the command that checks a plugin before you use or share it.

To have the agent write a plugin for you, start with `$plugin-creator`. It uses these rules and runs the command.

## Example

```json
{
  "schemaVersion": 1,
  "id": "review-tools",
  "version": "0.1.0",
  "displayName": "Review Tools",
  "description": "Reviews pull requests against your team's written rules.",
  "capabilities": ["skill", "mcp"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review pull requests against team rules",
    "developerName": "Acme",
    "category": "Coding",
    "capabilities": ["Skill", "MCP"],
    "defaultPrompt": "Review this branch against our review rules.",
    "logo": "./assets/logo.svg",
    "composerIcon": "./assets/icon.svg"
  }
}
```

## Manifest fields

`schemaVersion`, `id`, and `displayName` are required to load. A plugin must also declare at least one contribution or `interface`.

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | number | Must be `1` |
| `id` | string | Plugin identity. ASCII letters, digits, `.`, `_`, `-`, or `:` |
| `version` | string | Required in `MAJOR.MINOR.PATCH` form for a plugin with Desktop or .NET |
| `displayName` | string | Name shown when `interface.displayName` is absent |
| `description` | string | What the plugin does |
| `capabilities` | string[] | Contribution kinds the plugin ships, such as `skill`, `mcp`, or `desktop` |
| `skills` | path | Directory with one subdirectory per skill |
| `mcpServers` | path | MCP server config. Defaults to a root `.mcp.json` |
| `lspServers` | path | Language server config. Defaults to a root `.lsp.json` |
| `hooks` | path, array, or object | Lifecycle hooks. Defaults to a root `hooks/hooks.json` |
| `apps` | path | [App Binding](./app-binding) descriptors |
| `workflows` | path | Dynamic Workflow directory. Defaults to a root `workflows/` |
| `settings` | path | Settings schema |
| `desktop` | object | [Desktop extension](./desktop-extensions) module |
| `dotnet` | object | [.NET extension](./dotnet-extensions) metadata |
| `dependencies` | object | Direct plugin dependencies, valid only with `dotnet` |
| `interface` | object | Catalog metadata, below |

Every path starts with `./`, contains no `..`, and stays inside the plugin root. `tools`, `functions`, and `processes` are not supported. Use MCP for reusable external tools.

## Interface fields

DotCraft loads a plugin without most of these fields, but `dotcraft plugin verify` requires them so the plugin looks finished in the catalog.

| Field | Verified rule |
|---|---|
| `displayName` | Required, at most 30 characters. The top-level `displayName` has the same limit |
| `shortDescription` | Required, one line, at most 48 characters |
| `developerName` | Required. The real author or team |
| `category` | Required. One of `Coding`, `Design`, `Engineering`, `Security`, `Lifestyle`, `Productivity`, or `Research` |
| `capabilities` | Required. Display tags |
| `defaultPrompt` | Required, one line, at most 128 characters |
| `logo` | Required. See below |
| `composerIcon` | Required. May name the same file as `logo` |
| `longDescription` | Optional, at most 300 characters |
| `websiteUrl`, `privacyPolicyUrl`, `termsOfServiceUrl` | Optional links |

The top-level `description` is also required by verification: one line, at most 160 characters.

Write copy that states what the plugin does, in one plain statement. Leave out hypotheticals, advice, setup instructions, and implementation detail.

Icons are square SVG, PNG, JPEG, or WebP files inside the plugin, at most 512 KiB each. Raster icons are at least 256 × 256 for `logo` and 48 × 48 for `composerIcon`. SVG icons contain no scripts, event handlers, or external references. Include a background in the asset when it needs one for contrast.

## Verify a plugin

```bash
dotcraft plugin verify --candidate <plugin-root> [--for-publish] [--json]
```

The command writes nothing, uses no network, and does not load plugin code. For a .NET project, the candidate is its `plugin/` directory after `DotNetPlugin.Build`.

It checks that the manifest and every declared file, descriptor, Desktop output, and plugin skill follow runtime rules, that no `[TODO: ...]` placeholder remains, and that the fields and icons above are valid.

`--for-publish` adds the checks for public distribution:

- `version` is strict SemVer, and `id` is hyphen-case with at most 64 characters.
- The plugin has no `.git`, `node_modules`, `.builtin`, build intermediates, or symbolic links.
- Descriptors hold no literal credentials or absolute machine paths, and text files hold no recognizable tokens.

The exit status is 0 when there are no errors. Warnings, such as a `defaultPrompt` that only restates the plugin name, do not change it. `--json` writes:

```json
{
  "isValid": false,
  "candidatePath": "/path/to/plugin",
  "pluginId": "review-tools",
  "version": "0.1.0",
  "forPublish": false,
  "diagnostics": [
    {
      "severity": "error",
      "code": "PluginIconMissing",
      "message": "interface.logo is not declared.",
      "path": ".craft-plugin/plugin.json"
    }
  ]
}
```

`code` is stable, and `message` is English text for people to read.

## Related docs

- [Plugin Market](./plugin-market): publish a plugin through your own marketplace or the official one.
- [Build a Desktop extension](./desktop-extensions): add a Desktop surface.
- [Build a .NET extension](./dotnet-extensions): add in-process behavior.
