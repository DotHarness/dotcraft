# Desktop Plugins

| Field | Value |
| --- | --- |
| Version | 1.1 |
| Status | Accepted |
| Date | 2026-08-28 |
| Parent Specs | `specs/architecture/plugin-architecture.md`, `specs/architecture/tools-architecture.md`, `specs/clients/desktop-client.md` |

## Overview

Desktop Plugins are fully trusted, client-local TypeScript/React modules owned by a DotCraft Plugin. They run in the Desktop renderer itself and may improve, extend, wrap, or replace the UI that loaded them. Bundled and user-installed plugins use the same manifest, SDK, React runtime, activation path, and revision lifecycle.

The Desktop Plugin runtime has four kernel primitives:

- `effect` owns setup and cleanup;
- `ui.add`, `ui.replace`, and `ui.wrap` compose named surfaces;
- `services.provide` and `services.use` share renderer-local contracts;
- `events.on` and `events.emit` provide renderer-local notifications.

Six contribution families provide convenience APIs for common integrations. They are built on the runtime, not a closed list of what a Desktop Plugin may change.

## Trust and runtime model

Desktop Plugin code executes in the same renderer realm as DotCraft. Installation and enablement are the trust decision. The architecture does not introduce a permission system, JavaScript sandbox, process boundary, or separate Extension Host for Desktop Plugins.

Plugins may use the public SDK, the renderer DOM, browser APIs available in the realm, global CSS, and the existing preload API. Direct DOM access and selectors against DotCraft-owned markup are allowed, but only the public SDK is a compatibility contract. DotCraft may change internal elements, class names, stores, and component structure without preserving a plugin that depends on them. This distinction is about compatibility, not access control.

MCP Apps remain a separate sandboxed path for untrusted interactive tool content. They are not Desktop Plugins and do not participate in this renderer runtime.

## Goals

- Make the renderer a self-improving runtime rather than a fixed set of approved contribution points.
- Let plugins compose Core UI and other plugins through named surfaces.
- Keep bundled and user-installed plugins on one trust, loading, and lifecycle path.
- Own and withdraw each revision's effects, UI, services, events, and convenience contributions as one generation.
- Keep pure renderer extensibility independent from .NET and AppServer protocol design.

## Non-goals

- Permissions, capability grants, sandboxing, or source-based trust tiers for Desktop Plugins.
- A separate extension process, iframe host, worker host, or Extension Host.
- File watching, hot module replacement, or partial updates inside an active revision.
- A stable contract for DotCraft's private DOM, CSS selectors, stores, route unions, or feature components.
- Plugin-provided Electron main-process or preload entry points.
- Mirroring renderer-only features into .NET or AppServer, or loading executable Desktop code from a remote AppServer.

## Package contract

A top-level DotCraft Plugin owns at most one Desktop Plugin. The Desktop Plugin shares the parent plugin id, version, enabled state, and `interface` metadata. A parent that also contains .NET may declare dependencies, but those coordinate managed generations and do not order Desktop activation.

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

The inline `desktop` field is the sole declaration of executable Desktop code. The manifest's free-form `capabilities` labels neither restrict nor expand renderer access.

The official build preset rewrites React and JSX-runtime imports to a bundled proxy that resolves the Desktop-owned React runtime before plugin evaluation. The output contains no bare React import and no second React implementation.

The SDK exposes Host-owned UI primitives through that runtime handle. Plugin roots inherit public theme tokens. Internal components may still be reached through ordinary renderer techniques, but they are not SDK exports and carry no compatibility guarantee.

## Activation contract

The entry module exports `activate(host)`. Activation may be synchronous or asynchronous and may return nothing:

```ts
export function activate(host: DesktopPluginHost):
  | void
  | DesktopPluginActivation
  | Promise<void | DesktopPluginActivation>
```

Calling the kernel primitives during activation registers work directly into the new generation. Returning `DesktopPluginActivation` provides the six contribution arrays and an optional `dispose()` callback. A plugin may use either form or combine them.

Every effect, UI registration, service provider, event listener, returned contribution, style, and Host-owned subscription created during activation belongs to the same generation. Kernel calls take effect immediately. If activation later fails, the runtime disposes everything already registered by that generation.

## Kernel primitives

### Effects

`effect(setup)` runs renderer-local setup and may return cleanup. Use it for timers, observers, browser listeners, subscriptions, and imperative integrations without a natural React owner. Registrations from the other primitives are already generation-owned and need no duplicate effect cleanup.

### UI composition

Every public UI insertion point is a named surface. `ui.add`, `ui.replace`, and `ui.wrap` take a surface name and a React component:

- `add` keeps all active registrations. The surface decides where its additive content appears.
- `replace` selects the last active registration. Disposing it restores the previous replacement, or the surface's default content when none remains.
- `wrap` composes around the current surface. A later registration is the outer wrapper. Disposing any wrapper recomposes the remaining chain without it.

