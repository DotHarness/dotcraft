# Desktop Extensions

| Field | Value |
| --- | --- |
| Version | 0.14.0 |
| Status | Living |
| Date | 2026-11-22 |
| Parent Specs | [Plugin Architecture](plugin-architecture.md), [Tool Architecture](tools-architecture.md) |

## Overview

Desktop extensions are fully trusted, client-local TypeScript/React modules owned by a DotCraft Plugin. They run in the Desktop renderer itself and may improve, extend, wrap, or replace the UI that loaded them. Bundled and user-installed plugins use the same manifest, SDK, React runtime, activation path, and revision lifecycle.

The Desktop extension runtime has four kernel primitives:

- `effect` owns setup and cleanup;
- `ui.add`, `ui.replace`, and `ui.wrap` compose named surfaces;
- `services.provide` and `services.use` share renderer-local contracts;
- `events.on` and `events.emit` provide renderer-local notifications.

Seven contribution families provide convenience APIs for common integrations. They are built on the runtime, not a closed list of what a Desktop extension may change.

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

When an `app.background` contribution requests backdrop presentation, the window frame becomes transparent and the Host composites one glass layer over the media for the menu bar, the shared sidebar/main panel body, and the welcome surface. A plugin does not add its own translucent sidebar or main layer over that body.

Backdrop presentation is a glass request: a blur radius from 0 to 64, an opacity from 0 to 1, and an optional tint as a raw hex, `rgb()`, or `hsl()` color. The Host clamps the numbers and ignores a tint that is not a raw color. Without a tint the glass takes the app's own base surface color.

The Host keeps text readable over any backdrop, so a request is a preference rather than a guarantee:

- A tint whose lightness does not suit the applied appearance, such as a pale tint in dark mode, is dropped and the glass covers at least 88% of the backdrop, because Host text is drawn for the applied appearance.
- When the system asks for reduced transparency or increased contrast, in forced colors, and in print, the glass is opaque.
- Without GPU compositing the glass does not blur and covers at least 70%.
- Menus, dialogs, popovers, and approval prompts keep their own opaque surfaces.

## Package contract

A top-level plugin owns at most one Desktop module, sharing its id, version, enabled state and
interface metadata under [Plugin Architecture](plugin-architecture.md). Managed dependencies do
not order Desktop activation. The inline `desktop` declaration owns executable Desktop code;
output, chunks, and assets stay under `./desktop/dist/`. Free-form capability labels grant no
renderer authority. Bundled plugins carry the DotCraft release version in their manifests.

Imported assets resolve against the importing module's installed source and revision, including
split chunks; CSS URLs remain stylesheet-relative. An import with the `?raw` suffix bundles the file's
text as a string, so long text such as model instructions can live in its own file. Bundles use the Desktop-owned React/JSX runtime
and do not ship another React implementation. The Host UI kit and published theme tokens are
supported authoring contracts; private renderer components and properties are not.

### UI kit

Settings groups accept `framed` to retain their empty-state presentation. Toasts accept a description and subject icon in addition to their message and action.

`ToolDisclosure` provides the Host's transcript disclosure, including static non-expandable rows, keyboard interaction, and panel transitions. `RunningShimmer` provides the Host's running-label motion and reduced-motion handling. Plugin tool cards compose these primitives rather than reproducing transcript chrome.

`LayerBoundary` registers a modal layer for its mounted lifetime. It accepts `active` and `blocksNativeViews`, so plugin-owned theater and modal content suppress lower hover overlays and hide Electron native views while preserving tooltip nesting.

Value controls use `onValueChange` and `ariaLabel`; boolean controls use `onChange`. These SDK
names are stable independently of internal component props.

`Menu` opens a Host menu from a plugin-rendered trigger passed as its child. Its `items` are
`{ label, onSelect, icon?, disabled?, danger?, checked? }`, `{ type: "separator" }`, and
`{ type: "label", label }`. The Host owns placement, keyboard navigation, dismissal, and layering.

A plugin main view that browses or manages a collection uses the Host catalog frame:

- `CatalogPage` takes `{ title, tabs?, breadcrumb?, actions?, search?, filters?, children }` and renders the top band with `actions`, a pinned header with `title` and a search row, and a scrolling body centred on the catalog column. `search` is `{ value, placeholder, onValueChange }`. `filters` is `{ ariaLabel, groups }`, each group `{ label, icon?, value, options, onValueChange }` with options `{ value, label }`; the first option is the unfiltered choice. `tabs` is `{ value, items, onValueChange }` with items `{ value, label }`. It fills the band's navigation slot with the same top-bar tabs Desktop uses to switch Plugins and Skills. `breadcrumb` is `{ parentLabel, currentLabel, onBack, trail? }`, with `trail` segments `{ label, onClick }` for the pages between, outermost first. It takes the navigation slot instead of `tabs` for a page reached from another, like Plugins › Manage. `titleEditor` and `descriptionEditor`, each `{ value, placeholder, ariaLabel, onValueChange, invalid?, readOnly? }`, make the hero title and a one-line subtitle under it editable in place, frameless, for a page that edits one record. `meta` holds controls centred under them, such as the record's tags.
- `CatalogSection` titles one body group with `{ title, actions?, children }`. `actions` sit at the end of the title row and appear while the pointer is over it, focus is inside them, or their menu is open.
- `CatalogToolbarButton` is an icon-only band action, `{ icon, label, onClick }`, whose label is also its tooltip.
- `SplitButton` is a principal action with alternatives: `{ label, onClick, items, menuLabel, icon?, variant?, disabled?, ariaLabel? }`, where `variant` is `primary` or `secondary` and `items` follow `Menu` items.

Rows and tables inside a group remain plugin code built from theme tokens.

`SummarySection` (`{ title, actions?, children }`) and `SummaryRow` (`{ leading, label, meta?, title?, onClick? }`) render a Summary section and its rows with Core's presentation. A row with `onClick` is a button.

`SidebarRow` (`{ icon, label, active?, threadId?, onClick }`) renders one navigation row of a space sidebar with Core's sidebar row presentation. A row with `threadId` shows that thread's status in the same slot and vocabulary as a thread row: the waiting badge, then the running indicator, then the unread dot.

