# DotCraft LSP Plugin Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |

This specification owns plugin-bundled Language Server Protocol (LSP) configuration, runtime merging,
and lifecycle. Bundle identity, discovery, and manifest paths follow [Plugin Architecture](plugin-architecture.md).

## 1. Contribution Model

Installed, enabled plugins contribute server configuration to the LSP runtime. They do not create
model-callable tools. `Tools.Lsp.Enabled` controls the built-in `LSP` tool and server startup.
A plugin may contribute only LSP servers.

`lspServers` is an optional manifest-relative path to the server configuration. When omitted,
DotCraft discovers `./.lsp.json`. It follows the shared
[manifest path rules](plugin-architecture.md#3-manifest-path-rules).

## 2. LSP Configuration File

A plugin LSP file may use `{ "lspServers": { ... } }` or a direct server map:

```json
{
  "csharp": {
    "command": "csharp-ls",
    "arguments": [],
    "extensionToLanguage": { ".cs": "csharp" },
    "transport": "stdio"
  }
}
```

Server entries accept only the canonical workspace `LspServers` fields; unknown properties are
rejected:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `enabled` | boolean | no | Whether this declaration contributes to the effective runtime. Default `true`. |
| `command` | string | yes | Command that launches the server. Plugin-origin LSP may use plugin-relative `./...` paths. |
| `arguments` | string[] | no | Command-line arguments. |
| `extensionToLanguage` | object | yes | Non-empty map from file extension to LSP language id. |
| `transport` | string | no | `stdio` by default. Unsupported transports are skipped with diagnostics. |
| `environmentVariables` | object | no | Environment for the server process. |
| `initializationOptions` | JSON | no | Passed as `initialize.initializationOptions`. |
| `settings` | JSON | no | Reserved for workspace settings support. |
| `workspaceFolder` | string | no | Workspace folder override. |
| `startupTimeoutMs` | integer | no | Startup timeout in milliseconds. |
| `maxRestarts` | integer | no | Maximum restart attempts after crashes. |

## 3. Runtime Names and Origin Metadata

Each plugin LSP server has a **declared name** (its key in the plugin LSP file, for example
`csharp`) and a **runtime name** `{pluginId}:{declaredName}` (for example `csharp-lsp:csharp`) that
avoids collisions with workspace servers and other plugins.

Plugin-origin servers carry origin metadata equivalent to MCP origin metadata: `kind` (`plugin` or
`workspace`), `pluginId`, `pluginDisplayName` (interface display name, falling back to manifest
display name), and `declaredName`. Workspace-origin servers remain editable; plugin-origin servers
are read-only runtime entries controlled by plugin lifecycle.

## 4. Effective LSP Merge Rules

The effective configuration is workspace `LspServers` first, then enabled, installed plugin
declarations in plugin discovery order.

- Plugin servers are added as read-only runtime entries with origin metadata and are never persisted by workspace config update paths.
- A plugin runtime name that conflicts with a workspace server or a higher-priority plugin server is shadowed and not added. Shadowed and disabled declarations remain visible in plugin detail metadata but inactive.
- When several effective servers serve the same extension, routing selects the first, so workspace servers take priority.
- `Tools.Lsp.Enabled = false` disables the `LSP` tool and prevents server startup, even when plugin declarations exist.

Plugin lifecycle changes that affect effective LSP state emit `config/changed` with `regions`
including `"plugins"` and `"lsp"`, alongside any other affected regions such as `"skills"` or `"mcp"`.

## 5. Path and Variable Resolution

Manifest path fields such as `lspServers` are manifest-relative and stay inside the plugin root.
Runtime fields such as `workspaceFolder` may point outside it when explicitly configured.

- Relative `workspaceFolder` values resolve against the workspace root.
- Relative paths inside `environmentVariables` are not rewritten.
- Plugin-origin `command` values starting with `./` or `.\` resolve relative to the plugin root and must stay inside it. On Windows, DotCraft may probe `.exe`, `.cmd`, and `.bat` suffixes.
- Plugin-origin servers receive `DOTCRAFT_PLUGIN_ROOT` (absolute plugin root) and `DOTCRAFT_PLUGIN_DATA` (the plugin data directory defined in [Plugin Architecture](plugin-architecture.md)).
- `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` are substituted in `command`, `arguments`, and `environmentVariables`.

Workspace `LspServers` do not gain plugin-relative command resolution or these substitutions.

## 6. Loading and Diagnostics

Invalid declarations are isolated to the affected server or plugin and never prevent other plugins,
or the same plugin's skills and MCP servers, from loading. Diagnostics are non-fatal:

| Code | Severity | Description |
|------|----------|-------------|
| `InvalidPluginLspConfig` | error | Plugin LSP file could not be read or parsed. |
| `InvalidPluginLspServer` | warning | A server declaration is missing required fields or has invalid values. |
| `UnsupportedPluginLspTransport` | warning | Server transport is not supported. |
| `PluginLspServerShadowed` | info | Runtime name is shadowed by workspace or higher-priority plugin configuration. |

## 7. AppServer and Client Surface

LSP status is exposed only through plugin detail metadata; there is no standalone LSP management
API. `PluginInfo.lspServers` lists each declaration with its declared and runtime names, normalized
transport, served extensions, declaration `enabled` flag, optional `shadowedBy` (`workspace` or
`plugin`), and `active`, which is true only when the plugin is installed and enabled, the server is
enabled and not shadowed, and `Tools.Lsp.Enabled` permits LSP use.

When `Tools.Lsp.Enabled` is false and a plugin would otherwise contribute active declarations,
clients may offer an explicit action to enable LSP but never enable it silently.

## 8. Security and Trust

Plugin-bundled LSP servers execute local processes, so enabling an LSP plugin is a trust decision
equivalent to enabling a plugin-bundled stdio MCP server.

- Plugin LSP commands never run during plugin discovery or AppServer readiness; servers start lazily when a file needs one.
- LSP tool cancellation reaches file access, server startup, and protocol requests. The owning runtime stops its server processes on disposal.
- Manifest-relative `lspServers` paths and plugin-relative commands never escape the plugin root.
- Plugin diagnostics expose enough path and plugin identity for a user to inspect the source before enabling it.
