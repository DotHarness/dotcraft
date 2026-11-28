# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 0.15.1 |
| **Status** | Living |
| **Date** | 2026-11-28 |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills and
workflows, local plugin manifests, plugin-bundled MCP servers, client-facing plugin metadata, and the
TypeScript channel module contract.

## 1. Architecture Overview

DotCraft plugins are host-integrated capability bundles. They distribute skills, Dynamic Workflows,
MCP server declarations and optional client-facing metadata without
requiring the agent pipeline to know each integration's implementation details.

Contributions:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
3. **Lifecycle Hooks**: plugin-contained hook declarations.
4. **LSP Servers**: plugin-contained language server descriptors.
5. **Desktop extensions**: optional trusted Desktop modules that contribute views, actions, commands, and tool presentation.
6. **Dynamic Workflows**: plugin-contained JavaScript workflows registered under the plugin namespace.
7. **.NET extensions**: plugin-contained managed assemblies admitted by the host runtime.
8. **Channels**: plugin-contained messaging adapters run as workspace-owned child processes.
9. **Interface Metadata**: optional client-facing plugin metadata.

Plugin manifests do not declare model-callable native tools. External reusable services use MCP.
Thread-scoped client callbacks use the Runtime Dynamic Tool contract in
[Tool Architecture](tools-architecture.md).

## 2. Local Plugin Manifest

The manifest lives at `<plugin-root>/.craft-plugin/plugin.json`. The supported `schemaVersion` is `1`.

Manifest fields: `schemaVersion`, `id`, `version`, `displayName`, `description`, `capabilities`,
`interface`, `skills`, `mcpServers`, `hooks`, `lspServers`, `apps`, `desktop`,
`workflows`, `paths`, `dotnet`, `dependencies`, `settings`, `channel`.

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

### 2.1 Skills and workflows

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child
directory can contain a DotCraft-compatible `SKILL.md`. Skills contributed by enabled plugins appear
in `skills/list` with source `plugin` and `pluginId` / `pluginDisplayName` attribution. Disabling the
plugin removes its skills from agent context and hides built-in copies owned by that plugin.

Skill interface icons without parent traversal resolve only from the child skill's own `assets/`
directory. A plugin-contained skill may instead use a parent-relative path whose lexically
normalized target is inside the owning plugin's root `assets/` directory (for example
`../../assets/icon.svg` from `skills/<skill-id>/`). Any other target is ignored. Non-plugin skills
never receive this shared-assets policy.

`workflows` is a manifest-relative workflow directory; when omitted, an existing root
`./workflows/` is used. Enabled plugins contribute its top-level `*.js` definitions as
`{pluginId}:{workflowName}`. Plugin workflows never shadow workspace or personal definitions.

Disabling or removing a plugin withdraws all of its contributions.

### 2.2 MCP servers

`mcpServers` is an optional manifest-relative path to an MCP configuration file; when omitted,
DotCraft looks for `./.mcp.json`. The file may use `{ "mcpServers": { ... } }` or a direct server
map, with the canonical DotCraft fields `arguments`, `environmentVariables`, and `headers`; unknown
server properties are rejected. Relative `cwd` values resolve under the plugin root. A declaration
may set `toolApprovalMode`, per-tool modes in `tools`, and the `enabledTools` / `disabledTools`
filters as defined in [Tool Architecture](tools-architecture.md) (see §12.3); enabling the plugin
accepts the declared modes, since the plugin already supplies the server it launches.

Plugin stdio MCP declarations expand `${PLUGIN_ROOT}` (absolute plugin root),
`${PLUGIN_DATA}` (the shared plugin data directory in §2.5), and `${DOTCRAFT_BIN}` (the active DotCraft host executable or entry assembly) in `command`, `arguments`,
`environmentVariables` values, and `cwd`. Expansion is case-sensitive and single-pass: inserted
paths are not expanded again, and unknown placeholders remain literal. Arguments and environment
values otherwise remain opaque, without path normalization or shell escaping. Only path fields
such as `cwd` are normalized after expansion. HTTP URLs, headers, environment variable names,
and workspace MCP declarations are not expanded.

Plugin stdio processes receive `PLUGIN_ROOT` and `PLUGIN_DATA` as environment variables. These
host-owned values take precedence over declared values and host environment forwarding, including
case variants on Windows. Omitting `cwd` retains the host's
normal MCP working-directory behavior.

Contributed servers run in the same runtime as workspace `McpServers` under the runtime name
`{pluginId}:{serverName}`. That name is for connection routing; model-visible tool identity is
derived as defined in [Tool Architecture](tools-architecture.md) (see §5), and MCP server policies
may select the server by its declared name ([Agent Profiles](../features/agent-profiles.md) §6).

