# Desktop Extensions

| Field | Value |
| --- | --- |
| Version | 0.9.5 |
| Status | Living |
| Date | 2026-10-25 |
| Parent Specs | [Plugin Architecture](plugin-architecture.md), [Tool Architecture](tools-architecture.md) |

## Overview

Desktop extensions are fully trusted, client-local TypeScript/React modules owned by a DotCraft Plugin. They run in the Desktop renderer itself and may improve, extend, wrap, or replace the UI that loaded them. Bundled and user-installed plugins use the same manifest, SDK, React runtime, activation path, and revision lifecycle.

The Desktop extension runtime has four kernel primitives:

- `effect` owns setup and cleanup;
- `ui.add`, `ui.replace`, and `ui.wrap` compose named surfaces;
- `services.provide` and `services.use` share renderer-local contracts;
- `events.on` and `events.emit` provide renderer-local notifications.

Six contribution families provide convenience APIs for common integrations. They are built on the runtime, not a closed list of what a Desktop extension may change.

## Trust and runtime model

Desktop extension code executes in the same renderer realm as DotCraft. Installation and enablement are the trust decision. There is no permission system, JavaScript sandbox, process boundary, or separate extension host process for Desktop extensions.

Plugins may use the public SDK, the renderer DOM, browser APIs, global CSS, and the preload API. Only the public SDK is a compatibility contract; DotCraft may change internal elements, class names, stores, and component structure without preserving plugins that depend on them. This distinction is about compatibility, not access control.

MCP Apps remain a separate sandboxed path for untrusted interactive tool content and do not participate in this runtime.

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
snapshot. The host validates all operations and the resulting namespace before writing.

The API is an AppServer projection of [plugin configuration](plugin-architecture.md), not
renderer-local storage. Configuration holds small settings values; images, databases, and caches
remain backend-owned data.

### Settings changes

`settings.onChange(listener)` is generation-owned and delivers a complete snapshot only when stored
configuration changes through the connected AppServer. Subscribing and rejected writes emit nothing.
A mutation response and its AppServer notification are one change and are delivered once. An older
asynchronous read never replaces a newer published snapshot. External edits of `plugin-config.json`
are not observed. Plugin settings provide no revisions, conflict resolution, secrets, or filesystem
operations.

### Settings and appearance contract

A plugin settings page is built from the Host's settings shell, groups, and rows; the visual grammar is defined in [Desktop DESIGN.md](DESIGN.md#plugin-surfaces). The Host UI Kit is the settings control boundary: plugins use its shared controls instead of restyling native controls. A hidden native file input may still invoke the platform file picker when the visible trigger is a Host control.

- `host.ui.pickColor` opens Core's shared colour picker; the Host owns its portal, focus, validation, and localization.
- `host.ui.pickFolder({ title })` opens the platform folder dialog and resolves to the chosen absolute path on the Desktop machine, or `null` when cancelled; `title` is already localized.
- Continuous controls preview locally and persist on commit: `Slider` `onValueChange` tracks the live value and `onValueCommit` reports the final value once. Intermediate values are never sent through AppServer.

Global appearance is Host-owned. A plugin may contribute a theme-seed override or request backdrop presentation through `host.appearance`; it never writes Desktop's private CSS custom properties. Core Appearance preferences remain the base layer. Each generation owns at most one theme override and one backdrop contribution, removed when the generation is disposed.

When an `app.background` contribution requests backdrop presentation, the window frame becomes transparent and the Host composites one neutral layer over the media for the menu bar, the shared sidebar/main panel body, and the welcome surface. A plugin does not add its own translucent sidebar or main layer over that body.

## Package contract

A top-level plugin owns at most one Desktop module, sharing its id, version, enabled state and
interface metadata under [Plugin Architecture](plugin-architecture.md). Managed dependencies do
not order Desktop activation. The inline `desktop` declaration owns executable Desktop code;
output, chunks, and assets stay under `./desktop/dist/`. Free-form capability labels grant no
renderer authority. Bundled plugins carry the DotCraft release version in their manifests.

Imported assets resolve against the importing module's installed source and revision, including
split chunks; CSS URLs remain stylesheet-relative. Bundles use the Desktop-owned React/JSX runtime
and do not ship another React implementation. The Host UI kit and published theme tokens are
supported authoring contracts; private renderer components and properties are not.

### UI kit

