# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 0.7.8 |
| **Status** | Living |
| **Date** | 2026-09-28 |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills and
workflows, local plugin manifests, plugin-bundled MCP servers, client-facing plugin metadata, and the
TypeScript external channel module contract.

## 1. Architecture Overview

DotCraft plugins are host-integrated capability bundles. They distribute skills, Dynamic Workflows,
MCP server declarations, App Binding descriptors, and optional client-facing metadata without
requiring the agent pipeline to know each integration's implementation details.

The plugin contribution model is:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
3. **App Descriptors**: plugin-contained App Binding descriptors that make app connection and thread binding flows visible.
4. **Desktop Plugins**: optional trusted Desktop modules that contribute views, actions, commands, and tool presentation.
5. **Interface Metadata**: optional client-facing plugin metadata.
6. **Dynamic Workflows**: plugin-contained JavaScript workflows registered under the plugin namespace.
7. **.NET Plugins**: plugin-contained managed assemblies admitted by the host runtime.

Plugin manifests do not declare model-callable native tools. Legacy manifest fields `tools`, `functions`, and `processes` are unsupported and ignored with diagnostics. External reusable services should use MCP. Thread-scoped client callbacks use the Runtime Dynamic Tool contract in [Tool Architecture](tools-architecture.md).

## 2. Local Plugin Manifest

Local plugins use this manifest path:

```text
<plugin-root>/.craft-plugin/plugin.json
```

The supported manifest schema version is `1`.

Manifest metadata includes:

- `schemaVersion`
- `id`
- `version`
- `displayName`
- `description`
- `capabilities`
- `interface`
- `skills`
- `commands`
- `mcpServers`
- `hooks`
- `lspServers`
- `apps`
- `desktop`
- `workflows`
- `paths`
- `dotnet`
- `dependencies`
- `settings`

Plugins must declare at least one supported contribution: a plugin-contained `skills` path, plugin-bundled MCP servers, lifecycle hooks, App Binding descriptors, LSP server descriptors, a Desktop module, Dynamic Workflows, an in-process `dotnet` contribution, or interface metadata. Each contribution may appear without the others.

`commands` is a manifest-relative directory containing Markdown custom commands. Enabled plugins
contribute commands under `<pluginId>:<relative-command-name>`. Commands are a supported contribution
on their own. Workspace and user commands retain precedence. Removing or disabling a plugin removes
its commands.

`settings` is an optional manifest-relative path to a plugin settings schema, for example
`"./settings.schema.json"`. It does not count as a runtime contribution. The schema document has a
single `fields` array. Each field has a required `key` and `type`, plus optional `defaultValue`,
`options`, `min`, and `max` members. Supported types are `text`, `textarea`, `number`, `bool`,
`select`, `stringList`, `keyValueMap`, and `json`. Keys are compared case-insensitively and must be
unique. A declared default must pass the same validation as a stored value. `select` requires a
non-empty string `options` array and accepts only listed values. `min` and `max` apply only to
`number`.

Plugin configuration is stored separately from the host's layered `config.json` documents:

- personal: `<UserDataPath>/plugin-config.json`;
- workspace: `<DataPath>/plugin-config.json`.

For the official host these resolve to `~/.craft/plugin-config.json` and
`<workspace>/.craft/plugin-config.json`. Each document's root is an object whose properties are
canonical plugin ids and whose values are setting objects. There is no version or `plugins`
wrapper. The effective value is built from schema defaults, personal configuration, then workspace
configuration. Objects merge recursively. Arrays and scalar values replace the lower layer.

Every stored namespace is strict: it may contain only fields declared by that plugin's current
schema, and every value must validate. An invalid shared document or namespace produces a stable
configuration error and a plugin diagnostic. It does not prevent other AppServer or plugin
functions from operating. DotCraft rereads the documents for every configuration read, mutation,
and .NET activation. It does not watch these files, preserve a last-good value, retain unknown
fields, or migrate schemas.

Mutations perform an atomic read-modify-write under a cross-process file lock. The writer rereads
the complete document while holding the lock and changes only the target namespace. It never
overwrites an existing document that cannot be parsed. Concurrent changes to different namespaces
are retained; changes to the same namespace are last-writer-wins. Unsetting a key removes that
scope's override so the lower layer becomes effective. Disabling, removing, or reinstalling a
plugin does not remove either namespace.

