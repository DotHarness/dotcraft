# DotCraft SubAgent Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-08-12 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [Prompt Composition](../architecture/prompt-composition.md), [Prompt Cache](../architecture/prompt-cache.md), [Model Options](model-options.md), [Tool Architecture](../architecture/tools-architecture.md) |
| **Related Specs** | [Agent Profiles](agent-profiles.md), [External CLI SubAgent](external-cli-subagent.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: define the shared SubAgent child-thread, runtime, context, model-resolution, policy,
communication, and lifecycle contracts owned by Session Core.

---

## 1. Goals and boundaries

SubAgents delegate bounded tasks from one server-managed thread to child threads while preserving
DotCraft's session, policy, persistence, and ownership rules. This specification is authoritative for
the core behavior shared by model tools, AppServer controls, and module-managed callers.

This specification covers:

- native and external SubAgent runtime selection;
- child identity, ancestry, depth, and residency;
- full-history, fresh, and bounded context creation;
- role, profile, model, permission, and tool resolution;
- task dispatch, communication, continuation, cancellation, and closure;
- persistence, recovery, progress, and parent ownership.

Feature-specific orchestration, module-owned task graphs, client presentation, external runtime command
lines, and public wire DTOs remain in their owning specifications.

---

## 2. Core concepts

| Concept | Contract |
|---------|----------|
| **Child thread** | A normal `SessionThread` whose source is `subagent` and whose lifecycle is owned by its parent thread. |
| **Agent path** | An immutable address rooted at `/root`, formed from validated `taskName` path segments. |
| **Role** | A DotCraft policy selector for instructions, mode, model default, tools, shell access, and recursive Agent control. |
| **Profile** | A runtime selector and runtime-specific launch configuration. Omission selects the protected native profile. |
| **Runtime** | The execution engine selected by the profile. Current runtime families are native and external CLI. |
| **Spawn edge** | Durable parent-child metadata containing ancestry, path, role, profile, runtime, capabilities, and open/closed state. |
| **Fork mode** | The `forkTurns` selection that determines which parent history and runtime bindings enter the child. |
| **Default preference** | The complete provider-scoped model preference selected before role and invocation-specific overrides. |
| **Invocation override** | A caller-authorized model or reasoning change that applies only to one fresh or bounded native child. |

`taskName` is the stable path identity and contains only lowercase ASCII letters, digits, or
underscores. `agentNickname` and `Thread.DisplayName` are presentation metadata and do not change
routing. Siblings under one parent cannot reuse a child `taskName` or path, including after closure.

`agentRole` and `profile` are independent. A role controls DotCraft behavior; a profile selects the
runtime. A role name is not a profile name, and neither is display metadata.

---

## 3. Spawn lifecycle

Session Core creates a SubAgent in this order:

1. Validate the task, path, `forkTurns`, working directory, requested role, and requested profile.
2. Resolve the role, profile, runtime, advertised capabilities, parent/root ancestry, and child depth.
3. Enforce the configured depth and open-child residency limits.
4. Resolve the child thread configuration, including the model rules in section 6 and the role policy
   rules in section 7.
5. Create the child `SessionThread` and durable spawn edge before starting model execution.
6. Materialize the selected parent context and inheritable runtime bindings.
7. Emit the SubAgent start lifecycle hook and submit the initial task to the child runtime.
8. Persist terminal Turn state, update progress, and deliver a `FINAL_ANSWER` communication to the
   direct parent when the child Turn finishes.

The child thread exists before its first Turn starts. Clients and module-owned observers may navigate
or correlate the child as soon as creation is persisted.

`SubAgent.MaxDepth` defaults to `1`. The first child of a root thread has depth `1`; recursive spawning
requires both a higher configured limit and a role that permits Agent control.

`SubAgent.MaxConcurrentSubAgents` bounds open resident children within one root tree. Before spawning,
Session Core may close the oldest idle child to make room. It never evicts a running child; spawning
fails when every resident child is active.

---

## 4. Runtime paths

### 4.1 Native runtime

A native child uses the normal Session Core Turn, Item, persistence, model, tool, approval, and resume
pipeline. It shares the root thread's cache identity while retaining its own thread configuration,
history, role context item, path, and lifecycle.

Native children support new tasks after an idle terminal Turn. Running native children also support
current-Turn steering when the active Turn can still admit guidance.

### 4.2 External runtime

An external child remains a Session Core child thread, but its profile delegates execution to a
short-lived external process. The profile declares whether the runtime supports later input, session
resume, streaming, and closure. Session Core persists synthetic Turns and runtime session metadata when
the selected adapter provides them.

External runtimes do not consume native model preferences or invocation-specific native model
overrides. Their model and permission semantics belong to the selected external runtime and profile.
Running external children do not support current-Turn steering; callers must queue a later task.

---

## 5. Context and fork modes

| `forkTurns` | Context behavior | Native model behavior | Runtime binding behavior |
|-------------|------------------|-----------------------|--------------------------|
| `all` or omitted | Materializes the parent's complete stable model context. | Inherits the parent's complete provider and model preference. Overrides are disabled. | Snapshots eligible direct-parent tool and client bindings. |
| `none` | Starts without parent conversation Turns. | Resolves a fresh-child preference and permits authorized overrides. | Does not inherit parent runtime bindings. |
| Positive integer | Copies the selected trailing stable Turns and active user input. | Resolves a bounded-child preference and permits authorized overrides. | Does not inherit parent runtime bindings. |

A native full-history fork materializes the parent's effective model context before the first child
sampling. It copies ordered system, developer, and user material, drops assistant, reasoning, and tool
traffic, then appends the child role guidance and initial task. Stable reference-context pages and
eligible direct-parent bindings are snapshotted at creation. Later parent changes do not propagate.

The model-visible `SpawnAgent` declaration carries a bounded provider model catalog snapshot. The
snapshot is created before a thread's first provider request, persisted with its captured thread
configuration, and reused across Turns, agent rebuilds, and cold resumes. Catalog cache expiry does not
rewrite an existing thread snapshot. A full-history native child copies the parent's snapshot exactly.
Fresh and bounded native children discard the copied value and create an independent snapshot before
their first provider request. Entries are case-insensitively deduplicated in provider order and capped
at five. If catalog loading fails, `SpawnAgent` remains available with an empty override set; omitted
override fields still use the configured model, while explicit overrides fail locally.

Fresh and bounded children rebuild their model context from the child configuration. They do not
inherit the parent's client-owned dynamic tools, browser transport, or other full-history-only runtime
bindings.

External runtimes receive the selected parent Turns as rendered prompt context rather than native
provider history.

---

## 6. Native model resolution

Model resolution operates on a complete provider-scoped preference: model, reasoning configuration,
inference speed, and context-window mode. The parent provider remains authoritative; SubAgent spawning
does not switch providers.

### 6.1 Full-history children

For `forkTurns=all`, the child inherits the parent's complete captured preference. Session Core ignores:

- `SubAgent.ProviderPreferences`;
- a role model;
- an invocation-specific model or effort override.

This invariant keeps the materialized provider context and the child model configuration aligned.

### 6.2 Fresh and bounded children

For `forkTurns=none` or a positive integer, Session Core applies this order:

1. Start from the parent's complete captured preference.
2. Apply the caller-resolved native SubAgent default preference when present. The standard
   `SpawnAgent` path resolves `SubAgent.ProviderPreferences[parentProviderId]`; a missing entry inherits
   the parent preference. A module-managed caller may intentionally retain the parent preference as
   its feature contract requires.
3. Apply `role.Model`, when configured, as the role's model default.
4. Apply an authorized invocation-specific override. An explicit invocation model takes precedence
   over `role.Model`; an explicit effort takes precedence over inherited reasoning effort.
5. Normalize the complete preference against the final model's catalog capabilities.

An invocation model changes only the model before normalization. It does not reset reasoning, speed,
or context-window selections. An invocation effort enables reasoning at that effort while preserving
reasoning output, model, speed, and context-window mode. Normalization repairs only selections that the
final model does not support.

The model-visible `SpawnAgent` tool accepts optional `model` and `reasoningEffort` arguments. They are
valid only for fresh and bounded native children and MUST match the calling thread's persisted catalog
snapshot. Full-history native children reject either argument rather than silently ignoring it.

Unknown models and provider-side option rejection follow the existing Model Options and Turn failure
contracts. Core callers must not invent provider aliases or bypass the parent provider boundary.

---

## 7. Role, tools, and approval policy

The built-in roles are `default`, `worker`, and `explorer`. Workspace configuration may replace a
built-in role by name or add another role. Role resolution happens before the child thread is created;
an unknown role rejects the spawn.

A role may define:

- instructions and whether they replace normal SubAgent guidance;
- thread mode;
- a model default for fresh and bounded native children;
- tool allow and deny lists;
- shell access of `none`, `readOnly`, or `full`;
- Agent-control access and allowed Agent-control tools.

Native child tools use the normal session tool-construction path. The child inherits the parent's tool
allow/deny boundary, then applies role restrictions. Role restrictions narrow authority and cannot
bypass the parent's approval service, approval context, sandbox, or workspace policy.

Role instructions are stored as a thread context item after inherited history and before the initial
task. Invocation-specific task data belongs in the task input rather than stable base instructions.

External runtimes receive role instructions through their runtime prompt. Their profile translates
DotCraft approval intent into runtime launch behavior as defined by the External CLI SubAgent spec.

---

## 8. Communication and continuation

SubAgent communication uses durable envelopes scoped to one root Agent tree:

| Operation | Behavior |
|-----------|----------|
| `SendMessage` | Writes a passive `MESSAGE` to another open Agent path without starting a Turn. |
| `FollowupTask` | Delivers a `NEW_TASK`; starts an idle child, queues behind an active Turn, or steers a running native Turn when requested and still admissible. |
| `WaitAgent` | Waits for mailbox, graph, or steering activity and returns status/timeout state rather than duplicating the child result. |
| `ListAgents` | Returns the caller-visible SubAgent tree and current lifecycle state. |
| `CloseAgent` | Cancels active owned work, closes the spawn edge, and archives the child subtree. |

When an open child is idle, `FollowupTask` resumes the same child thread with its stored role, profile,
runtime, model configuration, workspace, and path. It does not create a replacement child. A running
child uses FIFO queueing by default. Steering is supported only for a running native child and fails if
the active Turn changes before guidance is admitted.

There is no distinct persisted SubAgent pause state. Cancelling or completing an active Turn leaves an
open child available for a later task; closing the Agent ends that reusable relationship.

The terminal `FINAL_ANSWER` mailbox entry records the direct child result and Turn provenance. Passive
messages are marked delivered only after their materialized input is persisted with a submitted,
queued, or steered task.

---

## 9. Persistence, recovery, and ownership

The child `SessionThread`, `ThreadSource.SubAgent`, spawn edge, mailbox entries, and model configuration
are durable. Cold recovery preserves open identity, ancestry, path, role, profile, runtime metadata,
capabilities, and pending communications. Recovery does not reopen an explicitly closed child.

Top-level thread lists hide SubAgent children by default. Clients that need the active child graph
should read the parent's open spawn edges instead of treating children as independent conversations.

The parent owns the complete descendant subtree:

- archiving a parent archives its descendants;
- restoring a parent restores descendants whose spawn edges remain open;
- permanent deletion removes the complete descendant tree and owned artifacts;
- direct archive or deletion of a child is invalid; callers close it through SubAgent control.

Provider, role, and SubAgent default changes may invalidate cached child agents, but never replace the
model of a running provider request. Existing child threads retain their captured configuration until
an explicit supported update or a new child is created.

---

## 10. Progress and observability

Session Core emits SubAgent progress snapshots while child work is active. A snapshot identifies the
child, current tool when available, cumulative usage, and completion state. Progress is observational;
the child Thread and Turn remain the durable authority.

Native usage is aggregated into the owning parent Turn according to the Session Core usage contract.
External usage is reported only when the runtime adapter supplies trustworthy metadata.

Lifecycle hooks observe SubAgent start and stop without replacing normal thread persistence or terminal
communication.

---

## 11. Acceptance criteria

- Every SubAgent is a durable, path-addressable child thread with a durable parent edge.
- Native and external runtimes preserve their distinct context, model, steering, and permission rules.
- Full-history native children always inherit the parent's complete model preference.
- Fresh and bounded native children apply default, role, invocation, and normalization precedence in
  the documented order.
- Partial invocation overrides preserve unrelated model-option fields.
- Roles narrow tool and shell authority without bypassing parent approval policy.
- Open children accept later tasks; closed children cannot be resumed through the same edge.
- Parent lifecycle operations consistently own the complete descendant subtree.
- Progress, mailbox delivery, and terminal results remain observable without becoming alternate
  persistence authorities.
