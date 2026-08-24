# DotCraft .NET Plugin Architecture

| Field | Value |
|---|---|
| **Version** | 0.14.0 |
| **Status** | Living |
| **Date** | 2026-08-24 |
| **Related specs** | [Plugin Architecture](plugin-architecture.md), [Runtime Module Boundaries](runtime-module-boundaries.md), [Session Core](session-core.md), [Tool Architecture](tools-architecture.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

This specification defines trusted, in-process .NET plugins. The shared plugin manifest,
discovery, installation, and content contributions remain owned by
[Plugin Architecture](plugin-architecture.md). A bundle may contain both content and .NET
contributions.

---

## 1. Scope and invariants

DotCraft combines two extension paths:

- Built-ins and compiled modules use direct, strongly typed contribution contracts on the host's
  normal hot paths.
- Trusted .NET plugins use the same contracts through a generation boundary that stages
  registration, controls calls, and supports best-effort unload.

The following invariants are normative:

1. The kernel owns the contribution-point catalog. Loading an assembly does not create an
   extension point.
2. Plugins never mutate or receive the built root DI container. They may resolve public,
   host-owned application services through a filtered view and must not dispose those services.
3. `ActivateAsync` is transactional. Registrations made during activation are invisible until the
   callback succeeds and the generation's call gate is published. Failure or timeout discards the
   staged set.
4. No long-lived host object calls a raw collectible-ALC object. The runtime registers host-owned,
   contract-specific adapters; factory results and async streams remain behind the same gate.
   Plugin tools use the stricter projection in §8.
5. Deactivation is ordered: revoke routing, reject new calls, signal `Stopping`, drain admitted calls
   and tracked work, dispose plugin-owned objects, then request ALC unload.
6. Routing revocation is deterministic. Memory reclamation is observable but best-effort.
7. Plugin code is fully trusted. The generation boundary is a lifecycle and type-identity
   mechanism, not a security sandbox.

---

## 2. Trust

An in-process plugin has the host process's operating-system authority. Install, enablement, and
trust are separate decisions:

- Trust grants live in the machine-local `dotnet-plugin-trust.json` authority file next to global
  configuration. This file is not part of configuration layering and cannot be overridden by a
  workspace.
- An enabled .NET plugin activates only when the exact pair of canonical plugin id and accepted
  bundle fingerprint has a durable grant. Several fingerprints for one plugin id may remain
  granted at the same time.
- `plugin/setTrusted` identifies the plugin; the server computes the currently accepted
  fingerprint. Clients do not choose the fingerprint. Grant adds that exact pair; revoke removes
  only that exact pair, leaving other grants for the same plugin id intact.
- Changed bytes require a grant for their new fingerprint. They do not erase grants for earlier
  fingerprints. Revocation stops the active generation and its dependants when it withdraws the
  active fingerprint's grant.
- A grant that cannot be persisted is not applied and activates nothing.
- Workspace configuration cannot grant .NET trust.
- Discovery, installation, and metadata preflight do not execute plugin code. Enabling may execute
  code only when all admission requirements, including an existing matching grant, are satisfied.

Marketplace or publisher information is display evidence, not a verified identity. Signature
verification, publisher attestation, and capability permissions are outside this version.
Lower-trust extensibility should use out-of-process tools, hooks, workflows, or channel adapters.

---

## 3. Host ABI and assembly identity

Plugins compile against `DotCraft.Core` and its public transitive contracts, including
`DotCraft.Agents` and Microsoft.Extensions.AI. There is no separate narrow plugin SDK.

The plugin load context shares the host's allowlisted DotCraft and framework assemblies by simple
name. A bundle copy of a shared assembly is ignored. Other dependencies resolve from the
generation shadow copy and its `.deps.json`. This preserves type identity for contribution
contracts while keeping plugin-private dependencies collectible.

The public host surface is a version-bound ABI, not an append-only compatibility promise:

- `dotnet.minHostVersion` is a hard admission floor.
- A plugin built against a different compatible host version loads best-effort; missing members
  may still fail on first use.
- Authors should compile and test against each supported DotCraft minor line and declare the
  earliest host whose APIs they actually use.

Provider plugins may export typed service-contract assemblies to direct dependants. Exported
assembly simple names must be unique within each consumer's dependency closure. The host validates
structure and identity, not every referenced member.

---

## 4. Contribution registry

`IContributionRegistry` is the kernel's register, resolve, revoke, and propagation primitive.
Consumers depend on `IContributionView`; composition code owns the write side.

```csharp
public interface IContributionView
{
    IReadOnlyList<T> Resolve<T>(string? threadId = null)
        where T : class, IContributionContract;
    IReadOnlyList<ContributionEntry<T>> ResolveEntries<T>(string? threadId = null)
        where T : class, IContributionContract;
    long GetRevision<T>() where T : class, IContributionContract;
}

public interface IContributionRegistrar : IDisposable
{
    ContributionOrigin Origin { get; }
    IContributionHandle Add<T>(T contribution, ContributionOptions? options = null)
        where T : class, IContributionContract;
}
```

### 4.1 Ordering, replacement, and scope

- `Order` is compared only within one contribution point; lower values resolve first and
  registration order breaks ties.
- `TargetName` names a default. `ReplaceTarget` replaces that name. Thread scope beats workspace
  scope; within one scope the later replacement wins. Losing replacements are diagnosed and are
  absent from the effective list.
- Disposing a handle is idempotent. It removes membership and restores any shadowed default.
- A thread-scoped registration applies only to its thread. Permanent deletion calls
  `ReleaseThread`; archive does not. Forks do not copy thread-scoped registrations.
- `BeginBatch` coalesces change notification. Subscribers run outside registry locks, and one
  failing subscriber does not starve the rest.

Built-ins, modules, and plugins share these rules. Plugin registrars add two lifecycle rules:

- During `ActivateAsync`, `Add` returns a generation-owned handle but does not publish the entry.
  Successful activation seals the registrar and publishes the staged set as one mutation batch.
  Calls to `Add` after activation are rejected; background work cannot change the contribution
  set of an active generation.
- Registry membership never owns the raw plugin object. Revocation removes host adapters first;
  raw targets are disposed in reverse registration order only after the generation gate drains.

### 4.2 Propagation

Call-time points resolve the current list for each operation. Five kernel contracts are captured
while an agent is built: `IToolSource`, `IToolRestriction`, `IChatMiddleware`,
`IAgentContextSource`, and `ISubAgentRuntimeSource`. Their mutations invalidate live thread agents
for the next turn; an in-flight operation keeps the view it started with. Prompt sections,
assemblers, and commands remain dynamically resolved. Command changes release the cached command
summary, while thread-prompt changes release pages for the current providers.

### 4.3 Failure semantics

Registration and composition failures are isolated so one bad entry does not prevent unrelated
entries from being published or revoked. Callback behavior is defined by each contribution point:
fan-out observers are normally logged and skipped, authority or fold failures may fail their host
operation, and tool failures use stable tool errors. There is no blanket promise that every plugin
exception is ignored.

---

## 5. Plugin-facing contribution catalog

The catalog reuses kernel contracts. Capability tiers are:

| Tier | Meaning |
|---|---|
| A | Add an ordered item. |
| B | Replace a named default until the handle is revoked. |
| C | Apply terminal composition to the point's assembled result. |

| Area | Contracts | Tiers | Evaluation |
|---|---|---|---|
| Prompt | `ISystemPromptSection` | A, B | Resolved per prompt build |
| Prompt | `ISystemPromptAssembler` | C | Resolved per prompt build |
| Context | `IChatContextProvider` | A | Per request; returned lines are copied by the host adapter |
| Context | `IThreadSystemPromptContextProvider` | A | Base-instruction path only; `ThreadContextItem` is rejected before activation; resolved per prompt build |
| Context | `IAgentContextSource` | A, B | Materialized; factory result is a gated host `AIContextProvider` |
| Compaction | `ICompactionSummarizer` | B | Per compaction |
| Compaction | `ICompactableToolPolicy` | A, B | Per compaction |
| Model pipeline | `IChatMiddleware` | A, B | Materialized; returned client is host-gated, including streams |
| Tools | `IToolSource` | A | Materialized through the §8 projection |
| Tools | `IToolPolicyEvaluator`, `IToolApprovalEvaluator` | A, B | Per dispatch, first denial wins |
| Tools | `IToolInvocationRecorder` | A, B | Per dispatch, contained fan-out |
| Tools | `IToolResultNormalizer` | A, B | Per dispatch, ordered fold |
| Tools | `IToolRestriction` | A | Materialized per tool snapshot |
| Lifecycle | `IThreadLifecycleContributor`, `ITurnLifecycleContributor` | A | Contained fan-out |
| Lifecycle | `IThreadRuntimeSignalContributor` | A | Bounded asynchronous fan-out |
| Suggestions | `ICommitMessageSuggester`, `IWelcomeSuggester` | B | Authority per request |
| Subagents | `ISubAgentRuntimeSource` | A | Materialized; runtime is a gated host adapter |
| Commands | `ICodeCommand` | A | Metadata copied; expansion per invocation |
| Tracing | `ITraceSink` | A | Bounded asynchronous fan-out when tracing is enabled |

The runtime uses an explicit adapter policy for this table. Unknown contribution contracts are
rejected instead of falling back to an unsafe reflection proxy. Stable metadata and lazy
collections are copied while the gate is held. Async leases cover task completion and complete
stream enumeration, not merely method return.

The following are also closed plugin surfaces: root DI mutation, `IDotCraftModule`,
`WorkspaceRuntime` composition, Session Core replacement, protocol handlers, persistence, and
wire-model implementations. Plugins may consume public services; they do not replace these owners.

---

## 6. Activation API

```csharp
public interface IDotCraftPlugin
{
    ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken);
}

public interface IPluginActivationContext
{
    PluginIdentity Plugin { get; }
    string ContentRoot { get; }
    string DataRoot { get; }
    string WorkspaceRoot { get; }
    JsonElement Settings { get; }
    IServiceProvider Services { get; }
    IContributionRegistrar Contributions { get; }
    IPluginServiceExportRegistrar Exports { get; }
    IPluginDependencyResolver Dependencies { get; }
    IPluginLifetime Lifetime { get; }
}
```

The manifest names one public, concrete, non-generic entry type with a public parameterless
constructor. Activation should register contributions, exports, owned resources, and tracked work;
work registered with `Lifetime.Run` starts only after activation commits. `Contributions`,
`Exports`, dependency resolution, and `Lifetime.Run` are activation-only and seal when activation
completes.

`Services` is a filtered, host-owned view of public application services. It does not expose the
root provider, contribution registry, service-scope factories, host lifecycle, or plugin-runtime
control plane. Plugins
must not dispose resolved services. `Settings` is a host-owned snapshot of the plugin's effective
`Plugins.Settings[id]` bag captured for the activation generation and is an empty object when
absent. Configuration changes require runtime reconciliation and a generation restart; they never
mutate an already activated context.

Resources and background work belong to `IPluginLifetime`. Contribution instances may borrow
those resources; they must not dispose them. Raw threads, static subscriptions, native callbacks,
and untracked tasks can pin a generation and are plugin defects.

### 6.1 Manifest

The `dotnet` block extends the shared plugin manifest:

```json
{
  "schemaVersion": 1,
  "id": "acme.review-core",
  "version": "1.2.0",
  "capabilities": ["dotnet"],
  "dotnet": {
    "minHostVersion": "0.3.0",
    "entryAssembly": "./lib/Acme.ReviewCore.Plugin.dll",
    "entryType": "Acme.ReviewCore.Plugin",
    "exportedApiAssemblies": ["./lib/Acme.ReviewCore.Api.dll"]
  },
  "dependencies": { "acme.review-base": "1.0.0" }
}
```

Rules:

- Plugin, host-floor, and dependency versions use canonical `MAJOR.MINOR.PATCH`.
- `version` and `minHostVersion` are required for .NET plugins.
- Paths are confined, `./`-relative bundle paths.
- Managed assembly paths are compared using ordinal case-insensitive semantics so admission is
  consistent across host operating systems.
- `dependencies` is .NET-only, cannot name the plugin itself, and each value is the minimum
  provider version within one compatibility line. When the required major version is at least 1,
  a provider must be greater than or equal to the minimum and share its major version. A `0.x`
  requirement additionally requires the same minor version.

---

## 7. Admission, activation, and teardown

### 7.1 Snapshot and preflight

Installed bytes are runtime identity. Admission fingerprints an immutable accepted snapshot;
each activation loads a separate shadow copy so install/remove can change the installed directory
without mutating a live generation.

The fingerprint input is a versioned canonical stream. Every directory and file record carries an
entry kind and an explicit UTF-8 path length; file records also carry an explicit content length
before their bytes. This makes different bundle trees unambiguous before SHA-256 is applied. The
deployment-only `.builtin` marker file is excluded from trust identity and runtime snapshots.

Metadata preflight uses `System.Reflection.Metadata` and does not load or execute the entry
assembly. It validates the target framework, host floor, entry type shape, declared assemblies,
dependency metadata, and `.deps.json`. Deterministic failures produce coded blockers before an ALC
is created.

### 7.2 States

```text
Blocked <-> Stopped -> Activating -> Active -> Deactivating -> Stopped
                          |                         |
                          v                         v
                       Faulted                 Reclaiming -> Stopped
```

- `Blocked`: activation was not attempted because trust, preflight, version, or dependency
  admission failed.
- `Faulted`: executable activation was attempted and failed or timed out.
- `Reclaiming`: routing and cleanup completed, but the collectible context or shadow files remain.

Only one live generation exists per plugin id. Updates stop the previous generation before
activating the accepted replacement; there is no automatic rollback.

Request cancellation may cancel waiting to acquire the runtime mutation lock. Once a transition
starts, caller cancellation must not strand a node in `Activating` or `Deactivating`. The activation
timeout and cleanup timeout bound how long ordinary mutation paths wait, not how long ignored
cancellation may keep plugin code alive. Until that code actually settles, the runtime retains the
pending transition: it cannot activate a replacement or tear down a provider that the pending
generation may still call.

### 7.3 Activation transaction

Activation performs:

1. create generation shadow copy and collectible ALC;
2. load exported API assemblies and the entry type;
3. create lifetime, exports, dependencies, and a staging contribution registrar;
4. invoke `ActivateAsync` under the activation timeout;
5. seal activation-only registrars;
6. re-read the durable fingerprint trust authority;
7. publish the generation call gate and commit staged host adapters as one transaction;
8. publish `Active`, then start tracked background work.

Any failure before commit discards staged registrations, disposes constructed plugin-owned
objects, requests ALC unload, and leaves no callable route. A timed-out callback that ignores
cancellation can never commit. The runtime retains it as a pending activation until the callback
settles and its functional teardown finishes; no replacement generation or dependency-provider
teardown may overlap it.

### 7.4 Deactivation and reclaim

Deactivation performs:

1. mark the registrar closed and revoke every registry handle without disposing raw targets;
2. remove the generation from gate lookup and close admission;
3. cancel `Lifetime.Stopping` so admitted calls and tracked work can begin a cooperative exit;
4. await already admitted contribution and tool calls, then tracked work;
5. dispose raw contribution targets, exports, entry, and owned lifetime resources in defined
   reverse order;
6. clear host references and call `AssemblyLoadContext.Unload()`;
7. track the ALC weak reference and generation-directory deletion off the mutation path.

After step 1 no new resolution reaches the plugin. Retained host adapters reject later calls with
an unavailable failure and never invoke their raw target. For an ordinary mutation,
`CleanupTimeout` bounds the wait for steps 3-5. If functional teardown is still running, the node
remains `Deactivating`; reactivation and dependency-provider teardown wait for its actual
completion. Only then may it enter `Reclaiming`, where only collectible-context and shadow-file
reclaim remain.

Host shutdown waits for actual functional teardown so consumers finish before their providers and
the host root are disposed. That safety wait is deliberately not bounded by `CleanupTimeout`; an
outer process or service manager owns any hard shutdown deadline.

The runtime never forces GC during normal operation. A collected generation deletes its shadow
copy; transient deletion failures are retried. Once functional teardown has completed, a pinned
context increments the leaked-generation projection and may recommend restart, but does not block
reactivation, dependent teardown, or shutdown.

### 7.5 Dependencies and typed exports

Providers activate before consumers; consumers stop before providers. A dependency version is a
minimum within one compatibility line, not an unbounded lower constraint: stable versions must be
greater than or equal to the minimum and share its major version, while `0.x` versions must also
share its minor version. Missing, disabled, blocked, too-old, or incompatible-line providers block
the consumer. Cycles and API assembly-name conflicts are deterministic blockers.

Across upgrades within one compatibility line, a provider must preserve each exported API
assembly's identity: simple name, `AssemblyVersion`, culture, and public-key token. A breaking API
change requires a new compatibility line and should normally use a new plugin id and API assembly
identity so old and new consumers cannot bind accidentally.

`IPluginDependencyResolver` is activation-only. A consumer may capture a direct provider export
for its generation. Correctness relies on dependency stop order and tracked work; exported objects
are not independently hot-swapped. Plugins must not call an export from work that outlives their
generation.

---

## 8. Tool containment

Plugin tools use `IToolSource`, but raw plugin registrations never enter a frozen
`EffectiveToolSnapshot`. A host aggregate source:

- resolves plugin-origin sources by `(pluginId, generationId)`;
- copies definitions, schemas, annotations, results, and stable errors into host-owned values;
- rewrites identity and policy scope so a plugin cannot claim another source or bypass host policy;
- replaces runtimes and leases with host proxies that re-resolve the live generation per call;
- holds the generation call lease across planning, invocation, thread release, and fork callbacks;
- returns stable `tool_unavailable` after revocation;
- omits host-private result channels and never forwards provider-flat naming overrides.

Schema validation, authority, policy, approval, hooks, recording, and result normalization remain
owned by [Tool Architecture](tools-architecture.md).

---

## 9. Protocol projection

AppServer projects plugin state; it does not own another plugin runtime.

- Mutations include install, install-local, remove, enable/disable, and `plugin/setTrusted`.
- `PluginOperationResult` reports `applied`, `noChange`, or `notApplied`; an applied operation may
  still leave a plugin blocked.
- `plugin/snapshot/updated` carries a monotonic revision. Clients reconcile by revision and ignore
  stale responses.
- Runtime projection includes state, blockers, trust, host floor, active generation, reclaim
  counts, diagnostics, contributed tools, and restart recommendation.
- Workspace plugin mutations are serialized from their first config/filesystem read through
  runtime reconcile and snapshot publication. `plugin/list` and `plugin/view` are serialized against
  that publication boundary, so a response cannot combine a post-mutation projection with the
  preceding revision.
- .NET generation revocation cannot veto a mutation. Quiescing root-backed content contributions
  can fail; that failure aborts the filesystem/config mutation and restores the prior projection.

Exact methods and wire fields are owned by
[AppServer Protocol](../protocols/appserver-protocol.md).

---

## 10. Non-goals and open decisions

Non-goals for this version:

- managed-code sandboxing, signatures, publisher identity, or capability permissions;
- runtime NuGet restore, source compilation, or dependency version-range solving;
- root DI mutation, dynamic module loading, Session Core or persistence replacement;
- external model-provider contributions;
- overlapping generations, automatic rollback, Native AOT, or bundle filesystem watching;
- guaranteed ALC collection.

Open decisions are limited to public-contract questions:

1. When should signed publishers supplement fingerprint trust?
2. Is member-reference preflight worth the compatibility and maintenance cost?
3. Should development clients get a first-class reload operation beyond disable/replace/enable?
4. Which provider lifecycle and capability adapters are required before external model providers
   can be admitted safely?