`mcpServers` is an optional manifest-relative path to a plugin-contained MCP configuration file. If omitted, DotCraft looks for `./.mcp.json` in the plugin root. The MCP file may use either `{ "mcpServers": { ... } }` or a direct server map. Plugin MCP config uses the canonical DotCraft fields `arguments`, `environmentVariables`, and `headers`; unknown server properties are rejected. Plugin-bundled MCP servers use the same runtime as workspace `McpServers`; relative MCP `cwd` values resolve under the plugin root. At runtime, contributed server names are prefixed as `{pluginId}:{serverName}` to avoid collisions with workspace MCP servers and other plugins. This prefixed value is the connection-facing `runtimeName`, not a model-visible tool namespace. MCP tool projection derives its separately normalized canonical namespace from the declared server name and retains `runtimeName` plus the raw MCP tool name only for exact source routing; clients and provider adapters MUST NOT split or flatten `runtimeName` to construct model identity.

Effective MCP merge rules:

- Workspace `McpServers` are loaded first and remain editable workspace configuration.
- Enabled, installed plugin MCP servers are then added as read-only runtime entries with origin metadata (`kind=plugin`, `pluginId`, display name, and declared server name).
- If a plugin runtime name conflicts with a workspace server or a higher-priority plugin server, the plugin declaration is marked shadowed in plugin metadata and is not connected.
- `mcp/list` returns the effective runtime view. Workspace config writes (`mcp/upsert`, `mcp/remove`, and config persistence) never write plugin-origin servers into `.craft/config.json`.
- Plugin-bundled MCP startup is non-fatal. A missing command, bad endpoint, timeout, or protocol error is reported through MCP runtime status (`mcpServerStatus/list` / `mcpServer/startupStatus/updated`) and diagnostics where applicable; it must not prevent plugin discovery, AppServer readiness, or Desktop connection. Agent tool materialization waits for the current effective MCP startup attempt to settle, so ready plugin MCP tools are available to new turns without making AppServer startup synchronous.

`hooks` declares plugin-contained lifecycle hook files. It accepts any of these shapes:

- `"hooks": "./hooks/hooks.json"`
- `"hooks": ["./hooks/a.json", "./hooks/b.json"]`
- `"hooks": { "hooks": { ... } }`
- `"hooks": [{ "hooks": { ... } }]`

If `hooks` is omitted, discovery checks `./hooks/hooks.json`, then `./hooks.json` for imported
bundle compatibility. An explicit declaration suppresses defaults. Hook paths follow the shared
manifest rules. Only installed, enabled plugins contribute hooks; discovery never executes them.

Plugin host-side data is stored at `<UserDataPath>/plugins/<pluginId>/data`, or at
`<DataPath>/plugin-data/<pluginId>` when no user-data root is configured. Hook, LSP, and .NET
contributions share this directory. Plugin configuration documents remain separate. The renderer
receives no data-directory path or general file API.

