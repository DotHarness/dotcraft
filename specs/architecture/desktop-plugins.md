# Desktop Plugins

| Field | Value |
| --- | --- |
| Version | 1.13 |
| Status | Accepted |
| Date | 2026-08-30 |
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
- Giving renderer code host filesystem paths or a general plugin storage API.
- Providing generated settings UI, secrets, revisions, or conflict resolution for plugin configuration in v1.

## Plugin settings

When the parent manifest declares `settings`, the Host API exposes `host.settings`:

```ts
const snapshot = await host.settings.get()

await host.settings.mutate("workspace", [
  { op: "set", key: "density", value: "compact" },
  { op: "unset", key: "accentOverride" },
])
```

`get()` returns the plugin's validated schema, personal and workspace values, effective value, and
the scopes writable through the current AppServer connection. `mutate(scope, operations)` accepts
only `personal` or `workspace`, targets the activated plugin id implicitly, and returns the new
snapshot. Operations are `{ op: "set", key, value }` and `{ op: "unset", key }`. The host validates
all operations and the resulting namespace before writing.

The API is an AppServer projection, not renderer-local storage. An activated generation reads its
initial snapshot explicitly and follows every later write through `onChange`. Configuration is for
small settings values. Images, databases, and caches remain backend-owned data and must not be
placed in `plugin-config.json`.

### Settings changes

`settings.onChange(listener)` subscribes to this plugin's configuration. Every notification carries a
complete snapshot, and the subscription is generation-owned like every other Host registration.
Four rules decide when it fires:

- **Once per change to the stored configuration.** A write through this AppServer broadcasts
  `workspace/configChanged` with the `plugins.config` region. That payload names no plugin and
  carries no value, so the Host re-reads. `PluginConfigStore.Mutate` returns `Get(manifest)`, so a
  mutate result and the post-broadcast read are byte-identical projections of one document.
- **A repeat is not an event.** A snapshot equal to the one last delivered publishes nothing, so
  the write's own result and the read its broadcast triggered collapse into a single delivery
  whichever order they arrive in. This is the cross-process form of what an in-process settings
  store gets from comparing the committed value against the previous one.
- **Only the newest read may publish.** Reads run concurrently and can land in any order. Each
  carries the issue number it was sent with, and a write bumps that number before publishing its own
  result, so a read of older state that arrives last is discarded rather than becoming the value
  listeners keep. Without this, two writes in quick succession — a settings slider is the ordinary
  case — leave a listener holding the older of the two snapshots.
- **Never on subscribe, and never for a rejected write.** Like `environment.onChange`, a subscriber
  that needs the current value calls `get()` once, as every sample already does. A rejected `mutate`
  leaves the file untouched, so there is no change to announce and the rejection alone reaches the
  caller.

The Host owns one watcher per plugin, not one per listener. A notification re-reads that plugin's
configuration exactly once and fans the result out, so three listeners inside one plugin cost one
read. The watcher starts with the first subscriber, reads a baseline so the first notification does
not publish an unchanged snapshot, and stops with the last subscriber.

Neither ordering makes a single write publish twice. A broadcast that arrives first issues a refresh
the revision guard drops, and that refresh read the same document the response publishes anyway, so
the echo check would have caught it too. A response that arrives first leaves the refresh to the
echo check alone. Nothing here needs a request cache, a queue, or a retry.

`onChange` covers writes made through this AppServer. A hand edit of `plugin-config.json` is not
observed, because Core runs no file watcher for plugin configuration.

Revisions, `expectedRevision`, and conflict errors stay out of scope: a plugin is the only writer of
its own namespace, so optimistic concurrency would add a failure mode to every call without removing
one. Secret redaction, path operations, and a separate per-scope event are also out of scope, and
Core itself reads no plugin's settings, so no Core surface subscribes to the `plugins.config` region.

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
    "description": "Adds review actions and result presentation to DotCraft Desktop.",
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

The parent plugin `version` is required in canonical `MAJOR.MINOR.PATCH` form. `description` is optional presentation metadata that describes the Desktop contribution rather than the parent plugin as a whole. `entry` and `styles` are manifest-relative paths inside `./desktop/dist/`. Runtime chunks and assets must also remain inside that output root.

