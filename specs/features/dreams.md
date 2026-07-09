# DotCraft Dreams Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Living |
| **Date** | 2026-05-18 |
| **Related Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Memory Consolidation](memory-consolidation.md), [Desktop Client](../clients/desktop-client.md), [Automations Lifecycle](automations-lifecycle.md) |

Purpose: Define **Dreams**, DotCraft's workspace-level background memory maintenance product and runtime capability. Dreams gives each workspace an offline memory management loop that can run from AppServer without an active Desktop, TUI, or conversation session.

---

## 1. Product Positioning

DotCraft's workspace promise is that sessions, memory, skills, tools, and automations stay useful across multiple entry points. Dreams strengthens that promise by making passive memory maintenance an AppServer-owned background capability instead of a task the user must explicitly request inside a conversation.

Terminology:

- Use **Dreams** for settings, UX, documentation, diagnostics, configuration sections, services, and code-level concepts.
- Describe Dreams as **background workspace memory organization**.
- Do not introduce a separate internal product name for this capability.

Core product statement:

> DotCraft has explicit long-term memory, and it also Dreams in the background to organize inferred workspace context.

Dreams is not a chat feature and not a standalone app. It is a workspace memory operations layer that turns recent workspace activity into passive background memory.

---

## 2. Goals And Non-Goals

### 2.1 Goals

1. Maintain workspace memory while AppServer is running, even when no client is open.
2. Separate passive inferred memory from explicit long-term memory.
3. Keep the product loop reviewable: schedule or request a run, generate a pending output store, let the user apply or discard it.
4. Make Dreams observable enough for Desktop review, Dashboard traces, and diagnostics.
5. Preserve the existing turn-time memory consolidation workflow.
6. Use Session Core for actual Dream model work so pruning and consolidation turns are inspectable.

### 2.2 Non-Goals

- Replacing `MEMORY.md` or turn-time memory consolidation.
- Creating a standalone Dreams app in the baseline design.
- Requiring Desktop or TUI to be open for background memory maintenance.
- Introducing remote memory administration or cross-workspace memory sharing.
- Automatically applying background-generated passive memory.
- Building semantic/vector retrieval or intelligent scheduling as part of the baseline Dreams design.
- Storing secrets, credentials, raw command output, large code excerpts, or sensitive personal profiling as passive memory.

---

## 3. Concept Model

| Concept | Definition |
|---------|------------|
| **Dreams** | Workspace background memory organization capability. |
| **Explicit Memory** | Durable facts, preferences, project context, and recurring instructions stored in `.craft/memory/MEMORY.md`. |
| **Historical Memory** | Append-only, grep-searchable event log stored in `.craft/memory/HISTORY.md`. |
| **Dream Store** | Passive inferred workspace context stored as `.craft/dreams/stores/<storeId>/INDEX.md` plus optional topic markdown files. |
| **Active Dream Store** | The Dream Store currently injected into future agent prompts. |
| **Pending Dream Store** | A generated output store awaiting user review. Pending stores do not affect prompts. |
| **Dream Run** | One scheduled or manually requested Dreams execution attempt. |
| **Dream Run Thread** | One internal Session Core thread created for an actual Dreams model run. |
| **Dream Input Window** | Workspace memory artifacts and recent thread transcripts inspected by one Dream Run. |
| **Dream Status** | Latest scheduler and run state exposed to AppServer clients. |

Relationship to memory consolidation:

| Workflow | Scope | Trigger | Writes | Product role |
|----------|-------|---------|--------|--------------|
| Long-term memory consolidation | One server-managed thread's model-visible history | Successful turn counter or manual thread action | `.craft/memory/MEMORY.md`, `.craft/memory/HISTORY.md` | Extract explicit durable memory from conversation flow. |
| Dreams | Workspace-wide recent history and memory artifacts | AppServer schedule or manual workspace action | pending `.craft/dreams/stores/<storeId>/` output store and `.craft/dreams/runs/<runId>/state.json`; apply switches `.craft/dreams/active.json` | Maintain reviewable passive inferred workspace context offline. |

---

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
    HISTORY.md