Example MCP plugin:

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
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill", "MCP", "Hooks"],
    "defaultPrompt": "Review this change."
  }
}
```

`interface` contains optional UI metadata for Desktop and other clients: display name, short and long descriptions, developer, category, capability tags, default prompt, icon/logo paths, and public website/privacy/terms links. Icon and logo assets own their complete visual treatment. Assets that need a background for theme contrast include it directly; assets that work in both themes may remain transparent. Path fields inside `interface` use the same manifest-relative path rules. Tool-result-specific renderer contracts are not declared in `interface`; trusted local presentation and MCP Apps boundaries are defined by [Tool Architecture](tools-architecture.md#14-presentation-boundary).

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child directory can contain a DotCraft-compatible `SKILL.md`. Skills contributed by enabled plugins are available in `skills/list` with source `plugin` and include `pluginId` / `pluginDisplayName` attribution. Disabling the plugin removes its contributed skills from agent context and hides compatibility built-in copies owned by that plugin.

Skill interface icons without parent traversal resolve only from the child skill's own `assets/`
directory. A plugin-contained skill may instead use a parent-relative path when its lexically
normalized target is inside the owning plugin's root `assets/` directory. For the standard
`skills/<skill-id>/` layout, `../../assets/icon.svg` reuses a plugin asset. Paths into any other
plugin directory or outside the plugin root are ignored. Non-plugin skills never receive this
shared-assets policy.

`workflows` is an optional manifest-relative path to a plugin-contained workflow directory, for example
`"./workflows/"`. If omitted and the root `./workflows/` directory exists, DotCraft discovers it by
default. Enabled and installed plugins contribute its top-level `*.js` definitions under the stable
name `{pluginId}:{workflowName}`. Plugin workflows never shadow workspace or personal definitions.
Disabling or removing the bundle withdraws its workflow contributions.

`apps` points to a plugin-contained App Binding descriptor document, for example `"./apps.json"`. Apps contributed by installed and enabled plugins become eligible for App Binding connection and thread binding. Catalog-visible built-in plugins may expose app metadata before installation, but connection and binding are blocked until the owning plugin is installed and enabled.

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

`description` is optional presentation metadata for clients listing the Desktop contribution. When it is absent, clients may fall back to the parent plugin description. The entry must be an ESM `.mjs` file, and every style must be `.css`. All paths are manifest-relative and confined to `./desktop/dist/`. The executable `entry` / `styles` declaration and output tree produce the revision projected through `plugin/list` and `plugin/view`; changing only `description` does not change that revision.

The Desktop host owns activation, runtime trust, and contribution lifecycle; manifest discovery does not execute the module.

### .NET manifest

`dotnet` declares an in-process managed contribution. The minimal shape is:

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

`dotnet` is optional. When present, `version` is mandatory and:

- `minHostVersion` is required and is the canonical `MAJOR.MINOR.PATCH` minimum DotCraft host version the plugin runs on. A host below it blocks the plugin before any of its code is loaded. The host version is the version of `DotCraft.Core`, the assembly that carries the plugin API, so an application that embeds DotCraft is measured by the engine it embeds rather than by its own version.
- `entryAssembly` is required and names one managed entry assembly.
- `entryType` is required and is the full CLR name of one public, concrete, non-generic type that implements `DotCraft.Plugins.IDotCraftPlugin` and has a public parameterless constructor.
- `exportedApiAssemblies` is optional and defaults to an empty array. Every entry names a separate managed contract assembly whose public API may be consumed by declared dependent plugins. The entry assembly itself cannot be exported.

`dependencies` is optional, is valid only when `dotnet` is present, and defaults to an empty map. Each key is a canonical plugin id and each value is the minimum provider version within one compatibility line: stable versions must share the required major version, while `0.x` versions must also share its minor version. Self-dependencies, duplicate ids after canonicalization, and range syntax are invalid. The map declares required .NET generation lifecycle edges; it does not describe private library or NuGet dependencies. A consumer may import a CLR service only from a plugin named directly in this map.

The deployment bundle must already contain the entry assembly, its adjacent `.deps.json`, all private managed dependencies, and all required native assets. Discovery, installation, and activation do not restore NuGet packages, contact package feeds, run MSBuild, execute install scripts, or compile source.

An installed `dotnet` plugin runs with the host process's full authority and requires an explicit, fingerprint-bound trust confirmation before any of its code loads. An authoring build may qualify its exact development fingerprint for the current process without modifying durable trust.

DotCraft discovers plugin roots from:

1. Workspace-local root: `<workspace>/.craft/plugins`, for plugins that come with the workspace. DotCraft never installs into it.
2. Explicit roots in `Plugins.PluginRoots` order
3. User-global root: `<craft-home>/plugins`, where every install lands.
4. Desktop-bundled built-in catalog roots from `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`
5. Configured plugin registry snapshots

Explicit roots may point either to one plugin root containing `.craft-plugin/plugin.json` or to a container directory containing multiple plugin roots. Missing roots are skipped with diagnostics. Local manifest plugins are enabled by default; `Plugins.DisabledPlugins` disables a plugin even when it is discovered from a default or explicit root.

When multiple roots contain the same plugin id, higher-priority roots win and lower-priority duplicates are skipped with diagnostics. A workspace, explicit, or user-global plugin suppresses the bundled catalog entry with the same id.

## 3. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, `mcpServers`, `settings`, `workflows`, `paths`, interface asset paths, and other
manifest-relative fields. Desktop entry and style paths also remain inside `./desktop/dist/`.

## 4. Loading and Diagnostics

Plugin loading has three responsibilities:

1. The manifest parser reads `.craft-plugin/plugin.json`, validates supported fields, normalizes paths, and returns metadata plus diagnostics.
2. The discovery service scans roots, resolves duplicate plugin ids, applies plugin enablement config, and produces plugin records.
3. Enabled plugins contribute skill sources, plugin-bundled MCP server declarations, lifecycle hook declarations, app descriptors, and Desktop module metadata to the workspace runtime/client metadata.

Diagnostics are non-fatal and available to logs and UI surfaces. They cover invalid JSON, missing fields, missing supported plugin capabilities, invalid ids, invalid manifest-relative paths, unsupported legacy native tool fields, duplicate plugin ids, disabled plugins, invalid MCP declarations, invalid hook declarations, and missing roots.

If a manifest declares `tools`, `functions`, or `processes`, DotCraft emits `UnsupportedPluginNativeTools` and ignores those fields. If no supported contribution remains, DotCraft also emits `MissingPluginCapabilities` and the plugin is not loaded.

Discovery or loading failures for one plugin must not prevent other plugins from loading.

## 5. Tool contributions

Tool registrations obey [Tool Architecture](tools-architecture.md). Plugin provenance identifies the
owning bundle and is independent from model-visible identity. For MCP, the runtime connection name
retains `{pluginId}:` for routing; it is never exposed by parsing that prefix into a model namespace.
Equal declared names are disambiguated through the shared identity normalization contract.

## 6. Built-In Plugin Lifecycle

Built-in plugin manifests are host-bundled filesystem plugins exposed through a built-in catalog. Desktop bundles the source-of-truth plugin container under `resources/plugins/dotcraft-bundled/plugins`; the official Docker image bundles the same container under `/opt/dotcraft/plugins`. Each host launches AppServer with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` pointing at its bundled container. Registry plugin manifests are discovered from configured source registry snapshots. Catalog entries are visible to clients before installation, but they are not active until installed into the user-global root `<craft-home>/plugins/<pluginId>`.