Value controls use `onValueChange` and `ariaLabel`; boolean controls use `onChange`. These SDK
names are stable independently of internal component props.

`Menu` opens a Host menu from a plugin-rendered trigger passed as its child. Its `items` are
`{ label, onSelect, icon?, disabled?, danger?, checked? }`, `{ type: "separator" }`, and
`{ type: "label", label }`. The Host owns placement, keyboard navigation, dismissal, and layering.

A plugin main view that browses or manages a collection uses the Host catalog frame:

- `CatalogPage` takes `{ title, actions?, search?, filters?, children }` and renders the top band with `actions`, a pinned header with `title` and a search row, and a scrolling body centred on the catalog column. `search` is `{ value, placeholder, onValueChange }`. `filters` is `{ ariaLabel, groups }`, each group `{ label, icon?, value, options, onValueChange }` with options `{ value, label }`; the first option is the unfiltered choice. The band has no navigation slot for plugins.
- `CatalogSection` titles one body group with `{ title, children }`.
- `CatalogToolbarButton` is an icon-only band action, `{ icon, label, onClick }`, whose label is also its tooltip.
- `SplitButton` is a principal action with alternatives: `{ label, onClick, items, menuLabel, icon?, variant?, disabled?, ariaLabel? }`, where `variant` is `primary` or `secondary` and `items` follow `Menu` items.

Rows and tables inside a group remain plugin code built from theme tokens.

`AgentAvatar` (`{ name, size?, animated? }`) renders the avatar Core shows for an agent nickname,
so a plugin and Core draw the same character for the same agent. `UserAvatar` (`{ size? }`) renders
the Desktop user's Profile avatar with Core's fallbacks. `Markdown` (`{ content }`) renders with
Core's message renderer: links open externally, Mermaid fences stay code, and wide content scrolls
inside the block. Plugins use these instead of bundling their own avatar or Markdown packages.

## Activation contract

The entry module exports `activate(host)`. Activation may be synchronous or asynchronous and may return nothing:

```ts
export function activate(host: DesktopPluginHost):
  | void
  | DesktopPluginActivation
  | Promise<void | DesktopPluginActivation>
```

Kernel calls during activation register directly into the new generation and take effect
immediately. Returning `DesktopPluginActivation` provides the six contribution arrays and an
optional `dispose()` callback. A plugin may use either form or both.

Every effect, UI registration, service provider, event listener, returned contribution, style, and
Host-owned subscription created during activation belongs to that generation. If activation fails,
the runtime disposes everything the generation already registered.

## Kernel primitives

### Effects

`effect(setup)` runs renderer-local setup and may return cleanup, for timers, observers, listeners, and imperative integrations without a React owner. Registrations from the other primitives are already generation-owned.

### UI composition

Every public UI insertion point is a named surface. `ui.add`, `ui.replace`, and `ui.wrap` take a surface name and a React component:

- `add` keeps all active registrations, ordered by an optional `order` (default 100) and then registration order. The surface decides where additive content appears.
- `replace` selects the last active registration. Disposing it restores the previous replacement, or the default content. A replaced default is not mounted; disposal remounts the current fallback.
- `wrap` composes around the current surface; a later registration is the outer wrapper. Disposing any wrapper recomposes the remaining chain.

"Later" means actual registration order, including across plugins, whether the surface belongs to Core or another plugin. Handles may be disposed early; otherwise the generation disposes them.

UI composition does not imply ownership of backend behavior. Replacing `composer` replaces its renderer surface but creates no Session API, tool, or AppServer method.

### Services

`services.provide` publishes a renderer-local service, and `services.use` returns a synchronous snapshot of the last active provider. Desktop modules may activate concurrently, so consumers resolve at the point of use and handle `undefined`; manifest dependencies do not make a provider ready first. Removing a provider reveals the previous one. A service needed by CLI, remote clients, another process, or an ordered dependency belongs in .NET or an AppServer protocol.

### Events

`events.on` subscribes to a renderer-local event and `events.emit` publishes one, for occurrence notifications that need no service reference. Listeners are generation-owned. Events neither persist Session data nor become AppServer notifications.

## Surface contract

A surface provides a stable name, typed render context, default content when applicable, and the three composition modes. Its name is the compatibility boundary; the DOM inside it is not.

The Core surfaces are:

| Surface | Placement and default |
| --- | --- |
| `app` | The complete rendered Desktop application. Core supplies the default app tree. |
| `app.background` | An empty decorative seat behind the application shell. Core's normal background remains inside `app`. |
| `app.overlay` | An empty seat in front of the application shell, click-through by default. |
| `app.status` | The Host-owned trailing status rail at the bottom-right of the window. Empty by default and click-through outside interactive contributions. |
| `composer` | The complete mounted Composer, including new-chat welcome, pre-thread embedded, and active-thread states. |
| `composer.mascot` | The Composer mascot's visual stage. Core supplies the DotCraft robot and keeps placement, interaction, bubbles, menus, and outer motion. |
| `composer.before` | Additive content immediately before the Composer body. Empty by default. |
| `composer.after` | Additive content immediately after the Composer shell. Empty by default. |
| `composer.input` | The complete attachment and rich-input region. |
| `composer.input.attachments` | The attachment strip inside the input region. |
| `composer.input.editor` | The rich editor and its anchored popovers. |
| `composer.toolbar` | The complete control row inside the Composer card. |
| `composer.toolbar.leading` | The leading control group: command, permission, mode, goal, and voice-status controls when applicable. |
| `composer.toolbar.trailing` | The trailing control group: context usage, model, voice, and submit controls when applicable. |
| `composer.toolbar.commands` | The command picker control. |
| `composer.toolbar.permissions` | The approval-policy control. |
| `composer.toolbar.mode` | The active profile or plan-mode control. |
| `composer.toolbar.goal` | The active goal control. |
| `composer.toolbar.context-usage` | The context-window usage control. |
| `composer.toolbar.model` | The provider and model control. |
| `composer.toolbar.voice` | The voice input control. |
| `composer.toolbar.submit` | The current submit, queue, stop, approval, or reply action. |
| `composer.status` | The status row below the Composer card. |
| `composer.status.workspace` | The context row: project, Run on, work location, branch, worktree, or changelist controls. Its individual chips are not surfaces. |
| `composer.status.subscription` | The provider usage indicator (subscription windows or account balance) when applicable. |
| `composer.status.trailing` | The trailing end of the status row, reserved for compact persistent readouts. Empty by default. |
| `thread.header.actions` | The action group in the active thread header, between the overflow menu and the Detail Panel toggle. Empty by default. |
| `conversation.aside.leading` | A seat beside the leading edge of the conversation reading column. Empty by default. |
| `conversation.aside.trailing` | A seat beside the trailing edge of the conversation reading column, for panels that accompany the conversation. Core supplies the pinned Summary card. |

`app.background`, `app.overlay`, and `app.status` share the application context. The overlay paints over the shell without consuming the single `app` wrapper. Overlay, status, and aside seats are click-through (`pointer-events: none`, inherited); a contribution opts back in on its own interactive elements.

`app.status` holds compact, persistent status readouts. The Host owns its inset, ordering, spacing, and coexistence with Core indicators; contributions render before the trailing Core indicator and never position themselves against the viewport. Passive readouts stay click-through. `app.overlay` is for decorative or independently positioned content.

Composer surface contexts use `threadId: null` until the Composer attaches to a real Session thread, including welcome and detached embedded Composers.

`provider` names the model provider the Composer samples with as `{ id, backendId }`, or is `null` while it is unknown. `backendId` identifies a plugin model backend and is `null` for built-in providers, so a backend plugin can recognize its own provider, for example to replace `composer.status.subscription`.

Every internal Composer surface stays mounted while its Composer region exists, even when its Core default does not apply to the current state; a plugin uses the shared Composer context to decide whether its content applies. `add` renders after the current default or replacement; content that must precede a Core control uses `wrap`. Replacing an internal control removes that Core behavior, and the plugin receives no private control callbacks.

`composer.mascot` adds the resolved mascot activity, expression, semantic light, base size, submit revision, reasoning effort, speed, and reduced-motion preference to the Composer context. Replacing it swaps only the visual character, which continues to ride Core's positioning and outer motion; additions act as overlays or accessories. `submitRevision` increments for each submit, even when state does not otherwise change. Plugin-specific occurrences use `events`. Replacing the complete `composer` surface is the way to own placement, bubbles, menus, or interaction. The error-screen mascot and Agent Profile avatars are unaffected.

On the new-chat Welcome screen, `composer` covers the complete pre-thread experience (app selector, hero, input, workspace footer, quick starts), which shares one draft and voice lifecycle and is replaced atomically.

