# DotCraft Dynamic Workflows Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.1 |
| **Status** | Draft |
| **Date** | 2026-08-12 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [SubAgent Core](subagents.md), [Tool Architecture](../architecture/tools-architecture.md), [Prompt Cache](../architecture/prompt-cache.md), [Model Options](model-options.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Plugin Architecture](../architecture/plugin-architecture.md) |

Purpose: define the runtime, persistence, AppServer control, and Desktop presentation contracts for
model-authored JavaScript workflows that coordinate native DotCraft child agents, continue in the
background, and notify the parent thread when execution reaches a terminal state.

The public script API defines DotCraft's stable orchestration contract. Runtime-specific extensions
are specified explicitly below.

---

## 1. Scope and Ownership

Dynamic Workflows comprise four cooperating boundaries:

1. The parent Agent authors or selects a workflow and invokes the stable `Workflow` model tool.
2. AppServer owns discovery, approval, run state, limits, replay, child-thread creation, and parent
   notification through `IDynamicWorkflowService`.
3. A hidden `workflow-worker` process evaluates one JavaScript execution attempt with Jint and asks
   AppServer to perform agent calls through an internal JSONL protocol.
4. Trusted AppServer clients read and control persisted runs through typed Workflow methods and render
   the resulting run projection without reading `.craft/workflows/runs` directly.

All model execution and model-visible tool use occurs in Session Core child threads. JavaScript is an
orchestration language and receives no direct filesystem, network, process, CLR, or DotCraft service
access.

This specification also defines the protocol-visible `Ultra` reasoning value and the required Desktop
Workflow tool card and detail presentation. AppServer remains authoritative for lifecycle and progress;
clients own localization, formatting, selection, and navigation.

---

## 2. Definitions and Discovery

### 2.1 File Format

A workflow is one JavaScript file. Its first executable declaration MUST be a literal metadata export:

```js
export const meta = {
  name: "review-change",
  description: "Review a change from independent perspectives",
  whenToUse: "Use for a substantive code review",
  phases: ["inspect", "review", "synthesize"]
};

const reviews = await parallel([
  () => agent("Review correctness.", { label: "correctness", phase: "review" }),
  () => agent("Review test coverage.", { label: "tests", phase: "review" })
]);

return agent({
  prompt: "Synthesize the completed reviews.",
  context: reviews
}, { label: "synthesis", phase: "synthesize" });
```

`meta.name` and `meta.description` are required non-empty strings. `whenToUse` and `phases` are
optional. Metadata MUST be statically parseable without evaluating JavaScript; computed properties,
function calls, imports, and runtime-dependent metadata are invalid. The remaining body supports
top-level `await` and MUST finish with a JSON-serializable value.

The parser rejects `import`, dynamic `import()`, `require`, `eval`, `Function`, CLR access, and uses of
time or random sources. `Date`, timers, and `Math.random` are not exposed. This keeps replay dependent
on the script, arguments, and recorded orchestration calls rather than ambient worker state.

### 2.2 Locations and Precedence

Saved workflows are discovered in this order:

1. Workspace `.craft/workflows/*.js`.
2. The current Craft home `workflows/*.js`.

Workspace definitions shadow personal definitions with the same `meta.name`. Two definitions with the
same name in one scope produce a diagnostic and neither ambiguous command is registered. Paths are
canonicalized before use. Discovery and execution reject paths that escape the declared root after
symlink resolution.

An enabled plugin may contribute workflows through its manifest `workflows` path. If the field is
absent and `<plugin-root>/workflows/` exists, that directory is used. Plugin workflows always use the
namespace `{pluginId}:{name}` and never shadow workspace or personal workflows.

### 2.3 Slash Commands

Saved workspace and personal workflows register as `/{name}`. Plugin workflows register as
`/{pluginId}:{name}`. A slash command does not bypass the Agent: its text arguments are delivered to
the parent Agent, which converts them to structured `args` and calls the same `Workflow` tool used for
inline or programmatic execution.

