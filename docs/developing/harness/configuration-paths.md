# Configure Harness paths

The host owns configuration sources and storage locations. Harness consumes one effective `AppConfig` and turns three path options into one validated `DotCraftPaths` context.

## Prepare configuration outside Harness

`AddDotCraftHarness` does not read configuration files, environment variables, or the user profile. Load and merge those sources in the application before registration.

```csharp
AppConfig appConfig = configurationStore.Load();

builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

This boundary lets desktop applications, services, and tests use different configuration systems without changing Runtime behavior.

## Choose path roots

| Option | Required | Default | Purpose |
| --- | --- | --- | --- |
| `WorkspacePath` | Yes | None | The application workspace used by sessions and tools. |
| `DataPath` | No | `.craft` | Workspace-local sessions, recovery data, tool results, and Runtime state. |
| `UserDataPath` | No | `null` | User-level skills, commands, hooks, authentication, marketplaces, and provider state. |

Use a different workspace data directory by setting a direct child name:

```csharp
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
    options.DataPath = ".agents";
});
```

`DataPath` also accepts the absolute path of that direct child. Harness rejects nested relative paths, traversal outside the workspace, and existing filesystem links that escape the workspace.

::: tip
Treat the selected data directory as Harness-owned state. Exclude it from source-control operations and avoid storing application documents inside it.
:::

## Enable user-level state explicitly

`UserDataPath` is disabled by default. Set it only when the application intends to own user-level discovery and persistence.

```csharp
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
    options.DataPath = ".agents";
    options.UserDataPath = applicationDataPath;
});
```

When `UserDataPath` is `null`, user-level discovery returns no entries. Operations that must persist user state fail with a clear error instead of selecting a profile directory implicitly.

## Resolve paths in application services

Harness registers one immutable `DotCraftPaths`. Resolve it through dependency injection instead of rebuilding path rules in each component.

```csharp
using DotCraft.Workspaces;

public sealed class ExportService(DotCraftPaths paths)
{
    public string GetSessionExportPath(string fileName) =>
        paths.Data.Resolve("exports", fileName);

    public string? GetOptionalUserTemplatePath(string fileName) =>
        paths.UserData.ResolveOrNull("templates", fileName);
}
```

Use `Require` when an operation cannot proceed without user-level persistence:

```csharp
var authFile = paths.UserData
    .Require("Provider authentication")
    .Resolve("auth.json");
```

`Resolve`, `ResolveOrNull`, and `Require` keep path availability and boundary checks in one place.

## Test isolated hosts

Tests should provide temporary workspace and user-data directories explicitly. Omit `UserDataPath` when verifying embedded operation without profile access.

```csharp
builder.Services.AddDotCraftHarness(testConfig, options =>
{
    options.WorkspacePath = temporaryWorkspace;
    options.DataPath = ".agents";
    options.UserDataPath = null;
});
```

## Related docs

- [Harness overview](./)
- [Hosting and lifecycle](./hosting-lifecycle)
- [Model providers](./model-providers)
