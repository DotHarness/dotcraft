# DotCraft Dynamic Workflows Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [SubAgent Core](subagents.md), [Tool Architecture](../architecture/tools-architecture.md), [Prompt Cache](../architecture/prompt-cache.md), [Background Tasks](../architecture/background-tasks.md), [Plugin Architecture](../architecture/plugin-architecture.md), [Code Mode](code-mode.md) |

Purpose: define the runtime, persistence, AppServer control, and Desktop presentation contracts for
model-authored JavaScript workflows that coordinate native DotCraft child agents, continue in the
background, and notify the parent thread when execution reaches a terminal state.

The public script API is DotCraft's stable orchestration contract.

## 1. Scope and Ownership

Dynamic Workflows comprise four cooperating boundaries:

1. The parent Agent authors or selects a workflow and invokes the stable `Workflow` model tool.
2. AppServer owns discovery, approval, run state, limits, replay, child-thread creation, and parent
   notification.
3. A hidden `script-worker workflow` process evaluates one JavaScript execution attempt and asks
   AppServer to perform agent calls through an internal JSONL protocol.
4. Trusted AppServer clients read and control persisted runs through typed Workflow methods and render
   the run projection without reading `.craft/workflows/runs` directly.

All model execution and model-visible tool use occurs in Session Core child threads. JavaScript is an
orchestration language with no direct filesystem, network, process, CLR, or DotCraft service access.

AppServer is authoritative for lifecycle and progress; clients own localization, formatting,
selection, and navigation.

## 2. Definitions and Discovery

### 2.1 File Format

A workflow is one JavaScript file whose first executable declaration is a literal metadata export:

```js
export const meta = {
  name: "review-change",
  description: "Review a change from independent perspectives",
  whenToUse: "Use for a substantive code review",
  phases: ["review", "synthesize"]
};

const reviews = await parallel([
  () => agent("Review correctness.", { label: "correctness", phase: "review" }),
  () => agent("Review test coverage.", { label: "tests", phase: "review" })
]);

return agent({ prompt: "Synthesize the reviews.", context: reviews }, { phase: "synthesize" });
```

`meta.name` and `meta.description` are required non-empty strings; `whenToUse` and `phases` are
optional. Metadata MUST be statically parseable without evaluating JavaScript. The body supports
top-level `await` and MUST finish with a JSON-serializable value.

The parser rejects `import`, dynamic `import()`, `require`, `eval`, `Function`, CLR access, and time or
random sources (`Date`, timers, and `Math.random` are not exposed), so replay depends only on the
script, arguments, and recorded orchestration calls.

### 2.2 Locations and Precedence

Saved workflows are discovered in this order:

1. Workspace `.craft/workflows/*.js`.
2. The current Craft home `workflows/*.js`.

Workspace definitions shadow personal definitions with the same `meta.name`. Duplicate names within one
scope produce a diagnostic and neither is registered. Paths are canonicalized, and paths that escape the
declared root after symlink resolution are rejected.

An enabled plugin may contribute workflows through its manifest `workflows` path, defaulting to
`<plugin-root>/workflows/` when present. Plugin workflows use the namespace `{pluginId}:{name}` and
never shadow workspace or personal workflows.

### 2.3 Slash Commands

Workspace and personal workflows register as `/{name}`; plugin workflows as `/{pluginId}:{name}`. A
slash command does not bypass the Agent: its text arguments go to the parent Agent, which converts them
to structured `args` and calls `Workflow`.

## 3. Model-Facing `Workflow` Tool

`Workflow`'s description, schema, identity, and relative tool order remain unchanged for the lifetime
of a thread.

### 3.1 Input

`mode` is required and selects the source; fields for other modes are ignored.

| `mode` | Required field | Source |
|--------|----------------|--------|
| `script` | `script` | A complete inline workflow, copied to the run directory as `script.js`. |
| `path` | `scriptPath` | A workflow under an allowed workspace, personal, plugin, or prior-run root. |
| `name` | `name` | Discovery per §2.2. |
| `resume` | `resumeFromRunId` | Deterministic replay of an eligible prior run's persisted script (§7). |

`args` is optional JSON exposed as the immutable global `args` (default `{}`).

Approval completes before the run starts. Invalid input, rejected approval, unavailable runtime, or an
ineligible resume source returns a normal tool error and no run.

### 3.2 Immediate Result

After persistence and worker launch, the tool returns `runId`, `name`, `status: "running"`,
`scriptPath`, and a `message` telling the Agent the run continues in the background, that it will be
notified on completion, and not to predict the result. The parent Turn continues normally; the
terminal state arrives as a task notification (§8.6).

## 4. JavaScript Runtime Contract

### 4.1 Globals

The worker exposes only these workflow-specific globals:

- `agent(input, options?)` starts one native child Agent and resolves to its result or `null`.
- `parallel(items)` evaluates deferred Agent calls concurrently and preserves input order.
- `pipeline(items, ...stages)` runs each item through stages sequentially while independent items
  progress concurrently. Each stage receives `(previous, original, index)`.
- `phase(name, detail?)` records a progress boundary and sets the current phase.
- `log(value)` records a bounded diagnostic event.
- `args` is the immutable invocation input.
- `budget` is a read-only view of hard limits and accumulated usage.
- `cwd` and restricted `process.cwd()` return the child workspace root.

Deterministic standard primitives (`JSON`, collections, promises, non-random `Math`) remain available.
The final value and every host-bound payload MUST be canonical-JSON serializable; cycles, functions,
symbols, bigint, non-finite numbers, and host objects fail the run.

### 4.2 `agent()` Options

| Field | Contract |
|-------|----------|
| `label` | Stable human-readable call label used in progress and diagnostics. |
| `phase` | Phase association; defaults to the current phase, else unphased. |
| `schema` | JSON Schema for a structured child result. |
| `model` | Optional child model override. |
| `effort` | Optional child reasoning override. |
| `isolation` | `shared` (default) or `worktree`. |
| `agentType` | Optional native Agent role selector. |

