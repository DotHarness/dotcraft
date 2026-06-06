# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 1.3.0 |
| **Status** | Living |
| **Date** | 2026-05-19 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Tool Result Presentation](../protocols/tool-result-presentation.md), [Session Core](../core/session-core.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills, local plugin manifests, plugin-bundled MCP servers, client-facing plugin metadata, and the TypeScript external channel module contract.

---

## 1. Architecture Overview

DotCraft plugins are host-integrated capability bundles. They distribute skills, MCP server declarations, App Binding descriptors, and optional client-facing metadata without requiring the agent pipeline to know each integration's implementation details.

The plugin contribution model is:

1. **Skills**: plugin-contained DotCraft-compatible `SKILL.md` directories.
2. **MCP Servers**: plugin-contained MCP server declarations loaded into DotCraft's MCP runtime.
3. **App Descriptors**: plugin-contained App Binding descriptors that make app connection and thread binding flows visible.
4. **Desktop Extensions**: optional trusted Desktop UI bundles that contribute client surfaces.
5. **Interface Metadata**: optional client-facing plugin metadata.

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
- `lspServers`
- `apps`
- `desktopExtensions`
- `paths`

Plugins must declare at least one supported contribution: a plugin-contained `skills` path, plugin-bundled MCP servers, App Binding descriptors, LSP server descriptors, Desktop extensions, or interface metadata. Skill-only, MCP-only, app-only, desktop-extension-only, and interface-only plugins are valid.

`mcpServers` is an optional manifest-relative path to a plugin-contained MCP configuration file. If omitted, DotCraft looks for `./.mcp.json` in the plugin root. The MCP file may use either `{ "mcpServers": { ... } }` or a direct server map. Plugin MCP config should use canonical DotCraft fields such as `arguments`, `environmentVariables`, and `headers`; for compatibility with common MCP config files, DotCraft also accepts `args`, `env`, and `httpHeaders` as read aliases. Plugin-bundled MCP servers use the same runtime as workspace `McpServers`; relative MCP `cwd` values resolve under the plugin root. At runtime, contributed server names are prefixed as `{pluginId}:{serverName}` to avoid collisions with workspace MCP servers and other plugins.

Effective MCP merge rules:

- Workspace `McpServers` are loaded first and remain editable workspace configuration.
- Enabled, installed plugin MCP servers are then added as read-only runtime entries with origin metadata (`kind=plugin`, `pluginId`, display name, and declared server name).
- If a plugin runtime name conflicts with a workspace server or a higher-priority plugin server, the plugin declaration is marked shadowed in plugin metadata and is not connected.
- `mcp/list` returns the effective runtime view. Workspace config writes (`mcp/upsert`, `mcp/remove`, and config persistence) never write plugin-origin servers into `.craft/config.json`.
- Plugin-bundled MCP startup is non-fatal. A missing command, bad endpoint, timeout, or protocol error is reported through MCP runtime status (`mcp/status/list` / `mcp/status/updated`) and diagnostics where applicable; it must not prevent plugin discovery, AppServer readiness, or Desktop connection. Agent tool materialization waits for the current effective MCP startup attempt to settle, so ready plugin MCP tools are available to new turns without making AppServer startup synchronous.

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
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review workflows and MCP tools.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill", "MCP"],
    "defaultPrompt": "Review this change.",
    "brandColor": "#2563EB"
  }
}
```

`interface` contains optional UI metadata for Desktop and other clients: display name, short and long descriptions, developer, category, capability tags, default prompt, brand color, icon/logo paths, and public website/privacy/terms links. Path fields inside `interface` use the same manifest-relative path rules. Tool-result-specific renderer contracts are not declared in `interface`; App Binding tools declare them in app descriptors as defined by [Tool Result Presentation](../protocols/tool-result-presentation.md).

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child directory can contain a DotCraft-compatible `SKILL.md`. Skills contributed by enabled plugins are available in `skills/list` with source `plugin` and include `pluginId` / `pluginDisplayName` attribution. Disabling the plugin removes its contributed skills from agent context and hides compatibility built-in copies owned by that plugin.

`apps` points to a plugin-contained App Binding descriptor document, for example `"./apps.json"`. Apps contributed by installed and enabled plugins become eligible for App Binding connection and thread binding. Catalog-visible built-in plugins may expose app metadata before installation, but connection and binding are blocked until the owning plugin is installed and enabled.

`desktopExtensions` points to a plugin-contained Desktop extension descriptor document, for example `"./desktop-extensions.json"`. Desktop extensions are trusted client UI bundles loaded only after the plugin is installed and enabled. Desktop extension v1 is not an untrusted JavaScript sandbox: extension code runs in the Desktop renderer as trusted plugin code. The descriptor is still the source of truth for host capabilities such as `requiredAppIds`, `connectOrigins`, and `surfaceWriteScopes`; any capability crossing from renderer to main, AppServer, shell, or local network must be enforced by Desktop from the verified plugin descriptor, not from renderer-supplied policy. The descriptor contains one or more ESM bundle entries and the Desktop surfaces they contribute:

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
      "requiredAppIds": [],
      "connectOrigins": []
    }
  ]
}
```

