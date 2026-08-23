# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 1.6.1 |
| **Status** | Living |
| **Date** | 2026-08-24 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [.NET Plugin Architecture](dotnet-plugins.md), [Plugin Registry](plugin-registry.md), [Tool Architecture](tools-architecture.md), [Session Core](session-core.md), [Lifecycle Hooks](../features/lifecycle-hooks.md), [Dynamic Workflows](../features/dynamic-workflows.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills and
workflows, local plugin manifests, plugin-bundled MCP servers, client-facing plugin metadata, and the
TypeScript external channel module contract.

---

## 1. Architecture Overview

DotCraft plugins are host-integrated capability bundles. They distribute skills, Dynamic Workflows,
MCP server declarations, App Binding descriptors, and optional client-facing metadata without
requiring the agent pipeline to know each integration's implementation details.

The plugin contribution model is:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
3. **App Descriptors**: plugin-contained App Binding descriptors that make app connection and thread binding flows visible.
4. **Desktop Extensions**: optional trusted Desktop UI bundles that contribute client surfaces.
5. **Interface Metadata**: optional client-facing plugin metadata.
6. **Dynamic Workflows**: plugin-contained JavaScript workflows registered under the plugin namespace.
7. **.NET Plugins**: plugin-contained managed assemblies loaded in-process, whose runtime, contribution points, trust model, and lifecycle are owned by [.NET Plugin Architecture](dotnet-plugins.md).

Plugin manifests do not declare model-callable native tools. Legacy manifest fields `tools`, `functions`, and `processes` are unsupported and ignored with diagnostics. External reusable services should use MCP. Thread-scoped client callback tools should use Runtime Dynamic Tools (`thread/start.dynamicTools`, `thread/resume.dynamicTools`, and `item/tool/call`) defined in [AppServer Protocol](../protocols/appserver-protocol.md).

---

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
- `mcpServers`
- `hooks`
- `lspServers`
- `apps`
- `desktopExtensions`
- `workflows`
- `paths`
- `dotnet`
- `dependencies`

Plugins must declare at least one supported contribution: a plugin-contained `skills` path, plugin-bundled MCP servers, lifecycle hooks, App Binding descriptors, LSP server descriptors, Desktop extensions, Dynamic Workflows, an in-process `dotnet` contribution, or interface metadata. Skill-only, MCP-only, hooks-only, app-only, desktop-extension-only, workflow-only, dotnet-only, and interface-only plugins are valid.

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

If `hooks` is omitted, DotCraft automatically discovers `./hooks/hooks.json` under the plugin root. If that file is absent, DotCraft also checks a top-level `./hooks.json` for compatibility with imported plugin ecosystems. Explicit `hooks` declarations always take precedence and suppress default discovery. Hook paths use the same manifest-relative path rules as other plugin paths: they must start with `./`, must not escape the plugin root, and must resolve inside the plugin directory. Plugin hook files reuse the workspace `.craft/hooks.json` shape defined by [Lifecycle Hooks](../features/lifecycle-hooks.md). DotCraft executes command hooks and reports unsupported reserved handler types through plugin diagnostics.

Plugin hooks are loaded only from installed and enabled plugins. They are listed by `hooks/list` with source `plugin`, and summarized in `plugin/list` / `plugin/view` as `{ key, eventName }`. Commands run from the workspace root, like config hooks. DotCraft expands `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` in plugin hook commands and injects the same values as environment variables. Plugin data uses the LSP data location: `%LocalAppData%/DotCraft/plugins/<pluginId>/data` on Windows, with platform-equivalent app data paths elsewhere. Compatibility aliases may be injected for imported plugin ecosystems, but DotCraft-authored plugins should use the `DOTCRAFT_*` variables.

Plugin hooks are user-trusted runtime behavior. Installing or enabling a plugin does not automatically trust its hooks. First appearance and any hash-changing edit returns `trustStatus` `untrusted` or `modified` from `hooks/list`; plugin hooks run only after `hooks/trustPlugin` stores the current hash for every current hook from that plugin in user-global `Hooks.State`. `hooks/setState` remains the per-hook compatibility path for clients that need it.

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
    "defaultPrompt": "Review this change.",
    "brandColor": "#2563EB"
  }
}
```

`interface` contains optional UI metadata for Desktop and other clients: display name, short and long descriptions, developer, category, capability tags, default prompt, brand color, icon/logo paths, and public website/privacy/terms links. Path fields inside `interface` use the same manifest-relative path rules. Tool-result-specific renderer contracts are not declared in `interface`; trusted local presentation and MCP Apps boundaries are defined by [Tool Architecture](tools-architecture.md#14-presentation-boundary).

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child directory can contain a DotCraft-compatible `SKILL.md`. Skills contributed by enabled plugins are available in `skills/list` with source `plugin` and include `pluginId` / `pluginDisplayName` attribution. Disabling the plugin removes its contributed skills from agent context and hides compatibility built-in copies owned by that plugin.

`workflows` is an optional manifest-relative path to a plugin-contained workflow directory, for example
`"./workflows/"`. If omitted and the root `./workflows/` directory exists, DotCraft discovers it by
default. Enabled and installed plugins contribute its top-level `*.js` definitions under the stable
name `{pluginId}:{workflowName}`. Plugin workflows never shadow workspace or personal definitions;
their parsing, approval, execution, and command registration follow
[Dynamic Workflows](../features/dynamic-workflows.md).

`apps` points to a plugin-contained App Binding descriptor document, for example `"./apps.json"`. Apps contributed by installed and enabled plugins become eligible for App Binding connection and thread binding. Catalog-visible built-in plugins may expose app metadata before installation, but connection and binding are blocked until the owning plugin is installed and enabled.

`desktopExtensions` points to a plugin-contained Desktop extension descriptor document, for example `"./desktop-extensions.json"`. Desktop extensions are trusted client UI bundles loaded only after the plugin is installed and enabled. Desktop extension v1 is not an untrusted JavaScript sandbox: extension code runs in the Desktop renderer as trusted plugin code. The descriptor is the source of truth for host capabilities such as `requiredAppSurfaces`; any capability crossing from renderer to main, AppServer, shell, or local network must be enforced by Desktop from the verified plugin descriptor, not from renderer-supplied policy. The descriptor contains one or more ESM bundle entries and the Desktop surfaces they contribute:

```json
{
  "extensions": [
    {
      "id": "team-card-board",
      "displayName": "Team card board",
      "description": "Team collaboration board.",
      "entry": "./desktop/team-card-board.mjs",
      "styles": ["./desktop/team-card-board.css"],
      "surfaces": [
        {
          "type": "mainView",
          "viewId": "teams",
          "label": "Team",
          "localizedLabel": { "en": "Team", "zh-Hans": "团队" },
          "icon": "kanban",
          "placement": "sidebar",
          "order": 40
        },
        {
          "type": "pluginDetail",
          "title": "Team card board",
          "description": "Adds the Team board to Desktop."
        }
      ],
      "requiredAppIds": ["com.example.team-board"],
      "requiredAppSurfaces": [
        {
          "appId": "com.example.team-board",
          "surfaceId": "board",
          "access": ["read", "write"]
        }
      ]
    }
  ]
}
```

Desktop extension path fields use the same manifest-relative path rules as other plugin paths. The supported surface `type` values are `mainView`, `pluginDetail`, `detailPanel`, `composerAction`, `conversationRenderer`, and `settingsPanel`. Unknown surface types are diagnostics and are ignored by clients.

A `mainView` surface may declare an optional `icon`, a host-resolved named glyph for its sidebar nav entry. The client maps known names to built-in icons; an omitted or unrecognized name falls back to the generic extension icon. Extensions do not ship raster assets for nav entries.

Surface display text is localized by the extension, not the host catalog. `label` is the required base (English) string; an optional `localizedLabel` object carries per-locale overrides keyed by app locale (for example `"zh-Hans"`). The client resolves the active locale and falls back to `label` when a locale is absent, so extensions ship their own translations and unknown locales degrade gracefully.

`requiredAppSurfaces` declares the app-owned surfaces an extension may use through Desktop. Each entry has:

- `appId`: the App Binding app id;
- `surfaceId`: the app-defined surface id published through `app/surface/publish`;
- `access`: a non-empty, duplicate-free subset of `read` and `write`.

Duplicate `(appId, surfaceId)` entries are invalid. Omission or an empty array grants no App Surface access. `requiredAppIds` remains the independent allow-list for the extension's `host.appBindings` connection-status, start, and open helpers; declaring a surface does not implicitly grant those helpers.

### Extension App Surface transport

Extensions access app-owned presentation APIs only through `host.appSurfaces.getJson(appId, surfaceId, path)` and `host.appSurfaces.postJson(appId, surfaceId, path, body)`. `getJson` requires descriptor access `read`; `postJson` requires `write`.

The renderer supplies only `appId`, `surfaceId`, an origin-relative path, and for POST a JSON body. The path MUST begin with `/` and MUST NOT contain a scheme, authority, user info, or fragment. Desktop main rejects network-path references such as `//host/path`, resolves the live endpoint through trusted-client `app/surface/resolve`, and verifies the descriptor grant before issuing the request. The renderer cannot provide or override an origin, endpoint, authorization header, or bearer.