`DOTCRAFT_BUILTIN_PLUGIN_ROOTS` is a platform path-list. Each entry may be a plugin container directory or a direct plugin root. Entries must be absolute; missing or invalid entries produce non-fatal plugin diagnostics. When the variable is absent or empty, AppServer exposes no uninstalled built-in catalog entries, but already-installed plugins remain discoverable.

Official hosts provide the default DotCraft plugin registry through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`. Docker persists the effective Craft home separately from the Workspace so user-added marketplace configuration and materialized registry snapshots survive container replacement. Registry availability does not install plugins automatically; `plugin/install` remains the only operation that copies a selected catalog plugin into the user-global root.

Installed built-ins carry a `.builtin` marker:

- `plugin/install` copies the selected desktop-bundled source directory into `<craft-home>/plugins/<pluginId>` and the plugin is enabled by default in every workspace.
- `.builtin` stores a fingerprint of the source directory. Directories with `.builtin` are owned by DotCraft and can be refreshed or removed by DotCraft lifecycle operations.
- Directories without `.builtin` are treated as user-owned and are not overwritten or removed by DotCraft.

`plugin/remove` removes an installed plugin directory from the user-global root, which removes it from every workspace, or from the workspace-local root. It first renames the directory into `<DataPath>/tmp` on the same volume, then cleans up the moved directory on a best-effort basis. A failure before the rename leaves the installed directory intact. Managed built-ins and registry-installed plugins carry `.builtin` so DotCraft can refresh them and can distinguish them from user-owned local plugins, but user-owned plugins in either root may also be removed explicitly through `plugin/remove`. Removing a plugin is distinct from disabling it: removed built-ins and registry plugins are absent from runtime discovery but remain visible in the installable catalog when their source is configured, while disabled installed plugins remain on disk and can be re-enabled.

Registry catalog entries are source paths inside a registry snapshot. `plugin/install` validates the marketplace entry, validates the target plugin manifest id, then copies the registry plugin directory into `<craft-home>/plugins/<pluginId>` with a managed marker. DotCraft never executes code directly from a registry URL; Desktop loads only the locally installed extension bundle.

## 7. TypeScript External Channel Modules

A TypeScript external channel module is the SDK-facing unit that represents one external channel integration variant, such as a first-party Feishu module or an enterprise Feishu module.

Hosts integrate modules through a stable module contract rather than package-internal source layout. A module owns platform protocol integration, platform-specific configuration, lifecycle behavior, runtime tool registration, and tool execution. The host owns discovery, workspace context, configuration storage, launcher lifecycle, and user-visible enablement.

The module contract defines:

- **Module identity**: stable `moduleId`, channel family, display metadata, optional UI interface metadata, variant semantics, and capability summary.
- **Manifest carrier**: a module-root SDK export that exposes host-readable module metadata.
- **Entry contract**: a documented startup entry that receives workspace context and returns a structured startup outcome.
- **Workspace context**: workspace path, `.craft` path, config path, state path, temp path, and AppServer connection information.
- **Configuration contract**: workspace-scoped configuration stored under `.craft/<configFileName>`, with module-owned validation and host-visible descriptors.
- **State and temp layout**: module-owned persistent state and temporary runtime files scoped to the active workspace.
- **Lifecycle contract**: structured statuses, errors, diagnostics, interactive setup needs, and restart requirements.
- **Capability and tool registration**: manifest-level capability summaries plus runtime channel tool descriptors declared during AppServer `initialize`.

Desktop may expose discoverable channel modules in the Channels workflow, but listing modules must not require executing module business logic. Bundled and user-installed modules can coexist; user-installed content wins when both provide the same `moduleId`.

Module manifests may include an optional `interface` object for host-rendered discovery and detail surfaces. It is display-only metadata and must not affect runtime startup. The recognized fields are:

- `shortDescription` / `localizedShortDescription`: compact list subtitle and brief detail subtitle.
- `longDescription` / `localizedLongDescription`: richer detail-page description.
- `previewPrompt` / `localizedPreviewPrompt`: short sample prompt or collaboration phrase for visual previews.

Localized `interface` maps use the same locale keys as other module display metadata: `en` and `zh-Hans`.

## 8. Configuration

The `Plugins` config section contains:

- `PluginRoots`: additional local plugin roots or plugin container directories. Relative paths resolve against the workspace root.
- `DisabledPlugins`: plugin ids turned off in the workspace. DotCraft writes it only to the workspace layer; turning a plugin back on removes its id.
- `PluginRegistries`: additional plugin marketplace sources. Each source declares its kind, source value, optional reference and sparse paths, and may override the marketplace path.
- `DisableDefaultPluginRegistry`: disables the host-provided default official plugin marketplace.

Marketplace sources are recorded in user-global configuration so one added source is available in every workspace. Installation is per user: `plugin/install` and `plugin/installLocal` copy the plugin into `<craft-home>/plugins/<pluginId>`, so it is available in every workspace that user opens on that machine, and each workspace can turn it off. For a remote workspace that root is on the remote machine.

Installing, removing, or turning a plugin on or off publishes the revision of the layer it wrote, so every other AppServer of the same user rediscovers plugins and refreshes their contributions without a restart.

Installed built-in plugins and local manifest plugins are enabled by default unless the workspace turns them off. Built-ins that are visible only through the catalog are installable but not enabled and do not contribute tools or skills to agent context.

Workspace-level MCP configuration continues to use `McpServers`. Plugin-bundled MCP servers are contributed by enabled plugins and merged into the effective MCP runtime configuration as read-only runtime entries. Desktop and other clients should show plugin MCP alongside workspace MCP in runtime settings, but edits and deletes apply only to workspace-origin entries.

## 9. Consumer boundaries

Protocol and client projections expose discovery and lifecycle state without creating another
plugin manager. A remote workspace remains the installation owner; a client-side presentation cache
is not a local plugin installation and does not activate unrelated contributions locally.
