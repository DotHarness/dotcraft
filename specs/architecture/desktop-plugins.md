# Desktop Plugins

| Field | Value |
| --- | --- |
| Version | 1.0 |
| Status | Accepted |
| Date | 2026-08-27 |
| Parent Specs | `specs/architecture/plugin-architecture.md`, `specs/architecture/tools-architecture.md`, `specs/clients/desktop-client.md` |

## Overview

Desktop Plugins are trusted, client-local TypeScript/React modules owned by a DotCraft Plugin. Bundled and user-installed plugins use the same manifest, SDK, module loader, Host API, contribution registries, and lifecycle. The Desktop shell and its Core infrastructure remain compiled into the application; they are not a privileged plugin tier.

## Goals

- Make the Desktop renderer modular and lazily loaded without changing existing behavior.
- Let one DotCraft Plugin contribute local Desktop views, settings, actions, commands, and renderers.
- Publish one immutable contribution generation per enabled plugin revision.
- Use one runtime path for every bundled and user-installed Desktop Plugin.
- Keep public identities, ownership, lifecycle, and fallback behavior deterministic.
- Preserve the existing trusted-code boundary and the separate MCP Apps sandbox boundary.

## Non-goals

- A general slot tree, DOM patching API, or replacement of the app root, sidebar, conversation, composer, or settings shell.
- Runtime contribution mutation, file watching, hot module replacement, or partial generation updates.
- JavaScript sandboxing for trusted Desktop Plugin bundles.
- Per-plugin Host API permissions or a bundled-versus-user plugin security tier.
- Plugin-provided Electron main-process or preload entry points.
- New Session item kinds, AppServer methods, or Agent tool identities for presentation alone.
- Remote loading of Desktop code from an AppServer host.

## Package contract

A top-level DotCraft Plugin owns at most one Desktop Plugin. The Desktop Plugin shares the parent plugin id, version, dependencies, enabled state, and `interface` metadata.

The plugin manifest declares the Desktop Plugin inline:

```json
{
  "schemaVersion": 1,
  "id": "acme.review",
  "version": "1.0.0",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review tools and Desktop presentation."
  },
  "desktop": {
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

The parent plugin `version` is required in canonical `MAJOR.MINOR.PATCH` form. `entry` and `styles` are manifest-relative paths inside `./desktop/dist/`. Runtime chunks and assets must also remain inside that output root.

The inline `desktop` field is the sole declaration of executable Desktop capability. The manifest's free-form `capabilities` labels do not authorize or activate Desktop code.

The entry module has one export shape:

```ts
export function activate(host: DesktopPluginHost):
  | DesktopPluginActivation
  | Promise<DesktopPluginActivation>
