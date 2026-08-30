# Desktop Plugin API

This page is the API reference for trusted Desktop Plugins. To create and build your first plugin, start with [Build a Desktop Plugin](./desktop-plugins).

## Use the four kernel primitives

The runtime has four kernel primitives. Six contribution families build on them with concise shortcuts for common product integrations.

### Own side effects

Use `host.effect` for work that has setup and cleanup but no natural React owner:

```ts
host.effect(() => {
  const interval = window.setInterval(refreshBoard, 30_000);
  window.addEventListener("online", refreshBoard);

  return () => {
    window.clearInterval(interval);
    window.removeEventListener("online", refreshBoard);
  };
});
```

Effects, Host-owned subscriptions, and registrations made through the other primitives all belong to the active revision generation. Disable, uninstall, revision replacement, and Desktop shutdown clean them up together.

### Compose UI surfaces

Use the three `host.ui` operations according to the change you need:

| Operation | Composition rule |
|---|---|
| **`add`** | Keeps every active registration. The surface renders them together, in `order`. |
| **`replace`** | Uses the last active registration. Disposing it restores the previous replacement or Core default. |
| **`wrap`** | Wraps the current surface. A later wrapper is outside earlier wrappers. |

Every call returns a disposable registration and is also generation-owned. Disposing an `add` removes only that item. Disposing a `replace` reveals the next active replacement. Disposing a `wrap` rebuilds the remaining wrapper chain.

An active replacement does not mount the replaced default component tree. Disposing it remounts the current fallback instead of revealing a hidden, still-running implementation.

“Last” and “later” mean actual registration order, including registrations from different plugins.

Give `add` an `order` when the arrangement matters. Additions render in ascending `order`, and those
sharing one — including every addition that omits it, which defaults to 100 — keep registration order
among themselves:

```ts
host.ui.add("composer.status", ReviewStatus, { order: 50 });
```

`replace` and `wrap` stay ordered by registration alone. Their stacking is a disposal contract rather
than an arrangement, so an `order` there would let an early registration outrank a later one
permanently.

Use `wrap` when you need to preserve the current implementation while adding behavior or layout around it:

```tsx
import type { DesktopPluginSurfaceWrapperProps } from "@dotcraft/plugin";

function ReviewFrame({
  children,
}: DesktopPluginSurfaceWrapperProps<"composer">) {
  return <section className="acme-board-review-frame">{children}</section>;
}

host.ui.wrap("composer", ReviewFrame);
```

### Share services

Use renderer-local services when another plugin needs a callable contract rather than a visual surface:

```ts
interface BoardService {
  openCard(id: string): void;
}

host.services.provide<BoardService>("acme-board.board", {
  openCard: (id) => openBoardCard(id),
});

const board = host.services.use<BoardService>("acme-board.board");
board?.openCard("DC-42");
```

`use` returns a synchronous snapshot of the last active provider. Disposing that provider takes `use` back to the previous one. Desktop modules may activate concurrently, so resolve cross-plugin services when an interaction needs them and handle `undefined`. Manifest dependencies order .NET generations and do not make a Desktop provider activate first. Renderer services do not cross into .NET, CLI, remote clients, or AppServer automatically.

### Publish events

Use events for occurrence notifications that do not need a shared service reference:

```ts
host.events.on<{ cardId: string }>("acme-board.card-opened", ({ cardId }) => {
  console.log("Opened", cardId);
});

host.events.emit("acme-board.card-opened", { cardId: "DC-42" });
```

Event listeners are removed with their generation. Events are renderer-local and never write Session data or become AppServer notifications.

## Target Core surfaces

DotCraft's formal surfaces cover the application and Composer. Composer surfaces form a hierarchy, so you can target a complete region or one Core control:

| Surface | Placement |
|---|---|
| **`app`** | The complete rendered Desktop application. |
| **`app.background`** | A Host-owned decorative seat behind the application shell. Render background media here; use `host.appearance` to control how the shell composes over it. |
| **`app.overlay`** | An empty seat in front of the application shell, click-through by default. |
| **`app.status`** | The Host-owned bottom-right status rail for compact persistent diagnostics. The Host owns placement and spacing alongside Core indicators. |
| **`composer`** | The complete mounted Composer, including new-chat welcome, pre-thread embedded, and active-thread states. |
| **`composer.mascot`** | The 58 × 58 logical-pixel visual stage for the Composer mascot. |
| **`composer.before`** | Content immediately before the Composer body. |
| **`composer.after`** | Content immediately after the Composer shell. |
| **`composer.input`** | The complete attachment and rich-input region. |
| **`composer.toolbar`** | The complete control row inside the Composer card. |
| **`composer.toolbar.leading`** | The leading command, permission, mode, and goal group. |
| **`composer.toolbar.trailing`** | The trailing context, model, voice, and submit group. |
| **`composer.status`** | The workspace and subscription row below the Composer card. |

Target these Core controls when a region is too broad:

| Region | Control surfaces |
|---|---|
| **Input** | `composer.input.attachments`, `composer.input.editor` |
| **Leading toolbar** | `composer.toolbar.commands`, `composer.toolbar.permissions`, `composer.toolbar.mode`, `composer.toolbar.goal` |
| **Trailing toolbar** | `composer.toolbar.context-usage`, `composer.toolbar.model`, `composer.toolbar.voice`, `composer.toolbar.submit` |
| **Status** | `composer.status.workspace`, `composer.status.subscription` |

![Composer public surface hierarchy](/desktop-plugin-composer-surfaces.svg)

Core supplies the normal component as each surface's default content. `add` renders after that content. To render before it while keeping its behavior, use `wrap`. To remove it and own the behavior yourself, use `replace`:

```tsx
import { Button } from "@dotcraft/plugin";
import type {
  DesktopPluginSurfaceProps,
  DesktopPluginSurfaceWrapperProps,
} from "@dotcraft/plugin";

function BeforeModel({ children }: DesktopPluginSurfaceWrapperProps<"composer.toolbar.model">) {
  return (
    <>
      <Button size="sm">Review model</Button>
      {children}
    </>
  );
}

function SubscriptionStatus(_: DesktopPluginSurfaceProps<"composer.status.subscription">) {
  return <span>Review ready</span>;
}

host.ui.wrap("composer.toolbar.model", BeforeModel);
host.ui.add("composer.status.subscription", SubscriptionStatus);
```

Use `app.status` for a passive status readout that should coexist with DotCraft's own window indicators. Do not position an `app.status` contribution against the viewport. Use `app.overlay` for decorative or independently positioned content instead.

The same names mount in thread, Welcome, approval, and user-input Composers. A surface stays available when its Core default is hidden by the current provider, compact mode, minimal chrome, or decision state. Inspect the shared Composer context before rendering plugin content. A surface name and its typed context are public contracts. The DOM the surface generates is not.

The Core names listed above are the complete set. Register under `app` or `composer` with a name Core does not define and Desktop keeps the registration but writes a console warning naming it, because at that point it is almost always a typo. Names outside those two roots belong to plugins and are never checked: targeting a surface another plugin has not mounted yet is normal, and your component renders as soon as that surface appears.

Composer surface contexts have `threadId: null` whenever the Composer has not created or attached to a real Session thread, including welcome and detached embedded Composers. They carry the real thread id after attachment.

| Field | Meaning |
|---|---|
| `workspacePath` | Current workspace path, or `null` when unavailable. |
| `threadId` | Attached Session thread, or `null` before attachment. |
| `mode` | Current `agent` or `plan` mode. |
| `busy` | The Composer is running, waiting, or performing maintenance. |
| `awaitingApproval` | A host approval decision is active. |
| `variant` | `default` or an embedded Composer variant such as `agentBuilder`. |
| `minimalChrome` | Core has hidden nonessential controls for an embedded Composer. |

On the new-chat Welcome screen, `composer` covers the complete pre-thread composition experience: app selection, hero, input, workspace footer, and quick starts. Those elements share one draft and voice lifecycle, so replacing `composer` swaps them as a single unit.

### Replace the Composer mascot

