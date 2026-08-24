# Build a .NET plugin

A .NET plugin runs **inside the DotCraft process**. Where an MCP server talks to DotCraft across a boundary, a .NET plugin composes the kernel from within: it contributes tools, prompt sections, middleware, and lifecycle observers through named contribution points, and it resolves host services to build those contributions out of DotCraft's own parts.

This page targets plugin authors. For the user-facing view of plugins, see [Plugins & Tools](../../features/agent-system/plugins-tools); for the manifest fields every plugin shares, see [Plugin Market](./plugin-market).

> [!CAUTION]
> A .NET plugin loads fully trusted code into the DotCraft process and receives that process's filesystem, network, credential, native interop, and OS authority. There is no managed sandbox and no permission model — safety is an installation-time trust decision, not a runtime boundary. Use MCP when code needs a real trust boundary.

The repository sample under `sdk/dotnet/samples/DotnetPluginSample/` contains two bundles, covers every public contribution point, and verifies the built result through the host's preflight and runtime.

## Prepare the bundle

A .NET plugin is an ordinary DotCraft plugin directory with a `dotnet` contribution. Build every managed and native dependency before installation: DotCraft never restores NuGet packages, runs MSBuild, or compiles source while it discovers, installs, or activates a plugin.

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

Plugin ids are canonical lowercase dotted identifiers, and `version` is mandatory whenever `dotnet` is present. Every path starts with `./`, stays inside the plugin root, and names a file that already exists in the built bundle.

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
using DotCraft.Contributions;
using DotCraft.Plugins;

namespace Acme.ReviewCore;

public sealed class Plugin : IDotCraftPlugin
{
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        var journal = new ReviewJournal(context.DataRoot);
        context.Lifetime.Own(journal);
        context.Contributions.Add<IToolSource>(new SummaryTool(journal));
        return ValueTask.CompletedTask;
    }
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

## Choose a contribution point

The catalog of contribution points is the whole contribution surface. Each one declares which capability tiers it supports:

| Tier | What it does |
|---|---|
| **A — Contribute** | Add an item alongside the existing ones, ordered by ascending `Order` with registration order breaking ties. |
| **B — Replace** | Shadow a *named* default with `ReplaceTarget`. The default returns as soon as the replacement's handle is disposed. |
| **C — Take over** | Terminal authority over a contribution point's assembled output. Realized through the Tier-B mechanism, on a contract that takes the assembled result. |

| Contribution point | Contract | Tiers |
|---|---|---|
| **Tool** | `IToolSource` | A |
| **System prompt section** | `ISystemPromptSection`, and `ISystemPromptAssembler` for Tier C | A, B, C |
| **Chat context item** | `IChatContextProvider` | A |
| **Thread prompt context** | `IThreadSystemPromptContextProvider` (`BaseInstructions` only) | A |
| **Pre-send context transform** | `IAgentContextSource` → `AIContextProvider` | A, B |
| **Chat middleware** | `IChatMiddleware` | A, B |
| **Dispatch stages** | policy, approval, recorder, and normalizer stage interfaces | A, B |
| **Thread lifecycle** | `IThreadLifecycleContributor` | A |
| **Turn lifecycle** | `ITurnLifecycleContributor` | A |
| **Thread runtime signals** | `IThreadRuntimeSignalContributor` | A |
| **Slash command** | `ICodeCommand` | A |
| **Tool restriction** | `IToolRestriction` | A |
| **Compaction summary** | `ICompactionSummarizer` | B |
| **Compactable tool** | `ICompactableToolPolicy` | A, B |
| **SubAgent runtime** | `ISubAgentRuntimeSource` | A |
| **Trace sink** | `ITraceSink` | A |
| **Auxiliary generators** | `ICommitMessageSuggester`, `IWelcomeSuggester` | B |

Failure handling is specific to each contribution point. Observation and fan-out contributions normally log and skip a failing contributor; authoritative transforms such as result normalization or compaction can fail the owning operation. Return expected failures through the contract's result type instead of throwing.

### Tier A — add a tool

A plugin contributes tools through `IToolSource`, the same contribution point the kernel's own tool sources use. A source declares each tool as a **definition** — identity, canonical name, description, JSON schema, policy hints — joined to a **runtime binding**, the code that executes it.

