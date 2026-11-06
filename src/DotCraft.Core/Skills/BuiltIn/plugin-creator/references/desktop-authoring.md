# Desktop extension authoring

Use Desktop authoring when a plugin needs local UI such as a main view or settings page. The API is documented at `https://www.dotcraft.net/extend/desktop-extension-api`.

## Files

Copy the templates into the plugin's `desktop/` directory:

| Template | Destination |
|---|---|
| `assets/template-desktop-package.json` | `desktop/package.json` |
| `assets/template-desktop-tsconfig.json` | `desktop/tsconfig.json` |
| `assets/template-desktop-index.tsx` | `desktop/src/index.tsx` |
| `assets/template-desktop-index.css` | `desktop/src/index.css` |

Declare the module in the manifest and add `desktop` to `capabilities`. A plugin with Desktop UI needs a canonical `MAJOR.MINOR.PATCH` version.

```json
"desktop": {
  "description": "One sentence about what the Desktop module adds.",
  "entry": "./desktop/dist/index.mjs",
  "styles": ["./desktop/dist/index.css"]
}
```

Set `@dotcraft/plugin` to the running DotCraft version. Use the SDK types and shared UI exports rather than Desktop renderer internals, and keep contribution ids unique within the plugin.

## Build

```powershell
npm install
npm run build
```

Run these from `desktop/`. The build type-checks the source and writes `dist/index.mjs` and `dist/index.css`. Treat `dist/` as generated output. Verification fails until it exists.

After building, the user refreshes Desktop > Plugins, enables the plugin, and opens its main view. After a source change, rebuild and refresh or re-enable the plugin.

Desktop extensions execute as trusted renderer code. Enabling the plugin is the trust decision, and the Host API is an authoring contract rather than a sandbox. Use MCP Apps for untrusted interactive tool UI.
