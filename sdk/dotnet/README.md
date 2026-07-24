# DotCraft.Sdk

`DotCraft.Sdk` is the .NET 10 SDK for building DotCraft clients, tools, and
native app integrations.

This package is intentionally documented briefly here for NuGet. For examples,
guides, and API usage patterns, use the public DotCraft SDK documentation.

## Install

Install the package with `dotnet add package DotCraft.Sdk`.

## What It Covers

Use `DotCraft.Sdk` when a .NET application needs to connect to a DotCraft
workspace or participate in an active DotCraft thread.

The package includes:

- Hub-managed local workspace discovery and AppServer startup.
- Direct AppServer WebSocket JSON-RPC connections.
- Thread and turn clients for starting, resuming, listing, and running
  persistent conversations.
- Buffered and streamed run helpers over DotCraft's normalized event model.
- Runtime dynamic tools, including callback routing for server-initiated tool
  calls.
- Approval and user-input callbacks for interactive clients.
- App Binding handoff parsing and app-side binding helpers.
- Low-level JSON-RPC transport APIs for tests, embedded hosts, and advanced
  integrations.

## Documentation

Start with the public SDK documentation:

- [SDK overview](https://www.dotcraft.net/developing/sdks/)
- [.NET SDK reference](https://www.dotcraft.net/developing/sdks/dotnet)
- [SDK quickstart](https://www.dotcraft.net/developing/sdks/quickstart)
- [Threads and runs](https://www.dotcraft.net/developing/sdks/runs)
- [Tools and approvals](https://www.dotcraft.net/developing/sdks/tools)
- [Build an App / App Binding](https://www.dotcraft.net/developing/integrations/build-an-app)
- [AppServer Protocol](https://www.dotcraft.net/developing/protocols/appserver-protocol)

The repository also includes sample projects under `sdk/dotnet/samples`.

## Namespaces

The main public namespaces are:

- `DotCraft.Sdk.AppServer` for `DotCraftClient`, thread and turn wrappers, run
  helpers, dynamic tool models, and typed run errors.
- `DotCraft.Sdk.AppBinding` for App Binding handoff parsing, binding RPC
  helpers, and standard App Binding tool error helpers.
- `DotCraft.Sdk.Hub` for local Hub discovery, status probing, AppServer lookup,
  and AppServer ensure operations.
- `DotCraft.Sdk.Wire` for low-level JSON-RPC transports and raw request APIs.
- `DotCraft.Sdk.Tools` for attribute-based runtime dynamic tool authoring and
  schema generation.

## Author Runtime Dynamic Tools

Prefer typed arguments and `DynamicToolRegistry` over hand-written JSON Schema.
The registry generates a closed schema, binds arguments, injects cancellation,
and normalizes tool failures.

```csharp
using System.ComponentModel;
using DotCraft.Sdk.AppServer;
using DotCraft.Sdk.Tools;

public sealed class GetIssueArgs
{
    [Description("Issue id to read.")]
    public required string Id { get; init; }
}

public sealed class IssueTools(IssueStore issues)
{
    [DynamicTool("GetIssue", "Read an issue from MyApp.")]
    public Task<Issue> GetIssueAsync(GetIssueArgs args, CancellationToken ct) =>
        issues.GetIssueAsync(args.Id, ct);
}

var registry = new DynamicToolRegistry();
registry.Register(new IssueTools(issueStore), "myapp");

var declarations = RuntimeDynamicToolDeclarationBuilder.Build(
    registry.ListDescriptors(),
    new Dictionary<string, string> { ["myapp"] = "MyApp issue tools." });
```

Pass `declarations` to `DotCraftThreadStartRequest.DynamicTools` or
`DotCraftThreadResumeRequest.DynamicTools`. Register the callback through
`DotCraftThread.OnToolCall`, invoke the registry, and map the outcome to a
`DynamicToolResult`. Successful model-visible results must include a useful text
`ToolContentItem`; structured content alone is not sufficient.

## Compatibility

`DotCraft.Sdk` targets `net10.0`.

SDK behavior, public API shape, protocol wrappers, and compatibility rules are
specified in the
[DotCraft .NET SDK Specification](https://github.com/DotHarness/dotcraft/blob/main/specs/sdk/dotnet.md).

DotCraft AppServer remains the source of truth for thread state, queue behavior,
approvals, model catalog resolution, permissions, and persistence. The SDK is a
client over those protocols, not a separate runtime authority.

## Security

Do not log Hub or AppServer bearer tokens. Treat endpoint URLs containing
`token=` query values as secrets.

App Binding request tokens and handoff URLs should also be handled as sensitive
short-lived credentials.

## Links

- [Source repository](https://github.com/DotHarness/dotcraft)
- [Issues](https://github.com/DotHarness/dotcraft/issues)
- [License](https://github.com/DotHarness/dotcraft/blob/main/LICENSE)