The SDK's `PluginSurface` component declares and renders a plugin-owned surface from its `name` and typed `context`. Core or another plugin may target it with `add`, `replace`, or `wrap` regardless of activation order; registrations render whenever it is mounted. Plugin-qualified names are recommended but not enforced.

Core surface names are closed. The Host warns about unknown names rooted at `app`, `composer`, `thread`, or `conversation` but still stores the registration. Plugin-owned names remain open.

### Thread and conversation surfaces

`thread.header.actions` and the two conversation asides mount only while a real Session thread shows its Chat view, not on the new-chat Welcome screen or while a conversation view contribution replaces the message stream. They share one thread context:

```ts
interface DesktopPluginThreadSurfaceContext {
  readonly workspacePath: string | null
  readonly threadId: string
  readonly busy: boolean
}
```

`thread.header.actions` holds compact controls for the current thread, each a single UI-kit icon button; the Host owns spacing, order, and header height.

The conversation asides add layout:

```ts
interface DesktopPluginConversationAsideContext extends DesktopPluginThreadSurfaceContext {
  readonly layout: "gutter" | "shift" | "overlay"
  readonly width: number
  pin(): DesktopPluginDispose
}
```

Each aside is a seat between the message stream edge and the reading column, spanning the visible stream height without scrolling with messages.

The Host owns layout, which depends only on the side space: half the difference between the stream width and the reading column width. During a shell panel transition it uses the target width, so contributions transition with the shell.

- `overlay`: side space under 180 logical pixels; a pin has no layout effect, and the contribution shows a compact or popover form instead of a panel.
- `shift`: 180 up to 400 logical pixels; while any trailing pin is live, the reading column, the Composer, and the controls docked above it move 153 logical pixels toward the leading edge.
- `gutter`: 400 logical pixels or more; the column stays centered.

A trailing contribution calls `pin` while it shows a panel and disposes the handle when it stops. `conversation.aside.leading` never pins. `width` is the seat's width: the side space plus the shift for the trailing seat, or minus it for the leading seat. When Core shows a rail in the outermost leading lane, the leading seat starts after it. Layout changes animate unless reduced motion is requested and never remount contributions.

## Convenience contributions

The convenience contribution contract is the concise API for common integrations:

| Contribution | Convenience behavior |
| --- | --- |
| Main view | Adds navigation, route, loading/error handling, and a full view. |
| Settings page | Adds Settings navigation and a page shell. |
| Conversation view | Adds a thread-scoped tab beside Chat. |
| Command | Adds command discovery, availability, argument handling, and invocation. |
| Tool renderer | Adds exact-presentation rendering with Core and generic fallbacks. |
| Message action | Adds an action to the standard assistant-message action area. |

These kinds are not an authorization list. Their ordering, fallback, localization, and navigation remain Host-owned. Tool renderers change presentation only; MCP App presentation stays on its sandboxed path. Composer UI uses named surfaces rather than a separate contribution family.

A contribution label carries a `default` string and an optional `translations` map. The Host resolves it against the normalized app locale it reports through `environment`, normalizing translation keys too (`zh`, `zh-CN`, and `zh-Hans` are one entry). A key outside the supported locale set never matches; the label falls back to `default`.

A contribution may supply a React icon component; otherwise the Host renders a fallback glyph. There is no glyph-name vocabulary.

## Runtime lifecycle

`PluginInfo.desktop` reports the optional description, manifest-relative entry and style declarations, and a content revision over the normalized executable declaration and complete `./desktop/dist/` tree. The revision identifies executable content for cache busting, generation replacement, and remote matching; description changes do not alter it, and it is independent from the .NET execution fingerprint.

For each installed and enabled plugin, Desktop authorizes its local root, loads its styles and revisioned module, opens a generation, and calls `activate` once. Kernel calls apply as they occur; a returned convenience activation is validated and registered afterward.

The whole content revision is the reload unit. Refreshing an already active revision is a no-op; development iterates by rebuilding the revision and refreshing or re-enabling the plugin. There is no file watcher, HMR, component-level reload, or partial-generation patch.

Disable, uninstall, revision replacement, or Desktop shutdown disposes the complete generation: components, styles, subscriptions, services, effects, and module routes are withdrawn and a returned `dispose()` runs once. UI registries reveal the next replacement, remove additive entries, and recompose wrappers. Invalidation withdraws Host-owned resources immediately; a new revision does not wait for an unfinished `activate()` or `dispose()`, and a late activation result cannot publish.