Replace `composer.mascot` with an image, SVG, canvas, Lottie player, or React character. This inline SVG example is self-contained and builds without a separate asset:

```tsx
import type { DesktopPluginActivate, DesktopPluginSurfaceProps } from "@dotcraft/plugin";

function Mascot({ context }: DesktopPluginSurfaceProps<"composer.mascot">) {
  return (
    <svg
      viewBox="0 0 58 58"
      width={context.size}
      height={context.size}
      data-activity={context.activity}
      role="img"
      aria-label="Acme mascot"
    >
      <circle cx="29" cy="29" r="24" fill="var(--accent)" />
      <circle cx="21" cy="26" r="3" fill="currentColor" />
      <circle cx="37" cy="26" r="3" fill="currentColor" />
      <path d="M20 38 Q29 44 38 38" fill="none" stroke="currentColor" strokeWidth="3" />
    </svg>
  );
}

export const activate: DesktopPluginActivate = (host) => {
  host.ui.replace("composer.mascot", Mascot);
};
```

Core keeps the mascot's placement, bubble, menu, click handling, sleep timer, Composer handoff, and outer motion. The context inherits the normal Composer fields and adds `activity`, `expression`, `light`, `size`, `submitRevision`, `reasoningEffort`, `speed`, `contextMax`, and `reducedMotion`. React to snapshots directly; watch `submitRevision` when repeated submissions need a one-shot animation. Use `host.events` for plugin-defined occurrences.

`ui.add("composer.mascot", ...)` layers an accessory or effect over the same stage. Replace `composer` instead when the plugin also needs to own positioning or interaction behavior. `composer.mascot` does not change the Error Screen mascot or Agent Profile avatars.

## Expose a plugin surface

Render `PluginSurface` inside your component to expose a plugin-owned extension point:

```tsx
import { PluginSurface } from "@dotcraft/plugin";

declare module "@dotcraft/plugin" {
  interface DesktopPluginSurfaceContextMap {
    readonly "acme-board.card.footer": {
      readonly issueId: string;
    };
  }
}

function BoardCard() {
  return (
    <article className="acme-board-card">
      <h2>DC-42</h2>
      <PluginSurface name="acme-board.card.footer" context={{ issueId: "DC-42" }} />
    </article>
  );
}
```

Add the custom name to `DesktopPluginSurfaceContextMap` to type both the owner and every consumer. Provider and consumer packages should import one shared declaration module when they exchange a surface contract. Without declaration merging, an unknown surface receives `unknown` context. Another enabled plugin can target `acme-board.card.footer` with `ui.add`, `ui.replace`, or `ui.wrap`, and activation order does not matter. Plugin-qualified names are recommended but not enforced.

The surface exists while its component is mounted. Registrations remain owned by their registering revisions and render whenever the surface is present.

## Use convenience contributions

Return `DesktopPluginActivation` when one of the standard product integrations already matches the job:

| Field | Convenience behavior |
|---|---|
| **`mainViews`** | Adds navigation, routing, and a full view. |
| **`settingsPages`** | Adds a page to Desktop Settings. |
| **`conversationViews`** | Adds a thread-scoped tab beside Chat. |
| **`commands`** | Adds a searchable command with availability and execution. |
| **`toolRenderers`** | Renders an exact `presentationId` with Core and generic fallbacks. |
| **`messageActions`** | Adds an action to the standard assistant-message action area. |

These six fields are convenience APIs, not an allowlist or a capability ceiling. Add Composer UI with calls like `host.ui.add("composer.toolbar.leading", ...)`. When a feature does not fit the convenience fields, reach for surfaces, services, events, and effects directly.

The returned activation may also provide `dispose()`. Contribution ids must be unique within one activation. Put localized labels in `label.translations`, keyed by app locale. Desktop normalizes both sides of that lookup, so a `zh-CN` key still reaches a `zh-Hans` reader, while a key outside the seven locales falls back to `label.default`. Set `order` only where the convenience API defines ordered placement.

### Give a contribution an icon

