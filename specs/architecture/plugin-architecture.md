# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.5 |
| **Status** | Living |
| **Date** | 2026-10-25 |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills and
workflows, local plugin manifests, plugin-bundled MCP servers, client-facing plugin metadata, and the
TypeScript external channel module contract.

## 1. Architecture Overview

DotCraft plugins are host-integrated capability bundles. They distribute skills, Dynamic Workflows,
MCP server declarations, App Binding descriptors, and optional client-facing metadata without
requiring the agent pipeline to know each integration's implementation details.

Contributions:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **Commands**: plugin-contained Markdown custom commands.
3. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
4. **Lifecycle Hooks**: plugin-contained hook declarations.
5. **LSP Servers**: plugin-contained language server descriptors.
6. **App Descriptors**: App Binding descriptors that make app connection and thread binding flows visible.
7. **Desktop extensions**: optional trusted Desktop modules that contribute views, actions, commands, and tool presentation.
8. **Dynamic Workflows**: plugin-contained JavaScript workflows registered under the plugin namespace.
9. **.NET extensions**: plugin-contained managed assemblies admitted by the host runtime.
10. **Interface Metadata**: optional client-facing plugin metadata.

Plugin manifests do not declare model-callable native tools. External reusable services use MCP.
Thread-scoped client callbacks use the Runtime Dynamic Tool contract in
[Tool Architecture](tools-architecture.md).

## 2. Local Plugin Manifest

The manifest lives at `<plugin-root>/.craft-plugin/plugin.json`. The supported `schemaVersion` is `1`.

Manifest fields: `schemaVersion`, `id`, `version`, `displayName`, `description`, `capabilities`,
`interface`, `skills`, `commands`, `mcpServers`, `hooks`, `lspServers`, `apps`, `desktop`,
`workflows`, `paths`, `dotnet`, `dependencies`, `settings`.

A plugin must declare at least one supported contribution (any of the contributions in §1). Each
contribution may appear without the others.

Example:

```json
{
  "schemaVersion": 1,
  "id": "review-tools",
  "version": "0.1.0",
  "displayName": "Review Tools",
  "description": "Adds review-oriented instructions and MCP tools.",
  "capabilities": ["skill", "mcp"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "hooks": "./hooks/hooks.json",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review workflows and MCP tools.",
    "category": "Coding",
    "defaultPrompt": "Review this change."
  }
}
```

### 2.1 Skills, commands, and workflows

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child
directory can contain a DotCraft-compatible `SKILL.md`. Skills contributed by enabled plugins appear
in `skills/list` with source `plugin` and `pluginId` / `pluginDisplayName` attribution. Disabling the
plugin removes its skills from agent context and hides built-in copies owned by that plugin.

Skill interface icons without parent traversal resolve only from the child skill's own `assets/`
directory. A plugin-contained skill may instead use a parent-relative path whose lexically
normalized target is inside the owning plugin's root `assets/` directory (for example
`../../assets/icon.svg` from `skills/<skill-id>/`). Any other target is ignored. Non-plugin skills
never receive this shared-assets policy.

`commands` is a manifest-relative directory of Markdown custom commands. Enabled plugins contribute
them as `<pluginId>:<relative-command-name>`. Workspace and user commands take precedence.

`workflows` is a manifest-relative workflow directory; when omitted, an existing root
`./workflows/` is used. Enabled plugins contribute its top-level `*.js` definitions as
`{pluginId}:{workflowName}`. Plugin workflows never shadow workspace or personal definitions.

Disabling or removing a plugin withdraws all of its contributions.

### 2.2 MCP servers

`mcpServers` is an optional manifest-relative path to an MCP configuration file; when omitted,
DotCraft looks for `./.mcp.json`. The file may use `{ "mcpServers": { ... } }` or a direct server
map, with the canonical DotCraft fields `arguments`, `environmentVariables`, and `headers`; unknown
server properties are rejected. Relative `cwd` values resolve under the plugin root.

Contributed servers run in the same runtime as workspace `McpServers` under the runtime name
`{pluginId}:{serverName}`. That name is for connection routing only; model-visible tool identity is
derived as defined in [Tool Architecture](tools-architecture.md) (see §5).

Effective MCP merge rules:

