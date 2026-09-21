# @dotcraft/plugin

`@dotcraft/plugin` provides the public contracts, React runtime bindings, and build command for
trusted DotCraft Desktop Plugins. Use Node.js 20 or later with React 19 and `react-dom` 19.

## Install

```shell
npm install --save-dev @dotcraft/plugin
```

## Activate

```tsx
import type { DesktopPluginActivate } from '@dotcraft/plugin'

export const activate: DesktopPluginActivate = (host) => {
  host.ui.add('composer.toolbar.leading', ({ context }) => (
    context.minimalChrome ? null : <button type="button">Review</button>
  ))
}
```

## Documentation

- [Build a Desktop Plugin](https://www.dotcraft.net/developing/integrations/desktop-plugins)
- [Desktop Plugin API](https://www.dotcraft.net/developing/integrations/desktop-plugin-api)
