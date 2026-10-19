# DotCraft SubAgent Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [Prompt Composition](../architecture/prompt-composition.md), [Prompt Cache](../architecture/prompt-cache.md), [Tool Architecture](../architecture/tools-architecture.md), [Background Tasks](../architecture/background-tasks.md) |
| **Related Specs** | [Agent Profiles](agent-profiles.md) |

Purpose: define the shared SubAgent child-thread, runtime, context, model-resolution, policy,
communication, and lifecycle contracts owned by Session Core.

## 1. Goals and boundaries

SubAgents delegate bounded tasks from one server-managed thread to child threads while preserving
DotCraft's session, policy, persistence, and ownership rules. This specification is authoritative for
the core behavior shared by model tools, AppServer controls, and module-managed callers.

It covers runtime selection; child identity, ancestry, depth, and residency; context creation;
role, profile, model, permission, and tool resolution; task dispatch, communication, continuation,
cancellation, and closure; persistence, recovery, progress, and parent ownership; and Desktop
identity resolution and activity state.

Feature-specific orchestration, module-owned task graphs, client layout, external runtime command
lines, and wire DTOs belong to their owning specifications.

## 2. Core concepts

| Concept | Contract |
|---------|----------|
| **Child thread** | A normal session thread whose source is `subagent` and whose lifecycle is owned by its parent thread. |
| **Agent path** | An immutable address rooted at `/root`, formed from validated `taskName` path segments. |
| **Role** | A DotCraft policy selector for instructions, mode, model default, tools, shell access, and recursive Agent control. |
| **Profile** | A runtime selector and runtime-specific launch configuration. Omission selects the protected native profile. |
| **Runtime** | The execution engine selected by the profile: native or external CLI. |
| **Spawn edge** | Durable parent-child metadata containing ancestry, path, role, profile, runtime, capabilities, and open/closed state. |
| **Fork mode** | The `forkTurns` selection that determines which parent history and runtime bindings enter the child. |
| **Default preference** | The complete provider-scoped model preference selected before role and invocation-specific overrides. |
| **Invocation override** | A caller-authorized model or reasoning change that applies only to one fresh or bounded native child. |

`taskName` is the stable path identity and contains only lowercase ASCII letters, digits, or
underscores. `root`, `.` and `..` are reserved. Absolute addresses begin with `/root`; relative
addresses append to the current path. Siblings under one parent cannot reuse a `taskName`, including
after closure. `agentNickname` and the thread display name are presentation metadata and do not
change routing.

`agentRole` and `profile` are independent: a role controls DotCraft behavior; a profile selects the
runtime.

### Desktop identity and activity

Tools, grouped activity, and previews resolve the child from the containing Turn's `threadId`. Spawn
resolves only direct children. An explicit child thread ID takes precedence over an agent path.
Control tools may resolve across parents only when both ancestries prove the same `rootThreadId`.
Activity entries open the active conversation's Subagents tab on the resolved child's read-only
transcript; unresolved entries open the tab's list.

Desktop discovers children through `subagent/children/list` including closed children and threads,
and refreshes on notifications. Status precedence: resolved runtime and explicit terminal state win
over stale running notifications; closed edges are finished; failure displays `interrupted`. After
successful discovery, a created historical agent with no active entity or runtime is finished. A
failed initial discovery with no reliable prior state shows only the name and navigation entry.
Groups prioritize failure, then running, and show finished only when every entry is confirmed
finished. Closed records never count as running.

## 3. Spawn lifecycle

Session Core creates a SubAgent in this order:

1. Validate the task, path, `forkTurns`, working directory, requested role, and requested profile.
2. Resolve the role, profile, runtime, advertised capabilities, parent/root ancestry, and child depth.
3. Enforce the configured depth and open-child residency limits.
4. Resolve the child thread configuration (sections 6 and 7).
5. Create the provisional child thread and run its preparation callback.
6. Materialize and persist the selected parent context and inheritable runtime bindings.
7. Persist the spawn edge, emit the SubAgent start lifecycle hook, and submit the initial task.
   Confirm startup only after durable input admission.
8. When a child Turn finishes, persist terminal state, update progress, and deliver an `agent` task
   notification to the direct parent (see [Background Tasks](../architecture/background-tasks.md)).

The child thread exists before its first Turn starts; clients and observers may navigate to or
correlate it as soon as creation is persisted.

### Startup admission and compensation

Spawn returns success only after context and spawn-edge persistence and acceptance of the initial
input. Native admission is the persisted `TurnStarted` boundary; external admission is persistence
of the initial synthetic Turn. A child-started observer runs after admission; its failures are
diagnostic and do not fail an admitted spawn. Caller cancellation still propagates.

