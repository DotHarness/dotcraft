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
    "description": "Adds a project board to DotCraft Desktop.",
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

`version` is required in canonical `MAJOR.MINOR.PATCH` form. The optional `description` says what this Desktop module does, and plugin detail pages prefer it over the parent plugin description. `entry` must name an existing `.mjs` file under `./desktop/dist/`, and every optional `styles` entry must name an existing `.css` file in the same output directory. Imported chunks and assets must also stay in that directory.

The Desktop module shares its parent plugin's id, version, enabled state, and `interface` metadata. A bundle that also contains .NET may declare dependencies, but those order managed generations rather than Desktop activation. Manifest `capabilities` labels neither grant nor restrict renderer access.

## Activate the plugin

Export a named `activate` function from `desktop/src/index.tsx`. It receives `DesktopPluginHost` and may register runtime work without returning anything:

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

Each call takes effect immediately and belongs to the current plugin revision. If `activate` later fails, Desktop withdraws the registrations already made. `activate` may instead return a `DesktopPluginActivation` convenience object, or combine returned contributions with direct kernel registrations.

## Build and reload

Install dependencies and build from the generated `desktop/` directory:

```bash
npm install
npm run build
```

The build script type-checks the source, then runs `dotcraft-plugin build` to bundle `src/index.tsx`, imported CSS, chunks, and assets into `dist/`, wiring React and the shared components to Desktop's own React runtime.

Build before DotCraft discovers or packages the plugin. Refresh the plugin list, then enable it. After a source change, rebuild and refresh or re-enable the plugin.

See [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) for two runnable bundles that each ship .NET and Desktop modules. The Core plugin replaces the background, wraps the app, and owns a renderer service, an event, and a custom surface. The Consumer adds Composer controls and contributes UI to the Core-owned surface.

For the complete surface catalog, contexts, composition semantics, Host API, and generation lifecycle, see [Desktop Plugin API](./desktop-plugin-api).

## Related docs

- [Build a .NET plugin](./dotnet-plugins) — add the managed half to the same bundle when the feature needs backend execution or Agent tools.
- [Plugin Market](./plugin-market) — publish the built plugin so other people can install it.