The inline `desktop` field is the sole declaration of executable Desktop code. The manifest's free-form `capabilities` labels neither restrict nor expand renderer access.

### Bundled assets

An asset imported from plugin source evaluates to the absolute URL of the emitted file. Desktop serves a plugin from `dotcraft-plugin://<id>/<revision>/`, an address that depends on the installed revision and cannot be known while building, so the official preset resolves the emitted path against the importing bundle's own module URL rather than baking in a static public path. Placing the repair in the build keeps the imported value usable at module scope, where an asset URL is normally needed and no Host handle is in reach, and it repairs already-published plugins on their next rebuild without adding API surface.

The imported value is therefore used as it comes, and moving code between the entry bundle and a split chunk does not change it. A stylesheet keeps an ordinary relative `url()`, because a stylesheet already resolves against its own address.

The official build preset rewrites React and JSX-runtime imports to a bundled proxy that resolves the Desktop-owned React runtime before plugin evaluation. The output contains no bare React import and no second React implementation.

The SDK exposes Host-owned UI primitives through that runtime handle. Plugin roots inherit public theme tokens. Internal components may still be reached through ordinary renderer techniques, but they are not SDK exports and carry no compatibility guarantee.

### UI kit prop names

The kit's prop names are its own contract, not a mirror of the Core component behind each entry. A control that reports a chosen value — `Select`, `Combobox`, `SegmentedControl` — names its callback `onValueChange` and its accessible name `ariaLabel`. A boolean toggle — `Checkbox`, `PillSwitch` — names its callback `onChange`. Core components keep the prop names their own call sites use; the runtime adapts them where the two differ, so a Core rename never becomes a plugin break and the reverse.

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

`add` takes an optional `order`, defaulting to 100. Additions render by ascending order and then registration order. `replace` and `wrap` use registration order only, preserving their disposal stack.

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
| `app.overlay` | An empty seat in front of the application shell, click-through by default. |
| `app.status` | The Host-owned trailing status rail at the bottom-right of the window. Empty by default and click-through outside interactive contributions. |
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

`app.background`, `app.overlay`, and `app.status` share the application context. The overlay mounts after the application, so its content paints over the shell without a plugin having to consume the single `app` wrapper. The seat sets `pointer-events: none`, and the property inherits, so a floating readout stays click-through by default and a plugin that wants clicks opts back in with `pointer-events: auto` on its own element. That default keeps a decorative overlay from swallowing the interface underneath it, which is the failure a plugin cannot recover from once shipped.

`app.status` is for compact, persistent diagnostics and status readouts rather than freely positioned overlays. The Host owns its bottom-right inset, horizontal ordering, spacing, and coexistence with Core indicators. Contributions render before the trailing Core indicator and must not position themselves against the viewport. A contribution may opt back into pointer events for a real control, but passive telemetry remains click-through.

Composer surface contexts use `threadId: null` whenever the Composer has not created or attached to a real Session thread, including welcome and detached embedded Composers. They carry the real thread id after attachment.

Every internal Composer surface remains mounted while its Composer region exists, even when its Core default is not applicable to the current provider, compact mode, minimal chrome, or decision state. A plugin uses the shared Composer context to decide whether its own content applies. `add` renders after the current default or replacement. A plugin that needs content before a Core control uses `wrap`, renders its content first, and then renders `children`. Replacing an internal control removes that Core behavior; the plugin owns any replacement behavior and receives no private control callbacks.

`composer.mascot` inherits the Composer context and adds the resolved mascot activity, expression, semantic light, base size, submit revision, reasoning effort, speed, context-window mode, and reduced-motion preference. Replacing it swaps only the visual character, so an image, SVG, canvas, Lottie player, or React component continues to ride Core's Composer positioning and outer motion. Additions share the same visual stage and act as overlays or accessories. The context exposes product state rather than private CSS classes or the default robot's internal idle vocabulary.

