# Build a Desktop Plugin

A Desktop Plugin adds trusted TypeScript and React UI to DotCraft Desktop. It can contribute views, settings, commands, actions, and tool presentation from the same DotCraft Plugin that owns its skills, tools, apps, or .NET code.

This page targets plugin authors. Use `$plugin-creator` for the recommended project layout and `@dotcraft/plugin` for the public contracts, shared UI components, and build command.

> [!CAUTION]
> Desktop Plugins run in DotCraft's renderer with the same Host API regardless of where they were distributed. Installing and enabling a plugin is the trust decision; there is no per-plugin permission tier or JavaScript sandbox. Use [MCP Apps](./mcp-apps) for untrusted interactive tool UI.

## Create the plugin

Ask `$plugin-creator` to create a plugin with Desktop support. The generated Desktop project is part of the plugin bundle:

```text
.craft/plugins/acme-board/
├── .craft-plugin/
│   └── plugin.json
└── desktop/
    ├── package.json
    ├── tsconfig.json
    └── src/
        ├── index.css
        └── index.tsx
```

The scaffold pins `@dotcraft/plugin` to the DotCraft version that created it. Keep that package aligned with the Desktop version that loads the plugin.

## Declare the Desktop module

Declare one Desktop module inline in `.craft-plugin/plugin.json`:

```json
{
  "schemaVersion": 1,
  "id": "acme-board",
  "version": "1.0.0",
  "displayName": "Acme Board",
  "desktop": {
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

`version` is required in canonical `MAJOR.MINOR.PATCH` form. `entry` must name an existing `.mjs` file under `./desktop/dist/`. Every optional `styles` entry must name an existing `.css` file in the same output tree. Imported chunks and assets must also stay in that tree.

The Desktop module shares its parent plugin's identity, version, enabled state, and interface metadata.

## Activate the plugin

Export a named `activate` function from `desktop/src/index.tsx`. It receives `DesktopPluginHost` and returns the complete contribution generation:

```tsx
import {
  Button,
  type DesktopPluginActivate,
  type DesktopPluginViewProps,
} from "@dotcraft/plugin";
import "./index.css";

function BoardView({ host }: DesktopPluginViewProps) {
  return (
    <main className="acme-board">
      <h1>Acme Board</h1>
      <Button
        onClick={() => host.ui.showToast({ message: "Board is ready." })}
      >
        Check status
      </Button>
    </main>
  );
}

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: "board",
      label: { default: "Acme Board" },
      component: BoardView,
    },
  ],
});
```

Contribution ids must be unique across the plugin's activation result. Use `label.translations` for localized labels and the optional `order` field when placement order matters.

## Choose contributions

`DesktopPluginActivation` accepts seven contribution arrays:

| Field | Placement and behavior |
|---|---|
| **`mainViews`** | Adds a full view to Desktop navigation. |
| **`settingsPages`** | Adds a page to Desktop Settings. |
| **`conversationViews`** | Adds a thread-scoped tab beside the host-owned Chat view. |
| **`commands`** | Adds a searchable command with an optional availability predicate and an `execute` callback. |
| **`toolRenderers`** | Renders one exact `presentationId`; Desktop keeps its optimized and generic fallbacks when no plugin renderer matches. |
| **`composerActions`** | Adds a component to the composer action area with read-only thread and mode context. |
| **`messageActions`** | Adds an action to assistant messages with a read-only message model. |

The activation result may also provide `dispose()`. Contribution components receive their `host` and contribution id through typed props; conversation and presentation contributions receive their corresponding read-only models.

## Use the Host API

`DesktopPluginHost` exposes product operations without exposing Desktop stores, Electron IPC, plugin filesystem paths, or product feature components.

| Area | Public operations |
|---|---|
| **`plugin`** | Read `id`, `version`, and `displayName`. |
| **`environment`** | Read the current `locale` and `theme`. |
| **`navigation`** | Call `openMainView`, `openSettingsPage`, or `openThread`, and subscribe to Desktop custom-scheme URLs with `onOpenUrl`. |
| **`ui`** | Call `showToast` or `confirm`. |
| **`appServer`** | Send a generated-contract `request` and subscribe with `onNotification`. |
| **`appBindings`** | Call `getConnectionStatus`, `startConnection`, or `openNativeApp`. |
| **`appSurfaces`** | Call `getJson` or `postJson` through Desktop's App Surface proxy. |
| **`workspaces`** | List local projects with `listLocalProjects`. |
| **`oratorio`** | Call `getContext`, `request`, `retry`, `getPendingHandoff`, `resolveHandoff`, `focusRun`, or `onEvent`. |

`navigation.onOpenUrl` dispatches absolute URLs whose scheme is not HTTP, HTTPS, or `mailto` to active Desktop Plugins. Return `true` when the listener handles a URL; Desktop stops at the first handler in stable plugin order. If no listener handles it, Desktop rejects the URL instead of sending it to the operating-system shell. HTTP, HTTPS, and `mailto` URLs use the existing AppServer-validated shell path. App Surface calls accept a relative path, while Desktop Main resolves the local endpoint and injects its bearer.

Import shared UI components from `@dotcraft/plugin`. Alongside the field and action primitives (`Button`, `IconButton`, `Input`, `Textarea`, `Select`, `Checkbox`, `Spinner`, and `Skeleton`), the package exposes the focused composition components used by bundled plugins (`ActionTooltip`, `Combobox`, `ModalHeader`, `PillSwitch`, `SettingsPanelShell`, `SettingsBreadcrumb`, `SettingsGroup`, `SettingsRow`, and the narrow `InlineDiff` adapter). React hooks and JSX use the React runtime owned by Desktop; the official builder prevents a second React runtime from entering the output. Contribution `icon` values may use a plugin React component or a string token resolved by Desktop.

Host-owned subscriptions and toasts are removed when the plugin generation stops. Main-process operations retain their normal URL, route, bearer, size, and timeout validation for every Desktop Plugin.

## Build and load

Install dependencies and build from the generated `desktop/` directory:

```bash
npm install
npm run build
```

The scaffold runs TypeScript checking followed by `dotcraft-plugin build`. The builder bundles `src/index.tsx`, imported CSS, chunks, and assets into `dist/`, while wiring React and the shared components to Desktop's runtime.

See [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) for a runnable plugin that combines a .NET tool with a Desktop view and renderer.

Build before DotCraft discovers the plugin or before packaging it. Refresh the plugin list, then enable the plugin to activate its Desktop module.

Desktop publishes all contributions from one plugin revision as a single generation. A matching active revision is unchanged. Updating, disabling, uninstalling, or shutting down withdraws the complete generation, removes its styles and Host-owned subscriptions, and calls `dispose()`.

Desktop never loads executable plugin code from a remote AppServer. When Desktop uses a remote workspace, it activates only locally packaged code whose plugin id, version, and Desktop content revision match the remote plugin snapshot.

## Related docs

- [Plugin Market](./plugin-market)
- [Build a .NET plugin](./dotnet-plugins)
- [MCP Apps](./mcp-apps)
- [Plugins & Tools](../../features/agent-system/plugins-tools)