Desktop main proxies an HTTP `GET` or `POST` to the resolved loopback HTTP(S) endpoint, preserves the endpoint's origin and base path, injects `Authorization: Bearer <resolved bearer>`, and returns the parsed JSON result. Redirects MUST NOT escape the resolved loopback origin. Missing or expired publication, including a lease that expires before dispatch, is exposed as the stable `AppSurfaceUnavailable` error. Endpoint and bearer values remain main-process-only and MUST NOT be returned to extension code.

A repeated app publication may replace the endpoint and bearer without changing the descriptor. Desktop resolves for each request rather than treating a prior resolution as durable authority. Renderer wrappers may reject calls early for user experience, but the main process is the enforcement and proxy boundary. Agent-invoked and externally visible writes still use App Binding tools and app-owned approval; `requiredAppSurfaces` grants only the trusted Desktop extension transport described here.

### Extension AppServer bridge (Desktop descriptor authority)

Some extensions manage DotCraft's own state rather than an external app's loopback surface — e.g. an Agent Builder that reads and writes Agent Profiles via `agent/profiles/*`. For these, Desktop may read `appServerScopes` directly from the verified `desktop-extensions.json` descriptor: a list of AppServer JSON-RPC method patterns the extension may call through `host.appServer.request(method, params)`. A trailing `*` is a wildcard prefix (e.g. `agent/profiles/*`); a bare method name matches exactly (e.g. `thread/start`).

