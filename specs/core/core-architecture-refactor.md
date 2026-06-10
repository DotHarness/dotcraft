# Core C# Architecture Refactor

Status: **Active** (M1-M5 done; M6-M7 planned)
Scope: `src/DotCraft.Core` — AppServer protocol layer, Session Core services, App Binding service.
Non-goal: any wire-protocol or behavior change. `specs/protocols/appserver-protocol.md` and
`specs/core/session-core.md` remain the behavioral source of truth and are not modified by this work.

## 1. Motivation

Four files dominate DotCraft.Core and have crossed the threshold where size itself causes
defects (merge conflicts, missed cleanup paths, review blind spots):

| File | Lines | Nature of the problem |
|------|------:|-----------------------|
| `Protocol/AppServer/AppServerRequestHandler.cs` | 7,096 | Single class dispatching 115+ RPC methods; 40+ optional constructor parameters; thin and thick handlers interleaved. |
| `Protocol/SessionService.cs` | 6,215 | God class: 72+ public methods across 12 responsibility clusters; **24 parallel `ConcurrentDictionary` fields** holding per-thread state. |
| `Protocol/AppServer/AppServerProtocol.cs` | 4,566 | ~250 wire DTO classes plus the `AppServerMethods` constants in one file. Pure data, no logic. |
| `AppBinding/AppBindingService.cs` | 3,323 | One service covering five distinct sub-domains (connections, binding lifecycle, tool attachment, context blocks, UI interaction). |

Concrete symptoms observed in the current code:

- `DeleteThreadCoreAsync` must remember to purge a thread from **18+ separate dictionaries**;
  forgetting one is a silent leak. The dictionaries are individually thread-safe but not
  mutually consistent — there is no single object representing "the runtime state of thread X".
- `AppServerRequestHandler`'s primary constructor takes 40+ parameters, almost all optional.
  Both construction sites (`AppServerHost`, `ExternalChannelHost` ×3) pass long positional/named
  argument lists that must stay in sync by hand.
- Handlers like `HandleTurnStartAsync` (~250 lines) and `HandleWorkspaceConfigUpdateAsync`
  (~300 lines) embed orchestration and config-file editing logic directly in the dispatch class.
- A clean extension mechanism (`IAppServerProtocolExtension`, used by `AppBindingProtocolExtension`
  for 23 `app/*` methods) already exists — but built-in domains do not use it, so the codebase
  carries two dispatch styles.

## 2. Goals and non-goals

Goals:

1. No source file in the touched areas exceeds ~1,500 lines; `AppServerRequestHandler` shrinks
   to a thin dispatcher (< 500 lines).
2. One dispatch mechanism: built-in domains and protocol extensions register methods the same way.
3. One owner for per-thread runtime state: a single registry of `ThreadRuntime` objects replaces
   the 24 parallel dictionaries.
4. Wire compatibility is provable: DTO shapes, JSON options, method names, and error codes are
   untouched; existing conformance tests pass unchanged (test-side construction code may be
   updated, assertions may not).

Non-goals:

- No protocol additions/removals, no DTO renames, no namespace changes for wire types.
- No change to `ISessionService` (consumed by CLI, ACP, Automations, external channel adapters).
- No DI-container rework of host composition beyond introducing dependency bundles.

## 3. Current architecture (as-built summary)

Request flow:

```
Client (stdio JSONL / WebSocket)
  → IAppServerTransport.ReadMessageAsync
  → AppServerRequestHandler.HandleRequestAsync          (per-connection instance)
      ├─ switch(method) over ~115 built-in methods       → Handle*Async(...)
      │     └─ delegates to ISessionService / SkillsLoader / CronService / ...
      └─ fallback: _extensionMethods[method]             → IAppServerProtocolExtension.HandleAsync
  → response/notification via transport
  (event streams: SessionEvent → AppServerEventDispatcher → notifications)
```

Existing extension contract (kept as-is for out-of-core extensions):