An exception or cancellation before admission rolls back the provisional child: cleanup settles
startup work, stops runtime resources, closes the edge, and deletes child artifacts through a
parent-ownership-checked internal operation with its own bounded budget, preserving the original
failure. Direct public deletion of child threads remains prohibited. Compensation covers live
startup failure, not process-crash recovery. Module integrations release preparation resources
before child deletion.

Once admitted, a child's failure or cancellation follows normal Turn lifecycle and keeps its
history. Its work is bound to the parent Turn, not to the tool call that started it, so a child
started from a code mode program keeps running after the program ends.

`SubAgent.MaxDepth` defaults to `1`. Recursive spawning requires both a higher limit and a role that
permits Agent control.

`SubAgent.MaxConcurrentSubAgents` bounds open resident children within one root tree. Before
spawning, Session Core may close the oldest idle child (including one stopped with `TaskStop`) to
make room. It never evicts a running child; spawning fails when every resident child is active.

## 4. Runtime paths

### 4.1 Native runtime

A native child uses the normal Session Core Turn, Item, persistence, model, tool, approval, and resume
pipeline. It shares the root thread's cache identity while retaining its own configuration, history,
role context item, path, and lifecycle. Native children accept new tasks when idle and current-Turn
steering while running.

### 4.2 External runtime

An external child is a Session Core child thread whose profile delegates execution to a short-lived
external process. The profile declares whether the runtime supports later input, session resume,
streaming, and closure. Session Core persists synthetic Turns and runtime session metadata when the
adapter provides them.

External runtimes do not consume native model preferences or overrides; their model and permission
semantics belong to the runtime and profile. Running external children do not support steering;
callers queue a later task.

## 5. Context and fork modes

| `forkTurns` | Context behavior | Native model behavior | Runtime binding behavior |
|-------------|------------------|-----------------------|--------------------------|
| `all` or omitted | Materializes the parent's complete stable model context. | Inherits the parent's complete preference. Overrides are disabled. | Snapshots eligible direct-parent tool and client bindings. |
| `none` | Starts without parent conversation Turns. | Resolves a fresh-child preference and permits authorized overrides. | Does not inherit parent runtime bindings. |
| Positive integer | Copies the selected trailing stable Turns and active user input. | Resolves a bounded-child preference and permits authorized overrides. | Does not inherit parent runtime bindings. |