Activation failure is reported through Desktop logging and toast surfaces and disposes the failed generation, including registrations that were already visible.

Local Desktop generations and their immutable module routes survive local workspace switches. Installed, enabled plugins with the same id, resolved root, version, and content revision keep their instances, original icons, and usable navigation; the target workspace's complete catalog removes or replaces changed entries. Pending activations are invalidated at the switch, and failure to discover the target catalog withdraws retained generations. Host session state and AppServer operations follow the foreground workspace, and settings listeners refresh for the new connection. Remote switches retain their source-scoped teardown and authorization rules.

### Remote workspaces

With a local AppServer, Desktop resolves the installed plugin root. With a remote AppServer advertising `desktopPluginArtifacts`, Desktop downloads the installed plugin's Desktop output through `plugin/desktop/read`. The package stays installed only in the remote workspace, with its other contributions and configuration. The client cache holds only a minimal manifest and `desktop/dist`, never source, credentials, or other executables.

Desktop asks once before running downloaded code from a remote workspace. The remembered grant covers later installs and revisions from that source and workspace, independently of server-side .NET trust. Declining leaves the other remote contributions usable; the plugin page allows retrying, granting, or revoking local execution. Revocation and workspace switching invalidate pending loads and withdraw active contributions.

Authorization and caches are keyed by the stable remote connection identity (for SSH, host/stack identity rather than tunnel port) and the server-reported `plugin/list.workspacePath`. Downloads are bounded and staged; the client rejects unsafe archive paths and links and recomputes the revision before publishing. Module routes are source- and revision-scoped, and only a current, authorized target activates. Cache failures never fall back to another revision or a same-named local plugin.

Verified cache entries survive disconnects; superseded revisions and removed plugins are pruned after active routes release them. When the server does not advertise the artifact capability, Desktop does not activate its Desktop contributions and explains that the server must be updated; other remote contributions remain available. Desktop never substitutes locally packaged code for remote Desktop output.

## Host and compatibility contract

Every Desktop extension receives the same Host contract: the four primitives plus stable product operations for metadata, locale and theme, session state, SubAgent state, navigation, open targets, project actions, notifications, AppServer, MCP tool calls, App Binding, App Surfaces, workspaces, and Oratorio. It is a supported authoring API, not a security membrane. Main-process validation remains a service invariant rather than a plugin permission.

Every `onChange` subscription below is generation-owned, carries a complete snapshot, and fires only when a value actually changes.

### Environment changes

`environment` reads the applied theme, its seed, and the UI locale; `environment.onChange` notifies when any of them changes. The Host owns the observation; how Core announces a change is not part of the contract.

`environment.themeSeed` is the four values Desktop derives its palette from: `surface` (the base plane: the page in dark, the card in light), `ink`, `accent`, and a 0-100 `contrast`. It is part of the snapshot because a recolor can leave `theme` unchanged. A plugin that only needs colors reads the published tokens.

