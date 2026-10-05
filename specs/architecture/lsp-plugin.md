# DotCraft LSP Plugin Specification

| Field | Value |
|-------|-------|
| **Version** | 0.7.8 |
| **Status** | Living |
| **Date** | 2026-09-28 |

This specification owns plugin-bundled Language Server Protocol (LSP) configuration, runtime merging,
and lifecycle. Bundle identity, discovery, and manifest paths follow [Plugin Architecture](plugin-architecture.md).

## 3. Contribution Model

Installed, enabled plugins contribute server configuration to the existing LSP runtime. They do not
create model-callable tools. `Tools.Lsp.Enabled` controls the built-in `LSP` tool and server startup.
LSP-only plugins are valid when they declare `lspServers` or contain `./.lsp.json`.

## 4. Manifest Fields

`lspServers` is an optional manifest-relative path to the server configuration. When omitted,
DotCraft discovers `./.lsp.json`. It follows the shared manifest path and contribution validation
rules in [Plugin Architecture](plugin-architecture.md#3-manifest-path-rules).

## 5. LSP Configuration File

A plugin LSP file may use either:

```json
{
  "lspServers": {
    "csharp": {
      "command": "csharp-ls",
      "arguments": [],
      "extensionToLanguage": {
        ".cs": "csharp"
      },
      "transport": "stdio"
    }
  }
}
```

or a direct server map:

```json
{
  "csharp": {
    "command": "csharp-ls",
    "arguments": [],
    "extensionToLanguage": {
      ".cs": "csharp"
    }
  }
}
```

Canonical field names match workspace `LspServers`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `enabled` | boolean | no | Whether this declaration contributes to the effective runtime. Default `true`. |
| `command` | string | yes | Command used to launch the language server. Plugin-origin LSP may use plugin-relative `./...` paths. |
| `arguments` | string[] | no | Command-line arguments. |
| `extensionToLanguage` | object | yes | Map from file extension to LSP language id. Must contain at least one entry. |
| `transport` | string | no | `stdio` by default. Unsupported transports are skipped with diagnostics. |
| `environmentVariables` | object | no | Environment variables for the language server process. |
| `initializationOptions` | JSON | no | Passed as `initialize.initializationOptions`. |
| `settings` | JSON | no | Reserved for workspace settings support. |
| `workspaceFolder` | string | no | Optional workspace folder override. |
| `startupTimeoutMs` | integer | no | Startup timeout in milliseconds. |
| `maxRestarts` | integer | no | Maximum restart attempts after server crashes. |

Server entries accept only these canonical fields. Unknown properties are rejected.

## 6. Runtime Names and Origin Metadata

Each plugin LSP server has two names:

- **Declared name**: the key inside the plugin LSP file, for example `csharp`.
- **Runtime name**: `{pluginId}:{declaredName}`, for example `csharp-lsp:csharp`.

Runtime names avoid collisions with workspace `LspServers` and other plugins. Plugin-origin LSP servers must carry origin metadata equivalent to MCP origin metadata:

| Field | Description |
|-------|-------------|
| `kind` | `plugin` for plugin-origin LSP, `workspace` for workspace config. |
| `pluginId` | Owning plugin id. |
| `pluginDisplayName` | Manifest interface display name, falling back to manifest display name. |
| `declaredName` | Name used inside the plugin LSP file. |

Workspace-origin servers remain editable. Plugin-origin servers are read-only runtime entries controlled by plugin lifecycle.

## 7. Effective LSP Merge Rules

Effective LSP runtime configuration is built in this order:

1. Workspace `LspServers` from `.craft/config.json`.
2. Enabled, installed plugin LSP declarations in plugin discovery order.

Rules:

- Workspace `LspServers` are loaded first and remain editable workspace configuration.
- Plugin LSP servers are added as read-only runtime entries with origin metadata.
- If a runtime name conflicts with a workspace server or higher-priority plugin server, the lower-priority plugin declaration is shadowed and not added to the effective runtime.
- Shadowed declarations remain visible in plugin detail metadata.
- Disabled plugin declarations are visible in plugin detail metadata but inactive.
- Plugin-origin LSP servers are never persisted by workspace config update paths.
- `Tools.Lsp.Enabled = false` disables the built-in model-facing `LSP` tool and prevents LSP server manager startup, even when plugin LSP declarations exist.

Plugin lifecycle changes that affect effective LSP runtime state must emit `config/changed` with `regions` including `"plugins"` and `"lsp"`. When the same operation also changes skill or MCP state, existing regions such as `"skills"` and `"mcp"` are preserved.

## 8. Path and Variable Resolution

Plugin LSP config uses two path scopes:

1. Manifest path fields, such as `lspServers`, are manifest-relative and must stay inside the plugin root.
2. Runtime fields, such as `workspaceFolder`, are process/runtime values and may point outside the plugin root when explicitly configured.

Resolution rules:

- Relative `workspaceFolder` values resolve against the workspace root, matching current workspace `LspServers` behavior.
- Relative paths inside `environmentVariables` are not automatically rewritten.
- Plugin-origin `command` values that start with `./` or `.\` resolve relative to the plugin root and must stay inside that root. On Windows, DotCraft may probe `.exe`, `.cmd`, and `.bat` suffixes for plugin-relative commands.
- DotCraft adds plugin variables before starting plugin-origin LSP servers:
  - `DOTCRAFT_PLUGIN_ROOT`: absolute plugin root path.
  - `DOTCRAFT_PLUGIN_DATA`: `<UserDataPath>/plugins/<id>/data` when `UserDataPath` is configured,
    otherwise `<DataPath>/plugin-data/<id>`. Hooks and .NET activation use the same directory.
- Plugin-origin LSP supports string substitution for `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` in `command`, `arguments`, and `environmentVariables`.

These substitutions are plugin LSP behavior only. Workspace `LspServers` continue to use the existing workspace configuration semantics and do not gain plugin-relative command resolution.

## 9. Loading and Diagnostics

The LSP runtime consumes the effective merged declarations. Invalid declarations are isolated to
the affected server or plugin.

Diagnostics are non-fatal and available to logs and UI surfaces. New diagnostic codes:

| Code | Severity | Description |
|------|----------|-------------|
| `InvalidPluginLspConfig` | error | Plugin LSP file could not be read or parsed. |
| `InvalidPluginLspServer` | warning | One server declaration is missing required fields or has invalid values. |
| `UnsupportedPluginLspTransport` | warning | Server transport is not supported by the current runtime. |
| `PluginLspServerShadowed` | info | Server runtime name is shadowed by workspace or higher-priority plugin configuration. |

Loading failures for one plugin must not prevent other plugins from loading. Invalid plugin LSP declarations must not prevent plugin-contained skills or MCP servers from loading.

## 10. AppServer and Client Surface

LSP status is exposed through plugin detail metadata; there is no standalone LSP management API.

Minimum AppServer impact:

- `PluginInfo` gains `lspServers: PluginLspServerInfo[]`.
- `config/changed.regions` accepts `"lsp"`.
- Plugin lifecycle methods include `"lsp"` in changed regions when effective LSP state may have changed.

`PluginLspServerInfo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Declared server name inside the plugin LSP file. |
| `runtimeName` | string | Effective runtime name, usually `{pluginId}:{name}`. |
| `transport` | string | Normalized transport after validation. |
| `enabled` | boolean | Whether the declaration itself is enabled. |
| `active` | boolean | True when the plugin is installed, enabled, server is enabled, not shadowed, and `Tools.Lsp.Enabled` permits LSP runtime use. |
| `extensions` | string[] | File extensions served by this declaration. |
| `shadowedBy` | `"workspace" | "plugin"` | Optional reason the declaration is inactive. |

Desktop should show plugin-bundled LSP in plugin detail pages alongside skills and MCP. If `Tools.Lsp.Enabled` is false and an installed enabled plugin contributes active LSP declarations except for the global switch, Desktop may offer a clear user action to enable LSP. It must not silently enable `Tools.Lsp.Enabled`.

## 11. Security and Trust

Plugin-bundled LSP servers execute local processes. Installing and enabling an LSP plugin is therefore a trust decision equivalent to enabling a plugin-bundled stdio MCP server.

Security rules:

- Plugin LSP commands are not executed during plugin discovery.
- Plugin LSP commands are not executed during AppServer readiness.
- LSP servers start lazily when the LSP manager needs a server for a file, matching existing LSP behavior.
- LSP tool cancellation reaches file access, server startup, and protocol requests. The owning runtime
  stops its language-server processes on disposal.
- Plugin-origin LSP declarations are read-only from workspace config APIs.
- Manifest-relative `lspServers` paths and plugin-relative LSP commands must not escape the plugin root.
- Plugin diagnostics must expose enough path and plugin identity context for a user to inspect the source before enabling it.

## 12. Compatibility and Migration

Existing workspace `LspServers` remain supported.

DotCraft should not migrate workspace `LspServers` into plugins automatically. Users may keep custom workspace config when they need local overrides. If both workspace and plugin LSP servers serve the same extension, current routing continues to select the first effective server for that extension. Workspace servers load first, so workspace config has priority.