`mainViews`, `settingsPages`, `conversationViews`, `commands`, and `messageActions` take an optional `icon`. Pass a component for anything specific to the plugin. It receives `size`, `strokeWidth`, `aria-hidden`, and `style`, and inherits the surrounding text color through `currentColor`:

```tsx
import type { DesktopPluginIconProps } from "@dotcraft/plugin";

function ReviewIcon({ size = 16, ...rest }: DesktopPluginIconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      {...rest}
    >
      <path d="M4 6h16M4 12h10M4 18h7" />
    </svg>
  );
}
```

A component is the only thing `icon` accepts. Leave `icon` off when the artwork does not matter to you, and Desktop draws its own fallback glyph so the row never appears blank.

## Use the Host API

Beyond the four primitives, `DesktopPluginHost` groups stable product operations by owner:

| Member | Use it for |
|---|---|
| `plugin`, `environment` | The plugin's id, version, and display name, plus the current locale, theme, and theme seed, and a change subscription. |
| `appearance` | Generation-owned theme-seed and backdrop-presentation contributions. |
| `session` | The foreground workspace, active thread, mode, and busy state, plus a change subscription. |
| `navigation` | Opening plugin views, Settings pages, and threads, and claiming custom-scheme links. |
| `ui` | Toasts, Host-owned confirmation and color dialogs, and the three surface operations. |
| `appServer` | Supported JSON-RPC requests and subscriptions. |
| `settings` | Reading, mutating, and following this plugin's schema-backed settings. |
| `appBindings`, `appSurfaces` | Connected-app binding and app-provided UI surfaces. |
| `workspaces` | Reading local workspace information. |
| `oratorio` | Team runs, handoffs, and events. |

The Host API is a compatibility contract, not an access-control boundary. Main-process URL, route, bearer, size, and timeout checks remain service invariants rather than plugin permissions.

### Read and mutate plugin settings

Declare a schema beside the manifest and point to it with `settings`:

```json
{
  "schemaVersion": 1,
  "id": "acme.wallpaper",
  "settings": "./settings.schema.json"
}
```

The schema uses a `fields` array. Supported types are `text`, `textarea`, `number`, `bool`, `select`, `stringList`, `keyValueMap`, and `json`. Field keys are case-insensitive and unique; `defaultValue` must satisfy the same validation as a stored value.

```json
{
  "fields": [
    { "key": "fit", "type": "select", "defaultValue": "cover", "options": ["cover", "contain"] },
    { "key": "dim", "type": "number", "defaultValue": 20, "min": 0, "max": 80 }
  ]
}
```

Read one complete snapshot during activation, then write only the changed fields. `unset` removes the selected scope's override and reveals the next lower layer:

```ts
const snapshot = await host.settings.get();
const current = snapshot.value as { fit: string; dim: number };

await host.settings.mutate("personal", [
  { op: "set", key: "fit", value: "contain" },
  { op: "unset", key: "dim" },
]);
```

The snapshot contains the schema, personal and workspace layers, the effective value, and writable scopes. Version 1 has no conflict token and no host-generated settings page. Use the settings file only for small JSON values. Keep images, SQLite files, and caches in a plugin backend instead; renderer plugins receive neither a host data-directory path nor a general file API.

### Follow settings changes

`host.settings.onChange` delivers a complete snapshot whenever this plugin's stored settings move:

```ts
host.settings.onChange((snapshot) => {
  applySettings(snapshot.value as WallpaperSettings);
});
```

It fires once per change, whether your plugin wrote it or another client did. A repeat is not a change: a snapshot equal to the last one delivered is dropped, so your own write never comes back a second time as an echo. A rejected `mutate` leaves the file untouched, so nothing is published and the rejection alone reaches you — keep the optimistic value only until the promise settles.

It never fires on subscribe. Read the value once during activation, keep it, and let `onChange` replace it from then on:

```ts
let settings = normalize((await host.settings.get()).value);

host.settings.onChange((snapshot) => {
  settings = normalize(snapshot.value);
  repaint(settings);
});
```