A native full-history fork materializes the parent's effective model context before the first child
sampling: it copies ordered system, developer, and user material, drops assistant, reasoning, and tool
traffic, then appends the role guidance and initial task. Stable reference-context pages (including the
parent's exact `AGENTS.md` snapshot for an environment-compatible child) and eligible bindings are
snapshotted at creation; later parent changes do not propagate. The child's base instructions and
model-visible tool schema match the parent's; role restrictions narrow invocation policy without
changing that prefix, though trust-tier gating may withhold capabilities never reachable from
delegated threads.

Fresh and bounded children rebuild their model context from the child configuration, establish an
independent prefix, and do not inherit client-owned dynamic tools, browser transport, or other
full-history-only bindings. External runtimes receive the selected parent Turns as rendered prompt
context.

The model-visible `SpawnAgent` declaration carries a bounded provider model catalog snapshot, created
before a thread's first provider request, persisted with its thread configuration, and reused across
Turns, rebuilds, and cold resumes. A full-history child copies the parent's snapshot; fresh and bounded
children create their own. Entries are case-insensitively deduplicated in provider order and capped at
five. If catalog loading fails, `SpawnAgent` stays available with an empty override set: omitted
override fields use the configured model, and explicit overrides fail.

## 6. Native model resolution

Model resolution operates on a complete provider-scoped preference: model, reasoning configuration,
and inference speed. The parent provider remains authoritative; spawning never switches providers.

### 6.1 Full-history children

For `forkTurns=all`, the child inherits the parent's complete captured preference and ignores
`SubAgent.ProviderPreferences`, the role model, and invocation overrides, keeping the materialized
context and the child model configuration aligned.

### 6.2 Fresh and bounded children

For `forkTurns=none` or a positive integer, Session Core applies this order:

1. Start from the parent's complete captured preference.
2. Apply the caller-resolved default preference. The standard `SpawnAgent` path uses
   `SubAgent.ProviderPreferences[parentProviderId]`; a missing entry inherits the parent. A
   module-managed caller may retain the parent preference when its feature contract requires.
3. Apply `role.Model`, when configured.
4. Apply an authorized invocation override: an explicit model takes precedence over `role.Model`; an
   explicit effort takes precedence over inherited effort.
5. Normalize the complete preference against the final model's catalog capabilities.

An invocation model changes only the model; an invocation effort enables reasoning at that effort.
Neither resets other selections; normalization repairs only selections the final model does not
support.

`SpawnAgent` accepts optional `model` and `reasoningEffort` arguments, valid only for fresh and bounded
native children and required to match the calling thread's catalog snapshot. Full-history children
reject either argument. Unknown models and provider rejection follow normal Turn failure.

## 7. Role, tools, and approval policy

The built-in roles are `default`, `worker`, and `explorer`. Workspace configuration may replace a
built-in role by name or add roles. An unknown role rejects the spawn.

A role may define:

- instructions and whether they replace normal SubAgent guidance;
- thread mode;
- a model default for fresh and bounded native children;
- tool allow and deny lists;
- shell access of `none`, `readOnly`, or `full` (default), where `readOnly` admits exactly the
  commands the read-only classifier in [Shell Command Safety](../architecture/shell-command-safety.md)
  accepts and rejects stdin writes while still allowing empty `WriteStdin` polls;
- Agent-control access and allowed Agent-control tools.

Native child tools use the normal session tool-construction path: the parent's allow/deny boundary,
then role restrictions. Roles only narrow authority and cannot bypass the parent's approval service,
approval context, or workspace policy. Tool reachability and shell access are independent gates. The
default role denies Agent control; explorer permits read-only exploration; worker permits writes and
Agent control within the depth limit. `TaskStop` on an Agent path requires Agent-control authority
(an allow-list grants it by naming `TaskStop`); stopping the child's own shell tasks does not.

Role instructions are a thread context item after inherited history and before the initial task.
Changing or clearing them establishes an explicit history replacement boundary before sampling. Task
data belongs in the task input, not base instructions.

External runtimes receive role instructions through their runtime prompt; their profile translates
DotCraft approval intent into launch behavior.

## 8. Communication and continuation

SubAgent communication uses durable envelopes scoped to one root Agent tree:

| Operation | Behavior |
|-----------|----------|
| `SendMessage` | To a descendant: delivers a `NEW_TASK` that starts the child when idle or stopped, steers a running native child, or runs after an external child's active Turn. To any other open path: writes a passive `MESSAGE` that never starts a Turn. |
| `ListAgents` | Returns `/root` and open path-addressable children with the latest Turn's lifecycle state, or `idle` when no Turn exists. |
| `TaskStop` | Given a descendant's Agent path, cancels the child's active Turn and leaves it open. Returns the settled status and last agent message. |

Continuing a descendant reuses the same child thread with its stored role, profile, runtime, model
configuration, workspace, and path, delivering pending mailbox messages with the task. A running
native child receives the message at its next sampling boundary, or as its next Turn if the active
Turn ends first. `SendMessage` fails for a closed edge or an external child whose edge has
`supportsFollowupTask = false`.

There is no persisted pause state. Cancelling, stopping, or completing a Turn leaves an open child
available for later tasks. Clients close a child through `subagent/close`, which cancels owned work,
closes the edge, and archives the child subtree; the model has no close tool.

Envelopes carry `id`, `rootThreadId`, author and recipient paths, `messageType` (`MESSAGE` or
`NEW_TASK`), payload, parent-Turn provenance, and creation time. They render as user-role
materialized context, not as a public Item or user-authored bubble.

Delivery is serialized per root and recipient. Pending messages enter sampling or tool boundaries
while the Turn accepts mailbox input; after the final answer, late passive messages wait for the next
Turn unless explicit guidance steering reopens admission. Internal goal steering is not mailbox
activity. Passive messages are marked delivered only after their materialized input is persisted with
a submitted, queued, or steered task. Delivery status is not exactly-once across process failure.

Every child Turn's result reaches its direct parent as an `agent` task notification whose `result` is
the child's final agent message, waking an idle parent (see
[Background Tasks](../architecture/background-tasks.md)). Exceptions:

- a child spawned with silent completion (persisted in its SubAgent source) never notifies, because
  its host consumes the result itself;
- a Turn cancelled by `TaskStop` that ends within the stop wait does not notify, because the stop
  result carries the outcome;
- a Turn cancelled because the parent Turn was cancelled notifies the parent as a user stop without
  starting a parent Turn.

### 8.1 Shared discussion board

Each persistent native root tree shares one board, scoped by state workspace and root thread ID. The
root and native descendants share channels and topics regardless of `forkTurns` or working directory.
Identity comes from the active Turn, never tool arguments. Temporary and internal threads and external
runtimes cannot use the board. Cross-root access is prohibited.