While a replacement is active, the replaced default component tree is not mounted. Disposing the replacement remounts the current fallback rather than revealing a hidden, still-running implementation.

“Later” means actual registration order, including across plugins. The same rules apply whether the target surface belongs to Core or another plugin. Registration handles may be disposed early; otherwise the generation disposes them automatically.

UI composition does not imply ownership of backend behavior. Replacing `composer`, for example, replaces its renderer surface but does not silently create a Session API, tool, or AppServer method.

### Services

`services.provide` publishes a renderer-local service, and `services.use` returns a synchronous snapshot of the last active provider. Desktop modules may activate concurrently, so consumers resolve a service at the point of use and handle `undefined`; manifest dependencies do not make a Desktop provider ready first. Removing a provider reveals the previous provider, or makes the service unavailable when none remains. A service needed by CLI, remote clients, another process, or a required ordered dependency belongs in .NET or an AppServer protocol.

### Events

`events.on` subscribes to a renderer-local event, and `events.emit` publishes one. Use events for occurrence notifications that do not need a service reference. Listeners are generation-owned. Events do not persist Session data or become AppServer notifications.

## Surface contract

A surface provides a stable name, typed render context, default content when applicable, and the three composition modes. Its name is the compatibility boundary; the DOM produced inside the surface is not.

The formal Core surfaces are:

| Surface | Placement and default |
| --- | --- |
| `app` | The complete rendered Desktop application. Core supplies the default app tree. |
| `app.background` | An empty decorative seat behind the application shell. Core's normal background remains inside `app`. |
| `composer` | The complete mounted Composer, including new-chat welcome, pre-thread embedded, and active-thread states. Core supplies the normal implementation. |
| `composer.mascot` | The Composer mascot's 58 × 58 logical-pixel visual stage. Core supplies the DotCraft robot and keeps ownership of placement, interaction, bubbles, menus, and outer motion. |
| `composer.before` | Additive content immediately before the Composer body. Empty by default. |
| `composer.after` | Additive content immediately after the Composer shell. Empty by default. |
| `composer.input` | The complete attachment and rich-input region. Core supplies the normal input implementation. |
| `composer.input.attachments` | The attachment strip inside the input region. Core supplies the current image and file attachments. |
| `composer.input.editor` | The rich editor and its anchored popovers. Core supplies the normal editor. |
| `composer.toolbar` | The complete control row inside the Composer card. Core supplies the leading and trailing control groups. |
| `composer.toolbar.leading` | The leading control group. Core supplies command, permission, mode, goal, and voice-status controls when applicable. |
| `composer.toolbar.trailing` | The trailing control group. Core supplies context usage, model, voice, and submit controls when applicable. |
| `composer.toolbar.commands` | The command picker control. |
| `composer.toolbar.permissions` | The approval-policy control. |
| `composer.toolbar.mode` | The active profile or plan-mode control. |
| `composer.toolbar.goal` | The active goal control. |
| `composer.toolbar.context-usage` | The context-window usage control. |
| `composer.toolbar.model` | The provider and model control. |
| `composer.toolbar.voice` | The voice input control. |
| `composer.toolbar.submit` | The current submit, queue, stop, approval, or reply action. |
| `composer.status` | The status row below the Composer card. Core supplies the workspace row when applicable. |
| `composer.status.workspace` | The workspace, project, branch, worktree, or changelist controls. |
| `composer.status.subscription` | The ChatGPT subscription indicator when applicable. |

Composer surface contexts use `threadId: null` whenever the Composer has not created or attached to a real Session thread, including welcome and detached embedded Composers. They carry the real thread id after attachment.

Every internal Composer surface remains mounted while its Composer region exists, even when its Core default is not applicable to the current provider, compact mode, minimal chrome, or decision state. A plugin uses the shared Composer context to decide whether its own content applies. `add` renders after the current default or replacement. A plugin that needs content before a Core control uses `wrap`, renders its content first, and then renders `children`. Replacing an internal control removes that Core behavior; the plugin owns any replacement behavior and receives no private control callbacks.

`composer.mascot` inherits the Composer context and adds the resolved mascot activity, expression, semantic light, base size, submit revision, reasoning effort, speed, context-window mode, and reduced-motion preference. Replacing it swaps only the visual character, so an image, SVG, canvas, Lottie player, or React component continues to ride Core's Composer positioning and outer motion. Additions share the same visual stage and act as overlays or accessories. The context exposes product state rather than private CSS classes or the default robot's internal idle vocabulary.

Stable mascot activity is a snapshot delivered through React context. `submitRevision` increments for repeated submit occurrences that may not otherwise change state. Plugin-specific occurrences continue to use `events.on` and `events.emit`; Core does not duplicate every mascot state transition onto the renderer event bus. Replacing the complete `composer` surface remains the escape hatch for plugins that also want to own placement, bubbles, menus, or interaction behavior. The error-screen mascot and Agent Profile avatars are separate surfaces and are not affected by `composer.mascot`.