The host interposes itself at the contribution boundary. It copies the definition into its own objects, re-keys the identity to `(pluginId, toolId)` with `PluginNative` provenance, and replaces the binding with a proxy that holds only `(pluginId, generationId, toolId)` and resolves your source again on every call. A frozen tool snapshot therefore holds nothing your plugin allocated: revoking the contribution takes effect at once, and a call dispatched from an older snapshot fails with `tool_unavailable`. Plugin tools get the same schema validation, policy, approval, hooks, and recording as built-in ones, and `PolicyHints` inform host policy without overriding it.

```csharp
internal sealed class SummaryTool(ReviewJournal journal) : IToolSource, IToolRuntime
{
    private const string ToolId = "review-summary";

    public string SourceId => "acme.review-core.summary";

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.PluginNative,
            SourceId,
            new SourceToolId(ToolId));
        var registration = new ToolRegistration(
            new ToolDefinition(
                definitionId,
                new ToolName("review", "summary"),
                "Normalizes review text.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { text = new { type = "string" } },
                    required = new[] { "text" },
                    additionalProperties = false
                }),
                policyHints: new ToolPolicyHints(RequiresApproval: false, ReadOnly: true)),
            new ToolRuntimeBinding(
                new RuntimeBindingId($"{SourceId}:{ToolId}:{context.Revision}"),
                definitionId,
                this,
                ToolBindingLeases.AlwaysAvailable,
                SourceId,
                context.Revision),
            ToolProjectionShape.StandardPair);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([registration]);
    }

    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        journal.Write("echo tool invoked");
        return ValueTask.FromResult(ToolExecutionResult.Succeeded(
            arguments["text"]?.GetValue<string>() ?? string.Empty));
    }
}
```

`GetRegistrationsAsync` runs once per planning pass, so keep it cheap and free of side effects and return the tools valid for the `ToolPlanningContext` it receives. One source may declare several tools; a duplicate tool id within one plugin is reported as a diagnostic and skipped rather than failing activation.

Report expected failures with `ToolExecutionResult.Failed` and a `ToolError`, whose code stays stable. A thrown exception's text is discarded and reaches the model only as an unspecified tool failure. The boundary is JSON-only in both directions: the host copies the arguments in, and copies the text, the structured content, and the error out.

### Tier A — add a slash command

A plugin contributes a slash command through `ICodeCommand`: a name, optional aliases, a description, and an `Expand` that turns one invocation into the text the turn runs on.

```csharp
internal sealed class TriageCommand(ReviewService service) : ICodeCommand
{
    public string Name => "triage";

    public string Description => "Summarize the open review queue";

    public IReadOnlyList<string> Aliases => ["tri"];

    public string? Expand(CommandInvocation invocation) =>
        service.BuildTriagePrompt(invocation.Arguments);
}
```

A contributed command is a markdown custom command whose body is code, and the host serves it exactly where it serves those: the command palette, `command/list`, `command/execute`, and the ACP slash-command list all pick it up with no client change. It produces model input only — returning a direct reply to the user stays with the host's own commands.

It never shadows anything: a built-in command, a workspace markdown command, and a workflow command all keep a contested name. Among contributions the lowest `Order` claims a name, and returning `null` declines the invocation so the next contribution may answer it. Revoking the handle removes the command from every listing and every resolution at once — the contribution point is read per call, so there is nothing left registered anywhere.

### Tier B — replace a named default

Built-in prompt sections and middleware register under stable target names. Set `ReplaceTarget` to one of them and your contribution shadows it for as long as its handle lives:

```csharp
context.Contributions.Add<ISystemPromptSection>(
    new ReviewResponseStyleSection(),
    new ContributionOptions(ReplaceTarget: SystemPromptSectionNames.ResponseStyle));
```

The agent's built-in memory provider registers on the same terms, under `AgentContextSourceNames.Memory`; replacing it takes over the whole system prompt, and `AgentContextRequest.PromptInputs` carries the build-time values the built-in would have used — tool names, deferred MCP servers, the SubAgent profile section, the skill-variant target. It is the one target where returning `null` **declines instead of suppressing**: the built-in composes in its place, so no agent runs without a prompt.

