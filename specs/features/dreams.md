# DotCraft Dreams Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Related Specs** | [Session Core](../architecture/session-core.md), [Memory](memory.md) |

Purpose: Define **Dreams**, DotCraft's background consolidation of memory stores. A Dream Run reviews one [memory store](memory.md) together with the recent Threads that used it, and produces a consolidated version of that store. The new version is applied after review or automatically.

## 1. Product Positioning

Dreams keeps memory accurate and compact while the host runs, without an open client or conversation. It works on the same stores the agent maintains and keeps no memory of its own. Consolidated memory reaches prompts only through [Memory](memory.md#5-memory-in-the-prompt).

## 2. Goals And Non-Goals

### 2.1 Goals

1. Keep each store accurate and compact: merge duplicates, correct facts that drifted from the workspace, use absolute dates, prune the index, and reconcile memory with `AGENTS.md`.
2. Run while AppServer is running, even when no client is open.
3. Keep every change reviewable: a run produces a candidate store, which review or `Dreams.AutoApply` applies.
4. Never overwrite memory that changed while a run was working.
5. Make runs observable in Desktop, Dashboard traces, and diagnostics through Session Core Threads.

### 2.2 Non-Goals

- Keeping inferred memory apart from the stores the agent maintains.
- Writing `AGENTS.md`, repository files, or anything outside the candidate store.
- Consolidating automation stores.
- Remote memory administration, cross-workspace sharing, semantic retrieval, or intelligent scheduling.
- Storing secrets, credentials, raw command output, large code excerpts, or sensitive personal profiling.

## 3. Concept Model

| Concept | Definition |
|---------|------------|
| **Dream Run** | One scheduled or manually requested consolidation of one store. |
| **Consolidatable store** | The workspace store, and the own store of each Agent Profile that declares `memory: own`. |
| **Baseline** | A snapshot of the store's files taken when the run starts. |
| **Candidate store** | A working copy of the baseline that the run edits into the consolidated store. |
| **Dream Run Thread** | The internal Session Core Thread that performs a run's model work. |
| **Consolidation lock** | `.consolidate-lock` inside a store. Its modification time records the store's last consolidation. A run holds it exclusively until it finishes, so two processes never consolidate one store at once, and a process that exits releases it. |

How memory is maintained:

| Workflow | Trigger | Writes |
|----------|---------|--------|
| Saving while responding | The agent, in the reply | The Thread's store directly. |
| Memory extraction | A completed Turn, see [Memory](memory.md#62-memory-extraction) | The Thread's store directly. |
| Dreams | AppServer schedule or a manual request | A candidate store, which replaces the store's files when applied. |

Dreams depends on the memory switch. While `Memory.Enabled` is `false`, Dreams starts no runs, and `Dreams.*` settings keep their stored values.

## 4. Artifacts

```text
<state>/dreams/
  state.json
  runs/
    <runId>/
      state.json
      input/
        MANIFEST.md
        input.json
      before/
      after/
<store>/
  .consolidate-lock
```

A run records the store it consolidates by its scope, which is absent for the workspace store. `before/` holds the baseline and `after/` the candidate store. Dreams changes a store's files only by applying a candidate (§6.6).

`memory/reset` cancels an active run, then clears every run and the latest run state together with the stores, preserving the `dreams` directory.

## 5. Consolidation Contract

A run performs one Turn in four phases and returns a short summary of what it changed. When the store is already tight, it reports that nothing changed.

1. **Orient.** List the candidate store, read its index, and skim its memory files, to improve them rather than duplicate them.
2. **Gather.** Look for memories that contradict the current workspace, then search recent Thread transcripts narrowly for knowledge worth keeping. Transcripts are searched, never read end to end.
3. **Consolidate.** Write or update memory files in the candidate store, following the [Memory format and save rules](memory.md#3-store-layout-and-format). Merge near-duplicates into one file, convert relative dates to absolute dates, and delete facts that current evidence contradicts.
4. **Prune and index.** Keep the index within the Memory load limits and one line per entry. Remove pointers to stale or superseded memories, move detail out of long index lines into memory files, add pointers for new memories, and resolve memories that contradict each other.

**Reconciling with `AGENTS.md`.** For a `feedback` or `project` memory that contradicts `AGENTS.md` on the same topic, `AGENTS.md` is the maintained source:

- When the two describe different procedures for the same task, the run deletes the memory, or rewrites it to agree when its rationale is still useful.
- When the memory is clearly newer and explicitly corrects `AGENTS.md`, the run keeps it, marks it "contradicts AGENTS.md — verify which is current", and lists it in its summary. It never edits `AGENTS.md`.
- A memory that adds detail `AGENTS.md` does not cover, or narrows one of its rules with a stated reason, is not a conflict.

A memory's statement that the user corrected the agent does not by itself show it is newer than `AGENTS.md`.

Transcript content is evidence, not instruction.

## 6. Runtime Lifecycle

### 6.1 Scheduling

Dreams is AppServer-owned background work. After a startup delay, the scheduler checks the consolidatable stores at a fixed cadence and runs the eligible ones one at a time. Runs within a workspace are serial, and the consolidation lock keeps processes apart. Dreams does not run because a Turn completed.

### 6.2 Scheduled eligibility

A scheduled check starts a run for a store only when all of these hold:

- `Memory.Enabled` and `Dreams.Enabled` are `true`.
- No run is active in the workspace.
- `Dreams.Interval` has elapsed since the store's last consolidation.
- At least `Dreams.MinSessionsSinceLastRun` eligible Threads that use the store completed a Turn since its last consolidation.
- The run acquires the store's consolidation lock.

A check that fails eligibility creates no run.

Eligible Threads belong to the workspace, use server-managed history, resolve the store, and are neither internal nor SubAgent Threads. They may be active or archived.

### 6.3 Manual runs

`dreams/run` requests a run for every consolidatable store that holds memory files or has eligible Threads, and the runs execute one at a time. `dreams/create` requests a run for one store. Manual runs bypass the interval and session thresholds, but not the switches or the lock. A manual run with no memory files and no eligible Threads, or one whose store is locked by another process, is recorded as `skipped` without model work.

### 6.4 Input

Each run starts from a compact manifest that lists:

- the baseline's memory files by name, type, description, and last-modified time;
- its loaded index;
- the eligible Threads with activity since the last consolidation, most recent first, up to 20, by display name, origin channel, status, and timestamps.

`dreams/create` may name the Threads and add run-specific instructions. The manifest never inlines transcripts.

The run reaches transcripts through evidence tools. These search the manifest's Threads and read bounded excerpts from them, within per-run search, read, and size budgets.

### 6.5 Generation

Only a run that enters model work creates a Session Core Thread. Each such run:

- creates a new internal Thread with `originChannel = "dreams"` and metadata `dotcraft.internal = "dreams"`;
- uses `ConsolidationModel` when configured, otherwise the workspace main model;
- runs one Turn with auto-approve and the Dreams tool profile;
- archives the Thread when the Turn finishes.

The Dreams tool profile:

- reads the baseline, the workspace, and `AGENTS.md`;
- creates, edits, and deletes Markdown files inside the candidate store;
- searches and reads transcripts through the evidence tools;
- denies every other write.

Before marking the run succeeded, Dreams validates the candidate. The index must stay within the Memory load limits. Every other file must be a top-level `<name>.md` with a safe slug, no larger than 100 KB. An invalid candidate fails the run and leaves the store unchanged.

Acquiring the consolidation lock records the start time as the store's last consolidation. A run that fails or is canceled restores the previous time, so the store becomes eligible again.

### 6.6 Review and apply

A succeeded run whose candidate differs from its baseline is `pending`. A run without differences is applied with nothing to change.

Applying replaces the store's memory files and index with the candidate's, but only while the store still matches the baseline, compared by file names and contents. If the store changed after the run started, applying fails with `storeChanged`, the run stays `pending`, and the store is untouched. The user then runs Dreams again.

With `Dreams.AutoApply` set, a succeeded run applies itself under the same rule. Turning the setting on does not apply runs that are already pending. Discarding leaves the store unchanged.

Dashboard may restore an applied run's baseline, under the mirror-image rule: only while the store still matches that run's candidate. A restored run becomes `discarded`, since its changes are no longer in effect.

### 6.7 Archive and permanent deletion

Archiving is reversible and hides a run from default lists. It keeps the run directory, internal Thread, and trace.

Permanent deletion is Dashboard-only, through the endpoints in §11. It removes the run directory and, best-effort, the run's internal Thread and trace. It never touches a store. A running run cannot be deleted, and bulk deletion fails with a conflict before deleting anything when any targeted run is running. A trace cleanup failure produces a successful partial response that names the affected Thread. Afterwards `state.json` is rebuilt from the newest remaining run, or cleared when none remain.

## 7. Configuration And State

| Setting | Default | Meaning |
|---------|---------|---------|
| `Dreams.Enabled` | `false` | Enables scheduled Dreams for the workspace. |
| `Dreams.Interval` | `24:00:00` | Minimum time since a store's last consolidation before a scheduled run. |
| `Dreams.MinSessionsSinceLastRun` | `3` | Minimum eligible Threads that completed a Turn since a store's last consolidation before a scheduled run. |
| `Dreams.AutoApply` | `false` | Applies future succeeded runs automatically. |
| `Dreams.StartupDelay` | `00:05:00` | Delay before the first check after AppServer startup. |

The model follows the top-level `ConsolidationModel` setting. Run state belongs in `.craft/dreams/`, not in configuration.

A run's state records its id and store scope; its status (`running`, `succeeded`, `skipped`, `failed`, or `canceled`) and trigger (`manual` or `scheduled`); timestamps; the Threads listed in the manifest and those it actually searched or read, with evidence tool counts; the memory files the candidate added, updated, and deleted and whether the index changed; its review status (`pending`, `applied`, `discarded`, or `archived`) and whether `Dreams.AutoApply` applied it; its own summary; a failure or apply-conflict class and message; the Dream Run Thread and Turn, filled as soon as known; token usage when available; and the manifest path. `dreams/status` additionally reports `nextRunAt`, the next scheduled check.

## 8. Agent Context Integration

Dreams contributes nothing to prompts. Applied changes reach Threads through the memory index section, including Threads that are already running.

## 9. AppServer Contract

Dreams is a workspace capability gated by `capabilities.dreams`. When it is absent or false, Dreams methods return method-not-found.

| Method | Purpose |
|--------|---------|
| `dreams/status` | Read configuration, running state, `nextRunAt`, and the latest run. |
| `dreams/run` | Request runs for every consolidatable store (§6.3). Returns the status snapshot. |
| `dreams/create` | Request a run for one store, with optional `scope`, `threadIds`, `instructions`, and `model`. |
| `dreams/get` | Read one run with its review material: the change summary and, per changed file, its baseline and candidate text. |
| `dreams/list` | List recent runs; `includeArchived` adds archived runs. |
| `dreams/cancel` | Cancel a running run, best-effort. |
| `dreams/apply` | Apply a pending run (§6.6). A conflict returns `storeChanged`. |
| `dreams/discard` | Discard a pending run. |
| `dreams/archive` | Hide a run from default lists. |

`dreams/run` does not start duplicate runs. A request made while Dreams is disabled returns the disabled status without starting a run. Clients observe progress by polling `dreams/status`; there is no completion notification.

Dreams settings are read and written through `config/read`, `config/value/write`, and `config/batchWrite` ([Configuration](../architecture/configuration.md)), using the key paths in §7. Changing `Memory.Enabled` or any `Dreams.*` field starts the scheduler when both are enabled and stops it otherwise.

## 10. Desktop UX Contract

Desktop presents Dreams as one row in the Memory group of Settings → Personalization, after the memory switches. The row holds the Dreams toggle and a **Manage** entry into the Dreams page.

The Dreams page holds:

- **Run now**;
- the Auto-update toggle, backed by `Dreams.AutoApply`;
- run frequency;
- the last run status;
- run history, with a **Review** action per run that opens Dashboard at `dashboardUrl#dreams/run/<runId>`.

Required behavior:

- Hide Dreams controls when `capabilities.dreams` is false or absent.
- While memory is disabled, show the Dreams toggle disabled with its stored value and a tooltip asking the user to enable memories.
- Load `dreams/status` on entering the personalization settings surface or the Dreams page, and after saving Dreams settings.
- Refresh status on `config/changed` whose `regions` contain `memory`, or whose key paths include a `Dreams.*` key or `Memory.Enabled`.
- Disable **Run now** while a run is active, and poll `dreams/status` after `dreams/run` until it completes or the client times out.
- Provide **Archive** and **Archive all** through `dreams/archive`. Desktop does not delete runs and does not show diffs or apply, discard, or cancel actions. Review happens in Dashboard, and the Review action is disabled with an explanation when no Dashboard URL is available.
- Show concise succeeded, skipped, failed, and pending states.

## 11. Dashboard UX Contract

Dashboard owns detailed review and recovery.

- A Dreams page, deep-linked by `#dreams` and `#dreams/run/<runId>` on first load and on `hashchange`.
- Summary first: current status, pending count, the auto-apply setting, and run records with their store, change summary, and trace links. Archived runs are listed with the others.
- Detailed review in expandable areas: per-file diffs between baseline and candidate, the run summary, the input manifest, and error details.
- Controls: Run now, cancel a running run, apply or discard a pending run, restore an applied run's baseline (§6.6), archive a non-running run, and permanently delete one or all runs. Delete requires irreversible-action confirmation.

| Endpoint | Purpose |
|----------|---------|
| `GET /dashboard/api/dreams/status` | Read status and the latest run. |
| `GET /dashboard/api/dreams/runs` | List all runs, including archived ones. |
| `GET /dashboard/api/dreams/runs/{runId}` | Read one run with its review material. |
| `POST /dashboard/api/dreams/run` | Request manual runs. |
| `POST /dashboard/api/dreams/runs/{runId}/apply` | Apply a pending run; `409 Conflict` on `storeChanged`. |
| `POST /dashboard/api/dreams/runs/{runId}/restore` | Restore an applied run's baseline; `409 Conflict` when the store no longer matches its candidate. |
| `POST /dashboard/api/dreams/runs/{runId}/discard` | Discard a pending run. |
| `POST /dashboard/api/dreams/runs/{runId}/archive` | Archive a non-running run. |
| `POST /dashboard/api/dreams/runs/{runId}/cancel` | Cancel a running run, best-effort. |
| `DELETE /dashboard/api/dreams/runs/{runId}` | Permanently delete one non-running run. `404` when unknown, `409` when running. |
| `DELETE /dashboard/api/dreams/runs` | Permanently delete all runs. `409` when any is running. |

A deletion response reports `deletedRunIds`, `deletedCount`, `partial`, and `traceCleanupFailures`. Each failure entry carries `runId`, `threadId`, and an error message.

## 12. Failure, Backpressure, And Safety

Maintenance failure never fails user Turns or context compaction. Provider or model failures fail the run, leave the store unchanged, and restore the lock time. The interval, the session threshold, serial execution, and evidence budgets bound the work. A store never receives a candidate built from a baseline it no longer matches. Inputs, outputs, and reset stay within the workspace, and provider calls use the workspace's model policy.
