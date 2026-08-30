# DotCraft LSP Plugin Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-08-29 |
| **Related Specs** | [Plugin Architecture](plugin-architecture.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define the interim design for plugin-bundled Language Server Protocol (LSP) configuration. This file is the working spec for implementation discussion. After the feature is complete and verified, the accepted content should be merged into [Plugin Architecture](plugin-architecture.md) and any AppServer/Desktop protocol sections that become normative.

---

## 1. Problem Statement

DotCraft already has an LSP runtime and an `LSP` tool, but users currently need to hand-author workspace `LspServers` configuration. That is too much ceremony for common language setups: users must know the language server command, arguments, extension mapping, workspace folder semantics, and the correct JSON shape.

Plugins should be able to package reusable LSP server declarations so a user can install or enable a plugin and immediately get the matching language intelligence behavior for that workspace, subject to the existing `Tools.Lsp.Enabled` global tool switch.

The design should mirror plugin-bundled MCP as much as possible:

- plugin LSP declarations are runtime contributions, not workspace config writes;
- workspace configuration remains editable and higher priority;
- plugin-origin declarations are attributed and read-only in client views;
- bad plugin LSP declarations are diagnostic failures for that plugin, not AppServer startup failures.

---

## 2. Goals and Non-Goals

Goals:

1. Allow a DotCraft plugin to declare one or more LSP servers.
2. Reuse the existing `LspServerConfig` fields and LSP runtime wherever possible.
3. Keep plugin-origin LSP declarations out of `.craft/config.json`.
4. Provide origin metadata and plugin detail metadata so Desktop can explain where an LSP server came from.
5. Make plugin install, remove, and enablement lifecycle reconcile effective LSP runtime state.
6. Define behavior-level tests before implementation.

Non-goals for the first implementation:

1. No marketplace recommendation UX is required yet.
2. No automatic installation of language server binaries.
3. No new LSP transports beyond those supported by the existing runtime.
4. No per-thread LSP server overrides.
5. No plugin native tool declaration. LSP remains exposed through DotCraft's existing built-in `LSP` tool.

---

## 3. Contribution Model

The plugin contribution model gains a fourth supported contribution:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
3. **LSP Servers**: plugin-contained LSP server declarations loaded into DotCraft's LSP runtime.
4. **Interface Metadata**: optional client-facing presentation metadata.

Plugin manifests do not declare model-callable LSP tools. A plugin can only contribute LSP server configuration. The model-facing tool remains the built-in `LSP` tool, controlled by `Tools.Lsp.Enabled`.

LSP-only plugins are valid when they declare `lspServers` or contain a default `./.lsp.json` file.

---

## 4. Manifest Fields

Local plugins continue to use:

```text
<plugin-root>/.craft-plugin/plugin.json
```

The supported manifest schema version remains `1`.

The manifest metadata set gains:

- `lspServers`

`lspServers` is an optional manifest-relative path to a plugin-contained LSP configuration file. If omitted, DotCraft looks for `./.lsp.json` in the plugin root. The path must obey the same manifest path rules as `skills`, `mcpServers`, `paths`, and interface assets:

- start with `./`;
- not be an absolute path;
- not contain `..`;
- resolve to a path that stays inside the plugin root.

Example manifest:

```json
{
  "schemaVersion": 1,
  "id": "csharp-lsp",
  "version": "0.1.0",
  "displayName": "C# LSP",
  "description": "Adds C# language intelligence through csharp-ls.",
  "capabilities": ["lsp"],
  "lspServers": "./.lsp.json",
  "interface": {
    "displayName": "C# LSP",
    "shortDescription": "Go to definition, hover, symbols, and references for C#.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["LSP"],
    "brandColor": "#512BD4"
  }
}
```

Plugins must declare at least one supported contribution: `skills`, `mcpServers`, `lspServers`, interface metadata, or a default contribution file discovered by convention. If no supported contribution remains after validation, DotCraft emits `MissingPluginCapabilities` and the plugin is not loaded.

---

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

---

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

---

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

Plugin lifecycle changes that affect effective LSP runtime state must emit `workspace/configChanged` with `regions` including `"plugins"` and `"lsp"`. When the same operation also changes skill or MCP state, existing regions such as `"skills"` and `"mcp"` are preserved.

Examples:

| Operation | Config Changed Regions |
|-----------|------------------------|
| `plugin/install` for an LSP plugin | `["plugins", "skills", "mcp", "lsp"]` as applicable |
| `plugin/remove` for an LSP plugin | `["plugins", "skills", "mcp", "lsp"]` as applicable |
| `plugin/setEnabled` for an LSP plugin | `["plugins", "skills", "mcp", "lsp"]` as applicable |
| Workspace `LspServers` update | `["lsp"]` |
| `Tools.Lsp.Enabled` update | `["lsp"]` |

---

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

---

## 9. Loading and Diagnostics

Plugin loading gains LSP responsibilities:

1. Manifest parser resolves and validates the optional `lspServers` path or default `./.lsp.json`.
2. Discovery identifies installed and enabled plugins as today.
3. Plugin LSP loader reads plugin LSP files and converts declarations to `LspServerConfig`.
4. Effective LSP resolver merges workspace and plugin declarations.
5. LSP manager initializes from the effective runtime view.

Diagnostics are non-fatal and available to logs and UI surfaces. New diagnostic codes:

| Code | Severity | Description |
|------|----------|-------------|
| `InvalidPluginLspConfig` | error | Plugin LSP file could not be read or parsed. |
| `InvalidPluginLspServer` | warning | One server declaration is missing required fields or has invalid values. |
| `UnsupportedPluginLspTransport` | warning | Server transport is not supported by the current runtime. |
| `PluginLspServerShadowed` | info | Server runtime name is shadowed by workspace or higher-priority plugin configuration. |

Loading failures for one plugin must not prevent other plugins from loading. Invalid plugin LSP declarations must not prevent plugin-contained skills or MCP servers from loading.

---

## 10. AppServer and Client Surface

This draft does not require a standalone LSP management API equivalent to `mcp/*`.

Minimum AppServer impact:

- `PluginInfo` gains `lspServers: PluginLspServerInfo[]`.
- Server capabilities may add `lspServerOrigins: true` if an effective LSP list/status surface is introduced.
- `workspace/configChanged.regions` accepts `"lsp"`.
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

---

## 11. Security and Trust

Plugin-bundled LSP servers execute local processes. Installing and enabling an LSP plugin is therefore a trust decision equivalent to enabling a plugin-bundled stdio MCP server.

Security rules:

- Plugin LSP commands are not executed during plugin discovery.
- Plugin LSP commands are not executed during AppServer readiness.
- LSP servers start lazily when the LSP manager needs a server for a file, matching existing LSP behavior.
- Plugin-origin LSP declarations are read-only from workspace config APIs.
- Manifest-relative `lspServers` paths and plugin-relative LSP commands must not escape the plugin root.
- Plugin diagnostics must expose enough path and plugin identity context for a user to inspect the source before enabling it.

---

## 12. Compatibility and Migration

Existing workspace `LspServers` remain supported.

DotCraft should not migrate workspace `LspServers` into plugins automatically. Users may keep custom workspace config when they need local overrides. If both workspace and plugin LSP servers serve the same extension, current routing continues to select the first effective server for that extension. Workspace servers load first, so workspace config has priority.

The final merged version of this spec should update:

- [Plugin Architecture](plugin-architecture.md): contribution model, manifest fields, loading, lifecycle, security, examples.
- [AppServer Protocol](../protocols/appserver-protocol.md): plugin info DTOs and `workspace/configChanged` region list if wire changes are implemented.
- [Desktop Client](../clients/desktop-client.md): plugin detail and enablement UX if Desktop exposes LSP details.

---

## 13. Behavior Test Plan

Implementation should follow vertical TDD slices. Each test should use public interfaces and verify behavior, not private parsing details.

Recommended first slices:

1. Manifest parser accepts a plugin with `lspServers: "./.lsp.json"` as a supported contribution.
2. Manifest parser discovers default `./.lsp.json` when `lspServers` is omitted.
3. Manifest parser rejects escaping `lspServers` paths with `InvalidPluginManifestPath`.
4. Plugin LSP loader parses `{ "lspServers": { ... } }` and direct map forms.
5. Plugin LSP loader maps declared names to runtime names and origin metadata.
6. Effective resolver gives workspace `LspServers` priority and marks plugin LSP summaries shadowed.
7. Disabled plugins do not contribute active LSP servers.
8. `LspServerManager` initializes from effective workspace plus plugin LSP declarations when `Tools.Lsp.Enabled = true`.
9. Plugin lifecycle config-change notifications include `"lsp"` when plugin LSP state changes.
10. Plugin detail wire DTO includes declared plugin LSP server metadata.

Tests are not required for purely presentational copy, but are required for manifest parsing, runtime merge behavior, AppServer wire DTO changes, and lifecycle notifications.

---

## 14. Open Questions

1. Should DotCraft expose `lsp/list` and `lsp/status/list`, or keep LSP visible only through plugin details and general config schema?
2. Should `Tools.Lsp.Enabled` remain default `false` after an LSP plugin install, or should built-in curated LSP plugins be allowed to request an enable prompt during install?
3. Should plugin variables support string substitution in `workspaceFolder`, or stay limited to command, arguments, and environment variables?
4. Should Desktop recommend LSP plugins based on file extensions and installed binaries, or should recommendation wait until there is a curated LSP plugin catalog?