Suppression is a replacement that produces nothing: a section returning `null` removes the built-in outright, and there is no separate remove verb. When two replacements target the same name, a thread-scoped one beats a workspace-scoped one and the later registration wins within a scope; `Order` only orders the resolved list and never decides a replacement. The loser is recorded as a `ReplaceConflict` diagnostic rather than failing the contribution point.

### Tier C — take over a contribution point's output

A takeover is an ordinary contribution point whose contract receives the assembled default result and returns the final one — `ISystemPromptAssembler` for the system prompt. The consumer applies the **last** contribution of the resolved list, so "at most one active takeover" follows from ordering rather than from a rule the registry enforces. A takeover that neither out-orders nor replaces its predecessor is simply not effective.

### Order and scope

Order is local to one contribution point. Use that point's published target names and order constants when positioning relative to a built-in; lower values run first unless the contract says otherwise. `ContributionOptions.Scope` is `Workspace` by default; `ContributionOptions.ForThread(threadId)` limits a contribution to one thread. A forked thread does not inherit thread-scoped contributions; use workspace scope or re-register on its `started` lifecycle event when a contribution should follow a fork.

Contribution points evaluated per turn or per call observe a change on the next evaluation. Those captured into per-thread state — the tool snapshot, the agent's pipeline and instructions — are rebuilt through the host's invalidation chain. In-flight turns finish with the contribution set they started with.

## Export and consume a typed service

Put public service interfaces in their own assembly and list it in `exportedApiAssemblies`. The provider exports an implementation during activation:

```csharp
context.Exports.Add<IReviewService>(new ReviewService());
```

A direct consumer declares the minimum compatible provider version it needs and resolves the interface during its own activation:

```json
{ "dependencies": { "acme.review-core": "1.0.0" } }
```

```csharp
var review = context.Dependencies.GetRequired<IReviewService>("acme.review-core");
```

Dependencies coordinate generation lifetime and API sharing; they do not resolve private packages. Resolution is activation-only, and providers activate before consumers and stop after them, so capture the resolved service in `ActivateAsync` and keep all work under the consumer's lifetime.

Keep exported signatures to plugin-owned exported types and host assemblies both plugins share. Within one consumer's providers, exported API assembly simple names must be unique; a conflict fails with `PluginApiAssemblyConflict`.

Dependency versions are minimums within one compatibility line. `"acme.review-core": "1.2.0"` accepts `1.2.0` and later `1.x` versions, but not `2.0.0`. For `0.x`, both major and minor must match: `0.2.1` accepts later `0.2.x` versions, not `0.3.0`. A provider below the minimum or outside the line leaves the consumer `blocked` with `PluginDependencyUnsatisfied`.

Within a compatibility line, a provider must keep each exported API assembly's identity unchanged: simple name, `AssemblyVersion`, culture, and public-key token. A breaking API change starts a new compatibility line and should normally use a new plugin id and API assembly identity. Declare the lowest compatible version that provides the API you use.

## Trust

Installing or enabling a `dotnet` plugin never grants trust. It runs only when enabled and the current bundle fingerprint already has an explicit grant in the machine-local authority; otherwise it remains blocked.

- **Grants bind an exact id and fingerprint.** The client asks for trust by plugin id; the server binds the grant to the bytes it has actually accepted. Several fingerprints of the same plugin id may remain granted. Changed bytes are `modified` only when their fingerprint has no matching grant.
- **Paths are part of the fingerprint.** DotCraft hashes a versioned, length-delimited bundle tree, so moving bytes between files changes identity as reliably as changing the bytes themselves. The deployment-only `.builtin` marker is excluded from both the identity and runtime snapshots.
- **The authority is separate from configuration.** Grants live in `dotnet-plugin-trust.json` next to global configuration. The file is not merged configuration, and workspace config cannot grant trust.
- **There is no implicit trust tier.** Every `dotnet` plugin needs an explicit grant, host-shipped bundles included.
- **Revocation is fingerprint-specific.** Revoking removes only the current plugin id and fingerprint pair. It stops the active closure when that pair was its authority, while grants for other fingerprints of the same id remain intact.