`environment.locale` is one of `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, or `de`; browser tags are normalized before publishing.

### Session state

`session` reads the foreground workspace, the active thread, that thread's mode, and whether a turn
is busy; `session.onChange` notifies when any of them changes.

```ts
interface DesktopPluginSessionSnapshot {
  readonly workspacePath: string | null
  readonly threadId: string | null
  readonly mode: "agent" | "plan"
  readonly busy: boolean
}
```

`session.workspacePath` matches the `active` entry from `workspaces.listLocalProjects()`; a Composer
surface's `context.workspacePath` may differ. `busy` means a turn is running or waiting on user
input. Approval state, Composer variant, and minimal chrome remain on the Composer surface context;
`workspaces` owns workspace lists and switching.

`workspaces.readImageAttachment(path)` returns an image the user attached to a local workspace's
conversation as a data URL. It reads only Desktop's image attachment folders and rejects any other
path, a non-image file, an oversized image, or a remote workspace.

### SubAgent state

`subagents.list(parentThreadId)` returns the SubAgent children Desktop tracks for a parent thread (those its Subagents tab lists). `subagents.onChange(parentThreadId, listener)` notifies when a listed child's identity or state changes.

```ts
interface DesktopPluginSubAgent {
  readonly parentThreadId: string
  readonly childThreadId: string
  readonly agentPath: string | null
  readonly nickname: string
  readonly state: "working" | "waiting" | "done" | "failed" | "cancelled"
}
```

`state` is a product reading, not the wire status: `waiting` means the child's active Turn needs approval or user input, and `done` covers a completed or closed child. `subagents.reveal(parentThreadId, childThreadId)` opens the parent's Subagents tab with that child selected.

### Navigation

`navigation` opens product destinations:

- `openExternal(url)` opens an `http` or `https` URL in the default browser and handles a `dotcraft:` link exactly as if the operating system had delivered it; other schemes are rejected. A plugin starts a chat with a prefilled message through `dotcraft://threads/new` ([Desktop client §5.2](../clients/desktop-client.md#52-start-a-new-conversation)).
- `openFile(path)` opens a workspace file in the Detail Panel file viewer.
- `openDetailPanel(tab)` opens the current thread's Detail Panel on `changes`, `plan`, or `subagents`.
- `openAutomation(automationId)` opens that automation in the Automations view.
- `openSkill(name)` opens that skill in the Skills view.
- `revealToolCall(threadId, callId)` opens that thread and reveals the tool item with that call id; a background terminal's `callId` names the `Exec` call that started it.
- `openMainView` is limited to the plugin's own main views.
- `openPath(path, { target?, line?, column? })` opens a local path in another app ([Desktop client §5.17](../clients/desktop-client.md#517-open-in-another-app)). `target` is an id from `listOpenTargets()`, such as `fileManager` or `custom:<id>`; without it the path's default target is used. It rejects on a remote workspace or an unavailable target.

### Project actions

`projectActions` reads and runs the actions a local workspace declares ([Desktop client
§5.18](../clients/desktop-client.md#518-project-actions)).

- `list(workspacePath)` returns the workspace's environments in discovery order — each with its
  `id`, `name`, file `path`, whether it is the default, an `error` when it needs attention, and its
  actions `{ id, name, icon, command }` for this platform, most recently run first — plus the
  selected environment id (`null` for No environment or when there are none).
- `onChange(workspacePath, listener)` delivers the same result when an environment file, the
  selection, or the run order changes.
- `select(workspacePath, environmentId | null)` changes the selected environment; `null` selects No
  environment.
- `run({ threadId, environmentId, actionId })` runs an action in the conversation's terminal, in
  the environment root inside the thread's execution workspace.
- `openTerminal({ threadId })` opens a terminal tab at the workspace root.
- `promptAddAction({ workspacePath, environmentId? })` opens the Host's add-action dialog. Saving
  makes the action the most recent one without running it. It resolves to `{ environmentId, action }`,
  or `null` when cancelled or when the action does not run on this platform.
- `openSettings({ workspacePath, environmentId? })` opens Settings › Worktrees › Environments at
  that project, on the environment when given; Back returns to the caller.

### Host commands

A plugin runs a program on the AppServer host through `appServer.request("command/exec", …)`
([AppServer protocol §25C](../protocols/appserver-protocol.md#25c-command-execution-methods)). On a
remote workspace the program runs on the remote machine.

### MCP tool calls

`mcp.callTool({ server, tool, arguments?, threadId? })` calls an MCP tool through
`mcpServer/tool/call` and returns its raw MCP result, through the server's normal authority,
approval, and hook pipeline. With `threadId`, it runs on that thread. Without it, the Host uses one
hidden ephemeral thread on the current AppServer connection, started on first use with
`approvalPolicy = deny`, shared by every plugin, and replaced after a reconnect or workspace switch.
A call never moves to another connection: it fails if the connection changes before dispatch, and
only a missing hidden thread on the same connection is recreated and retried once. An
approval-gated call on the hidden thread is rejected as its tool result; a plugin that needs the
user's approval passes the thread the user is looking at.

### AppServer notifications

`appServer.onNotification` is generation-owned and delivers each notification once per
subscription, including methods outside the generated typed catalog. Plugin subscriptions do not
change delivery to built-in surfaces. Bridged server requests have one responder and one response.

## .NET and AppServer boundary

Pure UI stays in the Desktop extension; Core does not mirror surfaces, renderer services, or renderer events into C#. A .NET extension or AppServer contract is needed only for backend execution, durable host-owned state, Agent tools or hooks, other clients, or cross-process coordination. A bundle may ship both modules, but neither requires the other. Renderer composition does not alter Agent prompts, tools, or backend authority.
