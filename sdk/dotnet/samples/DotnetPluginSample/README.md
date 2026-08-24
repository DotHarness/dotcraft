# .NET plugin sample

This sample contains two managed .NET plugin bundles that demonstrate contribution points, plugin
dependencies, and a typed service shared across a plugin boundary.

- `acme.review-core` contributes Host behavior and exports `IReviewService`.
- `acme.review-consumer` depends on `acme.review-core`, consumes the service, and contributes
  `review.normalize`.

## Verify the sample

Requires the .NET 10 SDK. Run the deterministic Host-level verification from this directory:

```powershell
.\verify.ps1
```

The script builds both bundles, runs plugin admission and activation, checks their observable
contributions, and verifies teardown through the real runtime.

## Run a live smoke test

Run the bundles through a real AppServer and the current local Model Provider:

```powershell
.\verify-live.ps1 -DotCraftBin C:\path\to\dotcraft.exe
```

Pass `-ProviderId` or `-Model` to override the current selection. The smoke test uses an isolated
temporary workspace and removes it after the run.

For plugin authoring and runtime details, see [Build a .NET plugin](../../../../docs/developing/integrations/dotnet-plugins.md).
