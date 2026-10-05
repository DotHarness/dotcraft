# Desktop Plugins

| Field | Value |
| --- | --- |
| Version | 0.7.10 |
| Status | Living |
| Date | 2026-10-02 |
| Parent Specs | [Plugin Architecture](plugin-architecture.md), [Tool Architecture](tools-architecture.md) |

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

`settings.onChange(listener)` is generation-owned and delivers a complete snapshot only when
stored configuration changes. Subscription itself and rejected writes emit nothing; callers obtain
the initial state with `get()`. A mutation response and its AppServer notification represent one
change and must not produce duplicate delivery. An older asynchronous read cannot replace a newer
published snapshot. One plugin's listeners share configuration observation.

Changes cover writes through the connected AppServer, not external edits of `plugin-config.json`.
Plugin settings do not provide revisions, conflict resolution, secrets, or filesystem operations.

### Settings and appearance contract

A plugin settings page is built from the Host's settings shell, groups, and rows. Ordinary controls — switches, selects, sliders — go in a row's control slot; previews, media pickers, theme choices, and other wide visual choices go in a block row or a flush group so a plugin never nests a decorative card inside a settings card. The visual grammar those pieces follow is defined in [Desktop DESIGN.md](DESIGN.md#plugin-surfaces).

The Host UI Kit is the settings control boundary. Plugins use its shared controls instead of restyling native selects, segmented controls, switches, or ranges. A hidden native file input may still invoke the platform file picker when the visible trigger is a Host control. Opaque colour choices use Core's shared picker, requested through `host.ui.pickColor`; a plugin never owns its portal, focus lifecycle, validation, or localization.

Continuous controls preview locally and persist when the interaction commits. `Slider` `onValueChange` tracks the pointer or keyboard value, while `onValueCommit` reports the final value once at the end of the gesture. Do not send each intermediate delta through AppServer or write it to configuration storage.

Global appearance is a Host-owned composition. A plugin may contribute a theme-seed override or request backdrop presentation through `host.appearance`; it must not write Desktop's private CSS custom properties. Core Appearance preferences remain the base layer. Each generation owns one theme override and one backdrop contribution, and Desktop removes both when that generation is disabled, reloaded, or disposed.

An `app.background` surface renders media only. When it also requests backdrop presentation, the window frame becomes transparent, and the menu bar, panel body, and welcome surface each composite one Host-owned neutral layer over the media. The panel body is shared by sidebar and main, so the main surface's rounded corner reveals the same neutral layer instead of raw media. A plugin must not place a second translucent sidebar or main layer over that body: compounded alpha makes one opacity setting mean different things across the shell.

Persistent window telemetry belongs in the Host-owned `app.status` rail, not in a freely positioned `app.overlay`. The rail keeps compact readouts and Core indicators on one alignment line and owns their spacing and window inset; contributions do not position themselves against the viewport. Use tabular numerals for changing measurements, omit unavailable facts instead of presenting false zeroes, and keep passive readouts click-through. `app.overlay` remains the surface for decorative or independently positioned content.

## Package contract

A top-level plugin owns at most one Desktop module, sharing its id, version, enabled state and
interface metadata under [Plugin Architecture](plugin-architecture.md). Managed dependencies do
not order Desktop activation. The inline `desktop` declaration owns executable Desktop code:
optional `description`, `entry`, and `styles`; executable output, chunks and assets remain under
manifest-relative `./desktop/dist/`. Free-form capability labels do not grant renderer authority.

### Bundled assets

Imported assets resolve against the importing module's installed source and revision, including
split chunks; CSS URLs remain stylesheet-relative. Bundles use the Desktop-owned React/JSX runtime
and do not ship another React implementation. The Host UI kit and published theme tokens are
supported authoring contracts; private renderer components and properties are not.

### UI kit prop names

Value controls use `onValueChange` and `ariaLabel`; boolean controls use `onChange`.
Host adapters preserve these SDK names independently of internal component prop names.

### Agent identity

`AgentAvatar` renders the avatar Core shows for an agent, using `{ name: string; size?: number; animated?: boolean }`. `name` is the agent nickname, the same value the Subagents tab uses, so a plugin and Core always draw the same character for the same agent. The Host supplies the component; a plugin does not bundle the avatar package to recreate it.

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
| `composer.status.workspace` | The context row: project, Run on, work location, branch, worktree, or changelist controls. The row is Core content; its individual chips are not surfaces. |
| `composer.status.subscription` | The ChatGPT subscription indicator when applicable. |
| `composer.status.trailing` | The trailing end of the status row, opposite the context row. Core contributes nothing; it is reserved for compact, persistent readouts. |
| `thread.header.actions` | The action group in the active thread header, between the overflow menu and the Detail Panel toggle. Empty by default. |
| `conversation.aside.leading` | A seat beside the leading edge of the conversation reading column, for transient or ambient content. Empty by default. |
| `conversation.aside.trailing` | A seat beside the trailing edge of the conversation reading column, for panels that accompany the conversation. Empty by default. |

`app.background`, `app.overlay`, and `app.status` share the application context. The overlay mounts after the application, so its content paints over the shell without a plugin having to consume the single `app` wrapper. The seat sets `pointer-events: none`, and the property inherits, so a floating readout stays click-through by default and a plugin that wants clicks opts back in with `pointer-events: auto` on its own element. That default keeps a decorative overlay from swallowing the interface underneath it, which is the failure a plugin cannot recover from once shipped.

`app.status` is for compact, persistent diagnostics and status readouts rather than freely positioned overlays. The Host owns its bottom-right inset, horizontal ordering, spacing, and coexistence with Core indicators. Contributions render before the trailing Core indicator and must not position themselves against the viewport. A contribution may opt back into pointer events for a real control, but passive telemetry remains click-through.

Composer surface contexts use `threadId: null` whenever the Composer has not created or attached to a real Session thread, including welcome and detached embedded Composers. They carry the real thread id after attachment.

Every internal Composer surface remains mounted while its Composer region exists, even when its Core default is not applicable to the current provider, compact mode, minimal chrome, or decision state. A plugin uses the shared Composer context to decide whether its own content applies. `add` renders after the current default or replacement. A plugin that needs content before a Core control uses `wrap`, renders its content first, and then renders `children`. Replacing an internal control removes that Core behavior; the plugin owns any replacement behavior and receives no private control callbacks.

`composer.mascot` inherits the Composer context and adds the resolved mascot activity, expression, semantic light, base size, submit revision, reasoning effort, speed, and reduced-motion preference. Replacing it swaps only the visual character, so an image, SVG, canvas, Lottie player, or React component continues to ride Core's Composer positioning and outer motion. Additions share the same visual stage and act as overlays or accessories. The context exposes product state rather than private CSS classes or the default robot's internal idle vocabulary.

Stable mascot activity is a snapshot delivered through React context. `submitRevision` increments for repeated submit occurrences that may not otherwise change state. Plugin-specific occurrences continue to use `events.on` and `events.emit`; Core does not duplicate every mascot state transition onto the renderer event bus. Replacing the complete `composer` surface remains the escape hatch for plugins that also want to own placement, bubbles, menus, or interaction behavior. The error-screen mascot and Agent Profile avatars are separate surfaces and are not affected by `composer.mascot`.

On the new-chat Welcome screen, `composer` deliberately covers the complete pre-thread composition experience, including its app selector, hero, input, workspace footer, and quick starts. Those elements share one draft and voice lifecycle and are replaced atomically.

These are the first stable surfaces, not a capability ceiling. The SDK's `PluginSurface` component declares and renders a plugin-owned surface from its `name` and typed `context`. Plugin-qualified names are recommended but not enforced. Core or another plugin may target it with `add`, `replace`, or `wrap`, regardless of activation order. It exists while mounted; registrations targeting it remain generation-owned and render whenever it is present.

### Thread and conversation surfaces

`thread.header.actions` and the two conversation asides mount only while a real Session thread shows its Chat view. They are absent on the new-chat Welcome screen and while a conversation view contribution replaces the message stream. They share one thread context:

```ts
interface DesktopPluginThreadSurfaceContext {
  readonly workspacePath: string | null
  readonly threadId: string
  readonly busy: boolean
}
```

`thread.header.actions` holds compact controls that act on the current thread. A contribution renders a single icon button from the UI kit; the Host owns spacing, order relative to Core controls, and the header height.

The conversation asides add layout to that context:

```ts
interface DesktopPluginConversationAsideContext extends DesktopPluginThreadSurfaceContext {
  readonly layout: "gutter" | "shift" | "overlay"
  readonly width: number
  pin(): DesktopPluginDispose
}
```

Each aside is a seat between the edge of the message stream and the reading column. It spans the visible stream height and does not scroll with messages. Like `app.overlay`, the seat sets `pointer-events: none`, and a contribution opts back in on its own interactive elements.

Layout belongs to the Host and depends only on width. The side space is half the difference between the stream width and the reading column width:

- `overlay`: the side space is under 180 logical pixels.
- `shift`: the side space is from 180 up to, but not including, 400 logical pixels.
- `gutter`: the side space is 400 logical pixels or more.

A trailing contribution calls `pin` while it shows a panel beside the conversation and disposes the handle when it stops. In `shift`, while any trailing pin is live, the reading column moves 153 logical pixels toward the leading edge. The Composer and the controls docked above it, the running turn's changes strip and the scroll-to-bottom button, move with it. In `gutter`, the column stays centered. In `overlay`, a pin has no effect on layout. A contribution keeps its pin and shows a compact or popover form instead of a panel.

`width` is the seat's current width in logical pixels. That is the side space, plus the shift for the trailing seat, or minus the shift for the leading seat. `conversation.aside.leading` never pins. While the [turn navigation rail](../features/turn-navigation.md) is shown, the rail keeps the outermost lane, and the leading seat starts after it. Layout changes, including the column shift, animate unless reduced motion is requested. They never remount contributions.

The Core surface names above are closed. The Host warns about unknown names rooted at `app`, `composer`, `thread`, or `conversation` but still stores the registration. Plugin-owned names remain open because their surface may mount later.

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

With a local AppServer, Desktop resolves the installed plugin root. With a remote AppServer advertising `desktopPluginArtifacts`, Desktop downloads the installed plugin's Desktop output through `plugin/desktop/read`. The package remains installed only in the remote workspace; its other contributions and configuration remain remote. The client cache contains only a minimal manifest and `desktop/dist`, never source, credentials, or managed/MCP executables outside that tree.

Desktop asks once before running downloaded code from a remote workspace. The remembered grant covers subsequent plugin installs and revisions from that source and workspace, independently of server-side .NET trust. Declining leaves remote contributions usable. The plugin page allows retrying, granting, or revoking local execution. Revocation and workspace switching invalidate pending loads and withdraw active contributions.

The main process keys authorization and caches by the stable remote connection identity and server-reported `plugin/list.workspacePath`. SSH identity uses host/stack identity rather than the tunnel port. Downloads use bounded chunks and staging; the client rejects unsafe archive paths and links and recomputes the existing Desktop revision before publishing. Module routes are source-scoped as well as revision-scoped. Only a current, authorized target may activate. Cache failures do not fall back to another revision or a same-named local plugin.

Verified cache entries survive disconnects. Superseded revisions and removed plugins are pruned after active routes release them. When the remote server does not advertise the artifact capability, Desktop does not activate its Desktop contributions and the Plugins page explains that the server must be updated. Other remote plugin contributions remain available. Desktop never substitutes locally packaged code for remote Desktop output. This adds no file watching, HMR, second installation, or Agent plugin-management API.

## Host and compatibility contract

Every Desktop Plugin receives the same Host contract: the four primitives plus stable product operations for metadata, locale and theme, session state, SubAgent state, navigation, notifications, AppServer, App Binding, App Surfaces, workspaces, and Oratorio. It is a supported authoring API, not a security membrane. Private stores, routes, components, DOM, and CSS remain reachable to trusted code but are not compatibility contracts. Main-process validation remains a service invariant rather than a plugin permission.

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

### SubAgent state

`subagents` reads the SubAgent children Desktop tracks for a parent thread. These are the children its Subagents tab lists. `subagents.list(parentThreadId)` returns a snapshot. `subagents.onChange(parentThreadId, listener)` notifies with a complete snapshot whenever a listed child's identity, state, or summary changes.

```ts
interface DesktopPluginSubAgent {
  readonly parentThreadId: string
  readonly childThreadId: string
  readonly agentPath: string | null
  readonly nickname: string
  readonly state: "working" | "waiting" | "done" | "failed" | "cancelled"
  readonly summary: string | null
}
```

`state` is a product reading, not the wire status. `waiting` means the child's active Turn needs approval or user input. `done` covers a completed or closed child. `summary` is a preview of the child's latest agent message, or `null` before Desktop has read one.


`subagents.reveal(parentThreadId, childThreadId)` opens the parent thread's Subagents tab with that child selected.

### Navigation

`navigation` opens product destinations. `openFile(path)` opens a workspace file in the Detail Panel file viewer. `openDetailPanel(tab)` opens the current thread's Detail Panel on `changes`, `plan`, or `subagents`. `openAutomation(automationId)` opens that automation in the Automations view. `openMainView` remains limited to the plugin's own main views.

### AppServer notifications

`appServer.onNotification` is generation-owned and delivers each notification once per
subscription, including methods outside the generated typed catalog. Plugin subscriptions do not
change delivery to built-in surfaces. Bridged server requests have one responder and one response.

## .NET and AppServer boundary

Pure UI stays in the Desktop Plugin; Core does not mirror surfaces, renderer services, or renderer events into C#. Add a .NET plugin or AppServer contract only for backend execution, durable host-owned state, Agent tools or hooks, other clients, or cross-process coordination. A bundle may ship both modules, but neither is required by the other. Renderer composition does not alter Agent prompts, tools, or backend authority.
