# Build a .NET plugin

A .NET plugin runs **inside the DotCraft process**. Where an MCP server talks to DotCraft across a boundary, a .NET plugin composes the kernel from within: it contributes tools, prompt sections, middleware, and lifecycle observers through named contribution points, and it resolves host services to build those contributions out of DotCraft's own parts.

This page targets plugin authors. For the user-facing view of plugins, see [Plugins & Tools](../../features/agent-system/plugins-tools); for the manifest fields every plugin shares, see [Plugin Market](./plugin-market).

> [!CAUTION]
> A .NET plugin loads fully trusted code into the DotCraft process and receives that process's filesystem, network, credential, native interop, and OS authority. There is no managed sandbox and no permission model. Safety depends on which code you choose to build or trust, not on a runtime boundary. Use MCP when code needs a real trust boundary.

![.NET plugin authoring and runtime topology](/dotnet-plugin-topology.svg)

The runnable [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) contains two bundles, covers every public .NET contribution point, verifies the built result through the host's preflight and runtime, and includes Desktop presentation for one tool.

## Create with DotCraft

Ask `$plugin-creator` to create a .NET plugin in the current workspace. It creates a persistent source project and a standard development bundle:

```text
.craft/plugin-projects/<plugin-id>/
├── src/
└── plugin/
    ├── .craft-plugin/plugin.json
    └── lib/
```

The project has no `.csproj` and does not restore NuGet packages. The agent edits `src/**/*.cs` and the manifest, uses `DotNetPlugin.Inspect` for exact API signatures and documentation, then calls `DotNetPlugin.Build`. The build compiles against the public plugin API and BCL shipped by the running host, runs metadata preflight, publishes the bundle atomically, and activates it without an external .NET SDK or network access.

A successful build qualifies the exact plugin id and fingerprint only in the current host process; it does not change `dotnet-plugin-trust.json`. Build the project again after restarting DotCraft. Rebuilding the active fingerprint from the same project is a no-op.

The Turn that performs the build keeps its frozen tool snapshot. New plugin tools become available on the next Turn, and the build does not invoke them. Source edits are applied only when the agent calls `DotNetPlugin.Build`.

## Prepare a prebuilt bundle

A .NET plugin is an ordinary DotCraft plugin directory with a `dotnet` contribution. For an externally built plugin, include every managed and native dependency before installation. Discovery, installation, and activation do not restore NuGet packages, run MSBuild, or compile source.

```text
acme.review-core/
├── .craft-plugin/
│   └── plugin.json
└── lib/
    ├── Acme.ReviewCore.Plugin.dll
    ├── Acme.ReviewCore.Plugin.deps.json
    ├── Acme.ReviewCore.Api.dll
    └── private dependencies
```

The entry assembly's `.deps.json` must sit beside it — it is how the load context resolves everything the bundle brings with it.

```json
{
  "schemaVersion": 1,
  "id": "acme.review-core",
  "version": "1.0.0",
  "displayName": "Review Core",
  "description": "In-process review services.",
  "capabilities": ["dotnet"],
  "dotnet": {
    "minHostVersion": "0.5.0",
    "entryAssembly": "./lib/Acme.ReviewCore.Plugin.dll",
    "entryType": "Acme.ReviewCore.Plugin",
    "exportedApiAssemblies": ["./lib/Acme.ReviewCore.Api.dll"]
  },
  "dependencies": { "acme.review-base": "1.0.0" }
}
```

| Field | Required | Meaning |
|---|---|---|
| **`minHostVersion`** | yes | The oldest DotCraft host the plugin runs on, as `MAJOR.MINOR.PATCH`. |
| **`entryAssembly`** | yes | The managed assembly carrying the entry type. |
| **`entryType`** | yes | Full CLR name of a public, concrete, non-generic type with a public parameterless constructor. |
| **`exportedApiAssemblies`** | no | Contract assemblies declared dependents may bind to. The entry assembly cannot be exported. |
| **`dependencies`** | no | Minimum compatible provider versions. Valid only alongside `dotnet`. |

Plugin ids start with an ASCII letter or digit; subsequent characters may also be `.`, `_`, `:`, or `-`. `version` is mandatory whenever `dotnet` is present. Every path starts with `./`, stays inside the plugin root, and names a file that already exists in the built bundle.

### Reference DotCraft.Core, and do not ship it

The plugin API is `DotCraft.Core` itself, plus what it references transitively — `DotCraft.Agents` and Microsoft.Extensions.AI. There is no separate SDK assembly.

```xml
<ProjectReference Include="path/to/src/DotCraft.Core/DotCraft.Core.csproj" Private="false" />
```

The load context resolves every DotCraft assembly and its package closure **by simple name** against the copies already loaded in the host, ignoring the version a bundle ships. That is what keeps type identity single: a `ChatMessage` a middleware contribution rewrites is the same type the kernel dispatches on, even if the bundle carries its own `Microsoft.Extensions.AI.Abstractions.dll`. Shipping those assemblies only enlarges the bundle. Everything else resolves from the bundle's `.deps.json` and adjacent probing, confined to the bundle directory.

