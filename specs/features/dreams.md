# DotCraft Dreams Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.3 |
| **Status** | Living |
| **Date** | 2026-10-06 |
| **Related Specs** | [Session Core](../architecture/session-core.md), [Memory](memory.md), [Automations Lifecycle](automations-lifecycle.md) |

Purpose: Define **Dreams**, DotCraft's workspace-level background memory maintenance product and runtime capability. Dreams gives each workspace an offline memory management loop that can run from AppServer without an active client or conversation session.

## 1. Product Positioning

Dreams maintains inferred workspace memory in the background while the host runs. It is a workspace capability, independent of any open conversation or client. Its generated stores are separate from explicit [Memory](memory.md) and enter future prompts only when active.

## 2. Goals And Non-Goals

### 2.1 Goals

1. Maintain workspace memory while AppServer is running, even when no client is open.
2. Separate passive inferred memory from explicit long-term memory.
3. Keep the product loop reviewable: schedule or request a run, generate a pending output store, let the user apply or discard it.
4. Make Dreams observable enough for Desktop review, Dashboard traces, and diagnostics.
5. Use Session Core for actual Dream model work so pruning and consolidation turns are inspectable.

### 2.2 Non-Goals

- Replacing or writing agent-maintained `MEMORY.md`.
- Creating a standalone Dreams app in the baseline design.
- Requiring a client to remain open for background memory maintenance.
- Introducing remote memory administration or cross-workspace memory sharing.
- Applying generated memory without the configured review or AutoApply policy.
- Building semantic/vector retrieval or intelligent scheduling as part of the baseline Dreams design.
- Storing secrets, credentials, raw command output, large code excerpts, or sensitive personal profiling as passive memory.

## 3. Concept Model

| Concept | Definition |
|---------|------------|
| **Dreams** | Workspace background memory organization capability. |
| **Explicit Memory** | Durable lessons the agent saved at the user's direction in `.craft/memory/MEMORY.md`. See [Memory](memory.md). |
| **Dream Store** | Passive inferred workspace context stored as `.craft/dreams/stores/<storeId>/INDEX.md` plus optional topic markdown files. |
| **Active Dream Store** | The Dream Store currently injected into future agent prompts. |
| **Pending Dream Store** | A generated output store awaiting user review. Pending stores do not affect prompts. |
| **Dream Run** | One scheduled or manually requested Dreams execution attempt. |
| **Dream Run Thread** | One internal Session Core thread created for an actual Dreams model run. |
| **Dream Input Window** | Workspace memory artifacts and recent thread transcripts inspected by one Dream Run. |
| **Dream Status** | Latest scheduler and run state exposed to AppServer clients. |

Relationship to explicit memory:

| Workflow | Scope | Trigger | Writes | Product role |
|----------|-------|---------|--------|--------------|
| Explicit memory | The current Thread | The agent, while responding to the user | `.craft/memory/MEMORY.md` | Keep lessons the user taught. |
| Dreams | Workspace-wide recent history and memory artifacts | AppServer schedule or manual workspace action | pending `.craft/dreams/stores/<storeId>/` output store and `.craft/dreams/runs/<runId>/state.json`; apply switches `.craft/dreams/active.json` | Maintain reviewable passive inferred workspace context offline. |

Dreams depends on the workspace memory switch. While `Memory.Enabled` is `false`, Dreams starts no runs and Dream Memory is not injected; `Dreams.*` settings keep their stored values.

## 4. Memory Artifact Model

Dreams uses a dedicated workspace Dreams root:

```text
.craft/
  dreams/
    active.json
    state.json
    runs/
      <runId>/
        input/
          MANIFEST.md
          input.json
          memory/
          dreams/
          sessions/
        state.json
    stores/
      <storeId>/
        INDEX.md
        PRUNING_NOTES.md
        memory/
          <topic>.md
  memory/
    MEMORY.md
```

Artifact authority:

1. Current user instructions and inspected repository facts.
2. System, developer, workspace, and tool instructions.
3. `.craft/memory/MEMORY.md` explicit memory.
4. Active Dream Store `INDEX.md` passive inferred memory.

