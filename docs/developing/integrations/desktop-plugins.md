# Build a Desktop Plugin

A Desktop Plugin adds fully trusted TypeScript and React behavior to DotCraft Desktop. It runs inside the renderer and can extend Core UI, replace or wrap public surfaces, expose services, react to events, and provide new surfaces for other plugins.

This page targets plugin authors. Use `$plugin-creator` for the recommended project layout and `@dotcraft/plugin` for the public runtime, React components, and build command.

> [!CAUTION]
> Installing and enabling a Desktop Plugin executes it in DotCraft's renderer. Desktop Plugins have no permission layer, sandbox, or separate Extension Host. Install only code you trust. Use [MCP Apps](./mcp-apps) when interactive tool content must remain sandboxed.

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

The Desktop module shares its parent plugin's identity, version, enabled state, and interface metadata. A bundle that also contains .NET may declare dependencies, but those order managed generations rather than Desktop activation. Manifest `capabilities` labels do not grant or restrict renderer access.

## Activate the plugin

Export a named `activate` function from `desktop/src/index.tsx`. It receives `DesktopPluginHost`. It may register runtime work and return nothing:

```tsx
import type { DesktopPluginActivate } from "@dotcraft/plugin";
import "./index.css";

function Wallpaper() {
  return <div className="acme-board-wallpaper" aria-hidden="true" />;
}

function ComposerHint() {
  return <p className="acme-board-composer-hint">Review mode is active.</p>;
}

export const activate: DesktopPluginActivate = (host) => {
  host.ui.replace("app.background", Wallpaper);
  host.ui.add("composer.before", ComposerHint);
};
```

Each call takes effect immediately and belongs to this plugin revision. If `activate` later fails, Desktop cleans up the registrations already made. `activate` may instead return a `DesktopPluginActivation` convenience object, or combine returned contributions with direct kernel registrations.

## Build and reload

Install dependencies and build from the generated `desktop/` directory:

```bash
npm install
npm run build
```

The scaffold runs TypeScript checking followed by `dotcraft-plugin build`. The builder bundles `src/index.tsx`, imported CSS, chunks, and assets into `dist/`, while wiring React and shared components to the Desktop runtime.

See [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) for two runnable .NET and Desktop bundles. The Core plugin replaces the background, wraps the app, and owns a renderer service, event, and custom surface; the Consumer adds Composer controls and contributes UI to the Core-owned surface.

Build before DotCraft discovers or packages the plugin. Refresh the plugin list, then enable it. After a source change, rebuild and refresh or re-enable the plugin.

For the complete surface catalog, contexts, composition semantics, Host API, and generation lifecycle, see [Desktop Plugin API](./desktop-plugin-api).

## Related docs

- [Desktop Plugin API](./desktop-plugin-api)
- [Plugin Market](./plugin-market)
- [Build a .NET plugin](./dotnet-plugins)
- [MCP Apps](./mcp-apps)
- [Plugins & Tools](../../features/agent-system/plugins-tools)