`model` and `effort` are invocation-specific overrides applied per
[SubAgent Core](subagents.md#62-fresh-and-bounded-children). `effort` accepts DotCraft's
provider-neutral values; `xhigh` normalizes to `extraHigh`. Children never inherit `ultra`, which
controls parent orchestration rather than provider reasoning.

The input may be a prompt string or an object with a prompt and serialized context. The effective
phase is journaled with every Agent operation.

### 4.3 Composition Semantics

`parallel()` requires deferred calls so call order is assigned deterministically before dispatch.
Results keep declaration order. An Agent that stops without a result or fails unrecoverably contributes
`null`.

`pipeline()` preserves input order. If a stage returns `null`, later stages for that item are skipped
and its result is `null`.

## 5. Child Agent Contract

### 5.1 Context and Policy

Each `agent()` call creates a fresh native child thread with `forkTurns=none` and waits for its
current task. The task name is scoped to the run, so runs never collide, and the child uses silent
completion: its result returns only to the script. It inherits the parent invocation's stable base
instructions, workspace, permission policy, and effective model defaults.

The child keeps the stable thread tool schema, including `Workflow`, to preserve prefix-cache identity,
but its invocation policy denies calls to `Workflow`. Volatile call data (prompt, schema, operation id,
label, phase, run id) goes in the task input, not the base prompt.

### 5.2 Structured Results

Without `schema`, the child's final text is returned. With `schema`, the child receives one fixed
terminating tool, `SubmitWorkflowResult`, with a stable model-visible schema; AppServer validates the
payload against the call's retained schema. A validation failure returns a tool error to the same child
for correction; a valid submission ends the child and becomes the `agent()` result.

### 5.3 Worktree Isolation

`isolation: "worktree"` uses the Session Core worktree lifecycle. The child thread id and worktree
reference are journaled. A clean worktree is removed when the child finishes; one with modifications,
untracked files, or new commits is retained. DotCraft never merges workflow worktrees automatically.

## 6. Worker and Host Protocol

### 6.1 Process Boundary

Every execution attempt runs `dotcraft script-worker workflow` through the shared script host defined
in [Code Mode](code-mode.md#9-script-host-and-worker). AppServer is authoritative for run state and
terminates the worker process tree on cancellation, limit violation, protocol failure, or shutdown.

The JavaScript engine runs without CLR interop, module loading, filesystem, network, process APIs,
dynamic code evaluation, time, or randomness, and enforces memory, statement-count, recursion, regex,
Promise, output-size, and cancellation limits. The host enforces a wall-clock deadline regardless of
cooperative yielding.

### 6.2 JSONL Messages

Host and worker exchange one UTF-8 JSON object per line on stdin/stdout. Every message includes a
protocol version, monotonic sequence number, scope (`<runId>/<attemptId>`), type, and payload.

| Direction | Types | Purpose |
|-----------|-------|---------|
| Host to worker | `initialize`, `agent.result`, `cancel` | Start evaluation, resolve a requested operation, or cancel. |
| Worker to Host | `ready`, `phase`, `log`, `agent.request`, `complete`, `failed` | Report progress, request work, or terminate. |

Worker diagnostics use bounded stderr. Malformed JSON, unexpected sequence numbers, unknown types,
duplicate terminal messages, or exit without a terminal message fail the attempt. AppServer validates
every request before creating Session Core work.

## 7. Deterministic Replay

AppServer journals Agent calls in deterministic declaration order: `agent.requested` (operation id,
label, effective phase, fingerprint, time) before dispatch; `agent.started` with the child thread id as
soon as it exists; then `agent.completed` or `agent.failed`. A replayed call records `agent.replayed`
with the source child thread, result, usage, and source operation. The journal suffices to rebuild
progress after reconnect or restart without re-evaluating the script.

The fingerprint covers the normalized prompt and context, label, effective phase, schema, model,
effort, isolation, agent type, and arguments.

Resume:

1. `resumeFromRunId` creates a new run with `resumedFromRunId` pointing to the source.
2. The new worker executes the selected script from the beginning.
3. The longest identical prefix of completed calls, compared in order, returns recorded results without
   new Agents.
4. The first added, changed, or incomplete call and every later call execute live.
5. New results are journaled under the new run, which may become a later replay source.

Replayed calls count their retained usage toward the new run, project with status `replayed`, keep
their source child navigable when it exists, and create no child or provider usage. Replay
reconstructs control flow by re-execution; it does not restore a JavaScript heap.

A source is eligible only while the same AppServer instance is alive and its status is `paused`,
`stopped`, `failed`, or `succeeded`. Active runs from a previous instance are marked `interrupted` at
startup and are not resumable. Script, argument, and option changes are allowed and establish the first
mismatch.

## 8. Lifecycle, Persistence, and Notification

### 8.1 AppServer Methods

The Dynamic Workflows module contributes:

| Method | Purpose |
|--------|---------|
| `workflow/run/list` | Page through runs owned by one parent thread. |
| `workflow/run/read` | Read one authoritative render-ready run projection. |
| `workflow/run/pause` | Pause a running run once the state persists. |
| `workflow/run/stop` | Stop a running or paused run once the state persists. |
| `workflow/run/resume` | Create a new run by replaying an eligible source. |
| `workflow/run/updated` | Server-to-client invalidation notification. |

The module advertises `capabilities.extensions.dynamicWorkflows`. Wire DTOs, pagination, and error
envelopes are defined by [AppServer Protocol](../protocols/appserver-protocol.md). There is no public
start or definition CRUD method: new execution begins only through the `Workflow` tool and its
approval contract.

### 8.2 Status

Runs use `running`, `paused`, `stopped`, `succeeded`, `failed`, or `interrupted`. Pause and stop both
cancel active children and the worker, retain the completed replay prefix, and remain resumable within
the current AppServer lifetime. Shutdown marks active runs `interrupted` after cancelling owned work.
The model stops a run with `TaskStop` and its run id; only the run's parent thread may, with
`workflow/run/stop` semantics.

Control rules:

- pause accepts `running`; repeating on `paused` is idempotent;
- stop accepts `running` or `paused`; repeating on `stopped` is idempotent;
- resume accepts `paused`, `stopped`, `failed`, or `succeeded` sources from the current instance and
  never changes the source;
- invalid transitions fail with `workflow_run_state_conflict` without mutation;
- an `interrupted` or other-instance source fails with `workflow_resume_unavailable`.

Pause and stop respond after the state and journal terminal event are durable. Resume responds after
the new run is persisted and launched.

A client resume keeps the source parent thread, reuses its parent Turn as provenance, and journals
`initiator: "client"`; a model resume uses the current Turn and journals `initiator: "model"`. Both
notify the parent normally.

### 8.3 Public Run Projection

AppServer builds the run projection from persisted state, ordered journal events, and child threads.
Clients treat it as authoritative and never read the run directory. It contains:

- run identity, owner thread, name, description, status, timestamps, resume source, result, and error;
- aggregate Agent counts, token usage, and tool-call count;
- server-derived `canPause`, `canStop`, and `canResume`;
- ordered phases with detail, status, and nested Agent operations;
- `unphasedAgents` for calls without a safe phase;
- per-Agent operation id, label, status, child thread, token usage, tool-call count, timestamps, and
  replay marker.

It never exposes model names, reasoning effort, script paths or contents, or raw arguments.

Declared phases form the initial graph in order; runtime-discovered phases append in first-observed
order. Phase detail is the latest `phase(name, detail)` value, projected as the string itself, compact
canonical JSON for other values, or absent for `null`.

Phase status is `pending`, `running`, `paused`, `completed`, `failed`, or `stopped`. A phase is active
when it is the latest entered phase or owns a non-terminal Agent. Entering a later phase completes an
earlier one once its Agents are terminal. A run that fails, stops, or pauses during an active phase
gives that phase the same state. A tolerated failed Agent does not fail its phase.

Agent status is `queued`, `running`, `completed`, `failed`, `stopped`, or `replayed`; child `cancelled`
and `stopped` project as `stopped`. Metrics come from Session Core when the child exists and otherwise
from journaled usage. The server emits `startedAt`/`completedAt`, not a ticking duration.

### 8.4 Files

```text
.craft/workflows/
├── *.js
└── runs/
    └── <runId>/
        ├── script.js
        ├── state.json
        └── journal.jsonl
```

`script.js` is the immutable script snapshot. `state.json` is an atomically replaced summary for status
reads and startup reconciliation. `journal.jsonl` is append-only and records operations, replay data,
child references, usage, progress, and the terminal event.

`.craft/workflows/runs/` belongs in `.craft/.gitignore`; saved definitions do not. Archiving the parent
thread retains runs; deleting it cancels active work and removes its run directories.

### 8.5 Client Invalidation

After an observable run change persists, AppServer broadcasts `workflow/run/updated` with `threadId`,
`runId`, and an open reason string such as `created`, `progress`, `control`, or `terminal`, without a
snapshot. Clients coalesce and call `workflow/run/read`; a later read wins. Clients may opt out through
the standard notification opt-out and establish state with list and read; no polling is needed. This
channel never starts an Agent Turn.

### 8.6 Parent Continuation

Exactly one `workflow` task notification is delivered for `succeeded`, `failed`, or `stopped`, except
when a `TaskStop` settles the run within the stop wait (the stop result carries the status). It uses
the envelope and delivery of [Background Tasks](../architecture/background-tasks.md):
`triggerKind = "workflow"`, `triggerLabel` is the workflow name, `triggerRefId` is the run id,
`triggerStatus` is the run status, `output_file` is the run directory, and `result` is the run result
on success or the error otherwise. A run stopped through `workflow/run/stop` is reported as stopped by
the user. Journaled delivery state prevents duplicate continuation.

## 9. Limits and Cancellation

The run owns one concurrency semaphore of capacity `min(16, max(1, logicalProcessors - 2))`. A run may
start at most 1000 Agent calls; further calls fail the run without starting a child. `budget` exposes
these limits, accumulated token usage (including replayed usage), and any explicit token budget.

Cancellation flows to queued calls, active child Turns, and the worker without discarding completed
journal entries. Final results, structured submissions, logs, stderr, and protocol frames have
independent size bounds.

## 10. Approval and Permissions

Starting a workflow in normal mode uses Session approval keyed by canonical source path and content
hash (inline scripts use their persisted run script). `Once`, `Session`, and `Always` keep their
meanings, with a workflow approval scope so `Always` can persist. A content change invalidates prior
authorization.

Ultra and an explicit `autoApprove` host policy skip workflow-start approval. This affects only the
orchestration script; every child Agent uses normal approval for its tools. Plugin installation or
enablement never implicitly approves a workflow.

## 11. Ultra and Prompt Cache

Ultra is a reasoning tier whose persistence and provider mapping are owned outside this specification.
Workflow children do not inherit its orchestration behavior.

The runtime context appends a short reminder to the latest user turn:

- in normal tiers, use `Workflow` only when the user, a slash command, or an active skill opts in;
- in Ultra, proactively plan workflows for substantive tasks when delegation gives a useful independent
  or staged structure; after launch, briefly tell the user what is running and end the Turn, deciding
  further phases when the notification arrives.

The reminder never changes base instructions. `Workflow` and `SubmitWorkflowResult` keep stable
descriptions, schemas, identities, and order; per-run and per-call values stay in volatile task input.

## 12. Desktop Presentation

Desktop renders Workflows in the existing tool-card and Detail Panel systems from the AppServer
projection, without a separate run manager.

The Workflow tool card keeps the normal tool-card header (lifecycle text, total elapsed, disclosure),
advances running elapsed time locally without projection reads, and shows phase summaries only
(previous, current, and declared future), each as status, title, and current detail. Activating a phase
title opens the Workflow Detail tab. The card has no stop control and does not show nested Agents.

A successful launch is a durable Turn result: Desktop keeps the latest successful launch card outside
the collapsed activity summary, before the Turn's final assistant message. Failed launches keep normal
tool-history behavior. After a successful launch in the visible parent thread, Desktop opens that run's
Detail tab once; reconnects, refreshes, continuations, and history hydration never reopen it.

The Workflow Detail tab shows the workflow name and description, a single outcome notice for stopped
(elapsed) or failed/interrupted (error) runs, and the complete phase graph with nested Agents. Each Agent
shows total tokens and tool-call count, plus elapsed time once terminal, never the model. Activating an
Agent with a child thread opens it in the Subagents tab. Stop is the only Desktop control; pause and
resume are protocol-only.

Queued parent continuations with `triggerKind = "workflow"` render the standard message-origin marker;
its `triggerRefId` opens the run's Detail tab.