```csharp
public interface IAppServerMethodHandler
{
    IReadOnlyCollection<string> Methods { get; }
    Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context);
}
public interface IAppServerCapabilityContributor
{
    void ContributeCapabilities(AppServerCapabilityBuilder builder);
}
public interface IAppServerProtocolExtension : IAppServerMethodHandler, IAppServerCapabilityContributor;
```

## 4. Target architecture

### 4.1 AppServer wire models — file split only (`Protocol/AppServer/Wire/`)

`AppServerProtocol.cs` is decomposed into per-domain files under a new `Wire/` folder.
**Namespace stays `DotCraft.Core.Protocol.AppServer`** so no call site changes.

Proposed layout (one file per protocol section, mirroring the spec's section structure):

```
Protocol/AppServer/Wire/
  WireEnvelope.cs          AppServerIncomingMessage + initialize handshake + capabilities
  AppServerMethods.cs      method-name constants (moved verbatim)
  ThreadWire.cs            thread/* params/results, ThreadRuntimeState
  TurnWire.cs              turn/* params/results
  WorktreeWire.cs          worktree/*
  SubAgentWire.cs          subagent/*
  TerminalWire.cs          terminal/*
  CommandWire.cs           command/*
  SkillsWire.cs            skills/*
  PluginWire.cs            plugin/*
  McpWire.cs               mcp/*
  CronWire.cs              cron/* + heartbeat
  UsageWire.cs             usage/* + profile insights
  DreamsWire.cs            dreams/*
  AutomationWire.cs        automation/*
  ProviderWire.cs          provider/* + model/* + auth/openai/*
  WorkspaceConfigWire.cs   workspace config update/schema/changed
  ChannelWire.cs           channel/* + external channel adapter envelopes
  DynamicToolWire.cs       dynamic tool + UI tool (DynamicToolSpec, UiToolMeta, ui/* params)
```

This is a pure mechanical move (cut/paste of sealed DTO classes). Zero behavior risk; it is the
first milestone precisely because every later diff becomes reviewable once this noise is gone.

### 4.2 AppServer dispatch — domain handler classes

Built-in domains move to per-domain handler classes that register into a method table. The
mechanism intentionally mirrors `IAppServerProtocolExtension` but is internal and richer (built-in
handlers legitimately need more than `AppServerExtensionContext` exposes):

```csharp
internal interface IAppServerDomainHandler
{
    /// Register "method name → delegate" pairs into the per-connection table.
    void RegisterMethods(AppServerMethodTable table);
    /// Contribute to the initialize handshake (default: no-op).
    void ContributeCapabilities(AppServerCapabilityBuilder builder) { }
}

internal sealed class AppServerMethodTable
{
    public void Map(string method,
        Func<AppServerIncomingMessage, CancellationToken, Task<object?>> handler);
    // throws on duplicate registration; validates against extension-reserved names
}
```

Handler classes (one per spec section group; names follow the RPC prefix):

| Handler class | Methods covered | Notes |
|---|---|---|
| `ThreadRequestHandler` | thread/* (20) | read/list/lifecycle/goal/rollback |
| `TurnRequestHandler` | turn/* (6) + item widget state | `HandleTurnStartAsync` orchestration extracted to `TurnStartCoordinator` (see below) |
| `WorktreeRequestHandler` | worktree/* + thread worktree handoff | |
| `SubAgentRequestHandler` | subagent/* (9) | |
| `TerminalRequestHandler` | terminal/* (5) | |
| `CommandRequestHandler` | command/* (2) | |
| `SkillsRequestHandler` | skills/* (6) | thin delegation to `SkillsLoader` |
| `PluginRequestHandler` | plugin/* (5) | |
| `McpRequestHandler` | mcp/* (6) | |
| `CronRequestHandler` | cron/* (4) + heartbeat/trigger | |
| `UsageRequestHandler` | usage/*, profile/insights | |
| `DreamsRequestHandler` | dreams/* (8+) | |
| `AutomationRequestHandler` | automation/* (9) | wraps existing `IAutomationsRequestHandler` pass-through |
| `ProviderRequestHandler` | provider/*, model/list, auth/openai/* | config edits via `WorkspaceConfigEditor` (below) |
| `WorkspaceRequestHandler` | workspace config update/schema, commit message suggest, welcome suggestions, memory reset | |
| `ChannelRequestHandler` | channel/*, external-channel/* | |
| `InitializeHandler` | initialize | stays closest to the dispatcher; walks all `ContributeCapabilities` |

Supporting changes that make this work:

- **`AppServerConnectionServices` dependency bundle.** A sealed record with init-only properties
  replacing the 40+ optional constructor parameters. Hosts (`AppServerHost`,
  `ExternalChannelHost`) build it once with named property initializers; the handler factory
  composes domain handlers from it. Adding a dependency becomes a one-property diff instead of a
  4-call-site signature change.
- **Shared request plumbing** (`GetParams<T>`, domain-exception → AppServer error translation,
  config helpers) moves to small static/utility classes (`AppServerParams`,
  `AppServerExceptionMapper`) used by all handlers — not a god base class.
- **`WorkspaceConfigEditor`.** The config-file read/modify/write helper cluster currently embedded
  in the request handler (`LoadWorkspaceConfigObject`, `UpsertOrRemoveConfigValue`,
  `WriteConfigObject`, refresh/invalidate hooks) becomes a service shared by
  `ProviderRequestHandler`, `McpRequestHandler`, and `WorkspaceRequestHandler`.
- **`TurnStartCoordinator`.** The ~250-line turn-start orchestration (input materialization,
  skill-reference recording, channel session scope, initial-turn TCS handshake) becomes a
  dedicated collaborator so `TurnRequestHandler` stays a protocol adapter.
- **Dispatcher.** `AppServerRequestHandler` keeps its name and public surface (creation sites and
  tests keep working) but shrinks to: lifecycle/initialization gates, the method table lookup,
  extension fallback, and exception translation. Reserved-name validation moves into
  `AppServerMethodTable` so built-ins and extensions share one conflict check.

`IAppServerProtocolExtension` remains the public contract for out-of-core modules. Internally,
extensions are adapted into the same method table at construction, so dispatch is one dictionary
lookup for everything.

### 4.3 SessionService — sub-service extraction + per-thread state aggregation

#### 4.3.1 `ThreadRuntime` (state aggregation)

A single registry replaces the 24 parallel dictionaries:

```csharp
internal sealed class ThreadRuntimeRegistry
{
    // the only per-thread map in Session Core
    private readonly ConcurrentDictionary<string, ThreadRuntime> _runtimes;
    public ThreadRuntime GetOrAdd(string threadId, ...);
    public bool TryRemove(string threadId, out ThreadRuntime runtime); // whole-object teardown
}

internal sealed class ThreadRuntime
{
    public SessionThread Thread { get; }
    public ThreadEventBroker Broker { get; }
    public SemaphoreSlim QueueLock { get; }
    public SemaphoreSlim AgentLock { get; }
    public AIAgent? Agent;                          // invalidated on config change
    public McpClientManager? McpManager;
    public AgentModeManager? ModeManager;
    public IReadOnlyList<AITool>? CurrentTools;
    public IReadOnlySet<string>? PluginFunctionToolNames;
    public IReadOnlySet<string>? DynamicToolNames;
    public ThreadMaintenanceState? Maintenance;     // compact / consolidate, one at a time
    public GoalRuntime Goal { get; }                // continuation flags, budget guidance, snapshots
    public MemoryConsolidationRuntime Consolidation { get; }  // turn counter, active/pending work
    public ConcurrentDictionary<string, TurnRuntime> Turns { get; }   // keyed by turnId
    public PromptRequestSnapshot? LastPromptRequest;
    public ContextUsageAnchor? ContextUsageAnchor;
    public bool PendingPermanentDeletion;
}

internal sealed class TurnRuntime   // replaces the TurnKey-keyed dictionaries
{
    public CancellationTokenSource? Cancellation;          // was _runningTurns
    public SessionApprovalService? PendingApproval;        // was _pendingApprovals
    public SessionUserInputRequestService? PendingUserInput;
    public GoalTurnSnapshot? GoalSnapshot;
}
```

Consequences:

- `DeleteThreadCoreAsync` becomes `TryRemove` + `runtime.DisposeAsync()` — teardown is a member
  of the state object, not a checklist in the service.
- Concurrency granularity is preserved: cross-thread access still goes through one
  `ConcurrentDictionary`; turn-keyed maps keep `ConcurrentDictionary` semantics inside
  `ThreadRuntime`. Fields that were previously "GetOrAdd a flag dictionary" become plain
  volatile/interlocked fields scoped to the runtime object.
- The registry is `internal` and injected into the sub-services below; `SessionService` no longer
  owns raw state.

#### 4.3.2 Sub-service extraction

`SessionService` stays the only implementation of `ISessionService` (facade, unchanged interface)
and delegates to coordinators, each owning one responsibility cluster:

| Coordinator | Pulled from SessionService | Approx. lines today |
|---|---|---|
| `GoalCoordinator` | goal get/set/clear, auto-continue on idle, budget guidance, pause-on-interrupt, goal turn snapshots | ~500 |
| `TurnQueueCoordinator` | enqueue/remove/reorder, `TryStartNextQueuedTurnAsync`, queue locks | ~450 |
| `MaintenanceCoordinator` | compact, manual + auto memory consolidation (incl. pending-work queue), maintenance interrupt, background-terminal cleanup | ~600 |
| `WorktreeCoordinator` | create-and-fork, create-and-start, handoff, list, status (delegating to `ThreadWorktreeManager`) | ~350 |
| `SubAgentTurnCoordinator` | spawn edges, mailbox, synthetic turn start/complete/cancel | ~300 |
| `ThreadLifecycleCoordinator` | create/fork/resume/ensure-loaded/delete/archive/rename/pause + runtime registry ownership | ~900 |

Turn execution (`SubmitInputAsync`/`StartTurnAsync`/`CancelTurnAsync` and the event pipeline)
remains in `SessionService` itself — it is the service's essential job and its main coupling
point; extracting it would create a pass-through layer with no seam. Target size for
`SessionService` after extraction: ~1,200 lines.

Coordinators receive `(ThreadRuntimeRegistry, SessionPersistenceService, broadcast delegates,
logger)` — they do not call back into `SessionService` (no cycles). Where two coordinators
interact (e.g., goal auto-continue submits input), the dependency is expressed as a narrow
delegate (`Func<SubmitInputRequest, Task>`) provided at composition time.

### 4.4 AppBindingService — sub-domain services

Same facade pattern as 4.3: `AppBindingService` keeps its public surface (consumed by
`AppBindingProtocolExtension`, `AppServerRequestHandler`, agent tool plumbing) and delegates to:

| Service | Responsibility (current line ranges) |
|---|---|
| `AppConnectionService` | connection start/complete/status/refresh/revoke (131–363) |
| `AppBindingLifecycleService` | binding requests, accept/cancel, revoke, refresh sweep, managed-binding repair (365–842, 1152–1430) |
| `AppToolAttachmentService` | tool attach/validate, active attachment registry, runtime tool creation, `InvokeAttachedToolAsync` (645–712, 1432–1568, 2178–2226) |
| `AppContextBlockService` | context block upsert/remove/list, prompt section building (844–1150) |
| `AppUiInteractionService` | UI tool invoke + approval gate, open-link policy, model context updates, UI resource reads (1577–1940) |

Shared internals (`AppBindingStore` access, `FindApp`/`FindBinding`/validation helpers, wire
mapping) move to an internal `AppBindingStoreAccessor` + `AppBindingWireMapper`. The
`_activeAttachments` registry is owned by `AppToolAttachmentService` and exposed to the others via
a narrow interface, eliminating the current cross-cutting `TryRemove` calls sprinkled through
revoke/refresh paths.

## 5. Milestones

Each milestone is independently mergeable with the full test suite green. Ordering minimizes
rebase pain: mechanical moves first, behavioral seams last.

| # | Milestone | Content | Risk | Status |
|---|---|---|---|---|
| M1 | **Wire split** | 4.1: `AppServerProtocol.cs` → `Wire/` files; no code changes beyond file moves. Also split `SessionWireModels.cs` if it exceeds budget after review. | Trivial (compile-verified moves) | **Done** (19 files; 386 conformance tests pass) |
| M2 | **Dispatch infrastructure** | `AppServerMethodTable`, `AppServerConnectionServices` bundle, `AppServerParams`/`AppServerExceptionMapper`; dispatcher consumes the table; extensions adapted into it. Built-in methods still live in the old class but register via the table. Update the two host construction sites + test fixtures. | Low–medium (constructor surface) | **Done** — services bundle, method table, `AppServerParams`, and `AppServerExceptionMapper` are in place; built-ins dispatch through the table before extension fallback. |
| M3 | **Domain handlers, batch 1** | Thin/CRUD domains: skills, plugin, mcp, cron, usage, dreams, terminal, command, automation, channel. Extract `WorkspaceConfigEditor` alongside provider/workspace handlers. | Low (thin delegation) | **Done** — dispatch seam built; **12 domains extracted to handler classes**: `cron/*`+`heartbeat/trigger`, `terminal/*`, `dreams/*`, `skills/*`, `mcp/*`, `channel/*`+`externalChannel/*`, `provider/*`+`model/list`+`auth/openai/*`, `workspace/config*`+welcome/commit-message/memory-reset, `plugin/*`, `command/*`, `usage/*`+`profile/insights`, `automation/*`. Shared helpers extracted: `AppServerContextInvalidation`, `SkillVariantContext`, `WorkspaceConfigEditor`, `AppServerMcpConfigService`, `McpWireMapper`, `ExternalChannelConfigService`, `ExternalChannelWireMapper`, `ProviderWireMapper`, `AppServerRuntimeConfigRefresher`. Tests green throughout. |
| M4 | **Domain handlers, batch 2** | thread, turn (+`TurnStartCoordinator`), worktree, subagent, initialize. Delete the old switch; `AppServerRequestHandler` reaches dispatcher-only form. | Medium (thick handlers) | **Done** — `subagent/*`, `worktree/*`, `thread/*`, `turn/*`, and `initialize` extracted. `AppServerRequestHandler` is dispatcher-only and under 500 lines. |
| M5 | **SessionService sub-services** | 4.3.2 coordinators extracted against the *existing* dictionaries (state untouched, moves only). | Medium | **Done** — `WorktreeCoordinator`, `SubAgentSessionCoordinator`, `ThreadIndexCoordinator`, `ThreadCreationCoordinator`, `ThreadLifecycleCoordinator`, `ThreadAccessCoordinator`, `ThreadConfigurationCoordinator`, `TurnControlCoordinator`, `ThreadGoalCoordinator`, `ThreadQueueCoordinator`, and `MaintenanceCoordinator` extracted. Turn execution remains in `SessionService`; state dictionaries remain untouched for M6. |
| M6 | **`ThreadRuntime` aggregation** | 4.3.1: introduce registry + runtime objects; coordinators and SessionService migrate field-by-field; delete the 24 dictionaries. | Highest — concurrency-sensitive; do last, smallest reviewable steps | **In progress** — `ThreadRuntimeRegistry`/`ThreadRuntime` introduced; cached threads, queue/agent locks, agent/tool/MCP caches, mode managers, and plugin/dynamic tool-name sets migrated into runtime registry; teardown count assertion added. Continue remaining state migrations. |
| M7 | **AppBinding split** | 4.4 sub-domain services. Independent of M5/M6; can run in parallel with them. | Low–medium | Planned |

> **As-built note (M1):** the wire namespace is `DotCraft.Protocol.AppServer` (the `RootNamespace`
> is `DotCraft`, not `DotCraft.Core`); §4.1 references to `DotCraft.Core.Protocol.AppServer` should
> be read as `DotCraft.Protocol.AppServer`. The 19 files produced match the proposed layout, with
> `ChannelWire.cs` also absorbing the channel-adapter capability/tool-descriptor DTOs.

> **As-built note (M3 dispatch seam):** the method table is internal (`AppServerMethodTable` +
> `IAppServerDomainHandler.RegisterMethods`), resolved by the dispatcher *before* the in-class
> switch, so built-ins and extensions converge on one lookup while un-migrated domains keep working.
> Domain handlers are constructed lazily in `BuildDomainHandlers()` from the
> `AppServerConnectionServices` bundle. `cron/*` and `terminal/*` are clean (only their own service
> + `sessionService`) and are extracted.
>
> **Shared-helper prerequisite for remaining M3 domains.** Auditing the next domains shows they are
> *not* self-contained — they call cross-domain helpers that must be extracted to shared
> static/injectable utilities before their handlers can move out:
> - `MarkSkillsContextDirty` / `MarkMemoryContextDirty` (context-page invalidation) — used by
>   skills, plugin, dreams(apply), memory-reset. → an injectable `AppServerContextInvalidator`.
> - `MapSkillToWire` — skills + plugin. → static `SkillWireMapper`.
> - `EnrichThreadWireAsync` — thread + command + others. This is thread-projection logic and is the
>   hard knot; domains that need it (command, plugin views) should wait until thread enrichment is
>   factored out in M4.
> - Pure helpers (`FormatTimeSpanForWire`, `ValidateEmptyObjectParams`, `NormalizeIdentityWorkspace`,
>   `ExtractCommandName`, `ParseUsageDate`) → static helper classes.
>
> Current recommended sequence: continue M5 SessionService coordinator extraction against the
> existing state dictionaries. Keep runtime ownership untouched until M6.
>
> **As-built (M3 batch, 12 domains done).** Extracted, each verified (Core build + 386 conformance
> tests, green per commit): `CronRequestHandler` (including `heartbeat/trigger`),
> `TerminalRequestHandler`, `DreamsRequestHandler`,
> `SkillsRequestHandler`, `McpRequestHandler`, `ChannelRequestHandler`, `ProviderRequestHandler`,
> `WorkspaceRequestHandler`, `PluginRequestHandler`, `CommandRequestHandler`, `UsageRequestHandler`,
> `AutomationRequestHandler`.
> Shared seams created: `AppServerContextInvalidation` (skills/memory page invalidation),
> `SkillVariantContext` (variant mode + target, shared with turn-start/initialize),
> `WorkspaceConfigEditor` (config JSON paths/load/write/upsert helpers),
> `AppServerMcpConfigService` (workspace MCP persistence + effective runtime reconnect), and
> `McpWireMapper`; plus `ExternalChannelConfigService` and `ExternalChannelWireMapper` for channel
> management; plus `ProviderWireMapper` and `AppServerRuntimeConfigRefresher` for provider/model/auth
> and workspace config runtime-refresh management. Single-domain helpers travelled with their handler (e.g. `MapSkillToWire`,
> `ToDreamRunWire`, `ParseUsageDate`, `MapPluginToWire`). `CommandRequestHandler` uses a small
> dispatcher-facing callback for reset-thread enrichment. `automation/*` was already a pass-through to
> `IAutomationsRequestHandler`, so its handler is a thin router.
>
> **Remaining domains and their blocker (next sub-pass before extraction):**
> - M5 SessionService sub-services — `WorktreeCoordinator`, `SubAgentSessionCoordinator`,
>   `ThreadIndexCoordinator`, `ThreadCreationCoordinator`, `ThreadLifecycleCoordinator`,
>   `ThreadAccessCoordinator`, `ThreadConfigurationCoordinator`, `TurnControlCoordinator`,
>   `ThreadGoalCoordinator`, `ThreadQueueCoordinator`, and `MaintenanceCoordinator` are
>   extracted. Continue with M6 `ThreadRuntime` aggregation before AppBinding split.
> - M6 `ThreadRuntime` aggregation — runtime registry exists and owns cached
>   `SessionThread` instances, queue/agent locks, agent/tool/MCP caches, mode managers,
>   and plugin/dynamic tool-name sets; remaining per-thread and per-turn dictionaries
>   still need field-cluster migrations into `ThreadRuntime`/`TurnRuntime`.

## 6. Verification strategy

### 6.1 Test-backing levels per milestone

Not all milestones need the same safety net. Classification agreed during review:

| Level | Milestones | Backing required |
|---|---|---|
| 0 — compile-verified | M1, most of M7 | Pure moves. Existing suite run; **no new tests**. |
| 1 — existing tests + route pin | M2, M3 | Non-destructive relocation. The only real failure mode is a dropped/mistyped route; one freeze test pinning the full method-name set eliminates the class. |
| 2 — existing conformance suite as gate + focused review | M4, M5 | Covered by the 43 AppServer suites. Risk lives in review, not test gaps: M4 must audit `ChannelSessionScope` (AsyncLocal) boundaries and the `initialTurnTcs` closure lifetime when converting closures to members. |
| 3 — new tests required | M6 | See 6.2. The only milestone whose failure modes are timing-dependent and invisible to the (mostly single-threaded request/response) conformance suites. |

### 6.2 Why M6 is not mechanical

Several dictionaries are concurrency-control primitives, not storage; their translation into
`ThreadRuntime` fields changes semantics unless done deliberately:

- `_goalContinuationStarting.TryAdd` (SessionService.cs:624) is a mutual-exclusion latch
  ("one goal continuation per thread"); as a field it must become
  `Interlocked.CompareExchange`.
- `_activeAutoMemoryConsolidations` uses a release-then-reacquire pattern
  (SessionService.cs:5127–5131) to chain pending consolidation work — a timing protocol.
- `_threadQueueLocks`/`_threadAgentLocks`/`_threadEventBrokers` are lazily `GetOrAdd`-ed
  (SessionService.cs:4428, 4435, 4815) on arbitrary threadIds. Today a delete-vs-GetOrAdd race
  recreates one cheap lock object (harmless leak); with a single registry the same pattern would
  **resurrect the whole runtime** of a deleted thread (live broker, deliverable events). Every
  call site must explicitly choose `TryGet` vs `GetOrAdd` — a design decision per site.

M6 gate, accordingly: the state-teardown test below, a reviewed checklist of every
`TryAdd`/`TryRemove` used as a latch with its translated equivalent, and field-cluster-sized
commits.

### 6.3 Suite-level checks

- **Conformance tests are the contract.** The 43 `DotCraft.Core.Tests/Protocol/AppServer` suites
  plus App-level and smoke tests must pass with *assertions unchanged* in every milestone. Test
  construction/fixture code may adapt to the `AppServerConnectionServices` bundle in M2 only.
- **Wire-surface freeze check.** Before/after M1–M4, diff the serialized initialize handshake and
  the reserved method-name set (`AppServerMethodTable` exposes it) in a unit test, pinning the
  full method list so an accidental drop of a route fails loudly.
- **State-teardown test (new, M6 gate).** A test that loads a thread, exercises every runtime
  facility (queue, goal, maintenance, MCP, approvals), deletes the thread, and asserts the
  registry is empty — codifying the leak class this refactor eliminates.
- Per dev-guide, no spec updates are required (no behavior change); this document tracks
  progress via the milestone table's Status column once implementation starts.

## 7. Risks and mitigations

- **In-flight branch conflicts.** M1 rewrites file layout under `Protocol/AppServer/`; schedule it
  when no large protocol PRs are open, and land it as a single mechanical commit (`git log
  --follow` friendly).
- **Concurrency regressions in M6.** The dictionaries are individually atomic today; folding them
  into `ThreadRuntime` must not widen any critical section. Mitigation: migrate one field cluster
  per commit, keep `ConcurrentDictionary` semantics for turn-keyed state, and gate on the
  teardown test plus existing stress/smoke suites.
- **Hidden coupling in thick handlers (M4).** `HandleTurnStartAsync` and
  `HandleWorkspaceConfigUpdateAsync` touch connection-scoped state (channel scope, client
  capabilities). Extraction must pass these through explicit parameters — audit for statics
  (`ChannelSessionScope`, `AppServerRequestContext`) during review.
- **Two dispatch styles during M2–M4.** Accepted as a transition state; the method table is
  authoritative from M2 onward, so "old style" is only the physical location of code, not a
  second mechanism.