Desktop owns the observation and re-reads a plugin's configuration once per change, however many listeners that plugin registered, so subscribing from several places costs nothing extra. Only the newest read is allowed to publish, so writes in quick succession — dragging a slider — never hand a listener the older value even when an earlier read resolves last. Writes that do not go through this API — a hand edit of `plugin-config.json` while Desktop is running — are not observed.

### React to theme and locale changes

`host.environment` reads the applied theme, its seed, and the UI locale. Subscribe with `onChange` when something outside React has to follow them — a canvas, a generated stylesheet, or a cached value:

```ts
host.environment.onChange(({ locale, theme, themeSeed }) => {
  repaintScene(theme, themeSeed.accent);
  relabelScene(locale);
});
```

Every call delivers a complete snapshot, and a call happens only when a value actually changed. The subscription is generation-owned, so it goes away with the plugin.

`themeSeed` carries the four values Desktop derives its palette from — `surface`, `ink`, `accent`, and a 0-100 `contrast`. Watch it when you paint something CSS cannot reach, such as a canvas: a user changing the accent leaves `theme` at `dark`, so the theme name alone would not tell you to repaint. For anything you can style in CSS, read the tokens instead of re-deriving the ramp.

`locale` is one of Desktop's seven app locales — `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, `de` — typed as `DesktopPluginLocale`. Desktop normalizes the browser tag first, so a user on `zh-CN` or `en-US` reaches your plugin as `zh-Hans` or `en`. Key a string catalog by app locale and read `host.environment.locale` directly; no base-language fallback of your own is needed.

Desktop owns the underlying observation. A plugin does not watch `document.documentElement` for `data-theme` or `lang`, and how Desktop notices a change is not part of the contract.

Inside a React tree, hold the snapshot in state from the same subscription:

```tsx
import { useEffect, useState } from "react";
import type { DesktopPluginViewProps } from "@dotcraft/plugin";

function useTheme(host: DesktopPluginViewProps["host"]) {
  const [theme, setTheme] = useState(host.environment.theme);
  useEffect(() => {
    setTheme(host.environment.theme);
    return host.environment.onChange((environment) => setTheme(environment.theme));
  }, [host]);
  return theme;
}
```

### Contribute a theme or backdrop presentation

Application-wide appearance goes through `host.appearance`. A theme plugin can override only the
seed fields it owns for either variant; Core Appearance settings remain the base layer:

```ts
host.appearance.setThemeSeedOverride({
  light: { surface: "#f7f2e8", ink: "#2d2924", accent: "#b64b3a" },
  dark: { surface: "#171413", ink: "#f3ece7", accent: "#e26a55" },
});
```

A wallpaper plugin renders its media in `app.background`, then asks the Host to compose each shell
region once over that media:

```ts
host.appearance.setBackdropPresentation({ surfaceOpacity: 0.72 });
```

Each plugin generation owns one slot of each kind. A later activation has priority without
discarding the earlier contribution; passing `null`, disabling, uninstalling, reloading, or failing
activation reveals the previous layer. Repeating the same value does not publish another theme
change. Desktop validates seed colours and constrains contrast and opacity.

These calls do not persist plugin choices. Store the chosen pack or opacity with `host.settings`,
reapply it during activation, and pass `null` when the effect is off. Do not set Desktop's private
CSS variables or wrap `app` to create a global appearance effect.

### Read the current session

`host.session` reports what Desktop is working on, and `onChange` follows it:

```ts
host.session.onChange((session) => {
  repaint(session.busy);
});
```

| Field | Meaning |
|---|---|
| `workspacePath` | The workspace in the foreground, or `null` when none is open. |
| `threadId` | The active thread, or `null` on the welcome screen. |
| `mode` | `agent` or `plan`. |
| `busy` | A turn is running or waiting for the user's input. |

`workspacePath` is the foreground workspace, not the active thread's. That is what makes it readable from a Settings page, a main view, or an effect with no component mounted anywhere — the places where the conversation panel does not exist. It matches the `active` entry of `host.workspaces.listLocalProjects()`. Inside a Composer surface, `context.workspacePath` still reports the thread's own workspace, which can differ from the foreground one.

Approval state, Composer variant, and minimal chrome are not here. They describe how the Composer presents itself, so they stay on the Composer surface context.