---

## 3. Model-Facing `Workflow` Tool

`Workflow` is a stable model tool. Its description, schema, canonical identity, and relative tool
order MUST remain unchanged for the lifetime of a thread.

### 3.1 Input

```json
{
  "script": "export const meta = { ... }; ...",
  "scriptPath": ".craft/workflows/review-change.js",
  "name": "review-change",
  "args": { "target": "src/" },
  "resumeFromRunId": "run_..."
}
```

Exactly one of `script`, `scriptPath`, or `name` is required.

- `script` supplies a complete workflow and is copied to the new run directory as `script.js`.
- `scriptPath` resolves a workflow under an allowed workspace, personal, plugin, or prior-run root.
- `name` uses the discovery rules in §2.2.
- `args` is optional JSON and is exposed as immutable global `args`; omission is equivalent to `{}`.
- `resumeFromRunId` requests deterministic replay from an eligible prior run while using the newly
  resolved script and arguments.

Approval completes before the run starts. Invalid input, rejected approval, unavailable runtime, or an
ineligible resume source returns a normal tool error and does not return a running run record.

### 3.2 Immediate Result

After persistence and worker launch, the tool returns immediately:

```json
{
  "runId": "run_...",
  "name": "review-change",
  "status": "running",
  "scriptPath": ".craft/workflows/runs/run_.../script.js"
}
```

After recording this successful tool result, AppServer completes the initiating parent Turn. The
Workflow continues in the background, and its terminal state resumes the parent through the
queued-turn contract in §8. A failed launch remains a normal tool error and does not complete the
parent Turn, so the Agent may recover or retry.

---

## 4. JavaScript Runtime Contract

### 4.1 Globals

The worker exposes only these workflow-specific globals:

- `agent(input, options?)` starts one native child Agent and resolves to its result or `null`.
- `parallel(items)` evaluates deferred Agent calls concurrently and preserves input order.
- `pipeline(items, ...stages)` runs each item through stages sequentially while independent items may
  progress concurrently. Each stage receives `(previous, original, index)`.
- `phase(name, detail?)` records a progress boundary.
- `log(value)` records a diagnostic event with bounded serialized output.
- `args` is the immutable structured invocation input.
- `budget` is a read-only view of applicable hard limits and accumulated usage.
- `cwd` and restricted `process.cwd()` return the child workspace root.

Standard deterministic JavaScript primitives such as `JSON`, arrays, objects, maps, sets, promises,
and non-random `Math` functions remain available. The final value and every Host-bound payload MUST be
serializable with the workflow protocol's canonical JSON encoder; cycles, functions, symbols, bigint,
non-finite numbers, and host objects fail the run.

### 4.2 `agent()` Options

`agent()` accepts these options:

| Field | Contract |
|-------|----------|
| `label` | Stable human-readable call label used in progress and diagnostics. |
| `phase` | Phase association for progress grouping. |
| `schema` | JSON Schema for a structured child result. |
| `model` | Optional child model override. |
| `effort` | Optional child reasoning override. |
| `isolation` | `shared` by default or `worktree`. |
| `agentType` | Optional native Agent role selector. |