Active Dream Store memory must not be treated as explicit user instruction. It is helpful inferred background context and should be ignored when it conflicts with more authoritative sources.

Dreams may read `.craft/memory/MEMORY.md` as evidence, but Dreams must not write `.craft/memory/*`.

`memory/reset` clears Dream Stores and Dreams-derived run state together with explicit memory artifacts, while preserving the `.craft/memory` and `.craft/dreams` directories.

## 5. Dream Memory Content Contract

Each store contains a concise `INDEX.md`, headed Dream Memory and labelled as inferred background context. The index covers workspace focus, active threads/open loops, inferred conventions, repeated problems/prior mistakes, stable understanding and low-signal context to ignore. Optional `memory/*.md` topic files hold focused detail and are read on demand.

Store only workspace-relevant, evidence-backed context. Do not store credentials, sensitive personal profiling, raw logs, large code excerpts or speculation presented as fact. Inferred memory cannot overwrite explicit preferences or instructions.


## 6. Runtime Lifecycle

### 6.1 Scheduling

Dreams is AppServer-owned background work.

Baseline scheduling behavior:

- Defaults are defined in [Configuration and state](#8-configuration-and-state).
- Runs on a fixed interval.
- Uses a startup delay before the first eligibility check.
- Skips model work when insufficient new completed turns exist.
- Allows a manual "Run now" request from clients.
- Allows only one active Dream Run per workspace.

Dreams does not run because an individual turn completed.

### 6.2 Scheduled Eligibility

A scheduled check may start a Dream Run only when all are true:

- `Memory.Enabled = true` and `Dreams.Enabled = true`.
- No Dream Run is already active for the workspace.
- The configured interval has elapsed since the last completed run attempt that performed or skipped model work.
- At least `Dreams.MinCompletedTurnsSinceLastRun` new completed turns exist across eligible threads.
- The workspace has enough input evidence to produce useful Dream Memory.

If eligibility fails because there is insufficient new history, the scheduler records a `skipped` run state without calling the model.

### 6.3 Manual Runs

Manual runs are requested through AppServer and reuse the same input and output contract as scheduled runs.

Manual runs:

- Bypass interval timing.
- Do not start if another run is already active.
- Respect `Memory.Enabled` and `Dreams.Enabled`.
- May still skip when there is no useful input evidence.
- Return quickly; clients observe completion through `dreams/status` polling or a later refresh.

### 6.4 Input Collection

Each run starts from a compact source manifest. The initial model prompt may
include previews of memory indexes and metadata for candidate sessions, but it
must not inline raw session transcripts.

Each run manifest may include:

- Current `.craft/memory/MEMORY.md`.
- Active Dream Store `INDEX.md`.
- Active Dream Store topic file names and lightweight metadata.
- Recent eligible Session Core thread metadata.
- Thread metadata such as display name, origin channel, status, created time, and last active time.

`dreams/create` may override the selected session ids, session lookback count,
and add run-specific instructions. Scheduled runs use the configured recent
eligible session window.

Eligible threads:

- Belong to the current workspace.
- Use server-managed Session Core history.
- Are top-level user or automation/task threads.
- May be active or archived.
- Are ordered by last activity descending.
- Stop after `Dreams.ThreadLookbackCount`; this setting limits candidate manifest size, not inline transcript content.

Excluded threads:

- Internal helper threads.
- Session-backed subagent child threads.

Input collection is read-only. It must not resume threads, start turns, change thread status, update thread metadata, emit conversation timeline items, or materialize raw transcripts into the initial Dream Run prompt.

### 6.5 Generation

Only attempts that actually enter model generation create a Session Core thread.
Disabled runs, already-active attempts, no-evidence attempts, and scheduled runs
that do not meet the completed-turn threshold record Dreams status only and do
not create Dashboard noise.

Each actual model run:

- Creates a new internal Session Core thread.
- Uses `originChannel = "dreams"`.
- Marks thread metadata with `dotcraft.internal = "dreams"`.
- Uses the workspace consolidation model policy.
- Runs two turns in the same thread: pruning pass, then consolidation pass.
- Runs with auto-approve and a Dreams file-tool profile.
- Persists the compact Dreams input manifest and the model's explicit evidence
  tool reads/searches into the session trace so Dashboard users can inspect
  what the maintenance run chose to read.
- Archives the run thread after both turns finish.

Dream Run threads are internal maintenance threads. They are visible to
Dashboard trace/session views, but ordinary Desktop `thread/list` views omit
them by default unless internal threads are explicitly requested.

The Dreams file-tool profile reuses the normal file helpers (`ReadFile`,
`WriteFile`, `EditFile`, `GrepFiles`, and `FindFiles`) but with a path-level
sandbox:

- read-only input snapshot under `.craft/dreams/runs/<runId>/input/`
- read-only current repository/spec/docs evidence
- read-only active Dream Store
- writable candidate output store under `.craft/dreams/stores/<outputStoreId>/`
- writes denied for the repository, `.craft/memory/*`, the active Dream Store,
  and any path outside the candidate output store

The pruning pass reads the input snapshot, active Dream Store, and eligible
session/repo evidence to write `PRUNING_NOTES.md` in the candidate output store.
It identifies stale, duplicated, contradictory, and low-signal passive memory.

The consolidation pass reads the same snapshot plus `PRUNING_NOTES.md` and writes:

- `INDEX.md`
- zero or more `memory/*.md` topic files

Dreams validates the candidate output store before marking the run succeeded.
When `Dreams.AutoApply` is `false`, scheduled and manual runs do not switch the
active store. The output stays pending until the user applies it. When
`Dreams.AutoApply` is `true`, future successful runs immediately switch the
active store to the generated output store and record the run as auto-applied.
Existing pending runs are not retroactively applied when the setting changes.

The model should receive enough current memory context to preserve useful passive memory and update stale sections, but it must be instructed to avoid copying raw transcripts or unverified details.

The maintenance model selection follows the existing memory maintenance model policy: use `ConsolidationModel` when configured; otherwise use the workspace main model.

Generated `INDEX.md` must follow the structure defined in [Section 5](#5-dream-memory-content-contract).

### 6.6 Persistence

Successful valid output may write:

- `.craft/dreams/stores/<outputStoreId>/INDEX.md`
- `.craft/dreams/stores/<outputStoreId>/PRUNING_NOTES.md`
- `.craft/dreams/stores/<outputStoreId>/memory/*.md`
- `.craft/dreams/runs/<runId>/input/*`
- `.craft/dreams/runs/<runId>/state.json`
- latest run state in `.craft/dreams/state.json`

Write rules:

- A pending output store is not prompt-visible until applied.
- `dreams/apply` switches `.craft/dreams/active.json` to the output store id.
- Old active stores are retained for rollback/archive workflows.
- Topic paths must be safe top-level markdown slugs under
  `.craft/dreams/stores/<storeId>/memory/`; absolute paths, traversal, non-markdown paths, and
  files over 100 KB are rejected.
- If generated Dream Memory or any topic write is invalid, existing Dreams
  artifacts remain unchanged.
- Failed runs must not switch the active Dream Store.

### 6.7 Archive And Permanent Deletion

Archive is a reversible review-state operation, not physical deletion. The existing
`dreams/archive` AppServer method sets `reviewStatus = archived` and retains the
run directory, input snapshot, output store, internal thread, and trace. Desktop
uses this method for both single-run **Archive** and **Archive all**; bulk archive
is a client-side sequence of `dreams/archive` calls and does not add a bulk
AppServer method.

Permanent deletion is Dashboard-only and uses the Dashboard HTTP `DELETE`
endpoints in [Section 11](#11-dashboard-ux-contract). Deleting a run removes:

- `.craft/dreams/runs/<runId>/`, including run state and input snapshot.
- The run's output store when it is not the active Dream Store.
- The related internal Dream Run thread and its trace records when `threadId` is
  present.

The active Dream Store is never removed by run deletion, even when the deleted
run originally produced it. A running run cannot be deleted. Bulk deletion fails
with a conflict before deleting anything when any targeted run is running.

After deletion, `.craft/dreams/state.json` is rebuilt from the newest remaining
run or cleared when no runs remain. Run and store deletion are authoritative;
internal thread/trace cleanup is best-effort. A trace cleanup failure therefore
produces a successful partial-cleanup response that identifies the affected
thread instead of restoring already deleted Dreams artifacts.

## 7. Agent Context Integration

Prompt composition loads only the active store's nonempty `INDEX.md`, after explicit memory and before conversation history. Missing active memory does not prevent prompt construction. Topic files remain on-demand references.

The block is labelled Dream Memory and states its lower authority: inferred context is subordinate to current instructions, repository evidence and `MEMORY.md`. It cannot establish hidden requirements, prove task completion or override user preferences. The [Memory switch](memory.md#5-memory-switch) governs whether the thread receives memory.


## 8. Configuration And State

Baseline configuration lives under `Dreams`.

| Setting | Default | Meaning |
|---------|---------|---------|
| `Dreams.Enabled` | `false` | Enables scheduled Dreams for the workspace. |
| `Dreams.Interval` | `24:00:00` | Minimum elapsed time between scheduled Dream eligibility checks that can run model work. |
| `Dreams.ThreadLookbackCount` | `20` | Maximum recent eligible threads listed in the per-run source manifest. |
| `Dreams.AutoApply` | `false` | Automatically applies future successful Dream Runs as the active Dream Store. Existing pending runs are unchanged. |
| `Dreams.MinCompletedTurnsSinceLastRun` | `5` | Minimum new completed turns across eligible threads before scheduled model work. |
| `Dreams.StartupDelay` | `00:05:00` | Delay before the first eligibility check after AppServer startup. |

Run state is not stored in config. It belongs in `.craft/dreams/state.json`.

Latest run state fields:

| Field | Meaning |
|-------|---------|
| `id` | Dream Run id. |
| `status` | `running`, `succeeded`, `skipped`, `failed`, or `canceled`. |
| `startedAt` | Run start timestamp. |
| `endedAt` | Run end timestamp when complete. |
| `processedThreadCount` | Number of eligible threads included. |
| `candidateThreadCount` | Number of eligible candidate threads listed in the manifest. |
| `evidenceThreadIds` | Thread ids that the Dream Run actually read or matched through evidence tools. |
| `writtenPaths` | Candidate output store paths changed by a successful run. |
| `evidenceSearchCount` | Number of evidence search tool calls used by the run. |
| `evidenceReadCount` | Number of evidence read tool calls used by the run. |
| `dreamWritten` | Whether the candidate output store contains a valid `INDEX.md`. |
| `outputStoreId` | Candidate Dream Store id generated by the run. |
| `reviewStatus` | `pending`, `applied`, `discarded`, or `archived` when review state exists. |
| `autoApplied` | Whether the run was automatically applied because `Dreams.AutoApply` was enabled at success time. |
| `errorType` | Machine-readable failure class when known. |
| `message` | Short skip/failure message. |
| `nextRunAt` | Next scheduled eligibility time when known. |
| `threadId` | Internal Session Core thread id for an actual model run, filled as soon as known. Omitted for pre-model skips. |
| `turnId` | Latest Session turn id for an actual model run, filled as soon as known. Omitted for pre-model skips. |
| `turnIds` | Both pruning and consolidation turn ids when generation entered both passes. |
| `usage` | Aggregate token usage for both Dream pass turns when available. |
| `inputManifestPath` | Path to the run input manifest snapshot. |
| `trigger` | `manual` or `scheduled`. |

## 9. AppServer Contract

The AppServer protocol exposes Dreams as a workspace capability, not as a thread method.

Baseline capability:

| Capability | Meaning |
|------------|---------|
| `capabilities.dreams` | Server supports workspace Dreams status, run creation, review lifecycle, and Dreams settings. |

Baseline methods:

| Method | Purpose |
|--------|---------|
| `dreams/status` | Read current configuration and run status for the connected workspace. |
| `dreams/run` | Shortcut for `dreams/create` with default manual parameters. |
| `dreams/create` | Request a Dream Run with optional `threadIds`, `threadLookbackCount`, and `instructions`. |
| `dreams/get` | Read one run state and review preview. |
| `dreams/list` | List recent run states. |
| `dreams/cancel` | Cancel a running Dream Run. |
| `dreams/apply` | Apply a succeeded pending output store as active. |
| `dreams/discard` | Discard a pending output store. |
| `dreams/archive` | Hide a run from default run lists. |

Clients must check `capabilities.dreams` before calling Dreams methods. If absent or false, the server returns method-not-found.

### 9.1 `dreams/status`

An empty request reads configuration, enabled/running state, active store id, next scheduled check and the latest run for the connected workspace. `lastRun` is null before the first attempt. Run metadata follows §8.

### 9.2 `dreams/run`

Requests an immediate Dream Run for the connected workspace.

Params: `{}` or omitted.

Result: same shape as `dreams/status`.

Semantics:

- If no run is active and Dreams is enabled, the server persists a `running`
  state before returning the updated status snapshot.
- If a run is already active, the server returns the active status snapshot without starting a duplicate run.
- If Dreams is disabled, the server returns a skipped or disabled status without starting a run.
- The baseline protocol does not require streaming run progress or a completion notification. Clients may poll `dreams/status`.

### 9.3 `dreams/create|get|list|cancel|apply|discard|archive`

`create` accepts optional session ids, lookback count, instructions and model. Run-specific operations identify `runId`; `list` supports `includeArchived` and returns compact entries. `get` includes active/output index Markdown and topic paths for review.

Successful runs follow §6.5–6.7: pending output has no prompt effect; apply switches the active store; discard/archive retain stores; cancellation is best-effort. Responses identify the affected run and active store.

### 9.4 Dreams Settings

Dreams settings are configuration fields read and written through the configuration methods (`config/read`, `config/value/write`, `config/batchWrite`) defined in [Configuration](../architecture/configuration.md).

| Key path | Type | Meaning |
|----------|------|---------|
| `Dreams.Enabled` | boolean | Enables scheduled Dreams. |
| `Dreams.Interval` | `TimeSpan` string | Scheduled interval; must be positive. |
| `Dreams.ThreadLookbackCount` | integer | Maximum recent eligible candidate threads listed in a Dream Run manifest; must be positive. |
| `Dreams.AutoApply` | boolean | Automatically applies future successful Dream Runs. |

Changing `Memory.Enabled` or any `Dreams.*` field starts the scheduler when both Dreams and memory are enabled and stops it otherwise. The change notification lists the changed key paths.

### 9.5 Memory Reset

`memory/reset` clears explicit memory and every Dreams store, run, state file and active pointer, preserving the root directories. The broader reset boundary follows [Memory](memory.md#6-deleting-memory).

## 10. Desktop UX Contract

Desktop presents Dreams as one row in the Memory group of Settings -> Personalization, after the memory switch. The row holds the Dreams toggle and a "Manage" entry into the Dreams page.

The Dreams page holds:

- Manual "Run now" action.
- Auto-update Dreams toggle for applying future successful runs automatically.
- Run frequency.
- Recent-thread range.
- Last run status.
- Run history, with a "Review" action per run that opens Dashboard at
  `dashboardUrl#dreams/run/<runId>` when a Dashboard URL is available.

Required UX behavior:

- Hide Dreams controls when `capabilities.dreams` is false or absent.
- While memory is disabled, show the Dreams toggle disabled with its stored value and a tooltip asking the user to enable memories.
- Load `dreams/status` when entering the personalization settings surface or the Dreams page.
- Refresh status after saving Dreams settings.
- Refresh Dreams status when receiving `config/changed` with `regions` containing `memory` or a `Dreams.*` or `Memory.Enabled` key path.
- Disable "Run now" while `running = true`.
- Poll `dreams/status` after `dreams/run` until the run completes or the client times out.
- Load `dreams/list` in the management surface.
- Provide **Archive** for one non-running run and **Archive all** for all eligible
  runs. Implement both through the existing `dreams/archive` method; **Archive
  all** issues one request per run.
- Treat archive as hiding run history, not deleting files, stores, internal
  threads, or traces. Desktop does not expose permanent deletion.
- Do not show raw markdown previews, index diffs, or apply/discard/cancel review
  actions in Desktop.
- Disable the run-row Review action when the connected server does not expose a
  Dashboard URL, and explain that review happens in Dashboard.
- Show concise success, skipped, and failure states.
- User-facing UI should consistently label the capability as Dreams.
- Do not show pending Dreams output as prompt-visible memory until apply succeeds.

## 11. Dashboard UX Contract

Dashboard owns detailed Dreams review and recovery.

Required Dashboard behavior:

- Expose a dedicated Dreams navigation page when Dreams endpoints are available.
- Support hash deep links `#dreams` and `#dreams/run/<runId>` on first load and
  on `hashchange`.
- Present summary-first status: current status, active store, pending count,
  auto-apply setting, run records, change summary, and trace/session links.
- List archived runs together with other run records so users can review and
  permanently delete archived history.
- Show detailed review material only in expandable areas: active/output index
  diff, raw markdown, topic paths, input manifest, and error details.
- Provide complete controls:
  - Run now.
  - Cancel running runs.
  - Apply pending runs.
  - Make active any succeeded, non-discarded, non-archived run.
  - Discard pending runs.
  - Archive non-running runs.
  - Permanently delete one non-running run.
  - Permanently delete all runs when none is running.
- Reusing apply semantics for "Make active" allows rollback to an older
  succeeded run after an automatically applied store proves bad.
- **Delete** and **Delete all** require irreversible-action confirmation and use
  the Dashboard HTTP `DELETE` endpoints. They do not call `dreams/archive`.

Dashboard HTTP endpoints:

| Endpoint | Purpose |
|----------|---------|
| `GET /dashboard/api/dreams/status` | Read Dreams config/status, active store, and latest run. |
| `GET /dashboard/api/dreams/runs` | List all Dream Runs, including archived runs. |
| `GET /dashboard/api/dreams/runs/{runId}` | Read one run plus review preview. |
| `POST /dashboard/api/dreams/run` | Request a manual Dream Run. |
| `POST /dashboard/api/dreams/runs/{runId}/apply` | Apply or make a succeeded run active. |
| `POST /dashboard/api/dreams/runs/{runId}/discard` | Discard a pending run. |
| `POST /dashboard/api/dreams/runs/{runId}/archive` | Archive a non-running run without physically deleting its artifacts. |
| `POST /dashboard/api/dreams/runs/{runId}/cancel` | Cancel a running run best-effort. |
| `DELETE /dashboard/api/dreams/runs/{runId}` | Permanently delete one non-running run and clean up its non-active artifacts. |
| `DELETE /dashboard/api/dreams/runs` | Permanently delete all runs when none is running. |

Deletion endpoint semantics:

- The single-run endpoint returns `404 Not Found` when `runId` does not exist.
- Either endpoint returns `409 Conflict` without deleting anything when a
  targeted run is running.
- A successful response reports `deletedRunIds`, `deletedCount`,
  `activeDreamStoreId`, `partial`, and `traceCleanupFailures`.
- `partial` is `true` only when Dreams run/input and eligible output-store
  deletion succeeded but one or more related internal thread/trace cleanups
  failed. Each `traceCleanupFailures` entry identifies `runId`, `threadId`, and
  an error message.
- `activeDreamStoreId` remains unchanged when its producing run is deleted.

## 12. Failure, Backpressure, And Safety

Maintenance failure must not fail user turns or context compaction. Runs report provider/model failures and normal insufficient-history skips without changing the active store. The schedule interval, lookback window and completed-turn threshold bound work. Inputs, outputs and reset remain workspace-scoped; provider calls use the workspace's configured model policy.