The four fields read live, so a component keeps what it needs in state rather than holding the object:

```tsx
const [busy, setBusy] = useState(host.session.busy);
useEffect(() => {
  setBusy(host.session.busy);
  return host.session.onChange((session) => setBusy(session.busy));
}, [host]);
```

## Use the UI kit

Import shared UI components from `@dotcraft/plugin` so a plugin page looks like the rest of Desktop without copying Core styles. The official builder connects hooks and JSX to Desktop's React runtime.

| Group | Components |
|---|---|
| **Controls** | `Button`, `IconButton`, `Input`, `Textarea`, `Select`, `SegmentedControl`, `Combobox`, `Checkbox`, `PillSwitch`, `Slider` |
| **Presentation** | `Spinner`, `Skeleton`, `ActionTooltip`, `ModalHeader`, `InlineDiff` |
| **Settings layout** | `SettingsPanelShell`, `SettingsBreadcrumb`, `SettingsGroup`, `SettingsRow` |

A control that reports a chosen value — `Select`, `Combobox`, `SegmentedControl` — calls `onValueChange` and takes its accessible name from `ariaLabel`. A boolean toggle — `Checkbox`, `PillSwitch` — calls `onChange`.

`Slider` calls `onValueChange` while its value moves and calls the optional `onValueCommit` once
when the pointer or keyboard interaction ends. Preview from `onValueChange`; persist from
`onValueCommit` when saving each intermediate value would perform I/O. Provide `valueText` when the
number needs a unit. Use `SettingsRow` with `orientation="block"` for controls that need the row width. For a custom visual
picker, use a block row or `SettingsGroup flush` so it keeps the standard Settings spacing and
border while owning its internal layout. `htmlFor` connects a row label to a native control, and
`align="flex-start"` aligns multiline inline rows at the top.

Reach for `SegmentedControl` when a few mutually exclusive choices fit on one row, and for `Select` when the list is longer or each option needs a description or icon:

```tsx
import { SegmentedControl, SettingsGroup, SettingsRow } from "@dotcraft/plugin";

function DensityRow({
  density,
  onDensityChange,
}: {
  density: "cozy" | "compact";
  onDensityChange: (density: "cozy" | "compact") => void;
}) {
  return (
    <SettingsGroup title="Board">
      <SettingsRow
        label="Density"
        control={
          <SegmentedControl
            value={density}
            options={[
              { value: "cozy", label: "Cozy" },
              { value: "compact", label: "Compact" },
            ]}
            onValueChange={onDensityChange}
            ariaLabel="Board density"
          />
        }
      />
    </SettingsGroup>
  );
}
```

### Request a color

Use `host.ui.pickColor` for an opaque RGB choice. Desktop owns the compact dialog, portal, focus
trap, localization, Hex validation, and keyboard controls. It accepts three- or six-digit Hex
input and returns a normalized lowercase `#rrggbb`. Changes preview inside the dialog only.

```ts
const result = await host.ui.pickColor({
  title: "Choose workspace color",
  description: "Used wherever this workspace appears.",
  initialColor: "#8b5cf6",
  allowReset: true,
  defaultColor: "#4566cc",
});

if (result.kind === "select") await save(result.color);
if (result.kind === "reset") await clearOverride();
```

Done returns `select`. Reset returns `reset` and closes immediately. Escape, the close button,
the backdrop, a competing picker request, or plugin disposal return `cancel`. Invalid Host
arguments reject with `TypeError`. Do not render a native `input[type="color"]` or implement a
plugin-owned color dialog.

## Use bundled assets

Import an image from plugin source and use the value as it comes. The builder resolves it to the URL of the emitted file, so it is already correct at module scope, from the entry bundle, and from a split chunk:

```tsx
import scene from "./assets/aurora.svg";

function Background() {
  return <div style={{ backgroundImage: `url("${scene}")` }} />;
}
```

Desktop serves a plugin from `dotcraft-plugin://<id>/<revision>/`, an address that no build can know in advance, so there is nothing to repair by hand. Wrapping the import in `new URL(asset, import.meta.url)` is now redundant rather than wrong: a plugin that still does it keeps working after a rebuild, because the value it wraps is already absolute.

