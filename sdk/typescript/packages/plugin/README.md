# @dotcraft/plugin

`@dotcraft/plugin` provides the public contracts, shared React runtime bindings, and build command for trusted DotCraft Desktop Plugins. Use Node.js 20 or later.

## Install

```shell
npm install --save-dev @dotcraft/plugin
```

React 19 and `react-dom` 19 are peer dependencies. The `$plugin-creator` scaffold supplies them and the required TypeScript setup.

## Activate

```tsx
import type { DesktopPluginActivate } from "@dotcraft/plugin";

export const activate: DesktopPluginActivate = (host) => {
  host.ui.add("composer.toolbar.leading", ({ context }) => (
    context.minimalChrome ? null : <button type="button">Review</button>
  ));
};
```

## Match Desktop settings and appearance

Build settings pages from the exported Host UI Kit, including `SettingsGroup`, `SettingsRow`,
`SegmentedControl`, and `Slider`. Put plugin-specific visual pickers in a block row or a
`SettingsGroup` with `flush` rather than reproducing Desktop layout styles.

Use `host.appearance` for application-wide theme or backdrop effects. Theme contributions provide
partial light and dark seed overrides; wallpaper-style plugins render media in `app.background`
and set the shell surface opacity with `setBackdropPresentation`. Passing `null`, deactivating, or
reloading the plugin restores the previous Host appearance. Private Desktop CSS variables are not
part of this API.

Use `app.status` for compact persistent diagnostics that share the Host-owned bottom-right status
rail with Core indicators. Use `app.overlay` for decorative or independently positioned content;
an `app.status` contribution does not position itself against the viewport.

Request an opaque RGB color with `host.ui.pickColor`. Desktop owns the dialog, Hex validation,
focus handling, and localized controls. The result distinguishes a selected color, a semantic
reset, and cancellation; plugin disable or reload cancels any request it owns.

```ts
const result = await host.ui.pickColor({
  title: "Choose workspace color",
  initialColor: "#8b5cf6",
  allowReset: true,
  defaultColor: "#4566cc",
});
```

## Build

The `$plugin-creator` scaffold supplies the source layout and build script. From its `desktop/` directory, run:

```shell
npm run build
```

The build writes the plugin module and imported assets to `dist/` for DotCraft to load.

## Links

- [Build a Desktop Plugin](https://dotharness.github.io/dotcraft/developing/integrations/desktop-plugins)
- [Desktop Plugin API](https://dotharness.github.io/dotcraft/developing/integrations/desktop-plugin-api)
- [Source repository](https://github.com/DotHarness/dotcraft)
- [Issues](https://github.com/DotHarness/dotcraft/issues)
- [License](./LICENSE)