- Workspace `McpServers` load first and remain editable workspace configuration.
- Enabled plugin servers are added as read-only runtime entries with origin metadata (`kind=plugin`, `pluginId`, display name, declared server name).
- A plugin runtime name that conflicts with a workspace server or a higher-priority plugin server is marked shadowed and not connected.
- `mcp/list` returns the effective runtime view. Workspace config writes (`mcp/upsert`, `mcp/remove`) never persist plugin-origin servers; clients may edit or delete only workspace-origin entries.
- Plugin MCP startup is non-fatal. Failures are reported through MCP runtime status (`mcpServerStatus/list` / `mcpServer/startupStatus/updated`) and diagnostics and never block plugin discovery, AppServer readiness, or client connection. Agent tool materialization waits for the current startup attempt to settle, so ready plugin MCP tools reach new turns without making AppServer startup synchronous.

### 2.3 Hooks

`hooks` accepts a path, an array of paths, an inline `{ "hooks": { ... } }` object, or an array of
inline objects. When omitted, discovery checks `./hooks/hooks.json`, then `./hooks.json`. An
explicit declaration suppresses defaults. Only installed, enabled plugins contribute hooks;
discovery never executes them.

### 2.4 Apps, Desktop, and interface metadata

`apps` points to an App Binding descriptor document, for example `"./apps.json"`. Apps of installed,
enabled plugins are eligible for App Binding connection and thread binding. Catalog-visible
built-ins may expose app metadata before installation, but connection and binding are blocked until
the plugin is installed and enabled.

`desktop` declares one trusted Desktop module built with `@dotcraft/plugin`:

```json
{
  "desktop": {
    "description": "Adds review actions and result presentation to DotCraft Desktop.",
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

`description` is optional presentation metadata; clients may fall back to the plugin description.
The entry must be an ESM `.mjs` file and every style a `.css` file, all confined to `./desktop/dist/`.
The `entry` / `styles` declaration and output tree produce the revision projected through
`plugin/list` and `plugin/view`; `description` does not affect it. The Desktop host owns activation,
runtime trust, and contribution lifecycle; manifest discovery does not execute the module.

`interface` contains optional client metadata: display name, short and long descriptions,
developer, category, capability tags, default prompt, icon/logo paths, and public
website/privacy/terms links. Icon and logo assets own their complete visual treatment, including any
background needed for theme contrast. Tool-result renderers are not declared here; presentation
boundaries are defined by [Tool Architecture](tools-architecture.md#14-presentation-boundary).

### 2.5 Settings and plugin configuration

`settings` is an optional manifest-relative path to a settings schema, for example
`"./settings.schema.json"`. It does not count as a contribution. The schema has a single `fields`
array. Each field has a required `key` and `type`, plus optional `defaultValue`, `options`, `min`,
and `max`. Supported types are `text`, `textarea`, `number`, `bool`, `select`, `stringList`,
`keyValueMap`, and `json`. Keys are unique case-insensitively. Defaults must pass the same
validation as stored values. `select` requires a non-empty string `options` array and accepts only
listed values. `min` and `max` apply only to `number`.

Plugin configuration is stored outside the layered `config.json` documents:

- personal: `<UserDataPath>/plugin-config.json` (`~/.craft/plugin-config.json` for the official host);
- workspace: `<DataPath>/plugin-config.json` (`<workspace>/.craft/plugin-config.json`).

Each document's root maps canonical plugin ids to setting objects, with no version or wrapper. The
effective value is schema defaults, then personal, then workspace. Objects merge recursively; arrays
and scalars replace the lower layer.

Every stored namespace may contain only fields declared by the plugin's current schema, and every
value must validate. An invalid document or namespace produces a stable configuration error and a
plugin diagnostic without affecting other AppServer or plugin functions. DotCraft rereads the
documents on every configuration read, mutation, and .NET activation; it does not watch files, keep
a last-good value, retain unknown fields, or migrate schemas.

Mutations are atomic read-modify-writes under a cross-process file lock that change only the target
namespace and never overwrite an unparseable document. Concurrent changes to different namespaces
are retained; the same namespace is last-writer-wins. Unsetting a key removes that scope's override.
Disabling, removing, or reinstalling a plugin does not remove its configuration.

Plugin host-side data is stored at `<UserDataPath>/plugin-data/<pluginId>`, or at
`<DataPath>/plugin-data/<pluginId>` when no user-data root is configured. It lies outside every
plugin root, so writing it never changes a plugin's content fingerprint, and removing or
reinstalling the plugin keeps it. Hook, LSP, and .NET contributions share this directory. The renderer receives no data-directory path or general file API.

### .NET manifest

`dotnet` declares an in-process managed contribution:

```json
{
  "schemaVersion": 1,
  "id": "acme.review-core",
  "version": "1.2.0",
  "displayName": "Acme Review Core",
  "capabilities": ["dotnet"],
  "dotnet": {
    "minHostVersion": "0.5.0",
    "entryAssembly": "./lib/Acme.Review.Core.dll",
    "entryType": "Acme.Review.Core.ReviewPlugin",
    "exportedApiAssemblies": ["./lib/Acme.Review.Contracts.dll"]
  },
  "dependencies": { "acme.review-base": "1.0.0" }
}
```

When `dotnet` is present, `version` is mandatory and:

- `minHostVersion` (required) is the canonical `MAJOR.MINOR.PATCH` minimum host version. A lower host blocks the plugin before any of its code loads. The host version is that of `DotCraft.Core`, which carries the plugin API, so an embedding application is measured by the engine it embeds.
- `entryAssembly` (required) names one managed entry assembly.
- `entryType` (required) is the full CLR name of one public, concrete, non-generic type that implements `DotCraft.Plugins.IDotCraftPlugin` and has a public parameterless constructor.
- `exportedApiAssemblies` (optional, default empty) names separate managed contract assemblies whose public API declared dependents may consume. The entry assembly cannot be exported.

`dependencies` is valid only with `dotnet` and defaults to an empty map. Each key is a canonical
plugin id and each value is the minimum provider version within one compatibility line: stable
versions share the required major version; `0.x` versions also share its minor version.
Self-dependencies, duplicate ids after canonicalization, and range syntax are invalid. The map
declares .NET generation lifecycle edges, not private library or NuGet dependencies. A consumer may
import a CLR service only from a plugin named directly in this map.

The bundle must already contain the entry assembly, its adjacent `.deps.json`, all private managed
dependencies, and required native assets. Discovery, installation, and activation never restore
packages, run MSBuild, execute install scripts, or compile source.

An installed `dotnet` plugin runs with the host process's full authority and requires an explicit,
fingerprint-bound trust confirmation before any of its code loads. An authoring build may qualify
its exact development fingerprint for the current process without modifying durable trust.

### 2.6 Plugin roots

DotCraft discovers plugin roots in priority order:

1. Workspace-local root `<workspace>/.craft/plugins`, for plugins that come with the workspace. DotCraft never installs into it.
2. Explicit roots in `Plugins.PluginRoots` order.
3. User-global root `<craft-home>/plugins`, where every install lands.
4. Host-bundled built-in catalog roots from `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`.
5. Configured plugin registry snapshots.

Explicit roots may be one plugin root or a container of plugin roots. Missing roots are skipped
with diagnostics. When multiple roots contain the same plugin id, the higher-priority root wins and
lower-priority duplicates are skipped with diagnostics; a workspace, explicit, or user-global plugin
suppresses the bundled catalog entry with the same id.

## 3. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, `commands`, `mcpServers`, `hooks`, `settings`, `workflows`, `paths`,
interface asset paths, and other manifest-relative fields. Desktop entry and style paths also remain
inside `./desktop/dist/`.

## 4. Loading and Diagnostics

Loading parses and validates each manifest, scans roots, resolves duplicate ids, applies enablement,
and lets enabled plugins contribute to the workspace runtime and client metadata.

Diagnostics are non-fatal and available to logs and UI surfaces. They cover invalid JSON, missing
fields, missing supported capabilities, invalid ids, invalid manifest-relative paths, unsupported
native tool fields, duplicate plugin ids, disabled plugins, invalid MCP or hook declarations, and
missing roots.

A manifest that declares `tools`, `functions`, or `processes` produces
`UnsupportedPluginNativeTools` and those fields are ignored. If no supported contribution remains,
DotCraft also emits `MissingPluginCapabilities` and does not load the plugin.

A discovery or loading failure for one plugin never prevents other plugins from loading.

## 5. Tool contributions

Tool registrations obey [Tool Architecture](tools-architecture.md). Plugin provenance identifies the
owning bundle and is independent from model-visible identity. For MCP, the runtime connection name
retains `{pluginId}:` for routing; clients and provider adapters never derive a model namespace by
splitting it. Equal declared names are disambiguated through the shared identity normalization
contract.

## 6. Built-In and Registry Plugin Lifecycle

Built-in plugins are host-bundled filesystem plugins exposed through a built-in catalog. Each
official host (Desktop, the Docker image) launches AppServer with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`
pointing at its bundled container. That variable is a platform path-list of absolute container
directories or plugin roots; missing or invalid entries produce non-fatal diagnostics. When it is
absent or empty, no uninstalled built-ins are listed, but installed plugins remain discoverable.