The builder bundles `.gif`, `.jpg`, `.jpeg`, `.png`, `.svg`, and `.webp` into `dist/assets/`. In CSS, keep the ordinary relative form — `url("./assets/aurora.svg")` — because a stylesheet resolves it against its own address, which is already under the plugin route.

## Use the theme tokens

Desktop derives its whole palette from a four-value seed, and the tokens below are the part a plugin may read. Style with them and your UI follows the user's theme, accent, background, and contrast without watching anything:

```css
.my-plugin-card {
  background: var(--bg-elevated);
  color: var(--text-primary);
  border: 1px solid var(--border-default);
  border-radius: var(--control-radius-md);
  box-shadow: var(--shadow-level-2);
}
```

| Family | Tokens |
|---|---|
| Surfaces | `--bg-primary`, `--bg-secondary`, `--bg-tertiary`, `--bg-active`, `--bg-hover`, `--bg-elevated` |
| Text | `--text-primary`, `--text-secondary`, `--text-dimmed`, `--text-tertiary`, `--text-disabled` |
| Borders | `--border-subtle`, `--border-default`, `--border-active` |
| Accent | `--accent`, `--accent-hover`, `--on-accent` |
| Status | `--success`, `--warning`, `--error`, `--info`, `--success-bg`, `--warning-bg`, `--error-bg` |
| Elevation | `--shadow-level-1`, `--shadow-level-2`, `--shadow-level-3` |
| Type | `--font-ui`, `--font-body`, `--font-mono`, `--type-body-size`, `--type-ui-size`, `--type-secondary-size`, `--type-hint-size`, `--type-heading-size` |
| Shape | `--control-radius-md`, `--button-height`, `--button-height-sm` |
| Seed | `--seed-surface`, `--seed-ink`, `--seed-accent`, `--seed-contrast` |

`--on-accent` is the foreground that stays legible on the accent, so put text on an accent fill with it rather than picking white yourself. The `--seed-*` four are the same values `host.environment.themeSeed` reports; read them only when you paint outside CSS.

Every other custom property is private, including `--composer-*`, `--sidebar-*`, `--shell-*`, `--main-surface-*`, `--glass-*`, `--tooltip-*`, `--scrollbar-*`, `--shimmer-*`, `--diff-*`, and `--ansi-*`. They move with Desktop's own layout work.

## Use DOM and CSS deliberately

Desktop Plugins may access the renderer DOM and load global CSS, and DotCraft does not block either. Its DOM structure, class names, private CSS variables, stores, and feature components are still not public contracts. Prefer a public surface or service when one exists, and expect to carry the maintenance cost of anything you reach through DOM or CSS instead.

## Keep UI and backend responsibilities separate

A custom background, Composer decoration, wrapper, plugin surface, renderer service, or renderer event needs only a Desktop Plugin. DotCraft does not create matching C# or AppServer APIs for pure UI.

Add a [.NET plugin](./dotnet-plugins) or AppServer contract when the feature needs backend execution, durable host-owned state, Agent tools or hooks, another client, or cross-process and remote coordination. One plugin bundle may contain both modules, but neither is required solely because the other exists.

## Generation and reload lifecycle

Desktop activates the whole content revision as one generation and calls `activate` once for it. Refreshing an unchanged revision is a no-op. An updated revision disposes the previous generation before activating the new one. The build and refresh steps live in [Build a Desktop Plugin](./desktop-plugins).

Disabling or replacing a revision withdraws Host-owned registrations immediately. Desktop does not wait for an unfinished plugin `activate()` or `dispose()` promise before continuing, and a late activation result is stale and cannot publish.

The revision is the development iteration unit. Desktop Plugins do not have a built-in file watcher, HMR, component-only reload, or partial-generation update. Rebuild, then refresh or re-enable the plugin.

Desktop never loads executable plugin code from a remote AppServer. With a remote workspace, it activates only locally packaged code whose plugin id, version, and Desktop content revision match the remote snapshot.