Desktop extension path fields use the same manifest-relative path rules as other plugin paths. The supported surface `type` values are `mainView`, `pluginDetail`, `detailPanel`, `composerAction`, `conversationRenderer`, and `settingsPanel`. Unknown surface types are diagnostics and are ignored by clients.

A `mainView` surface may declare an optional `icon`, a host-resolved named glyph for its sidebar nav entry. The client maps known names to built-in icons; an omitted or unrecognized name falls back to the generic extension icon. Extensions do not ship raster assets for nav entries.

Surface display text is localized by the extension, not the host catalog. `label` is the required base (English) string; an optional `localizedLabel` object carries per-locale overrides keyed by app locale (for example `"zh-Hans"`). The client resolves the active locale and falls back to `label` when a locale is absent, so extensions ship their own translations and unknown locales degrade gracefully.

`connectOrigins` declares the loopback origins a trusted Desktop extension may access through Desktop's extension network bridge. Origins must be absolute `http`, `https`, `ws`, or `wss` loopback origins without path, query, or fragment; dynamic local app ports may be declared with a wildcard port such as `http://127.0.0.1:*`. Desktop must reject renderer-initiated extension network requests whose target origin is not listed by the verified descriptor loaded by the main process. Renderer-supplied `connectOrigins` values are never an authorization source. By itself `connectOrigins` permits local presentation data transport (read), not app mutation authority; mutation over a declared origin is allowed only through the scoped write transport below.

The concrete surface endpoint (host and port) the extension talks to is discovered at runtime from the connected app's `publicMetadata.surfaceEndpoints`, not hard-coded. A native app that reopens on a new dynamic loopback port may refresh that endpoint without a new user grant via the App Binding connection metadata refresh (see [App Binding](../protocols/app-binding.md) §9.6), so a wildcard-port `connectOrigins` keeps working across app restarts.

### Extension surface write transport

The Desktop extension network bridge is read-only by default: it issues HTTP `GET` JSON requests to a declared `connectOrigins` target for presentation, exposed to the bundle as `host.network.getJson(url)`.

An extension may additionally issue scoped mutating requests (HTTP `POST` with a JSON body, exposed as `host.network.postJson(url, body)`) to an app's published loopback surface endpoint only when all of the following hold:

- the target origin is declared in the extension's `connectOrigins`;
- the extension descriptor declares a non-empty `surfaceWriteScopes` — the App Binding mutate scope ids (drawn from a required app's descriptor) the extension exercises over its surface endpoints (optional, defaults to empty = read-only).

Desktop must reject a renderer-initiated mutating extension request when the target origin is not declared in the verified descriptor or `surfaceWriteScopes` is empty. `surfaceWriteScopes` is the extension's declared write intent: it is surfaced when the plugin is installed and the app is connected, and it gates whether Desktop exposes `postJson` at all. Renderer host wrappers may reject calls early for user experience, but the main process is the enforcement point for descriptor-bound origins, app ids, and write intent. Per-request authorization is enforced by the app's loopback surface using the connection credential it issued, not re-checked by Desktop — App Binding scopes are granted per thread binding rather than per connection, so the surface endpoint (not Desktop) is the authority for an un-bound, workspace-level surface write. The extension should issue writes only while a required app is connected; Desktop does not prompt per write, because the user authorized the app connection and its published surface. The app's loopback surface must validate every request and may reject it. This is the explicit mutation grant anticipated by [App Binding](../protocols/app-binding.md) `publicMetadata.surfaceEndpoints`; agent-invoked and externally-visible writes still go through App Binding tools and app-owned approval.

DotCraft discovers plugin roots from:

1. Workspace-local root: `<workspace>/.craft/plugins`
2. Explicit roots in `Plugins.PluginRoots` order
3. User-global root: `<craft-home>/plugins`
4. Desktop-bundled built-in catalog roots from `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`

Explicit roots may point either to one plugin root containing `.craft-plugin/plugin.json` or to a container directory containing multiple plugin roots. Missing roots are skipped with diagnostics. Local manifest plugins are enabled by default; `Plugins.DisabledPlugins` disables a plugin even when it is discovered from a default or explicit root.

When multiple roots contain the same plugin id, higher-priority roots win and lower-priority duplicates are skipped with diagnostics. A workspace, explicit, or user-global plugin suppresses the bundled catalog entry with the same id.

---

## 3. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, `mcpServers`, `desktopExtensions`, `paths`, interface asset paths, and path fields inside Desktop extension descriptors.

---

## 4. Loading and Diagnostics

Plugin loading has three responsibilities:

1. The manifest parser reads `.craft-plugin/plugin.json`, validates supported fields, normalizes paths, and returns metadata plus diagnostics.
2. The discovery service scans roots, resolves duplicate plugin ids, applies plugin enablement config, and produces plugin records.
3. Enabled plugins contribute skill sources, plugin-bundled MCP server declarations, app descriptors, and Desktop extension descriptors to the workspace runtime/client metadata.

Diagnostics are non-fatal and available to logs and UI surfaces. They cover invalid JSON, missing fields, missing supported plugin capabilities, invalid ids, invalid manifest-relative paths, unsupported legacy native tool fields, duplicate plugin ids, disabled plugins, invalid MCP declarations, and missing roots.

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

The long-term Chrome automation runtime contract is defined in [Chrome Browser Runtime](../runtime/chrome-browser-runtime.md). Plugin architecture owns contribution and installation semantics; Chrome Browser Runtime owns browser session lifecycle, tab ownership, command timeout, diagnostics, and runtime migration goals.

### Oratorio Built-In Plugin

DotCraft ships Oratorio as the built-in plugin `oratorio`. It contributes:

- The `oratorio` skill, loaded from the plugin's `skills` directory. It describes the Oratorio board model and how agents should use Oratorio app-bound tools in user conversations.
- The Oratorio App Binding descriptor, loaded from the plugin's `apps.json`.
- Client-facing metadata for Desktop and plugin-management views.

Installing the plugin does not install or launch the native Oratorio Desktop application. The Oratorio App Binding descriptor declares the native app requirement and OS protocol handoff; Desktop surfaces native app installation, connection, and thread binding as separate steps.

### External Channel Tools

External channel tools are runtime-declared by channel adapters during AppServer `initialize`. Static plugin manifests are not required for external-channel runtime tools. Execution continues to use the `ext/channel/toolCall` server-to-client request defined by [External Channel Adapter](../protocols/external-channel-adapter.md).

External channel tool invocations may still be projected as Session Core `pluginFunctionCall` items for wire compatibility. This projection is a runtime adapter detail, not a plugin manifest native-tool capability.

### Runtime Dynamic Tools

Runtime Dynamic Tools are declared by AppServer clients on `thread/start.dynamicTools` or rebound by `thread/resume.dynamicTools`, then invoked through the `item/tool/call` server-to-client request. They are bound to the current declaring connection and are suitable for client-owned, thread-scoped capabilities such as an external review runner submitting a draft back to its caller.

Runtime Dynamic Tools are not plugin manifest tools.

### MCP Tools

MCP tools are configured through workspace `McpServers`, per-thread `ThreadConfiguration.McpServers`, or plugin-bundled MCP declarations. They are discovered by the MCP runtime and injected through the MCP tool path.

---

## 6. Built-In Plugin Lifecycle

Built-in plugin manifests are desktop-bundled filesystem plugins exposed through a built-in catalog. Desktop bundles the source-of-truth plugin container under `resources/plugins/dotcraft-bundled/plugins` and launches AppServer with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` pointing at that container. Catalog entries are visible to clients before installation, but they are not active until installed into workspace `.craft/plugins/<pluginId>`.

`DOTCRAFT_BUILTIN_PLUGIN_ROOTS` is a platform path-list. Each entry may be a plugin container directory or a direct plugin root. Entries must be absolute; missing or invalid entries produce non-fatal plugin diagnostics. When the variable is absent or empty, AppServer exposes no uninstalled built-in catalog entries, but already-installed workspace plugins remain discoverable.

Installed built-ins carry a `.builtin` marker:

- `plugin/install` copies the selected desktop-bundled source directory into `.craft/plugins/<pluginId>` and enables the plugin by default.
- `.builtin` stores a fingerprint of the source directory. Directories with `.builtin` are owned by DotCraft and can be refreshed or removed by DotCraft lifecycle operations.
- Directories without `.builtin` are treated as user-owned and are not overwritten or removed by DotCraft.

`plugin/remove` deletes only managed built-in directories that still carry the `.builtin` marker. Removing a plugin is distinct from disabling it: removed built-ins are absent from runtime discovery but remain visible in the installable catalog when desktop-bundled roots are configured, while disabled installed plugins remain on disk and can be re-enabled.

Built-in catalog entries may also be remote release entries instead of bundled source directories. A remote entry supplies plugin metadata plus a GitHub Release ZIP URL, a fixed version, and a SHA-256 checksum. `plugin/install` downloads the ZIP, verifies the checksum, rejects path traversal or manifest-id mismatches, then installs the extracted plugin into `.craft/plugins/<pluginId>` with a managed marker. DotCraft never executes code directly from a remote URL; Desktop loads only the locally installed extension bundle.

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

Installed built-in plugins and local manifest plugins are enabled by default unless disabled. Built-ins that are visible only through the catalog are installable but not enabled and do not contribute tools or skills to agent context.

Workspace-level MCP configuration continues to use `McpServers`. Plugin-bundled MCP servers are contributed by enabled plugins and merged into the effective MCP runtime configuration as read-only runtime entries. Desktop and other clients should show plugin MCP alongside workspace MCP in runtime settings, but edits and deletes apply only to workspace-origin entries.

---

## 9. Protocol Boundaries

- AppServer JSON-RPC methods and capability negotiation are defined in [AppServer Protocol](../protocols/appserver-protocol.md).
- Session item payloads, including `pluginFunctionCall`, are defined in [Session Core](../core/session-core.md).
- External channel adapter handshake, delivery, and `ext/channel/*` requests are defined in [External Channel Adapter](../protocols/external-channel-adapter.md).
- Desktop user-facing module workflows are defined in [Desktop Client](../clients/desktop-client.md).