```

`DesktopPluginActivation` contains the plugin's contribution arrays and an optional `dispose()` callback. Contribution ids are unique within the owning plugin. Duplicate ids, unknown contribution kinds, missing implementations, and invalid metadata fail activation.

The official build preset rewrites React and JSX-runtime imports to a bundled proxy that resolves the Desktop-owned React runtime from a stable host runtime handle before plugin evaluation. The output contains no bare React import and no second React implementation.

The Desktop Plugin SDK exposes Host-owned UI primitives through that same runtime handle, backed by the existing shared implementations. The public surface includes fields and actions (`Button`, `IconButton`, `Input`, `Textarea`, `Select`, `Checkbox`, `Spinner`, and `Skeleton`), the focused composition primitives required by bundled plugins (`ActionTooltip`, `Combobox`, `ModalHeader`, `PillSwitch`, `SettingsPanelShell`, `SettingsBreadcrumb`, `SettingsGroup`, and `SettingsRow`), and a narrow `InlineDiff` adapter. Plugin roots inherit the public theme tokens. Internal models and product-specific components are not SDK exports.

## Contribution contract

The public contribution set is closed and strongly typed:

| Contribution | Host-owned placement | Plugin-owned content |
| --- | --- | --- |
| Main view | Navigation entry, route, loading and error state | View component, stable id, localized label, icon, order |
| Settings page | Settings navigation and page shell | Page component, stable id, localized label, icon, order |
| Conversation view | Conversation tab strip, active tab and current-thread lifecycle | View component, stable id, localized label, icon, order |
| Command | Command discovery and invocation | Stable id, localized label, availability predicate, execute callback |
| Tool renderer | Tool card placement, status and fallback | Component selected by an exact presentation id and priority |
| Composer action | Fixed additive composer action area | Stable id, localized label, availability and action component |
| Message action | Fixed assistant-message action area | Stable id, localized label, availability and execute callback |

Contribution icons may be a public plugin React icon component or a string token resolved to a Host-owned glyph. Core does not reserve plugin-specific icon tokens.

Main views and settings pages are additive. Commands and actions cannot remove or replace Core entries. Composer actions cannot replace the editor, model controls, submission flow, approval controls, attachment handling, or command menu.

Conversation views are additive tabs inside the existing conversation shell. The host-owned Chat view is always present and cannot be replaced or hidden. A plugin view receives the current thread identity and its public host, owns only its tab body, and does not add Session item kinds or intercept transcript rendering.

If the active plugin-owned main view is withdrawn, Desktop replaces it with the existing Plugins/Skills view when that view is available and otherwise with Conversation. If an active plugin-owned settings page is withdrawn, Desktop replaces it with General settings. If an active plugin-owned conversation view is withdrawn, that thread returns to Chat. Back and forward navigation cannot restore a withdrawn route or tab.

A Tool renderer registers one exact presentation id and an optional numeric priority. Active plugin renderers for that key are ordered by ascending priority, then plugin id and contribution id using ordinal comparison; the first entry wins. This allows a plugin to replace an existing presentation without a bundled-plugin privilege or provenance ownership rule. When no plugin renderer claims the key, Desktop uses its optimized Core renderer and then the generic renderer. MCP App presentation remains on its existing sandboxed path. Renderer selection never grants tool execution authority.

## Runtime lifecycle

`PluginInfo.desktop` reports the manifest-relative entry and style declarations plus a content revision over that normalized declaration and the complete `./desktop/dist/` tree. The revision identifies module content for cache busting, generation replacement, and remote matching. It is independent from the .NET execution fingerprint, so changing only the Desktop declaration or `desktop/` tree does not invalidate an existing .NET trust grant. With a local AppServer, Desktop resolves the installed plugin root. With a remote AppServer, Desktop loads only matching local packaged code identified by plugin id, version, and Desktop revision; remote snapshots never provide executable code or filesystem paths.

For each installed and enabled local Desktop Plugin, whether bundled or user-installed, the same runtime:

1. authorizes the exact local plugin root and manifest declaration;
2. loads styles and a revisioned module URL;
3. calls `activate` once;
4. validates the complete activation;
5. publishes all contributions as one immutable generation.

Refreshing an already active Desktop revision is a no-op. Disable, uninstall, revision replacement, or Desktop shutdown withdraws the complete generation, unmounts its components, removes its styles and Host-owned subscriptions, removes its module-route entry, and invokes `dispose` once. JavaScript that has already executed cannot be revoked; installation and enablement are the trust decision. Re-enabling activates a new generation. Replacement activation failure leaves the new generation unavailable and reports through the existing Desktop logging and toast surfaces; there is no rollback.

Contribution order is stable: the host owns each public insertion point, then plugin entries are ordered by declared order, plugin id, and contribution id using ordinal comparison. Plugin styles follow the host stylesheet graph and are ordered by plugin id and declaration order. Activation timing cannot change visible order or stylesheet order.

## Host contract

Every Desktop Plugin receives the same Host contract. It exposes stable product operations rather than renderer implementation details:

- plugin id and read-only version metadata;
- current locale and theme;
- semantic navigation to a main view, settings page, or thread, plus generation-owned custom-URL listeners;
- host toast notifications;
- one generated-type-aware `request` method plus notification subscriptions over the Desktop's current AppServer connection;
- App Binding connection and native-open operations;
- App Surface JSON reads and writes through the existing main-process bearer proxy;
- read-only local project discovery;
- the existing Oratorio service operations and events;
- contribution-specific read-only context models.

The Host contract does not expose Zustand stores, product feature components, internal route unions, plugin root paths, raw configuration files, arbitrary filesystem access, or raw Electron IPC. A Host operation required by any plugin becomes part of this same typed contract, which is the only supported Desktop Plugin Host API.

Notification subscriptions reuse the Desktop's existing AppServer notification stream, are owned by the active generation, and are removed with it. They do not register new AppServer methods or alter Session persistence.

Custom-URL listeners receive absolute schemes other than HTTP, HTTPS, and `mailto`. Active listeners run in stable plugin order until one returns `true`; an unhandled custom URL is rejected and is never sent to the operating-system shell. HTTP, HTTPS, and `mailto` links continue through the existing AppServer validation and shell-opening path.

Desktop Plugin code runs in the trusted renderer realm and can observe the renderer's preload API. Installation and enablement are the trust decision; the Host contract defines the supported authoring surface rather than an access-control boundary. Every trusted Desktop Plugin receives the same Host contract and operation-level checks regardless of its distribution source. Main-process services apply their existing URL, protocol, bearer, expiry, path, size, and timeout validation to every caller. Untrusted interactive tool UI continues to use the MCP Apps sandbox and opaque handles.

## Unified plugin and Core architecture

The app shell owns window layout, route selection, global error boundaries, and cross-feature orchestration. Product features own their components, stores, tests, localization, and styles and are loaded through feature-level lazy boundaries.

Agent Teams, Oratorio, and every other plugin-owned Desktop feature declare a real `desktop` bundle and enter the same activation, registry, revision, generation, and teardown path as user-installed plugins. Product builds precompile, optimize, and code-split bundled plugin output, but delivery optimization does not create another API or lifecycle. Agent Builder is an always-available Core lazy feature because it composes the host-owned conversation, model, approval, voice, and thread workflows rather than contributing an isolated plugin surface.

Core shell modules remain compiled and lazily loaded by the application. Core tool rendering retains its existing strongly typed registry and optimized plans; exact-key plugin renderers compose ahead of that fallback. The detail-tab registry remains Core-owned, and built-in conversation item families remain a closed host union.

Plugin install and detail surfaces represent the inline Desktop Plugin using the top-level `interface` metadata.

The following behavior remains host-owned:

- route history, deep links, window modes, and native window integration;
- thread, Turn, Item, approval, and tool execution lifecycle;
- conversation ordering, grouping, streaming, generic fallback, and accessibility;
- composer draft, attachment, mention, command, model, permission, voice, and submission state;
- settings navigation, search, feature gates, locale, theme, and global CSS order;
- MCP Apps sandboxing and main/preload authority.

Moving source into a feature module must preserve the effective DOM, CSS cascade, keyboard behavior, loading states, and protocol traffic unless a separately reviewed product change says otherwise.

## Prompt-cache behavior

Desktop contributions are client presentation and do not alter Agent instructions, tools, tool ordering, or tool schemas. Publishing or withdrawing a Desktop registry generation does not change Agent prompt-cache inputs. Backend contributions owned by the same top-level plugin continue to follow their existing bundle, trust, snapshot, and prompt-cache rules.

## Acceptance criteria

- Existing Desktop navigation, local and remote threads, workspaces, worktrees, modes, and deep links behave identically after modularization.
- Every current conversation item state, grouping rule, streaming state, approval path, and generic fallback remains available.
- Composer, detail viewers, review, terminal, browser, artifacts, MCP Apps, automations, subagents, plugin management, OAuth, multi-window behavior, quick chat, shortcuts, tray, menus, downloads, notifications, updates, locales, themes, and accessibility retain their current behavior.
- Bundled plugin routes, Core feature routes, and heavy panels load from separate production chunks.
- Deterministic fixtures cover every public contribution; the user-facing sample demonstrates one coherent plugin without becoming an API catalog.
- Install, enable, no-op refresh, update, disable, uninstall, restart, and remote-host/local-code mismatch are covered by deterministic tests.
- Disabled, replaced, or uninstalled plugins leave no mounted component, registry entry, stylesheet, module-route entry, or stale module generation.