On the new-chat Welcome screen, `composer` deliberately covers the complete pre-thread composition experience, including its app selector, hero, input, workspace footer, and quick starts. Those elements share one draft and voice lifecycle and are replaced atomically.

These are the first stable surfaces, not a capability ceiling. The SDK's `PluginSurface` component declares and renders a plugin-owned surface from its `name` and typed `context`. Plugin-qualified names are recommended but not enforced. Core or another plugin may target it with `add`, `replace`, or `wrap`, regardless of activation order. It exists while mounted; registrations targeting it remain generation-owned and render whenever it is present.

## Convenience contributions

The existing contribution contract remains the recommended concise API for common product integrations:

| Contribution | Convenience behavior |
| --- | --- |
| Main view | Adds navigation, route, loading/error handling, and a full view. |
| Settings page | Adds Settings navigation and a page shell. |
| Conversation view | Adds a thread-scoped tab beside Chat. |
| Command | Adds command discovery, availability, argument handling, and invocation. |
| Tool renderer | Adds exact-presentation rendering with Core and generic fallbacks. |
| Message action | Adds an action to the standard assistant-message action area. |

These six kinds are convenience APIs, not a closed set or authorization list. Their ordering, fallback, localization, and navigation behavior remains host-owned. Tool renderers change presentation only; MCP App presentation remains on its sandboxed path. Composer UI uses `ui.add`, `ui.replace`, or `ui.wrap` against named surfaces instead of a separate contribution family.

## Runtime lifecycle

`PluginInfo.desktop` reports the manifest-relative entry and style declarations plus a content revision over that normalized declaration and the complete `./desktop/dist/` tree. The revision identifies executable content for cache busting, generation replacement, and remote matching. It remains independent from the .NET execution fingerprint.

For each installed and enabled plugin, Desktop authorizes its local root, loads its styles and revisioned module, opens a generation, and calls `activate` once. Kernel calls apply as they occur; any returned convenience activation is validated and registered afterward.

The whole content revision is the iteration and reload unit. Refreshing an already active revision is a no-op. Development requires rebuilding the revision and refreshing or re-enabling the plugin. This architecture does not provide a file watcher, HMR, component-level reload, or a partial-generation patch path.

Disable, uninstall, revision replacement, or Desktop shutdown disposes the complete generation. This withdraws components, styles, subscriptions, services, effects, and module routes and invokes an optional returned `dispose()` once. UI registries reveal the next replacement, remove additive entries, and recompose wrappers.

Invalidation withdraws Host-owned resources immediately. A new revision does not wait for an unfinished `activate()` or returned `dispose()` promise; a late activation result is stale and cannot publish.

Activation failure reports through existing Desktop logging and toast surfaces and disposes the failed generation, including registrations that were already visible.

With a local AppServer, Desktop resolves the installed plugin root. With a remote AppServer, Desktop loads only matching local packaged code identified by plugin id, version, and Desktop revision. Remote snapshots never provide executable code or filesystem paths.

## Host and compatibility contract

Every Desktop Plugin receives the same Host contract: the four primitives plus stable product operations for metadata, locale and theme, navigation, notifications, AppServer, App Binding, App Surfaces, workspaces, and Oratorio. It is a supported authoring API, not a security membrane. Private stores, routes, components, DOM, and CSS remain reachable to trusted code but are not compatibility contracts. Main-process validation remains a service invariant rather than a plugin permission.

## .NET and AppServer boundary

Pure UI stays in the Desktop Plugin; Core does not mirror surfaces, renderer services, or renderer events into C#. Add a .NET plugin or AppServer contract only for backend execution, durable host-owned state, Agent tools or hooks, other clients, or cross-process coordination. A bundle may ship both modules, but neither is required by the other. Renderer composition does not alter Agent prompts, tools, or backend authority.

## Acceptance criteria

- `effect`, UI composition, services, and events are sufficient to build higher-level plugin APIs.
- `add`, `replace`, and `wrap` follow the defined composition and disposal semantics.
- `app`, `app.background`, and the documented outer, region, and control-level Composer names are stable Core surfaces.
- `composer.mascot` replacements receive typed semantic state while Core retains the mascot controller and outer motion.
- `PluginSurface` enables a plugin to expose a surface that another plugin can extend.
- The six convenience contribution families continue to work without limiting kernel use; Composer UI uses surfaces directly.
- `activate` may return no value, and all registrations still belong to one revision generation.
- A disposed revision leaves no component, wrapper, replacement, service, listener, effect, stylesheet, route, or stale generation behind.
- Development reloads whole revisions; no watcher, HMR, or partial-generation mechanism is implied.
- Pure Desktop UI requires no parallel .NET or AppServer API.