Effective MCP merge rules:

- Workspace `McpServers` load first and remain editable workspace configuration.
- Enabled plugin servers are added as read-only runtime entries with origin metadata (`kind=plugin`, `pluginId`, display name, declared server name).
- A plugin runtime name that conflicts with a workspace server or a higher-priority plugin server is marked shadowed and not connected.
- `mcp/list` returns the effective runtime view. Workspace config writes (`mcp/upsert`, `mcp/remove`) never persist plugin-origin servers; clients may edit or delete only workspace-origin entries.
- A plugin server's own settings use the MCP settings extension ([Tool Architecture §12.4](tools-architecture.md#124-settings-extension)). The server stores them; plugin configuration (§2.5) is not passed to MCP servers.
- Plugin MCP startup is non-fatal. Failures are reported through MCP runtime status (`mcpServerStatus/list` / `mcpServer/startupStatus/updated`) and diagnostics and never block plugin discovery, AppServer readiness, or client connection. Agent tool materialization waits for the current startup attempt to settle, so ready plugin MCP tools reach new turns without making AppServer startup synchronous.

### 2.3 Hooks

`hooks` accepts a path, an array of paths, an inline `{ "hooks": { ... } }` object, or an array of
inline objects. When omitted, discovery checks `./hooks/hooks.json`, then `./hooks.json`. An
explicit declaration suppresses defaults. Only installed, enabled plugins contribute hooks;
discovery never executes them.

### 2.4 Desktop and interface metadata

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
array. Each field has a required `key` and `type`, plus optional `title`, `description`,
`defaultValue`, `options`, `min`, and `max`. `title` and `description` are display text for host
forms; a field without a `title` is labeled by its key. Supported types are `text`, `textarea`, `number`, `bool`, `select`, `stringList`,
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
with diagnostics. Discovery compares directory paths case-insensitively on Windows and ordinally on
other platforms. Roots 1 to 3 hold installed plugins. Plugin IDs are case-insensitive. When several
roots contain the same plugin ID, the higher-priority root wins and duplicates are skipped with
diagnostics. The plugin ID is the runtime identity for skills, workflows, MCP and LSP runtime names,
hooks, settings, trust, and plugin data. At most one plugin per ID is installed and contributes.

### 2.7 Plugin keys

Roots 4 and 5 are catalogs of installable entries. Each catalog entry has a plugin key
`<id>@<marketplace>`, where `<marketplace>` is the marketplace document name for a marketplace entry
and the reserved name `dotcraft-bundled` for a bundled entry, which no marketplace may use. An
installed plugin that DotCraft does not manage has the plugin id as its key. The key is the catalog,
install, and update identity, and it is never a runtime or model-visible name.

- Entries with different keys coexist, so every marketplace that lists a plugin id shows its own
  entry for it.
- Within one key, the first entry in source order wins and later duplicates are skipped without a
  diagnostic, because a source configured twice or a repeated marketplace entry gives the user
  nothing to act on.
- A managed install records the key it was installed from. It hides the catalog entry with that key
  and receives updates only from it.
- A catalog entry whose id is already installed from another key, or by a user-owned or
  workspace-local plugin, stays listed but is not installable until that plugin is removed.

## 3. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, `mcpServers`, `hooks`, `settings`, `workflows`, `paths`,
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
into `<craft-home>/plugins/<pluginId>`. Registry availability never installs anything. Install
validates the marketplace entry and the target manifest id. DotCraft never executes code directly
from a registry URL.

Catalog and local-folder installs stage copies beside the destination before renaming them into
place. On Windows, installation renames (including backup and rollback) retry access-denied and
sharing-violation failures up to five times, waiting 100, 200, 300, 400, and 500 ms. Other errors
and exhausted retries fail the affected plugin operation; staging paths and rollback behavior are unchanged.

Installed built-in and registry plugins carry a `.builtin` marker holding a fingerprint of the
source directory and, for a marketplace install, the marketplace name and canonical source identity
that bind it to its originating catalog source. Marked directories are DotCraft-owned and may be
refreshed or removed by plugin lifecycle operations. Unmarked directories are user-owned and never
overwritten or removed automatically. Discovery and thread projections never update installed
plugin directories.