`appServerScopes` is a Desktop-side descriptor authority, not a C# plugin catalog wire field. The C# `plugin/list` projection currently does not model or forward this field; a Desktop host that implements the bridge must read the verified descriptor on disk when creating the extension grant.

Rules:

- The allow-list is enforced in the main process, read straight from the verified `desktop-extensions.json` on disk when the extension grant is created — the on-disk descriptor is the authority.
- A request whose method matches no declared `appServerScopes` pattern is rejected, and an extension that declares no `appServerScopes` cannot reach AppServer at all (default-closed).
- Unlike `host.appSurfaces` (descriptor-authorized loopback HTTP to a published app surface), this bridge targets the DotCraft AppServer itself — the same JSON-RPC the Desktop client uses — so it is appropriate only for extensions managing first-party DotCraft capabilities. The declared scopes are the extension's AppServer intent and may be surfaced by Desktop at install time.
- Renderer host wrappers may reject early for UX, but the main process is the enforcement point.

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

- `minHostVersion` is required and is the canonical `MAJOR.MINOR.PATCH` minimum DotCraft host version the plugin runs on. A host below it blocks the plugin before any of its code is loaded.
- `entryAssembly` is required and names one managed entry assembly.
- `entryType` is required and is the full CLR name of one public, concrete, non-generic type that implements `DotCraft.Plugins.IDotCraftPlugin` and has a public parameterless constructor.
- `exportedApiAssemblies` is optional and defaults to an empty array. Every entry names a separate managed contract assembly whose public API may be consumed by declared dependent plugins. The entry assembly itself cannot be exported.