`MascotOutfit` (`{ pack, wear, children }`) dresses the Composer character it wraps in a `composer.mascot` wrapper. The person's own mascot wears the `wear` items in place of its own for those slots and keeps its palette, other slots, expressions, and motion. `pack` is an avatar extension pack holding the plugin's items ([Avatar System §5.1](../features/avatar-system.md#51-extension-packs-registries-and-outfit-presets)).

`PersonalMascot` (`{ size?, greeting? }`) renders the person's own mascot as the Composer shows it, with motion. `greeting` plays a wave. Inside `MascotOutfit` it wears the outfit.

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
immediately. Returning `DesktopPluginActivation` provides the seven contribution arrays and an
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
| `composer.before` | Additive content immediately before the Composer body. The Composer mascot stands on top of it while it has height. Empty by default. |
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
| `composer.status.workspace` | The context row: project, work location, branch, worktree, changelist, and contributed execution-location controls. |
| `composer.status.workspace.run-on` | An empty execution-location seat in the workspace row. Its dedicated context carries thread and workspace identity, busy state, visibility eligibility, and coordinated menu state. |
| `composer.status.subscription` | The provider usage indicator (subscription windows or account balance) when applicable. |
| `composer.status.trailing` | The trailing end of the status row, reserved for compact persistent readouts. Empty by default. |
| `thread.header.actions` | The action group in the active thread header, between the overflow menu and the Detail Panel toggle. Empty by default. |
| `conversation.aside.leading` | A seat beside the leading edge of the conversation reading column. Empty by default. |
| `conversation.aside.trailing` | A seat beside the trailing edge of the conversation reading column, for panels that accompany the conversation. Core supplies the pinned Summary card. |
| `summary.sources` | Additive rows inside a thread's Summary Sources section, after Core's sources. Empty by default. |
| `summary.sections` | Additive sections inside a thread's Summary, after its sources and before its context and environment sections. Empty by default. |
| `conversation.empty` | The conversation area of an open thread that has no messages yet, behind the Composer. Empty by default. Receives the thread surface context and `cover()`, which hides the thread header and Composer until its returned dispose runs, so the contribution has the whole conversation panel. |
| `conversation.turn.status` | A seat inside the last visible turn, for status that belongs to the conversation's current work: above the reply's actions once the turn has a reply, otherwise directly above its working line. Empty by default. |
| `settings.<section>` | Each built-in Settings section (`general`, `import`, `profile`, `appearance`, `archived-threads`, `voice`, `personalization`, `pet`, `usage`, `model-providers`, `browser`, `computer-use`, `connections`, `mcp`, `hooks`, `source-control`, `worktrees`). Additions append groups with the Settings section spacing, and wrappers can swap in a Settings subpage using the context `{ section, title }`. |

`app.background`, `app.overlay`, and `app.status` share the application context. The overlay paints over the shell without consuming the single `app` wrapper. Overlay, status, and aside seats are click-through (`pointer-events: none`, inherited); a contribution opts back in on its own interactive elements.

`app.status` holds compact, persistent status readouts. The Host owns its inset, ordering, spacing, and coexistence with Core indicators; contributions render before the trailing Core indicator and never position themselves against the viewport. Passive readouts stay click-through. `app.overlay` is for decorative or independently positioned content.

Composer surface contexts use `threadId: null` until the Composer attaches to a real Session thread, including welcome and detached embedded Composers.

`provider` names the model provider the Composer samples with as `{ id, backendId }`, or is `null` while it is unknown. `backendId` identifies a plugin model backend and is `null` for built-in providers, so a backend plugin can recognize its own provider, for example to replace `composer.status.subscription`.

Every internal Composer surface stays mounted while its Composer region exists, even when its Core default does not apply to the current state; a plugin uses the shared Composer context to decide whether its content applies. `add` renders after the current default or replacement; content that must precede a Core control uses `wrap`. Replacing an internal control removes that Core behavior, and the plugin receives no private control callbacks.

`composer.mascot` adds the resolved mascot activity, expression, semantic light, base size, submit revision, reasoning effort, speed, and reduced-motion preference to the Composer context. Replacing it swaps only the visual character, which continues to ride Core's positioning and outer motion; additions act as overlays or accessories. A wrapper that only changes what the person's mascot wears uses `MascotOutfit`. `submitRevision` increments for each submit, even when state does not otherwise change. Plugin-specific occurrences use `events`. Replacing the complete `composer` surface is the way to own placement, bubbles, menus, or interaction. The error-screen mascot and Agent Profile avatars are unaffected.

On the new-chat Welcome screen, `composer` covers the complete pre-thread experience (app selector, hero, input, workspace footer, quick starts), which shares one draft and voice lifecycle and is replaced atomically.

The SDK's `PluginSurface` component declares and renders a plugin-owned surface from its `name` and typed `context`. Core or another plugin may target it with `add`, `replace`, or `wrap` regardless of activation order; registrations render whenever it is mounted. Plugin-qualified names are recommended but not enforced.

Core surface names are closed. The Host warns about unknown names rooted at `app`, `composer`, `thread`, `conversation`, `summary`, or `settings` but still stores the registration. Plugin-owned names remain open.

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

`conversation.turn.status` mounts with the same conditions in the last visible turn of a thread the user can act on, and moves to the next turn when one starts. Its context names the turn and its status, where a turn waiting on approval or input is still `running`:

```ts
interface DesktopPluginTurnStatusContext {
  readonly workspacePath: string | null
  readonly threadId: string
  readonly turnId: string
  readonly status: "running" | "completed" | "failed" | "cancelled"
  readonly settled: boolean
  readonly plan: DesktopPluginTurnPlan | null
  readonly runningTasks: DesktopPluginTurnRunningTasks
}

interface DesktopPluginTurnRunningTasks {
  readonly count: number
  open(): void
  claim(): DesktopPluginDispose
}

interface DesktopPluginTurnPlan {
  readonly title: string
  readonly todos: readonly DesktopPluginPlanTodo[]
  readonly implementsPlan: boolean
  readonly updatedTodos: boolean
}

interface DesktopPluginPlanTodo {
  readonly id: string
  readonly content: string
  readonly status: "pending" | "in_progress" | "completed" | "cancelled"
  readonly activeForm?: string
}
```

The plan is the thread's saved snapshot. `implementsPlan` identifies the execution turn started by accepting the Plan in Desktop. `updatedTodos` identifies a turn that successfully updated the checklist.

`settled` is true once the turn has ended and no Agent or Workflow it started is still running, so a contribution announces the user's task as done only when it is settled.

`runningTasks.count` is the thread's running background task count that the turn's working line shows: every running task while the turn runs, and only Agents and Workflows after it ends, and `open` shows the Summary that lists them. A contribution that shows the count itself calls `claim` and disposes the handle when it stops. While any claim is live, the working line omits the count.

The seat stays mounted after the turn ends, so a contribution decides from how the turn ended whether its status still applies. Content renders in the reading column with the transcript's spacing, and an empty contribution takes no space.

`summary.sections` mounts wherever the Summary shows a thread, pinned or as a popover, once the Summary has loaded, and receives the thread surface context. In a thread a plugin owns ([Owned threads](#owned-threads)), the Summary is headed by the owned thread's title and shows Core's Chats, background tasks, and context sections with contributed sections. Sources, environment, SubAgents, and plan describe project work, which an owned thread leaves to the chats it starts. A contribution builds its section from the UI kit's `SummarySection` and `SummaryRow`, so it matches Core's sections, and renders nothing when it has nothing to show. `summary.sources` mounts in the same places and renders `SummaryRow`s alone. The Sources section is hidden while neither Core nor any contribution has a row. Contributed rows count toward the section's row limit together with Core's, so past the limit they collapse behind the same View all row.

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
| Space | Adds one sidebar destination and a sidebar of its own while one of its locations is open. |

These kinds are not an authorization list. Their ordering, fallback, localization, and navigation remain Host-owned. Tool renderers change presentation only; MCP App presentation stays on its sandboxed path. Composer UI uses named surfaces rather than a separate contribution family.

A contribution label carries a `default` string and an optional `translations` map. The Host resolves it against the normalized app locale it reports through `environment`, normalizing translation keys too (`zh`, `zh-CN`, and `zh-Hans` are one entry). A key outside the supported locale set never matches; the label falls back to `default`.

A contribution may supply a React icon component; otherwise the Host renders a fallback glyph. There is no glyph-name vocabulary.

### Spaces

A space gathers a plugin's main views and the threads it owns behind one sidebar entry, so a product made of a conversation and a view reads as one place:

```ts
interface DesktopPluginSpaceContribution {
  readonly id: string
  readonly label: DesktopLocalizedText
  readonly icon?: DesktopPluginContributionIcon
  readonly order?: number
  readonly views: readonly string[]
  readonly sidebar: ComponentType<DesktopPluginSpaceSidebarProps>
  open(host: DesktopPluginHost): void | Promise<void>
}

interface DesktopPluginSpaceSidebarProps extends DesktopPluginViewProps {
  readonly location: { readonly kind: "view"; readonly viewId: string } | { readonly kind: "thread"; readonly threadId: string }
}
```

- A space's locations are the plugin's main views named in `views` and the threads the plugin owns with that space's `id`.
- A space's entry is a sidebar destination like a plugin main view, ordered with them by `order`. It is pinned by default, and the person can unpin it to More and pin it again. Selecting the entry returns to the view last open in the space. Otherwise it calls `open`, which navigates with the ordinary Host operations, so a plugin reaching an owned thread confirms it through `threads.ensureOwned` each time.
- A main view named in a space is reached only through it: it is not a sidebar destination, a More item, or a pin, and `openMainView` on it opens the space.
- While the active location belongs to a space, the Host replaces the sidebar with a Back row followed by the contribution's `sidebar`. The Host owns the frame, the Back row, and the sidebar's width and collapse. The contribution renders its rows with `SidebarRow`.
- On entering a space, the Host records the location it left, including its workspace. Back returns there, switching workspace when an owned thread's workspace replaced it. Moving between the space's own locations keeps the record, including while a workspace switch is in progress. Opening a location outside the space leaves the space without returning, and the record is dropped. Archiving the open owned thread leaves the space as Back does.

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

Every Desktop extension receives the same Host contract: the four primitives plus stable product operations for metadata, locale and theme, session state, SubAgent state, navigation, open targets, project actions, notifications, AppServer, MCP tool calls, App Binding, App Surfaces, workspaces, owned threads and linked chats, Oratorio, satellites, thread routes, and screen sessions. It is a supported authoring API, not a security membrane. Main-process validation remains a service invariant rather than a plugin permission.

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

`workspaces.readImageAttachment(path)` returns an image attached to the owning workspace's
conversation as a data URL, reading through that workspace's local or remote connection. It reads
only its image attachment folders and rejects any other path, a non-image file, or an image over
10 MiB.

### Owned threads

A plugin may own threads that serve as its product's conversation. `threads.ensureOwned` returns the plugin's thread for a key, creating it in the default Chat workspace the first time:

```ts
interface DesktopPluginOwnedThreadRequest {
  readonly key: string
  readonly space?: string
  readonly title: string
  readonly config: DesktopPluginOwnedThreadConfig
}

interface DesktopPluginOwnedThreadConfig {
  readonly developerInstructions: string
  readonly approvalPolicy?: "prompt" | "autoApprove"
}

interface DesktopPluginOwnedThread {
  readonly threadId: string
  readonly workspacePath: string
}
```

- Desktop records each owned thread per plugin and key in its per-user state. A later call returns the recorded thread while it exists, and creates a new one when it was deleted. `threads.findOwned(key)` returns the recorded thread without creating one, or `null`, so a plugin can recognize its thread before the person opens it.
- The thread starts in its own chat folder with Desktop's thread tools and standalone-chat instructions, like any new chat, followed by the plugin's instructions and approval policy. When a later call's configuration differs from the one last applied, Desktop updates the thread's configuration before returning, so a plugin update reaches its existing thread.
- `space` attaches the thread to one of the plugin's spaces, so opening it opens that space.
- Desktop leaves an owned thread out of its thread lists, search, and archived threads. The thread stays an ordinary AppServer thread, so other clients list it like any Chats thread.
- Disabling or uninstalling the plugin keeps its threads and their records, so re-enabling it returns the same conversations.

### Linked chats

`threads.linked(parentThreadId)` returns the chats Desktop follows for a thread ([Desktop client, Following chats](../clients/desktop-client.md#514-desktop-runtime-thread-tools)), newest first. `threads.onLinkedChange(parentThreadId, listener)` notifies with the complete list when a chat is linked or removed, or its title or state changes.

```ts
interface DesktopPluginLinkedChat {
  readonly threadId: string
  readonly projectId: string
  readonly projectName: string
  readonly workspacePath: string
  readonly title: string
  readonly state: "idle" | "working" | "waiting" | "done" | "failed" | "stopped"
  readonly createdAt: string
}
```

`state` is the product reading Desktop shows on the chat's card: `waiting` means the chat needs the person's approval, input, or plan confirmation.

### App Binding

`appBindings.enableThread(threadId, appId)` enables a connected app in one thread through Desktop's enable flow ([App Binding §4.1](../protocols/app-binding.md#41-one-click-enable)). It delivers the activation handoff when the app has to be woken and resolves once the binding is active. When activation fails it cancels the request and rejects.

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
- `startChat({ prompt, skill? })` opens a new chat in the current workspace with the prompt in the Composer, after a mention of the skill when it is installed. It sends nothing.
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

### Satellites and thread routing

`satellites` exposes the local machine's Hub fleet: list, invite, revoke, activity, read-only local sharing status, invitation ownership, and generation-owned events. Hub credentials stay in the main process. Fleet identity is independent of the foreground AppServer and invitation URLs remain in memory.

`remoteToolHost` projects the Host's thread-route controller with snapshots and subscriptions, listing, connect/disconnect, welcome selections, next-idle selections, and bounded restoration attempts. Its snapshot includes whether the AppServer is remote and supports routing. It uses the current connection and ignores results from earlier connections. The controller never imports plugin-owned fleet state. The Host applies a welcome selection before submitting the first turn, retaining the normal failure reporting and submission behavior.

The `composer.status.workspace.run-on` context carries its own thread and workspace identity, eligibility, and menu coordination. A plugin uses that context rather than assuming the foreground thread is its Composer. The seat remains available when no built-in workspace control applies.

Disposal withdraws the generation's subscriptions, uncommitted selections, and restoration attempts. It does not disconnect confirmed tool routes, revoke pairings, or terminate remote tasks. Route-change system notices remain Host-owned session facts.

### Screen sessions

`screenView` opens a generation-owned screen handle, tunes demand, receives state and frame events, acknowledges decoded frames, and closes the handle. The Host binds a view to its window and keeps Hub credentials and transport in the main process. Disposing a generation closes its screen handles and subscriptions without affecting tool sessions. The Host exposes only small layout preferences for the viewer, not a general settings or filesystem API.

### Tool presentation data

A tool renderer receives its exact `presentationId`, options, arguments, result, live/completed status, success, structured error fields, and an optional discriminated progress snapshot. Progress updates replace the snapshot delivered to the component. Renderers do not subscribe to a second progress stream or acquire execution authority. A missing or failed renderer falls back to the Host's ordinary tool presentation. A renderer that renders nothing leaves no row, so a plugin that presents a tool elsewhere can omit its transcript row.

## .NET and AppServer boundary

Pure UI stays in the Desktop extension; Core does not mirror surfaces, renderer services, or renderer events into C#. A .NET extension or AppServer contract is needed only for backend execution, durable host-owned state, Agent tools or hooks, other clients, or cross-process coordination. A bundle may ship both modules, but neither requires the other. Renderer composition does not alter Agent prompts, tools, or backend authority.