Without a matching grant, the plugin is `blocked` on `PluginUntrusted` or `PluginTrustModified` and **no load context is created**, so none of its code has run.

## Lifecycle and updates

Each activation gets its own collectible load context and an opaque generation id. Activation loads the bundle from a per-generation shadow copy, so the installed directory can be replaced while a generation is live.

| State | Meaning |
|---|---|
| **`stopped`** | No generation is live because the plugin is disabled. |
| **`blocked`** | Not attempted, and here is why: preflight failed, `minHostVersion` is unsatisfied, a dependency is unavailable, or trust is missing or stale. No load context exists. |
| **`activating`** | A candidate generation is being built; nothing of it is published yet. |
| **`active`** | One generation is committed and accepting calls. |
| **`deactivating`** | Admission is closed and the generation is draining. |
| **`faulted`** | Attempted, and here is how it broke: construction, activation, or registered background work failed. |
| **`reclaiming`** | Functionally stopped and routing nothing; its memory has not come back yet. |

`blocked` is non-terminal and re-evaluated whenever its cause can have changed — a host upgrade, a dependency activating, a trust grant, a reinstall. A `faulted` plugin is re-attempted by disabling and re-enabling it.

### Revocation is guaranteed, reclaim is not

Deactivation revokes every contribution handle first, so no new call reaches that generation; an older tool snapshot receives `tool_unavailable`. For an ordinary mutation, the runtime waits up to its cleanup timeout. If plugin work ignores cancellation, functional teardown remains pending: the plugin cannot reactivate and its dependency providers cannot stop until that work actually settles.

Host shutdown waits for actual functional teardown before disposing providers and the host root, even when that exceeds the cleanup timeout. A service manager or other outer process owns any hard shutdown deadline. Once functional teardown is complete, assembly-memory reclaim is best-effort and blocks neither replacement, dependency teardown, nor shutdown. `leakedGenerations` and `restartRecommended` expose generations whose load contexts remain pinned; only a process restart releases their memory.

### Replace a bundle

DotCraft never compiles a plugin's source and never watches plugin roots, so new bytes become executable only through an explicit cycle: **disable the plugin, replace the files, enable it again**. That is the whole update path, and it is also the loop to run after an external `dotnet build` when the plugin is installed from a root you edit in place.

Disabling revokes the .NET generation and its consumers, consumers first. A filesystem mutation also asks root-backed declarative contributions to stop; if that step fails, the mutation returns `notApplied` with `PluginContributionQuiesceFailed` and leaves the bundle directory unchanged. Re-enabling re-admits the current bytes.

The new bytes are a new bundle with a new fingerprint. They activate only if that exact fingerprint was already granted; otherwise trust becomes `modified` until the user confirms it. A content change that keeps the same version still produces a new generation. DotCraft does not roll back to the previous bundle if the new one ends `blocked` or `faulted`.

### Client operations

| Method | What it does to a `dotnet` plugin |
|---|---|
| `plugin/install`, `plugin/installLocal` | Copies a bundle in and never grants trust. Activation still requires an enabled plugin and a matching fingerprint grant. |
| `plugin/setEnabled` | Changes intent only. Enabling activates when every other precondition holds; disabling always applies. |
| `plugin/setTrusted` | Grants or revokes the current exact plugin-id/fingerprint pair, replanning the closure either way. |
| `plugin/remove` | Quiesces .NET and root-backed contributions, then deletes the directory. A quiesce failure leaves it unchanged. |

Every mutation returns a `PluginOperationResult` — `applied`, `noChange`, or `notApplied` — with the ids and states of every other plugin the batch affected. The operation outcome is independent of the runtime state it produced: an applied install can end `blocked`. Blockers carry stable codes and structured parameters; clients should offer remedies for trust, host-version, manifest, dependency, and activation failures. Wire shapes and the authoritative code list are in the [AppServer protocol](../protocols/appserver-protocol).

## Related docs

- [Plugins & Tools](../../features/agent-system/plugins-tools)
- [Plugin Market](./plugin-market)
- [Desktop Extensions](./desktop-extensions)
- [AppServer protocol](../protocols/appserver-protocol)
- [Security and sandbox](../../features/self-hosted/security)