Official hosts provide the default plugin registry through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`.
Docker persists Craft home separately from the workspace so marketplace configuration and registry
snapshots survive container replacement. Registry catalog entries are source paths inside a
registry snapshot.

Catalog entries are visible before installation but inactive until `plugin/install` copies them
into `<craft-home>/plugins/<pluginId>`; registry availability never installs anything. Install
validates the marketplace entry and the target manifest id. DotCraft never executes code directly
from a registry URL.

Installed built-in and registry plugins carry a `.builtin` marker holding a fingerprint of the
source directory. Marked directories are DotCraft-owned and may be refreshed or removed by DotCraft
lifecycle operations; unmarked directories are user-owned and never overwritten or removed
automatically.

`plugin/remove` removes an installed plugin directory from the user-global root (removing it from
every workspace) or from the workspace-local root, including user-owned plugins. It first renames
the directory into `<DataPath>/tmp` on the same volume, then cleans up best-effort; a failure before
the rename leaves the plugin intact. Removed built-in and registry plugins remain visible in the
installable catalog while their source is configured. Disabling is distinct: a disabled plugin
stays on disk and can be re-enabled.

## 7. TypeScript External Channel Modules

A TypeScript external channel module is the SDK-facing unit for one external channel integration
variant, such as a first-party or enterprise Feishu module.

Hosts integrate modules through a stable module contract rather than package-internal layout. A
module owns platform protocol integration, platform-specific configuration, lifecycle behavior,
runtime tool registration, and tool execution. The host owns discovery, workspace context,
configuration storage, launcher lifecycle, and user-visible enablement.

The module contract defines:

- **Module identity**: stable `moduleId`, channel family, display metadata, optional interface metadata, variant semantics, and capability summary.
- **Manifest carrier**: a module-root SDK export with host-readable metadata.
- **Entry contract**: a startup entry that receives workspace context and returns a structured startup outcome.
- **Workspace context**: workspace path, `.craft` path, config path, state path, temp path, and AppServer connection information.
- **Configuration contract**: workspace-scoped configuration under `.craft/<configFileName>`, with module-owned validation and host-visible descriptors.
- **State and temp layout**: module-owned persistent state and temporary files scoped to the workspace.
- **Lifecycle contract**: structured statuses, errors, diagnostics, interactive setup needs, and restart requirements.
- **Capability and tool registration**: manifest capability summaries plus runtime channel tool descriptors declared during AppServer `initialize`.

Listing modules never executes module business logic. Bundled and user-installed modules may
coexist; user-installed content wins for the same `moduleId`.

The optional manifest `interface` object is display-only and does not affect startup. Recognized
fields are `shortDescription`, `longDescription`, and `previewPrompt`, each with a `localized*`
variant keyed by `en` and `zh-Hans`.

## 8. Configuration

The `Plugins` config section contains:

- `PluginRoots`: additional plugin roots or container directories. Relative paths resolve against the workspace root.
- `DisabledPlugins`: plugin ids turned off in the workspace. DotCraft writes it only to the workspace layer; turning a plugin back on removes its id.
- `PluginRegistries`: additional marketplace sources. Each declares its kind, source value, optional reference and sparse paths, and may override the marketplace path.
- `DisableDefaultPluginRegistry`: disables the host-provided default marketplace.

Marketplace sources are recorded in user-global configuration. Installation is per user:
`plugin/install` and `plugin/installLocal` copy into `<craft-home>/plugins/<pluginId>`, so the plugin
is available in every workspace that user opens on that machine (the remote machine, for a remote
workspace), and each workspace can turn it off.

Installed plugins are enabled by default unless the workspace disables them. Catalog-only built-ins
are installable but contribute nothing to agent context.

Installing, removing, enabling, or disabling a plugin publishes the revision of the layer it wrote,
so every other AppServer of the same user rediscovers plugins and refreshes contributions without a
restart, including stopping or adopting a .NET extension whose bundle was removed, added, or changed.

## 9. Consumer boundaries

Protocol and client projections expose discovery and lifecycle state without creating another
plugin manager. A remote workspace remains the installation owner; a client-side presentation cache
is not a local plugin installation and does not activate unrelated contributions locally.