Stable mascot activity is a snapshot delivered through React context. `submitRevision` increments for repeated submit occurrences that may not otherwise change state. Plugin-specific occurrences continue to use `events.on` and `events.emit`; Core does not duplicate every mascot state transition onto the renderer event bus. Replacing the complete `composer` surface remains the escape hatch for plugins that also want to own placement, bubbles, menus, or interaction behavior. The error-screen mascot and Agent Profile avatars are separate surfaces and are not affected by `composer.mascot`.

On the new-chat Welcome screen, `composer` deliberately covers the complete pre-thread composition experience, including its app selector, hero, input, workspace footer, and quick starts. Those elements share one draft and voice lifecycle and are replaced atomically.

These are the first stable surfaces, not a capability ceiling. The SDK's `PluginSurface` component declares and renders a plugin-owned surface from its `name` and typed `context`. Plugin-qualified names are recommended but not enforced. Core or another plugin may target it with `add`, `replace`, or `wrap`, regardless of activation order. It exists while mounted; registrations targeting it remain generation-owned and render whenever it is present.

The Core surface names above are closed. The Host warns about unknown names rooted at `app` or `composer` but still stores the registration. Plugin-owned names remain open because their surface may mount later.

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

### Localized labels

A contribution label carries a `default` string and an optional `translations` map. The Host resolves it against the same normalized app locale it reports through `environment`, and it matches a translation key by normalizing that key too, so `zh`, `zh-CN`, and `zh-Hans` are one entry rather than three misses. A key outside the supported set falls back to `default` instead of claiming the English entry, because normalization resolves an unknown tag to English and a Portuguese translation must not be served to English readers.

### Contribution icons

A contribution may supply a React icon component; otherwise the Host renders a fallback glyph. Core does not publish a separate glyph-name vocabulary.

## Runtime lifecycle

`PluginInfo.desktop` reports the optional contribution description, manifest-relative entry and style declarations, plus a content revision over the normalized executable declaration and complete `./desktop/dist/` tree. The revision identifies executable content for cache busting, generation replacement, and remote matching. Presentation-only description changes do not alter it. It remains independent from the .NET execution fingerprint.

For each installed and enabled plugin, Desktop authorizes its local root, loads its styles and revisioned module, opens a generation, and calls `activate` once. Kernel calls apply as they occur; any returned convenience activation is validated and registered afterward.

The whole content revision is the iteration and reload unit. Refreshing an already active revision is a no-op. Development requires rebuilding the revision and refreshing or re-enabling the plugin. This architecture does not provide a file watcher, HMR, component-level reload, or a partial-generation patch path.

Disable, uninstall, revision replacement, or Desktop shutdown disposes the complete generation. This withdraws components, styles, subscriptions, services, effects, and module routes and invokes an optional returned `dispose()` once. UI registries reveal the next replacement, remove additive entries, and recompose wrappers.

Invalidation withdraws Host-owned resources immediately. A new revision does not wait for an unfinished `activate()` or returned `dispose()` promise; a late activation result is stale and cannot publish.

Activation failure reports through existing Desktop logging and toast surfaces and disposes the failed generation, including registrations that were already visible.

With a local AppServer, Desktop resolves the installed plugin root. With a remote AppServer, Desktop loads only matching local packaged code identified by plugin id, version, and Desktop revision. Remote snapshots never provide executable code or filesystem paths.

## Host and compatibility contract

Every Desktop Plugin receives the same Host contract: the four primitives plus stable product operations for metadata, locale and theme, session state, navigation, notifications, AppServer, App Binding, App Surfaces, workspaces, and Oratorio. It is a supported authoring API, not a security membrane. Private stores, routes, components, DOM, and CSS remain reachable to trusted code but are not compatibility contracts. Main-process validation remains a service invariant rather than a plugin permission.

### Environment changes

`environment` reads the applied theme, its seed, and the UI locale, and `environment.onChange` notifies when any of them changes. Each notification carries a complete snapshot and fires only when a value differs from the one last delivered. The subscription is generation-owned like every other Host registration.