AppServer starts a non-blocking background reconciliation pass shortly after startup, serialized
through the workspace plugin mutation path, using the effective merged plugin configuration to
refresh installed managed plugins whose on-disk source (host-bundled roots, cached registry
snapshots, or local marketplaces) has a newer fingerprint. Startup reconciliation never installs
uninstalled catalog entries, never overwrites unmarked user-owned plugins, skips `.dotnet` plugins,
requires a matching recorded source identity before updating a marketplace install (skipping
installs whose source identity is missing or differs), preserves existing plugin enablement and
settings, and collects failures per plugin while keeping the previous installation usable. Managed
refresh commits only while the installed marker is still present on the shared plugins root,
synchronized with plugin removal so another workspace removing a plugin cannot cause it to be
reinstalled. When any managed installation changes, AppServer refreshes plugin-contributed skills,
MCP/LSP servers (including active threads whose stdio MCP connection identity incorporates the
deployed plugin fingerprint), and hooks, advances the plugin revision, and broadcasts
`plugin/snapshot/updated` so connected clients reload affected metadata and Desktop extensions
without prompting.

Marketplace refresh runs the same serialized managed-plugin update flow, collecting failures per
plugin and keeping the previous installation usable when replacement fails. A failed update must not
prevent unrelated plugins, thread reads, or mode changes from working; an occupied directory is an
update failure, not a reason to stop other workspaces.

`plugin/remove` removes an installed plugin directory from the user-global root (removing it from
every workspace) or from the workspace-local root, including user-owned plugins. It first renames
the directory into `<DataPath>/tmp` on the same volume, then cleans up best-effort; a failure before
the rename leaves the plugin intact. Removed built-in and registry plugins remain visible in the
installable catalog while their source is configured. Disabling is distinct: a disabled plugin
stays on disk and can be re-enabled.

## 7. TypeScript Channel Modules

A TypeScript channel module is the SDK-facing unit for one messaging integration
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

### 7.1 Channel contributions

A plugin may declare one `channel` contribution. It contains `manifest` (the static module
descriptor), `entry` (a bundled Node.js ESM launcher), and `minHostVersion` (the first supported
DotCraft version). Both paths are relative to and contained in the installed plugin root, including
after filesystem resolution. The installed bundle is self-contained apart from the host Node
runtime and declared platform prerequisites. The host never installs npm packages or runs install
scripts. Discovery and verification read metadata without executing adapter code.

A channel plugin declares `channel` in `capabilities`. The marketplace category `Channels`
requires a validated channel contribution. A presentation tag never authorizes execution. A plugin
may combine non-configuration contributions. For a channel plugin, the module's configuration descriptors are its settings schema. The same plugin cannot declare a second `settings` schema. Channel settings are workspace-only, use the module's workspace configuration file as the sole store, and never project saved credentials. Unspecified fields in the platform file are preserved.

### 7.2 Workspace activation

The workspace host resolves a configured channel from the installed, enabled plugin selected for
its `channelName`. `pluginId` identifies the distributed package, `moduleId` identifies the
module and its state, and `channelName` identifies the conversation route. The host permits one
active implementation per workspace and channel name. Conflicting registrations are reported,
not started in parallel. A user must explicitly select a different implementation.

Installing a channel plugin makes it available to configure and does not by itself start an adapter.
The workspace operator must explicitly enable the channel. The grant applies to the selected
installation and workspace. The staged launch bundle must match that grant before starting.
Updates from the same managed source retain the installation identity. Reinstalling or
switching sources requires a new grant. The operator
grants executable access to the workspace and to the channel credentials the adapter needs.
Child-process isolation is not an operating system sandbox.

The host checks the host version, supported module contract and adapter protocol, workspace
configuration, and managed WebSocket transport before launch. It uses the host-provided Node
runtime or an operator-configured Node.js 22 or later on a standalone host. It passes the active
workspace and connection context to the admitted entry point. A failed or blocked channel does
not block the workspace host or other channels. Plugin disablement suspends the channel without clearing
credentials or workspace intent. Plugin removal stops it and retains channel configuration,
state, and conversation bindings. No catalog entry, listing, or client reconnect starts an adapter.

Plugin mutations reconcile affected channels across running workspaces. Updates validate the
replacement before committing and keep the current installation available if validation fails.
On a revision change, the host stops the running process before starting its replacement.
Runtime failures surface through channel diagnostics without affecting other contributions.

The optional manifest `interface` object is display-only and does not affect startup. Recognized
fields are `shortDescription`, `longDescription`, `previewPrompt`, and `setupSteps` (an ordered list
of plain-text steps, where backticks mark literal values and `[label](https://...)` marks a link), each with a `localized*` variant keyed by
the Desktop locales (`en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, and `de`).

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
restart, including stopping or adopting affected .NET and channel contributions.

## 9. Consumer boundaries

Protocol and client projections expose discovery and lifecycle state without creating another
plugin manager. A remote workspace remains the installation owner; a client-side presentation cache
is not a local plugin installation and does not activate unrelated contributions locally.