`dependencies` is optional, is valid only when `dotnet` is present, and defaults to an empty map. Each key is a canonical plugin id and each value is the minimum provider version within one compatibility line: stable versions must share the required major version, while `0.x` versions must also share its minor version. Self-dependencies, duplicate ids after canonicalization, and range syntax are invalid. The map declares required .NET generation lifecycle edges; it does not describe private library or NuGet dependencies. A consumer may import a CLR service only from a plugin named directly in this map.

The deployment bundle must already contain the entry assembly, its adjacent `.deps.json`, all private managed dependencies, and all required native assets. DotCraft does not restore NuGet packages, contact package feeds, run MSBuild, execute install scripts, or compile source while discovering, installing, or activating a plugin. Plugin authors produce the bundle with the normal .NET SDK before DotCraft consumes it.

A `dotnet` plugin runs with the host process's full authority and requires an explicit, fingerprint-bound trust confirmation before any of its code loads. The contribution points it may contribute to, the assembly load and reclaim lifecycle, the trust model, and the runtime projection are defined by [.NET Plugin Architecture](dotnet-plugins.md); everything in this spec applies to it unchanged.

DotCraft discovers plugin roots from:

1. Workspace-local root: `<workspace>/.craft/plugins`
2. Explicit roots in `Plugins.PluginRoots` order
3. User-global root: `<craft-home>/plugins`
4. Desktop-bundled built-in catalog roots from `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`
5. Configured plugin registry snapshots

Explicit roots may point either to one plugin root containing `.craft-plugin/plugin.json` or to a container directory containing multiple plugin roots. Missing roots are skipped with diagnostics. Local manifest plugins are enabled by default; `Plugins.DisabledPlugins` disables a plugin even when it is discovered from a default or explicit root.

When multiple roots contain the same plugin id, higher-priority roots win and lower-priority duplicates are skipped with diagnostics. A workspace, explicit, or user-global plugin suppresses the bundled catalog entry with the same id.

---

## 3. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, `mcpServers`, `desktopExtensions`, `workflows`, `paths`, interface asset
paths, and path fields inside Desktop extension descriptors.

---

## 4. Loading and Diagnostics

Plugin loading has three responsibilities:

1. The manifest parser reads `.craft-plugin/plugin.json`, validates supported fields, normalizes paths, and returns metadata plus diagnostics.
2. The discovery service scans roots, resolves duplicate plugin ids, applies plugin enablement config, and produces plugin records.
3. Enabled plugins contribute skill sources, plugin-bundled MCP server declarations, lifecycle hook declarations, app descriptors, and Desktop extension descriptors to the workspace runtime/client metadata.

Diagnostics are non-fatal and available to logs and UI surfaces. They cover invalid JSON, missing fields, missing supported plugin capabilities, invalid ids, invalid manifest-relative paths, unsupported legacy native tool fields, duplicate plugin ids, disabled plugins, invalid MCP declarations, invalid hook declarations, and missing roots.

If a manifest declares `tools`, `functions`, or `processes`, DotCraft emits `UnsupportedPluginNativeTools` and ignores those fields. If no supported contribution remains, DotCraft also emits `MissingPluginCapabilities` and the plugin is not loaded.

Discovery or loading failures for one plugin must not prevent other plugins from loading.

---

## 5. Built-In and External Tool Sources

### Browser Built-In Plugin

DotCraft ships Browser as the built-in plugin `browser`. It contributes:

- The `browser` skill, loaded from the plugin's `skills` directory.
- Client-facing metadata for Desktop and plugin-management views.

When Browser is installed and enabled, DotCraft may expose the server-owned `NodeReplJs` runtime tool for threads bound to an AppServer client that advertises both Node REPL and Browser support. `NodeReplJs` is not declared in the plugin manifest.

### Chrome Built-In Plugin

DotCraft ships Chrome automation as the built-in plugin `chrome`. It contributes:

- The `chrome` skill, loaded from the plugin's `skills` directory.
- Client-facing metadata for Desktop and plugin-management views.
- Setup and diagnostic scripts for Chrome extension and Native Messaging host installation state.

When Chrome is installed and enabled, DotCraft may expose the server-owned `NodeReplJs` runtime tool for threads bound to an AppServer client that advertises both Node REPL and Browser support. The Chrome skill selects the `chrome-extension` browser backend inside the Node runtime; `NodeReplJs` is not declared in the plugin manifest.

Chrome setup detection must not inspect cookies, passwords, session stores, local storage, or browsing databases. The development extension uses a fixed manifest key for deterministic unpacked extension IDs; production distribution must replace it with the official Chrome Web Store, private, unlisted, or enterprise-managed extension ID.

The long-term Chrome automation runtime contract is defined in [Chrome Browser Runtime](../features/chrome-browser-runtime.md). Plugin architecture owns contribution and installation semantics; Chrome Browser Runtime owns browser session lifecycle, tab ownership, command timeout, diagnostics, and runtime migration goals.

### External Integration Registry Plugins

Optional external application integrations should be distributed through the plugin registry rather than bundled with the DotCraft Desktop package. A registry plugin may contribute:

- skills loaded from the plugin's `skills` directory;
- App Binding descriptors loaded from plugin-owned descriptor files;
- Desktop extension descriptors and assets;
- client-facing metadata for Desktop and plugin-management views.

Installing a registry plugin does not install or launch any native application required by the integration. The plugin's App Binding descriptor declares native app requirements and handoff behavior; Desktop surfaces native app installation, connection, and thread binding as separate steps.

### External Channel Tools

External channel tools are runtime-declared by channel adapters during AppServer `initialize`. Static plugin manifests are not required for external-channel runtime tools. Execution continues to use the `ext/channel/toolCall` server-to-client request defined by [External Channel Adapter](../protocols/external-channel-adapter.md).

External channel tool invocations are projected as standard Session Core `toolCall` and `toolResult` items with plugin/channel provenance.

### Runtime Dynamic Tools

Runtime Dynamic Tools are declared by AppServer clients on `thread/start.dynamicTools` or rebound by `thread/resume.dynamicTools`, then invoked through the `item/tool/call` server-to-client request. They are bound to the current declaring connection and are suitable for client-owned, thread-scoped capabilities such as an external review runner submitting a draft back to its caller.

Runtime Dynamic Tools are not plugin manifest tools.

### MCP Tools

MCP tools are configured through workspace `McpServers`, per-thread `ThreadConfiguration.McpServers`, or plugin-bundled MCP declarations. They are discovered by the MCP runtime and injected through the MCP tool path.

Plugin provenance and MCP model identity are independent. The plugin id remains available through origin provenance and the runtime connection name, while the model sees only the collision-safe composite tool identity defined by [Tool Architecture](tools-architecture.md). Equal declared server/tool names from different effective runtimes are disambiguated deterministically during batch normalization rather than by exposing the `{pluginId}:` routing prefix.

---

## 6. Built-In Plugin Lifecycle