`environment.themeSeed` is the four values Desktop derives its palette from: `surface`, `ink`, `accent`, and a 0-100 `contrast`. It is on the snapshot because the theme name alone cannot express a recolor — a user changing the accent leaves `theme` at `dark`, and a plugin that cached a computed color would never re-read. `surface` is the base plane, which is the page in dark and the card in light, so both variants move away from it the same way. A plugin that only needs colors should read the published tokens rather than re-deriving the ramp from the seed.

The Host owns the observation. A plugin does not watch `documentElement` for the theme attribute or the language attribute, and how Core announces a change is an implementation detail of the runtime rather than part of this contract.

`environment.locale` is one of `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, or `de`. The Host normalizes browser tags such as `zh-CN` and `en-US` before publishing the typed SDK value.

### Session state

`session` reads the foreground workspace, the active thread, that thread's mode, and whether a turn
is busy. `session.onChange` notifies when any of the four changes, carries a complete snapshot, fires
only on an actual change, and is generation-owned like every other Host registration.

```ts
interface DesktopPluginSessionSnapshot {
  readonly workspacePath: string | null
  readonly threadId: string | null
  readonly mode: "agent" | "plan"
  readonly busy: boolean
}
```

`session.workspacePath` is the foreground workspace and matches the `active` entry from
`workspaces.listLocalProjects()`. A Composer surface reads its thread's workspace from
`context.workspacePath`, which may differ.

`busy` means a turn is running or waiting on user input. Approval state, Composer variant, and
minimal chrome remain on the Composer surface context; `workspaces` owns workspace lists and
switching.

### AppServer notifications

`appServer.onNotification` subscribes to one AppServer notification method by name, and the subscription is generation-owned like every other Host registration.

Desktop's preload bridge carries AppServer notifications on two channels: a raw channel and a typed channel restricted to the methods declared in the generated contracts. The raw channel sees every notification; the typed channel sees the typed subset. Each channel delivers a notification to each of its own subscribers exactly once. A plugin therefore observes every notification method, generated or not, and its subscriptions do not change what Core surfaces receive.

Bridged AppServer server *requests* do not follow this rule. Each carries a bridge identity that must be answered exactly once, so a bridged request reaches one responder, never both.

## .NET and AppServer boundary

Pure UI stays in the Desktop Plugin; Core does not mirror surfaces, renderer services, or renderer events into C#. Add a .NET plugin or AppServer contract only for backend execution, durable host-owned state, Agent tools or hooks, other clients, or cross-process coordination. A bundle may ship both modules, but neither is required by the other. Renderer composition does not alter Agent prompts, tools, or backend authority.

## Acceptance criteria

- `effect`, UI composition, services, and events are sufficient to build higher-level plugin APIs.
- `add`, `replace`, and `wrap` follow the defined composition and disposal semantics.
- `app`, `app.background`, `app.overlay`, `app.status`, and the documented outer, region, and control-level Composer names are stable Core surfaces.
- Additions render in `order` and then registration order, while `replace` and `wrap` stay last-registration-wins.
- `composer.mascot` replacements receive typed semantic state while Core retains the mascot controller and outer motion.
- `PluginSurface` enables a plugin to expose a surface that another plugin can extend.
- The six convenience contribution families continue to work without limiting kernel use; Composer UI uses surfaces directly.
- `activate` may return no value, and all registrations still belong to one revision generation.
- A disposed revision leaves no component, wrapper, replacement, service, listener, effect, stylesheet, route, or stale generation behind.
- `appServer.onNotification` receives every AppServer notification method, generated or not, exactly once per subscription.
- `environment.locale` and contribution-label resolution use app locales, so a `zh-CN` document language resolves a `zh-Hans` label.
- `settings.onChange` fires after every `mutate` settles, suppresses that write's own broadcast echo, re-reads once per plugin per notification, and never delivers a re-read that a later publish has superseded.
- `session` reports the foreground workspace, active thread, mode, and busy state with no Composer mounted, and `session.onChange` fires only when one of the four changes.
- Development reloads whole revisions; no watcher, HMR, or partial-generation mechanism is implied.
- Pure Desktop UI requires no parallel .NET or AppServer API.
