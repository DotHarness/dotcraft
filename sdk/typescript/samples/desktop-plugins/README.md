# Desktop Plugin samples

Three pure-Desktop plugin bundles, each exercising a different part of the plugin contract.

| Bundle | Demonstrates |
|---|---|
| [`grok-mascot`](grok-mascot) | `ui.replace` and `ui.add` on one surface, the full `composer.mascot` context |
| [`wallpaper`](wallpaper) | `app.background`, `host.appearance`, bundled assets, a scene per theme |
| [`token-hud`](token-hud) | `app.status`, `host.appServer` requests and notifications, `host.session` |

All three also contribute a settings page, a command, and a seven-locale catalog. `wallpaper` and
`grok-mascot` are the pair that collaborate: one provides a renderer service and an event, the
other consumes both.
See [Build a Desktop Plugin](../../../../docs/developing/integrations/desktop-plugins.md) for the
authoring model and [Desktop Plugin API](../../../../docs/developing/integrations/desktop-plugin-api.md)
for the surface catalog.

## Build and verify

Requires Node.js 20 or later. From the repository root:

```bash
node sdk/typescript/samples/desktop-plugins/verify-samples.mjs
```

That builds every bundle and loads each one the way Desktop does — installing the shared React
runtime, importing the built module, calling `activate`, and checking what it registered.

To work on one bundle, install once and build from its `desktop/` directory:

```bash
cd sdk/typescript/samples/desktop-plugins/grok-mascot/desktop && npm install && npm run build
```

## Add to DotCraft

Build the bundles first: a bundle without `desktop/dist/index.mjs` is skipped during discovery.
Then open **Plugins**, choose **Add marketplace**, click **Browse**, and pick this `desktop-plugins`
directory. DotCraft reads [the marketplace index](.craft/plugins/marketplace.json) and lists all
three bundles for workspace installation. After a rebuild, refresh this marketplace from the
Plugins page because Desktop does not watch plugin files.

`grok-mascot` and `wallpaper` carry a browser harness under `desktop/preview/` for visual work:
run its `refresh.sh`, then `node preview-server.mjs <bundle>`.
