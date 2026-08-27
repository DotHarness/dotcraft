# .NET plugin sample

This sample contains two managed .NET plugin bundles that demonstrate contribution points, plugin
dependencies, a typed service shared across a plugin boundary, and Desktop presentation for one tool.

- `acme.review-core` contributes Host behavior and exports `IReviewService`.
- `acme.review-consumer` contributes `review.normalize` and a Desktop main view and renderer for its
  `acme.review-normalize` presentation.

## Verify the sample

Requires the .NET 10 SDK. Run the deterministic Host-level verification from this directory:

```powershell
.\verify.ps1
```

The script builds both bundles, runs plugin admission and activation, checks their observable
contributions, and verifies teardown through the real runtime.

Build the optional Desktop module with Node.js 20 or later:

```powershell
.\verify-desktop.ps1
```

## Run a live smoke test

Run the bundles through a real AppServer and the current local Model Provider:

```powershell
.\verify-live.ps1 -DotCraftBin C:\path\to\dotcraft.exe
```

Pass `-ProviderId` or `-Model` to override the current selection. The smoke test uses an isolated
temporary workspace and removes it after the run.

For authoring details, see [Build a .NET plugin](../../../../docs/developing/integrations/dotnet-plugins.md)
and [Desktop Plugins](../../../../docs/developing/integrations/desktop-plugins.md).