Built-in plugin manifests are host-bundled filesystem plugins exposed through a built-in catalog. Desktop bundles the source-of-truth plugin container under `resources/plugins/dotcraft-bundled/plugins`; the official Docker image bundles the same container under `/opt/dotcraft/plugins`. Each host launches AppServer with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` pointing at its bundled container. Registry plugin manifests are discovered from configured source registry snapshots. Catalog entries are visible to clients before installation, but they are not active until installed into workspace `.craft/plugins/<pluginId>`.

`DOTCRAFT_BUILTIN_PLUGIN_ROOTS` is a platform path-list. Each entry may be a plugin container directory or a direct plugin root. Entries must be absolute; missing or invalid entries produce non-fatal plugin diagnostics. When the variable is absent or empty, AppServer exposes no uninstalled built-in catalog entries, but already-installed workspace plugins remain discoverable.

Official hosts provide the default DotCraft plugin registry through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`. Docker persists the effective Craft home separately from the Workspace so user-added marketplace configuration and materialized registry snapshots survive container replacement. Registry availability does not install plugins automatically; `plugin/install` remains the only operation that copies a selected catalog plugin into the Workspace.

Installed built-ins carry a `.builtin` marker:

- `plugin/install` copies the selected desktop-bundled source directory into `.craft/plugins/<pluginId>` and enables the plugin by default.
- `.builtin` stores a fingerprint of the source directory. Directories with `.builtin` are owned by DotCraft and can be refreshed or removed by DotCraft lifecycle operations.
- Directories without `.builtin` are treated as user-owned and are not overwritten or removed by DotCraft.

`plugin/remove` removes an installed workspace plugin directory under `.craft/plugins/<pluginId>` when that directory is controlled by the current workspace plugin manager. Managed built-ins and registry-installed plugins carry `.builtin` so DotCraft can refresh them and can distinguish them from user-owned local plugins, but workspace-local user plugins may also be removed explicitly through `plugin/remove`. Removing a plugin is distinct from disabling it: removed built-ins and registry plugins are absent from runtime discovery but remain visible in the installable catalog when their source is configured, while disabled installed plugins remain on disk and can be re-enabled.

Registry catalog entries are source paths inside a registry snapshot. `plugin/install` validates the marketplace entry, validates the target plugin manifest id, then copies the registry plugin directory into `.craft/plugins/<pluginId>` with a managed marker. DotCraft never executes code directly from a registry URL; Desktop loads only the locally installed extension bundle. The public registry process for these curated source entries is defined in [Plugin Registry](plugin-registry.md).

---

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

---

## 8. Configuration

The `Plugins` config section contains:

- `PluginRoots`: additional local plugin roots or plugin container directories. Relative paths resolve against the workspace root.
- `EnabledPlugins`: plugin ids explicitly enabled for the workspace.
- `DisabledPlugins`: plugin ids explicitly disabled for the workspace. Disabled entries override enabled/default entries.
- `PluginRegistries`: additional plugin marketplace sources. Each source declares its kind, source value, optional reference and sparse paths, and may override the marketplace path. Source kinds and the add/refresh/remove lifecycle are defined in [Plugin Registry](plugin-registry.md).
- `DisableDefaultPluginRegistry`: disables the host-provided default official plugin marketplace.

Marketplace sources are recorded in user-global configuration so one added source is available in every workspace. Plugin installation stays per workspace: installing a marketplace plugin copies it into that workspace's `.craft/plugins/<pluginId>`.

Installed built-in plugins and local manifest plugins are enabled by default unless disabled. Built-ins that are visible only through the catalog are installable but not enabled and do not contribute tools or skills to agent context.

Workspace-level MCP configuration continues to use `McpServers`. Plugin-bundled MCP servers are contributed by enabled plugins and merged into the effective MCP runtime configuration as read-only runtime entries. Desktop and other clients should show plugin MCP alongside workspace MCP in runtime settings, but edits and deletes apply only to workspace-origin entries.

---

## 9. Protocol Boundaries

- AppServer JSON-RPC methods and capability negotiation are defined in [AppServer Protocol](../protocols/appserver-protocol.md).
- Session item payloads are defined in [Session Core](session-core.md).
- External channel adapter handshake, delivery, and `ext/channel/*` requests are defined in [External Channel Adapter](../protocols/external-channel-adapter.md).
- Desktop user-facing module workflows are defined in [Desktop Client](../clients/desktop-client.md).
