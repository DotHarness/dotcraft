# .NET plugin sample

This sample contains two .NET and Desktop plugin bundles that demonstrate contribution points,
plugin dependencies, a typed backend service, and open renderer composition across plugins.

- `acme.review-core` contributes Host behavior, exports `IReviewService`, installs a Desktop
  background and custom Composer mascot, wraps the app, provides a renderer service and event,
  and owns an overlay surface.
- `acme.review-consumer` contributes `review.normalize`, keeps its Desktop main view and tool
  renderer, adds an action to the leading Composer toolbar, wraps the model control, adds status
  beside the ChatGPT subscription indicator, and extends the Core-owned overlay surface.

Start with [ReviewProvider/Plugin.cs](./ReviewProvider/Plugin.cs) for provider activation and
[ReviewConsumer/Plugin.cs](./ReviewConsumer/Plugin.cs) for typed dependency consumption. The
Desktop pair is split the same way: [Core](./Desktop/Core/src/index.tsx) owns services and surfaces,
while [Consumer](./Desktop/Consumer/src/index.tsx) extends them through shared contracts.

## Verify the sample

Requires the .NET 10 SDK. Run the deterministic Host-level verification from this directory:

```powershell
.\verify.ps1
```

The script builds both bundles, runs plugin admission and activation, checks their observable
contributions, and verifies teardown through the real runtime.

Build both Desktop modules with Node.js 20 or later:

```powershell
.\verify-desktop.ps1
```

The Desktop build uses one dependency install and type-check, then builds the Core and Consumer
modules separately and copies both generated `dist/` trees into their bundles.

## Try the Desktop dogfood

Run both verification scripts, then add this sample's absolute `bundles/` path to the workspace
`Plugins.PluginRoots`. Refresh the plugin list, enable both plugins, and grant their .NET trust when
prompted. Review Core supplies the background, state-responsive mascot, and overlay surface; Review
Consumer adds the overlay control and Composer UI. Use any **Pulse** action to resolve the Core
service at click time and send an event back to its background. Disable Review Core to restore the
default DotCraft robot immediately.

After changing Desktop source, rerun `verify-desktop.ps1` and refresh the plugin list. Disabling
either plugin demonstrates that its effects, surfaces, services, events, and UI are withdrawn as one
generation.

## Run a live smoke test

Run the bundles through a real AppServer and the current local Model Provider:

```powershell
.\verify-live.ps1 -DotCraftBin C:\path\to\dotcraft.exe
```

Pass `-ProviderId` or `-Model` to override the current selection. The smoke test uses an isolated
temporary workspace and removes it after the run.

For authoring details, see [Build a .NET plugin](../../../../docs/developing/integrations/dotnet-plugins.md)
and [Desktop Plugins](../../../../docs/developing/integrations/desktop-plugins.md).