```

Artifact authority:

1. Current user instructions and inspected repository facts.
2. System, developer, workspace, and tool instructions.
3. `.craft/memory/MEMORY.md` explicit memory.
4. Active Dream Store `INDEX.md` passive inferred memory.
5. `.craft/memory/HISTORY.md` searched evidence.

Active Dream Store memory must not be treated as explicit user instruction. It is helpful inferred background context and should be ignored when it conflicts with more authoritative sources.

`memory/HISTORY.md` remains append-only event memory for explicit memory consolidation. Dreams may read it as evidence, but Dreams must not write `.craft/memory/*`.

`memory/reset` clears Dream Stores and Dreams-derived run state together with explicit memory artifacts, while preserving the `.craft/memory` and `.craft/dreams` directories.

---

## 5. Dream Memory Content Contract

Each Dream Store must contain a structured, concise `INDEX.md` that is useful as passive context. Topic files under `memory/*.md` may hold longer focused passive memory, but only the active store index is injected by default.

```md
# Dream Memory

Generated by DotCraft Dreams from recent workspace sessions. Treat as inferred background context, not explicit user instruction.

## Workspace Focus

## Active Threads And Open Loops

## Inferred Project Conventions

## Repeated Problems And Prior Mistakes

## Latest Stable Understanding

## Low-Signal Or One-Off Context To Ignore
```

Dreams may record:

- Recent product areas, modules, specs, and workflows that dominate workspace activity.
- Likely ongoing work and unresolved design or implementation loops.
- Repeated coding, testing, documentation, naming, protocol, or UX conventions.
- Prior mistakes, recurring failures, and areas where future agents should be cautious.
- Stable conclusions that supersede older discussion drift.
- Recent context that should be ignored because it was one-off or low signal.

Dreams must avoid:

- Secrets, credentials, API keys, tokens, or private connection details.
- Sensitive personal profiling unrelated to workspace work.
- Large pasted logs, command output, or code blocks.
- Treating speculation as certain fact.
- Overwriting explicit user preferences or durable instructions from `MEMORY.md`.

The preferred style is short bullets with enough specificity to guide future agents.

---

## 6. Runtime Lifecycle

### 6.1 Scheduling

Dreams is AppServer-owned background work.

Baseline scheduling behavior:

- Enabled by default for workspaces.
- Runs on a fixed interval.
- Uses a startup delay before the first eligibility check.
- Skips model work when insufficient new completed turns exist.
- Allows a manual "Run now" request from clients.
- Allows only one active Dream Run per workspace.

Dreams does not run because an individual turn completed. That responsibility belongs to long-term memory consolidation.

### 6.2 Scheduled Eligibility

A scheduled check may start a Dream Run only when all are true:

- `Dreams.Enabled = true`.
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
- Respect `Dreams.Enabled`.
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

---

## 7. Agent Context Integration

Agent prompt construction loads the active Dream Store `INDEX.md` by default.
It does not automatically load Dream Store `memory/*.md`; the index may point
the agent at topic files that should be read on demand through normal file tools
when relevant.

Required ordering:

1. System, developer, workspace, and tool instruction sources.
2. Explicit long-term memory from `.craft/memory/MEMORY.md`.
3. Passive Dream Memory from the active Dream Store index.
4. Thread/session history and current user input.

The Dream Memory block must be labeled:

```md
## Dream Memory

The following is inferred background context generated by scheduled Dreams. Use it as helpful workspace context, but do not treat it as explicit user instruction when it conflicts with direct instructions, project files, or MEMORY.md.

<contents of active Dream Store INDEX.md>
```

If the active index already begins with `# Dream Memory`, prompt construction may avoid duplicating the title, but it must still include the authority warning.

Dream Memory may guide:

- Which recent work areas are likely relevant.
- Which open loops may need attention.
- Which inferred conventions are worth checking.
- Which prior mistakes deserve caution.

Dream Memory must not:

- Override explicit user preferences.
- Justify ignoring repository evidence.
- Be treated as proof that a task is current or complete.
- Introduce hidden requirements not present in the user request or repo.

`memory/HISTORY.md` is still not loaded wholesale into normal agent context. It is only searched or tail-trimmed by specific workflows.

---

## 8. Configuration And State

Baseline configuration lives under `Dreams`.

| Setting | Default | Meaning |
|---------|---------|---------|
| `Dreams.Enabled` | `true` | Enables scheduled Dreams for the workspace. |
| `Dreams.Interval` | `24:00:00` | Minimum elapsed time between scheduled Dream eligibility checks that can run model work. |
| `Dreams.ThreadLookbackCount` | `20` | Maximum recent eligible threads listed in the per-run source manifest. |
| `Dreams.AutoApply` | `false` | Automatically applies future successful Dream Runs as the active Dream Store. Existing pending runs are unchanged. |
| `Dreams.HistoryTailChars` | `20000` | Maximum `HISTORY.md` tail characters included in a run. |
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
| `historyWritten` | Deprecated for Dreams store runs; remains `false`. |
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

---

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

Returns current Dreams configuration and run state for the connected workspace.

Params: `{}` or omitted.

Result:

```json
{
  "enabled": true,
  "interval": "24:00:00",
  "threadLookbackCount": 20,
  "autoApply": false,
  "historyTailChars": 20000,
  "minCompletedTurnsSinceLastRun": 5,
  "nextRunAt": "2026-05-12T00:00:00Z",
  "running": false,
  "activeDreamStoreId": "store_20260511000000_active",
  "lastRun": {
    "id": "dream_20260511_abc123",
    "status": "succeeded",
    "startedAt": "2026-05-11T00:00:00Z",
    "endedAt": "2026-05-11T00:00:28Z",
    "processedThreadCount": 18,
    "candidateThreadCount": 18,
    "evidenceThreadIds": ["thread_abc"],
    "writtenPaths": ["stores/store_20260511000000_pending/INDEX.md"],
    "evidenceSearchCount": 3,
    "evidenceReadCount": 4,
    "dreamWritten": true,
    "historyWritten": false,
    "outputStoreId": "store_20260511000000_pending",
    "reviewStatus": "pending",
    "autoApplied": false,
    "threadId": "thread_20260511_abcd",
    "turnId": "turn_001",
    "turnIds": ["turn_001", "turn_002"],
    "trigger": "manual",
    "message": null
  }
}
```

If Dreams has never run, `lastRun` is `null`.

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

`dreams/create` params:

```json
{
  "threadIds": ["thread_abc"],
  "threadLookbackCount": 20,
  "instructions": "Focus protocol decisions.",
  "model": "gpt-5.2"
}
```

`dreams/get`, `dreams/cancel`, `dreams/apply`, `dreams/discard`, and
`dreams/archive` params:

```json
{ "runId": "dream_20260511000000_abc123" }
```

`dreams/list` params:

```json
{ "includeArchived": false }
```

Run-result methods return:

```json
{
  "run": {
    "id": "dream_20260511000000_abc123",
    "status": "succeeded",
    "reviewStatus": "pending",
    "outputStoreId": "store_20260511000000_pending"
  },
  "activeDreamStoreId": "store_20260510000000_active",
  "preview": {
    "activeStoreId": "store_20260510000000_active",
    "outputStoreId": "store_20260511000000_pending",
    "activeIndexMarkdown": "# Dream Memory\n\n...",
    "outputIndexMarkdown": "# Dream Memory\n\n...",
    "activeTopicPaths": [],
    "outputTopicPaths": ["api-conventions.md"]
  }
}
```

`preview` is returned by `dreams/get`; list responses stay compact.

Review semantics:

- Succeeded runs are `pending` until applied, discarded, or archived.
- Applying a run switches `activeDreamStoreId` to that run's `outputStoreId`.
- Discarding and archiving do not delete stores in the baseline implementation.
- Canceling is best-effort; the final state becomes `canceled` when the internal run observes cancellation.

### 9.4 Workspace Config Fields

Workspace config read/update surfaces expose Dreams using user-facing wire field names while persisting internal `Dreams.*` keys.

| Wire field | Config field | Type | Meaning |
|------------|--------------|------|---------|
| `dreamsEnabled` | `Dreams.Enabled` | boolean \| null | Enables/disables scheduled Dreams; `null` removes workspace override. |
| `dreamsInterval` | `Dreams.Interval` | string \| null | Positive `TimeSpan` string for scheduled interval; `null` removes workspace override. |
| `dreamsThreadLookbackCount` | `Dreams.ThreadLookbackCount` | number \| null | Maximum recent eligible candidate threads listed in a Dream Run manifest; `null` removes workspace override. |
| `dreamsAutoApply` | `Dreams.AutoApply` | boolean \| null | Automatically applies future successful Dream Runs; `null` removes workspace override. |

Validation:

- `dreamsInterval` must parse as a positive `TimeSpan`.
- `dreamsThreadLookbackCount` must be a positive integer.
- Invalid fields return the same invalid-params style as other workspace config updates.

Successful Dreams setting changes emit `workspace/configChanged` with `regions: ["memory"]`.

### 9.5 Memory Reset

`memory/reset` clears:

- `.craft/memory/MEMORY.md`
- `.craft/memory/HISTORY.md`
- `.craft/dreams/stores/*`
- `.craft/dreams/runs/*`
- `.craft/dreams/state.json`
- `.craft/dreams/active.json`
- Other memory-derived files and caches

The memory and Dreams root directories remain in place.

---

## 10. Desktop UX Contract

Desktop presents Dreams from Settings -> Personalization.

Required user-visible controls:

- Long-term memory toggle for turn-time memory consolidation.
- Dreams toggle for background workspace memory organization.
- Auto-update Dreams toggle for applying future successful runs automatically.
- Run frequency.
- Recent-thread range.
- Last run status.
- Manual "Run now" action.
- "Manage Dreams" entry into a lightweight run-history surface.
- Run-history rows with a "Review" action that opens Dashboard at
  `dashboardUrl#dreams/run/<runId>` when a Dashboard URL is available.

Required UX behavior:

- Hide Dreams controls when `capabilities.dreams` is false or absent.
- Load `dreams/status` when entering the personalization settings surface.
- Refresh status after saving Dreams settings.
- Refresh Dreams status when receiving `workspace/configChanged` with `regions: ["memory"]`.
- Disable "Run now" while `running = true`.
- Poll `dreams/status` after `dreams/run` until the run completes or the client times out.
- Load `dreams/list` in the management surface.
- Do not show raw markdown previews, index diffs, or apply/discard/archive/cancel
  review actions in Desktop.
- Disable the run-row Review action when the connected server does not expose a
  Dashboard URL, and explain that review happens in Dashboard.
- Show concise success, skipped, and failure states.
- User-facing UI should consistently label the capability as Dreams.
- Do not show pending Dreams output as prompt-visible memory until apply succeeds.

---

## 11. Dashboard UX Contract

Dashboard owns detailed Dreams review and recovery.

Required Dashboard behavior:

- Expose a dedicated Dreams navigation page when Dreams endpoints are available.
- Support hash deep links `#dreams` and `#dreams/run/<runId>` on first load and
  on `hashchange`.
- Present summary-first status: current status, active store, pending count,
  auto-apply setting, run records, change summary, and trace/session links.
- Show detailed review material only in expandable areas: active/output index
  diff, raw markdown, topic paths, input manifest, and error details.
- Provide complete controls:
  - Run now.
  - Cancel running runs.
  - Apply pending runs.
  - Make active any succeeded, non-discarded, non-archived run.
  - Discard pending runs.
  - Archive non-running runs.
- Reusing apply semantics for "Make active" allows rollback to an older
  succeeded run after an automatically applied store proves bad.

Dashboard HTTP endpoints:

| Endpoint | Purpose |
|----------|---------|
| `GET /dashboard/api/dreams/status` | Read Dreams config/status, active store, and latest run. |
| `GET /dashboard/api/dreams/runs` | List Dream Runs. |
| `GET /dashboard/api/dreams/runs/{runId}` | Read one run plus review preview. |
| `POST /dashboard/api/dreams/run` | Request a manual Dream Run. |
| `POST /dashboard/api/dreams/runs/{runId}/apply` | Apply or make a succeeded run active. |
| `POST /dashboard/api/dreams/runs/{runId}/discard` | Discard a pending run. |
| `POST /dashboard/api/dreams/runs/{runId}/archive` | Archive a non-running run. |
| `POST /dashboard/api/dreams/runs/{runId}/cancel` | Cancel a running run best-effort. |

Suggested English copy:

> Dreams periodically organize workspace context in the background so DotCraft can keep passive project memory fresh even when no conversation is open.

Suggested Chinese copy:

> Dreams 会定期在后台整理工作区上下文，让 DotCraft 在没有打开对话时也能维护被动项目记忆。

---

## 12. Failure, Backpressure, And Safety

Dreams is best-effort maintenance.

Runtime rules:

- Dream failure does not fail active turns.
- Dream failure does not disable long-term memory consolidation.
- Dream failure does not trip context compaction circuit breakers.
- Failed runs do not switch the active Dream Store.
- Skipped runs should be normal when there is insufficient new history.
- Provider or model unavailability is recorded in run status.
- Repeated failure may delay future scheduled work, but the baseline design does not require a full backoff policy.

Cost controls:

- Fixed interval.
- Thread lookback limit.
- explicit memory history tail limit for input snapshots.
- Minimum completed-turn eligibility.

Safety boundaries:

- Stay within the current workspace memory scope.
- Do not merge memory across workspaces.
- Do not store secrets or credentials in Dream Memory.
- Do not record sensitive personal inferences unrelated to workspace work.
- Preserve the lower authority of inferred Dream Memory.
- Respect `memory/reset` as clearing all derived memory artifacts including Dream Memory.

Dreams may still call the configured model provider like other DotCraft model workflows. Provider privacy depends on the workspace's configured endpoint and model.

---

## 13. Acceptance Checklist

- New workspaces have `Dreams.Enabled = true` by default.
- AppServer creates a Dreams scheduler without blocking startup.
- Scheduler waits for `Dreams.StartupDelay` before the first eligibility check.
- Scheduler skips without model calls when there are too few new completed turns.
- Scheduler starts at most one run per workspace at a time.
- Manual runs bypass interval timing but respect active-run and enabled checks.
- Actual model runs create one new internal Session Core Dream Run thread with `originChannel = "dreams"`.
- Actual model runs submit two turns: pruning pass and consolidation pass.
- Pre-model skipped attempts do not create Session threads.
- Dream Run threads are hidden from ordinary `thread/list` by default but remain visible to Dashboard trace/session views.
- Dream Run state includes `threadId`, `turnId`, `turnIds`, `trigger`, `outputStoreId`, `reviewStatus`, `usage`, and `inputManifestPath` when applicable.
- Dream Run threads do not trigger long-term memory consolidation.
- Input collection reads eligible recent top-level server-managed threads without mutating them.
- A successful run writes a valid output store with `INDEX.md`.
- With `Dreams.AutoApply = false`, a successful run stays pending and does not affect prompts until applied.
- With `Dreams.AutoApply = true`, a future successful run becomes active immediately and records `reviewStatus = applied` plus `autoApplied = true`.
- Enabling `Dreams.AutoApply` does not retroactively apply existing pending runs.
- Failed generation leaves the active Dream Store unchanged.
- Latest run state is persisted or recoverable for status surfaces.
- Missing active Dream Store does not affect agent prompt building.
- Non-empty active Dream Store index is included in main agent context by default after `MEMORY.md`.
- Dream Memory prompt text includes lower-authority language.
- `memory/HISTORY.md` is still not loaded wholesale into normal agent context.
- `memory/reset` clears Dreams artifacts and derived state.
- `initialize` advertises `capabilities.dreams` when the server supports Dreams.
- `dreams/status` returns config, running state, active store id, next run time, and last run state.
- `dreams/run` starts an immediate background run when enabled and idle, and does not start duplicates.
- `dreams/create|get|list|cancel|apply|discard|archive` implement the review lifecycle.
- Workspace config read/update supports Dreams fields and validation.
- Successful Dreams config changes emit `workspace/configChanged` with `regions: ["memory"]`.
- Desktop hides Dreams controls when capability is unavailable.
- Desktop shows Dreams controls under Personalization when available.
- Desktop exposes a lightweight Dreams run-history surface that opens Dashboard review links.
- Dashboard exposes detailed Dreams review, diff, trace links, apply/make-active, discard, cancel, and archive controls.
- Desktop uses Dreams terminology consistently.
- Desktop can run now and refresh status.

---

## 14. Evolution Path

The baseline design starts with reviewable file stores and leaves room for richer memory operations.

Likely future extensions:

- Dream versions and rollback.
- Per-channel, per-thread, or per-origin inclusion policies.
- Usage and cost summaries per run.
- Plugin hooks that contribute additional read-only signals to Dream input.