### Bind to a host version

Because the whole public surface of the kernel is the plugin API, compatibility is bound to the host version rather than to an append-only contract.

- `minHostVersion` is a hard gate. A host below it keeps the plugin `blocked` with `PluginHostVersionUnsatisfied` and runs none of its code.
- A newer host loads the plugin best-effort, and reports nothing about the `DotCraft.Core` the bundle was compiled against. If that difference breaks anything, it breaks at member resolution on first use, not at load.
- **Recompile against each host minor version.** One host minor is one compatibility target, and `minHostVersion` is how a plugin declares which one it was built for.

## Implement the entry point

Implement `IDotCraftPlugin` on one public type with a public parameterless constructor. The host constructs it once per activation generation.

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugin.AcmeReview;

public sealed class Plugin : IDotCraftPlugin
{
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        context.Contributions.Add<IToolSource>(new PluginTool());
        return ValueTask.CompletedTask;
    }
}

internal sealed class PluginTool : AIFunctionToolSource
{
    public override string SourceId => "acme-review";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        yield return AIFunctionFactory.Create(
            () => "Acme Review is active.",
            name: "acme_review",
            description: "Reports whether Acme Review is active.");
    }

    protected override ToolPolicyHints GetPolicyHints(
        AIFunction function,
        ToolPlanningContext context) => new(ReadOnly: true);
}
```

The activation context carries both directions of the plugin model:

| Member | Purpose |
|---|---|
| **`Contributions`** | The activation-only registration path. Every contribution names its contribution point and returns a generation-owned handle. |
| **`Services`** | A filtered, read-only view of public host application services. |
| **`Exports` / `Dependencies`** | Typed services across plugin boundaries. Activation-only. |
| **`Lifetime`** | Owned resources, background work, and the `Stopping` token. |
| **`ContentRoot` / `DataRoot` / `WorkspaceRoot`** | The generation's read-only shadow copy, the plugin's mutable data directory, and the workspace. |
| **`Settings`** | This plugin's effective settings, snapshotted for the activation generation. |

Treat `ContentRoot` as read-only and put mutable state under `DataRoot`.

Make every `Contributions.Add` call inside `ActivateAsync`. The host seals the registrar when activation commits, so later calls from background work are rejected. To change a generation's contribution set, change its inputs and let runtime reconciliation restart the generation.

### Own resources through Lifetime, not through contributions

Teardown revokes contribution handles, signals `Stopping`, drains admitted calls and tracked work, and only then disposes raw contribution targets. Register shared resources with `context.Lifetime.Own` or `OwnAsync`; those resources outlive contribution targets, and contributions can borrow them without owning them.

Background work goes through `context.Lifetime.Run`. It starts after activation commits and is cancelled through `Lifetime.Stopping` when teardown begins. Raw threads, static event subscriptions, untracked tasks, and global caches can pin the collectible load context: routing still stops immediately, but memory is not reclaimed until the stray reference is released, often only at process restart.

## Access host services

`context.Services` is a filtered `IServiceProvider` view. Resolve public application services from it to compose behavior out of kernel parts:

```csharp
using DotCraft.Sessions;

var sessions = (ISessionService?)context.Services.GetService(typeof(ISessionService))
    ?? throw new InvalidOperationException("ISessionService is unavailable.");
```

The provider is host-owned and read-only. A plugin cannot register, decorate, or replace container services. The view excludes the root provider, contribution registry, service-scope factories, host lifecycle, and plugin-runtime control plane. Never dispose a resolved service; dispose only what the plugin itself created, through `context.Lifetime`.

Consumption carries the same version binding as the rest of the kernel surface: a service you resolve today is guaranteed by the host version you compiled against, not by an append-only promise. Release callbacks, event subscriptions, and other references to host services before the generation stops so the load context can unload.

### Read your own settings

`context.Settings` is a snapshot of this plugin's effective `Plugins.Settings[id]` bag, captured when its generation activates. Its shape belongs to the plugin and the host does not validate it. A configuration edit becomes visible only after runtime reconciliation restarts the generation; it never mutates a captured activation context.

```csharp
var limit = context.Settings.TryGetProperty("checklistLimit", out var value)
    && value.TryGetInt32(out var parsed) ? parsed : 3;
```

Give every field a fallback because an unconfigured plugin reads an empty object. If you deserialize to plugin-defined types, keep serializer options and metadata plugin-owned so the generation can unload.

For the complete contribution catalog, ordering, typed exports, trust, and lifecycle, see [.NET Plugin API and lifecycle](./dotnet-plugin-reference).

## Related docs

- [.NET Plugin API and lifecycle](./dotnet-plugin-reference)
- [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample)
- [.NET Plugin architecture specification](https://github.com/DotHarness/dotcraft/blob/main/specs/architecture/dotnet-plugins.md)
- [Plugins & Tools](../../features/agent-system/plugins-tools)
- [Plugin Market](./plugin-market)
- [Desktop Plugins](./desktop-plugins)
- [AppServer protocol](../protocols/appserver-protocol)
- [Security and sandbox](../../features/self-hosted/security)