The default native tools are `BoardCreateChannel`, `BoardGetChannels`, `BoardListThreads`,
`BoardSearchPosts`, `BoardReadThread`, `BoardReadPost`, `BoardSubscribe`, `BoardUnsubscribe` and
`BoardPost`. Their schemas are stable across Plan and Agent modes. Board access is independent of
Agent-control authority; Plan and built-in explorer permit collaboration writes, while parent, profile,
and custom role tool restrictions still apply. Direct calls and CodeMode obey the same contract.

Posts are append-only and contain a stable message ID, channel, author path, creation time, and topic
root ID; the first post identifies its topic. A post requires exactly one destination: an existing
channel, a new channel, or a topic root ID. Writes return metadata. Retrying the same runtime
invocation returns the original post without re-sending notices; reusing that identity with different
arguments fails; distinct nested CodeMode calls remain distinct.

Creating a channel subscribes its creator; posting subscribes the author to the topic unless opted
out. Channel subscriptions cover new topics; topic subscriptions cover replies. Subscribe and
unsubscribe can target another open native Agent in the tree. Explicit recipients receive a one-time
notice without changing subscriptions. Recipients are deduplicated and exclude the author. Shared
content is peer data, not system instructions.

List and search tools return previews and opaque cursors; search is case-insensitive substring.
Concurrent writes may shift pages; callers restart the query. Text offsets count Unicode scalar
values. Tool contract limits:

| Value | Limit |
| --- | --- |
| Page size | Default 20, maximum 50 posts or channels. |
| Post preview | Default 1,000, maximum 20,000 characters. |
| Full post slice | Default and maximum 20,000 characters, with a continuation offset. |
| Response | Valid JSON within 8,000 UTF-8 bytes; reads reduce previews/pages and writes reject oversized metadata before commit. |
| Post body | 1–65,536 UTF-8 bytes. |
| Channel name | 1–128 UTF-8 bytes, without edge whitespace or control characters. |
| Channel description | At most 1,024 UTF-8 bytes. |
| Explicit recipients | At most 256 per post. |

A post becomes durable before best-effort notification. A notice carries metadata and at most 150
characters of preview and reaches only the recipient's current active Turn while it accepts
collaboration input. Delivery never starts idle Agents, loads execution resources, or retains notices
for future Turns. Pending notices are bounded and may wake `Sleep`. Final-answer admission closure,
cancellation, and Turn termination discard undelivered notices; callers read the board to catch up.
Notice failure never fails a committed post.

Consumed notices follow the internal communication and model-history persistence contract, including
recovery after an interrupted commit. Board tool calls use generic activity records with the
`core.board` presentation and `options.operation` set to `createChannel`, `getChannels`,
`listThreads`, `searchPosts`, `readThread`, `readPost`, `subscribe`, `unsubscribe`, or `post`. Storage
errors return a stable code and English fallback without resetting board data or blocking unrelated
work.

Channels, posts, subscriptions, and opt-outs are durable shared state independent of transcripts.
Rollback, compaction, projection rebuild, archive, unload, child closure, and cold resume preserve
them. An ordinary conversation fork starts with an empty board. Permanent root deletion removes board
content and prevents delayed writes from recreating it; child deletion preserves it. Single-thread
recovery packages do not include the board.

## 9. Persistence, recovery, and ownership

The child thread, its SubAgent source, spawn edge, mailbox entries, and model configuration are
durable. Cold recovery preserves open identity, ancestry, path, role, profile, runtime metadata,
capabilities, and pending communications. Recovery does not reopen a closed child.

Top-level thread lists hide SubAgent children by default; clients read the parent's spawn edges for
the child graph.

The parent owns the complete descendant subtree:

- archiving a parent archives its descendants;
- restoring a parent restores descendants whose spawn edges remain open;
- permanent deletion removes the descendant tree and owned artifacts;
- direct archive or deletion of a child is invalid; callers close it through SubAgent control.

Provider, role, and SubAgent default changes may invalidate cached child agents but never replace the
model of a running request. Existing children keep their captured configuration.

## 10. Progress and observability

Session Core emits SubAgent progress snapshots while child work is active, identifying the child,
current tool when available, cumulative usage, and completion state. Progress is observational; the
child Thread and Turn remain authoritative.

Native usage aggregates into the owning parent Turn per the Session Core usage contract. External
usage is reported only when the adapter supplies trustworthy metadata. Lifecycle hooks observe
SubAgent start and stop without replacing persistence or terminal communication.
