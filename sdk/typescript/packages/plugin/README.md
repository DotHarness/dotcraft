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