For a fresh native child, an explicit `model` or `effort` is an invocation-specific override. It is
applied after the selected role's model default and before final capability normalization, as defined
by [SubAgent Core](subagents.md#62-fresh-and-bounded-children).

`effort` accepts DotCraft's provider-neutral child values and compatibility aliases. `xhigh` and
`max` normalize to `extraHigh`. `ultra` is not inherited by workflow children because Ultra controls
parent orchestration behavior rather than provider reasoning.

`phase(name, detail?)` establishes `name` as the current phase after recording its progress boundary.
An `agent()` call with an explicit `options.phase` uses that value. Otherwise it inherits the current
phase, if one exists. Calls made before any phase and without an explicit phase remain unphased. The
effective phase, not merely the caller-supplied option, is journaled with every Agent operation.

The input may be a prompt string or a JSON object containing a prompt and serialized context. Model,
effort, schema, prompt, isolation, and role are included in the call fingerprint defined in §7.

### 4.3 Composition Semantics

`parallel()` requires deferred calls so call order is assigned deterministically before dispatch.
Results retain declaration order regardless of completion order. An Agent that stops without a result
or encounters an unrecoverable execution error contributes `null`.

`pipeline()` preserves input order. Each item advances through its stages independently. If a stage
returns `null`, later stages for that item are skipped and its final result remains `null`.

---

## 5. Child Agent Contract

### 5.1 Context and Policy

Each `agent()` call creates a fresh native Session Core child thread with `forkTurns=none`. It waits for
that child's current task to finish but does not inherit the parent conversation transcript. It
inherits the stable base instructions, workspace, permission policy, and effective model defaults of
the parent invocation.

The model-visible tool schema remains the stable thread schema, including `Workflow`, to preserve
prefix-cache identity. The child invocation policy denies model-originated calls to `Workflow`.

`NativeSubAgentGuidance` carries the stable child completion protocol. Volatile call data -- prompt,
schema, operation id, label, phase, and run id -- is placed in the current task input rather than the
base prompt. Requested and effective model and effort values are recorded in the journal.

### 5.2 Structured Results

When `schema` is absent, the child's final text result is returned. When `schema` is present, the child
receives one fixed terminating tool, `SubmitWorkflowResult`, whose model-visible schema is stable.
AppServer retains the call-specific JSON Schema outside the model tool definition.

On submission, AppServer validates the payload against the retained schema. A validation failure is
returned to the same child as a tool error so it may correct the result. A valid submission ends the
child immediately and becomes the `agent()` result. The per-call schema is never projected as a
dynamic model tool schema.

### 5.3 Worktree Isolation

`isolation: "worktree"` uses the Session Core worktree lifecycle. The child thread id and worktree
reference are journaled. A clean worktree is removed automatically when the child finishes. A
worktree with modifications, untracked files, or a new commit is retained for inspection. DotCraft
does not merge workflow worktrees automatically.

---

## 6. Worker and Host Protocol

### 6.1 Process Boundary

`DotCraft.DynamicWorkflows` owns the service and starts the current DotCraft executable in hidden
`workflow-worker` mode for every execution attempt. The worker uses Jint `EvaluateAsync` and runs one
script attempt. AppServer is authoritative for run state and terminates the entire worker process tree
on cancellation, limit violation, protocol failure, or shutdown.

Jint is configured without CLR interop, module loading, filesystem, network, process APIs, dynamic
code evaluation, time, or random capabilities. The worker applies memory, statement-count, recursion,
Promise, output-size, and cancellation constraints. A wall-clock deadline and process termination
remain Host-enforced even when the engine cannot cooperatively yield.

### 6.2 JSONL Messages

Host and worker exchange one UTF-8 JSON object per line on redirected standard input/output. Every
message includes a protocol version, run id, attempt id, type, and monotonic sequence number.

The minimum message families are:

| Direction | Types | Purpose |
|-----------|-------|---------|
| Host to worker | `initialize`, `agent.result`, `cancel` | Start evaluation, resolve a requested operation, or cancel. |
| Worker to Host | `ready`, `phase`, `log`, `agent.request`, `complete`, `failed` | Report progress, request controlled work, or terminate. |

Only stdout carries protocol messages. Worker diagnostics use redirected stderr and are bounded.
Malformed JSON, unexpected sequence numbers, unknown message types, duplicate terminal messages, or a
worker exit without a terminal message fail the attempt. AppServer validates every worker request
before creating Session Core work.

---

## 7. Deterministic Replay

AppServer journals Agent calls in their deterministic declaration order. Every call records an
`agent.requested` event before replay or live dispatch, including its operation id, label, effective
phase, fingerprint, and request time. A live child records `agent.started` with its child thread id as
soon as the child exists, followed by a terminal `agent.completed` or `agent.failed` event. A replayed
call records `agent.replayed` with the source child thread, result, usage, and source operation
reference. These events retain enough information to reconstruct progress after reconnect or process
restart without evaluating the script again.

The canonical fingerprint covers the normalized prompt and context, label, effective phase, schema,
model, effort, isolation, agent type, and arguments. Requested and effective model options remain
runtime audit data and are not part of the public Desktop run projection.

Resume follows these rules:

1. `resumeFromRunId` creates a new run with a new run id and `resumedFromRunId` pointing to the source.
2. The new worker executes the selected JavaScript from the beginning.
3. AppServer compares requested calls with the source journal in order.
4. The longest identical prefix of completed calls returns its recorded results without new Agents.
5. The first added, changed, or incomplete call executes live. Every subsequent call also executes
   live, even if a later fingerprint happens to match.
6. New results are journaled under the new run and may become a later replay source.

Replayed calls contribute their retained token usage to the new run's accounting and appear in the
public projection with status `replayed`. Their source child thread remains navigable when it still
exists. A replayed call does not create another child thread or duplicate provider usage.

Replay does not restore a JavaScript heap, stack, closure, or Promise. It reconstructs control flow by
re-executing deterministic code and replaying completed orchestration results.

A source is eligible only while the same AppServer instance remains alive and the source status is
`paused`, `stopped`, `failed`, or `succeeded`. Active runs from a previous AppServer instance are
marked `interrupted` during startup and are not resumable. Script edits, argument changes, and option
changes are allowed; they naturally establish the first replay mismatch.

---

## 8. Lifecycle, Persistence, and Notification

### 8.1 Service Contract

The internal `IDynamicWorkflowService` covers:

- start and resume;
- pause and stop;
- current state and progress reads;
- worker and child cancellation;
- AppServer shutdown interruption;
- terminal notification deduplication;
- cleanup when the owning parent thread is deleted.

The Dynamic Workflows module also contributes these typed AppServer methods:

| Method | Purpose |
|--------|---------|
| `workflow/run/list` | Page through runs owned by one parent thread. |
| `workflow/run/read` | Read one authoritative render-ready run projection. |
| `workflow/run/pause` | Pause a running run and wait for the state to persist. |
| `workflow/run/stop` | Stop a running or paused run and wait for the state to persist. |
| `workflow/run/resume` | Create a new run by replaying an eligible source. |

The module contributes `workflow/run/updated` as a server-to-client invalidation notification and
advertises version 1 through `capabilities.extensions.dynamicWorkflows`. Exact Wire DTOs, pagination,
notification delivery, and error envelopes are defined by the AppServer Protocol specification.

There is no public Workflow start or definition CRUD method. New script execution still begins only
through the model-visible `Workflow` tool and its approval contract. Protocol clients cannot bypass
source validation, approval, or parent-Turn handoff.

### 8.2 Status

Runs use `running`, `paused`, `stopped`, `succeeded`, `failed`, or `interrupted`. Pause and stop both
cancel active children and the worker and retain the completed replay prefix. Their product meaning
differs, but both are resumable within the current AppServer lifetime. AppServer shutdown marks active
runs `interrupted` after cancelling owned work.

`cancelled` is an internal cleanup outcome and is not a public resumable status. Public controls use
these rules:

- pause accepts `running`; an already `paused` request is idempotent;
- stop accepts `running` or `paused`; an already `stopped` request is idempotent;
- resume accepts `paused`, `stopped`, `failed`, or `succeeded` only while the source belongs to the
  current AppServer instance;
- invalid transitions fail with `workflow_run_state_conflict` and do not mutate the run;
- a source from another AppServer instance or an `interrupted` source fails with
  `workflow_resume_unavailable`.

Pause and stop responses are returned only after the requested state and journal terminal event are
durably written. Resume returns after the new run has been persisted and launched. It never changes
the source run.

A client-originated resume keeps the source `ParentThreadId`, reuses the source `ParentTurnId` as child
provenance, and journals `initiator: "client"`. A model-tool resume keeps the same parent thread but
uses the current Turn and journals `initiator: "model"`. Both completion paths enqueue their terminal
continuation to the parent thread in the normal way.

### 8.3 Public Run Projection

AppServer builds `WorkflowRunView` from persisted run state, ordered journal events, and Session Core
child threads. The client treats the projection as authoritative and MUST NOT read the run directory.
The projection contains:

- run identity, owner thread, name, description, status, timestamps, resume source, result, and error;
- aggregate Agent counts, token usage, and tool-call count;
- server-derived `canPause`, `canStop`, and `canResume` controls;
- ordered phases with detail, status, and nested Agent operations;
- a separate `unphasedAgents` collection for calls that cannot be assigned safely;
- per-Agent operation id, label, status, child thread, token usage, tool-call count, timestamps, and
  replay marker.

The projection does not expose model names, reasoning effort, script paths, script contents, or raw
arguments. Opaque JSON results and English runtime error fallback text may be present only when the run
has produced them.

Declared metadata phases form the initial graph in declaration order. Runtime-discovered phase names
not present in metadata append in first-observed order. Phase detail is the latest bounded detail from
`phase(name, detail)`. The runtime accepts any JSON detail value while the public string projection uses
the original string, compact canonical JSON for non-string scalars, arrays, and objects, and no value for
JSON `null`. Calls use their journaled effective phase. Old journals that lack an effective phase or early
child-thread event remain readable through best-effort projection and are not rewritten.

Public phase status uses `pending`, `running`, `paused`, `completed`, `failed`, or `stopped`. A phase is
active when it is the latest entered phase or owns a non-terminal Agent. Moving to a later phase
completes an earlier phase once all of its Agent operations are terminal. If the run fails, stops, or
pauses while a phase is active, that phase reflects the corresponding state. A tolerated failed Agent
does not by itself fail a phase after the workflow proceeds successfully.

Public Agent status uses `queued`, `running`, `completed`, `failed`, `stopped`, or `replayed`. A journaled
Agent completion preserves the child terminal outcome: `cancelled` and `stopped` project as `stopped`,
`failed` projects as `failed`, and only a successful completion projects as `completed`. Token and
tool-call metrics are derived from Session Core when a child thread is available and fall back to
journaled terminal usage. Elapsed presentation derives from Agent `startedAt` and `completedAt`; the
server does not emit a ticking duration field.

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

`script.js` is the immutable script snapshot for that run. `state.json` is an atomically replaced
summary used for status reads and startup reconciliation. `journal.jsonl` is append-only and records
protocol operations, replay data, child references, usage, progress, and the terminal event.

`.craft/workflows/runs/` belongs in `.craft/.gitignore`; saved workflow definitions do not. Run data
follows its parent thread's retention. Archiving the thread retains runs; deleting the thread removes
its run directories after active work is cancelled.

### 8.5 Client Invalidation

After an observable run change is persisted, AppServer broadcasts one or more
`workflow/run/updated` invalidation notifications containing the owner `threadId`, `runId`, and an open
reason string such as `created`, `progress`, `control`, or `terminal`. The notification carries no run
snapshot. Clients coalesce duplicate notifications and call `workflow/run/read`; a later read always
wins over older local state.

Initialized trusted clients may opt out of this notification through the standard AppServer
notification opt-out capability. Clients establish initial or reconnect state through
`workflow/run/list` and `workflow/run/read`; no periodic polling is required. A selected-thread client
ignores notifications for other thread ids.

This invalidation channel is independent of the parent continuation below. It updates user interface
state and never starts an Agent Turn.

### 8.6 Parent Continuation

Exactly one terminal notification is enqueued for `succeeded`, `failed`, or `stopped`:

```json
{
  "triggerKind": "workflow",
  "triggerRefId": "run_..."
}
```

The queued turn carries the terminal status, workflow name, result or error summary, and references
needed to inspect persisted details. If the parent thread is idle, Session Core starts the queued turn
automatically. If it is busy, the notification waits in the existing FIFO queue. Journaled delivery
state prevents duplicate continuation after reconnect or reconciliation.

---

## 9. Limits and Cancellation

The root run owns one shared concurrency semaphore. Its default capacity is:

```text
min(16, max(1, logicalProcessors - 2))
```

A run may start at most 1000 Agent calls. Additional calls fail the run without starting a child.
`budget` exposes these hard limits, accumulated token usage, and any explicit token budget as
read-only values. Token accounting includes replayed
results; cached results retain their original usage but do not create new provider usage.

Cancellation flows from the service to queued calls, active child turns, and the worker. Cancellation
does not discard completed journal entries. Output sizes for final results,
structured submissions, logs, stderr, and protocol frames are bounded independently.

---

## 10. Approval and Permissions

Starting a workflow in normal mode uses Session approval keyed by canonical source path and source
hash. Inline scripts use their persisted run script path and content hash. Existing `Once`, `Session`,
and `Always` decisions retain their meanings; the approval store gains a generic workflow scope so an
`Always` decision can persist. A content change produces a new hash and invalidates the prior
authorization.

Ultra and an explicit `autoApprove` host policy skip workflow-start approval. This affects only the
orchestration script. Every child Agent continues to use the normal permission and approval policy for
its model-visible tools. Plugin installation or enablement does not implicitly approve a workflow.

---

## 11. Ultra and Prompt Cache

`Ultra` is a DotCraft-owned reasoning tier with wire value `ultra`. It is persisted in the existing
thread reasoning configuration and does not introduce a new `AgentMode`. Provider request adapters
map it to the selected model's highest supported reasoning effort, currently `ExtraHigh`.

`model/list` advertises Ultra only when the model supports Extra High and the Dynamic Workflow runtime
is available. A workflow child receives the mapped provider effort but does not inherit the parent's
Ultra orchestration behavior.

Desktop presents Ultra through the existing reasoning selector. Ultra does not define a new mascot
effect: the composer maps it to the existing Extra High mascot state so both tiers use identical
animation, color, glow, speed, and combined context/speed effects.

`RuntimeContextBuilder` appends a short reminder to the latest user turn:

- in normal reasoning tiers, use `Workflow` only when the user, a slash command, or an active skill
  explicitly opts in;
- in Ultra, proactively plan one or more workflows for substantive tasks when delegation provides a
  useful independent or staged execution structure.

The reminder never changes base instructions. `Workflow` and `SubmitWorkflowResult` keep stable model
tool descriptions, schemas, identities, and order. Child model/effort overrides use the existing
request cache dimensions. Per-run and per-call values remain in the volatile latest task input.

---

## 12. Desktop Presentation

Desktop mounts Workflow progress in the existing conversation tool-card and Detail Panel systems. It
uses the AppServer projection and shared design primitives rather than introducing an independent run
manager surface.

The Workflow tool card:

- has no Workflow logo and no stop control;
- retains the normal tool-card header with lifecycle text, total elapsed presentation, and disclosure;
- advances running elapsed time locally once per second without issuing projection reads; terminal elapsed time remains fixed;
- shows phase summaries only, including previous, current, and declared future phases;
- presents each phase as status icon, phase title, and current phase detail on one line;
- opens the Workflow Detail tab when the phase title is activated;
- has no row highlight or decorative hover block beyond the shared quiet-action text behavior;
- does not show nested Agent operations, phase fractions, or a separate View button.

A successful Workflow launch is a durable Turn result. Desktop keeps the latest successful launch
card, together with an immediately preceding visible handoff message, outside the collapsed
`Processed` summary. The Workflow card follows the handoff text inside the same assistant message;
Turn completion content follows the card, and the message's standard copy, fork, and time footer
remains last. Failed launch attempts retain normal tool-history behavior and do not become pinned
results.

Workflow tool construction and launch use dedicated lifecycle copy rather than the generic external-tool
labels. After a successful launch in the currently visible parent thread, Desktop opens the corresponding
Workflow Detail tab once for that run. Reconnect, invalidation refresh, terminal continuation, and later
history hydration do not reopen a tab the user has left.

The Workflow Detail tab:

- uses the shared Workflow icon in its Detail Panel tab chrome;
- displays the workflow name and metadata description, without a logo or aggregate progress subtitle;
- shows the complete phase graph with nested Agents and neutral status icons;
- applies the shared running gradient to current phase detail;
- shows each Agent's total tokens and tool-call count, appending elapsed time only after the Agent is
  terminal;
- never shows the Agent model column;
- opens the corresponding child thread when an Agent label with a child thread id is activated;
- hosts Stop as the only Workflow control in this version.

Pause and resume remain public protocol capabilities but are not added to Desktop until their controls
and states have passed a separate design-system review. All Workflow UI strings use the Desktop locale
catalogs. Running, completed, failed, stopped, and long-content fixtures remain mounted in the design
system and point to production sources after implementation.

Queued parent continuations with `triggerKind = "workflow"` render the standard message-origin marker
outside the user bubble. Its label identifies the Workflow, and its `triggerRefId` opens that run in the
Workflow Detail tab. This marker does not introduce a Workflow-specific bubble type.

---

## 13. Verification Requirements

| Area | Required coverage |
|------|-------------------|
| Parser | Literal metadata; dynamic metadata rejection; `import`, `eval`, time, and random rejection; top-level `await`; non-serializable return rejection. |
| Worker | Promise/.NET Task bridge; infinite-loop cancellation; memory and recursion limits; worker exit; malformed or out-of-sequence stdio messages. |
| Script API | Stable `parallel` ordering; cross-item `pipeline` concurrency; `null` propagation; phase and log events; structured-output validation and retry. |
| Replay | Full prefix hit; first incomplete call; prompt, schema, model, script, and argument changes; live execution after the first mismatch. |
| Lifecycle | Immediate background result; busy-parent queueing; exactly-once terminal notification; pause, stop, resume; shutdown interruption and post-restart resume rejection. |
| Projection | Declared, discovered, and unphased grouping; old-journal fallback; running and terminal child metrics; failed and replayed Agent operations. |
| AppServer | Typed list/read/pause/stop/resume dispatch; pagination; capability advertisement; ownership hiding; stable errors; invalidation and opt-out behavior. |
| Limits | Shared concurrency queue; 1000-Agent cap; explicit token budget. |
| Permissions | `Once`, `Session`, and `Always`; source-hash invalidation; Ultra and `autoApprove`; independent child tool approval. |
| Discovery | Workspace override; personal definitions; plugin namespace; same-scope duplicate; canonical path escape and symlink rejection. |
| Worktree | Clean automatic cleanup; preservation on modifications, untracked files, new commits, and cancellation. |
| Ultra | Wire round trip; provider `ExtraHigh` mapping; thread persistence; no proactive-orchestration inheritance by a child. |
| Prompt cache | Ultra changes only the volatile tail; stable base/tool fingerprint; stable workflow-child prefix; requested/effective override dimensions. |
| Desktop | Tool-card recognition; phase-to-detail navigation; child-thread navigation; Stop lifecycle; reconnect/read refresh; failed, stopped, and long-content presentation. |

Acceptance requires every row to have automated coverage at the narrowest appropriate parser, service,
process-integration, Session Core integration, or protocol-contract layer.
