# Desktop Plugin Authoring

Use Desktop authoring when a DotCraft Plugin needs local UI such as a main view or settings page.

## Scaffold

Create a plugin with a minimal main view:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --without-skill --with-desktop
```

The scaffold adds the inline `desktop` manifest declaration and source files:

```text
.craft/plugins/my-plugin/
├── .craft-plugin/plugin.json
└── desktop/
    ├── package.json
    ├── tsconfig.json
    └── src/
        ├── index.css
        └── index.tsx
```

The package pins `@dotcraft/plugin` to the deployed DotCraft version. The creator does not install dependencies or generate `dist/`.

To add Desktop source to an existing plugin, pass the parent that already contains the plugin directory:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --with-desktop --path .craft/plugins
```

The creator preserves the existing manifest and plugin files, then adds only the inline `desktop` declaration, the `desktop` capability, and the source scaffold. It fails if the manifest already declares `desktop` or a Desktop source directory already exists.

For a managed plugin with Desktop UI, create both parts together:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --dotnet --with-desktop
```

The C# project and Desktop source share the `.craft/plugin-projects/my-plugin/plugin` development bundle and one plugin id. Build the Desktop output before calling `DotNetPlugin.Build`, which validates the complete bundle.

## Main view

The entry exports a named `activate` function and returns typed contributions:

```tsx
import { Button, type DesktopPluginActivate, type DesktopPluginViewProps } from "@dotcraft/plugin";

function MainView({ host }: DesktopPluginViewProps) {
  return <Button onClick={() => host.ui.showToast({ message: "Ready" })}>Verify plugin</Button>;
}

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: "main",
      label: { default: "My Plugin" },
      component: MainView,
    },
  ],
});
```

Use the SDK types and shared UI exports rather than Desktop renderer internals. Keep contribution ids unique within the plugin.

## Build and verify

Install the declared dependencies, then build from the Desktop source directory:

```powershell
npm install
npm run build
```

The build script type-checks the source, then runs `dotcraft-plugin build` to compile `src/index.tsx` into `dist/index.mjs` and imported CSS into `dist/index.css`. Treat `dist/` as generated output and do not edit it. Build it before DotCraft discovers the local plugin or before packaging it, then refresh DotCraft, enable the plugin, and open its main view.

Desktop Plugins execute as trusted renderer code. Installing and enabling the plugin is the trust decision; the Host API is a stable authoring contract, not a sandbox. Use MCP Apps for untrusted interactive tool UI.
