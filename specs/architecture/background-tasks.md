# DotCraft Background Tasks Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.10 |
| **Status** | Living |
| **Date** | 2026-10-17 |
| **Parent Specs** | [Session Core](session-core.md), [Tool Architecture](tools-architecture.md) |
| **Related Specs** | [SubAgent Core](../features/subagents.md), [Dynamic Workflows](../features/dynamic-workflows.md), [Prompt Cache](prompt-cache.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define one model-facing task model for work that keeps running after the tool call that
started it: background terminals, SubAgents, and Dynamic Workflows. The model stops any of them with
one tool and learns that any of them ended through one notification envelope, while each kind keeps
its own execution runtime, persistence, and client protocol.

## 1. Goals

- One stop tool for every kind of background task.
- One notification envelope and one delivery path for every task ending, so an idle Thread is woken
  the same way whichever kind of task finished.
- A small model-facing surface: one stop tool, one message tool for continuing children, and
  notifications instead of waiting or polling.

## 2. Non-goals

- One execution runtime. Terminals run in the background terminal runtime, SubAgents in Session Core
  SubAgent control, Workflows in the Dynamic Workflow service.
- A shared task id format. Each kind uses its own id.
- A unified AppServer `task/*` protocol. `terminal/*`, `subagent/*`, and `workflow/run/*` are the
  client contracts.
- Line-level output monitoring (a Monitor tool) and notifications for Remote Tool Host terminals.
- Changes to the shared discussion board or to agent-to-agent `MESSAGE` delivery.

## 3. Task model

A background task is one of:

| Kind | Started by | Task id | Owner |
|------|-----------|---------|-------|
| `shell` | `Exec` handed over after its yield time, or `runInBackground` | terminal session id, `term_<12 hex>` | the Thread that ran `Exec` |
| `agent` | `SpawnAgent`, or `SendMessage` that starts an idle child | Agent path, `/root/<taskName>[/...]` | the parent Agent path in the same root tree |
| `workflow` | `Workflow` | run id, `run_<timestamp>_<suffix>` | the parent Thread of the run |

Session Core keeps a task registry over every kind. The registry holds no task state of its own: each
runtime registers its kind once, and the registry reads a task record from that runtime whenever it
resolves an id, and asks the runtime to stop it. A task record carries:

- `taskId`, `kind`, owner Thread, the Turn and tool call that started it (`callId`), and a short label
  (the shortened command, the Agent nickname or path, or the Workflow name);
- `status`: `running`, `completed`, `failed`, or `stopped`, plus the kind-specific status the runtime
  reports (for example `timedOut`, `lost`, `interrupted`);
- the output location the model may read (terminal log, Workflow run directory) when the kind has one;
- a `notified` latch and the stop origin (`model`, `user`, or `shutdown`).

Because records are read from each runtime's own durable state, they follow its recovery: terminals
left `running` after a host restart read as `lost`, Workflow runs left `running` as `interrupted`, and
open SubAgent edges with their latest Turn state.

`TaskStop` and notifications resolve a task id by its shape: `term_` ids are shells, `run_` ids are
Workflows, and paths, absolute or relative to the caller's Agent path as `SendMessage` targets are,
are Agents. An id that matches no task the caller owns is an error.

## 4. Model-facing tools

The background-work tool surface is `Exec`, `WriteStdin`, `SpawnAgent`, `SendMessage`, `ListAgents`,
`Workflow`, and `TaskStop`. Every
schema stays stable across Plan and Agent modes and for the lifetime of a Thread, per
[Prompt Cache](prompt-cache.md); mode and role restrictions are enforced by policy, not by removing
tools.

### 4.1 `TaskStop`

```json
{ "taskId": "term_3f9a1c2b7d10" }
```

- A `shell` task: stops the process tree, including a terminal on the Thread's Remote Tool Host
  route. Interactive stdin is not required.
- An `agent` task: cancels the child's active Turn and leaves the child open, so a later
  `SendMessage` continues it with its stored configuration. Its slot stays occupied until the
  existing eviction of the oldest idle child frees it.
- A `workflow` task: stops the run with the same semantics as `workflow/run/stop`, so it stays
  resumable within the current AppServer lifetime.

`TaskStop` returns after the task settles or after a bounded wait, whichever comes first. The result
carries `taskId`, `kind`, the label, and the settled status; for an Agent it also carries the child's
last agent message when one exists. A task that settled within the wait is marked `notified`, so no
notification follows. A task that had already ended returns its final status rather than an error. A
task that does not settle within the wait returns `stopping`, and its notification is delivered when
it settles.

Authority:

- shell tasks: the owner Thread, as for `WriteStdin`;
- agent tasks: the caller's own descendants, gated by Agent-control authority;
- workflow tasks: the run's parent Thread.

A SubAgent role that denies Agent control may still stop its own shell tasks. `TaskStop` carries a
`core.task` presentation with `options.operation = "stop"` and `options.kind` set to the task kind.

### 4.2 `WriteStdin`

`WriteStdin` writes input and reads unread output, including the empty-input wait. Input that is
only Ctrl+C is ordinary input and does not settle the terminal.

### 4.3 `SendMessage`

`SendMessage(target, message)` both messages and continues Agents:

- to one of the caller's descendants: starts the child when it is idle or was stopped, injects the
  message into its active Turn at the next sampling boundary when the child is a running native
  child, and otherwise runs it after the active Turn. Pending mailbox messages for the child are
  delivered with it. The child resumes with its stored role, profile, runtime, model configuration,
  workspace, and path;
- to any other open path in the root tree (the parent, an ancestor, a sibling): writes a passive
  `MESSAGE` that never starts a Turn.

A target whose spawn edge was closed by a client is an error.

### 4.4 `SpawnAgent`, `ListAgents`, `Workflow`, `Exec`

Their results name the task id and tell the model that a notification arrives when the task ends.

## 5. Notifications

### 5.1 When a notification is sent

A task that the model received in a still-running state is armed. When it ends, Session Core delivers
exactly one notification to its owner, unless the task is already `notified`:

- the model received the final status through a tool result (`Exec`, `WriteStdin`, `TaskStop`);
- a host shutdown ended it;
- a Workflow child with silent completion ended, because its run consumes the result.

A client stop (`terminal/stop`, `turn/interrupt` on a SubAgent child, `subagent/close`,
`workflow/run/stop`) still notifies and records the stop origin as `user`. A stop requested through
the `/stop all` or `/tasks stop` command ([Channel Messaging and Commands](../features/channel-messaging.md) §12.2)
goes through the task registry with stop origin `user`; like `TaskStop`, a task that settles within
the wait owes no notification, because the command's reply reports it. A shell stall (see [Tool Architecture](tools-architecture.md) §7.3) sends a
notification without settling the task; its completion notification still follows.

### 5.2 Envelope

The notification is a queued input. Its display text is one line, for example `Background command
finished (exit code 0): npm run build`, `Subagent docs_reviewer finished`, or `Workflow
review-change was stopped by the user`; clients show it, and the model receives only:

```text
<task_notification>
<task_id>term_3f9a1c2b7d10</task_id>
<status>completed</status>
<summary>Background command "npm run build" completed (exit code 0)</summary>
<output_file>.craft/terminals/.../term_3f9a1c2b7d10.log</output_file>
</task_notification>
```

- `status` is `completed`, `failed`, `stopped`, or `waitingForInput` for a stall; the summary names
  the kind-specific status (`timedOut`, `lost`, exit code) in words, and says when the user stopped
  the task.
- `output_file` is the terminal log for a shell and the run directory for a Workflow; Agents have
  none, and the model does not read child transcripts.
- `result` is the child's final agent message for an Agent, the run result or error for a failed or
  completed Workflow, and, for a shell stall only, at most 2,000 characters from the end of the unread
  output. A finished shell carries no `result`; its output is in `output_file`.
- A stall adds the instruction to answer through `WriteStdin` when stdin is open, and otherwise to
  stop the task and rerun it non-interactively.

Trigger fields: `triggerKind` is `terminal`, `subagent`, or `workflow`;
`triggerLabel` is the task label; `triggerRefId` is the task id; `triggerStatus` is the kind-specific
final status, or `waitingForInput` for a stall. Clients do not present these inputs as user messages.

### 5.3 Delivery

- While the owner Thread has an active Turn, the notification is drained at the next sampling
  boundary as guidance. It does not preempt a model request, does not hand over a waiting foreground
  `Exec`, and skips `UserPromptSubmit` hooks. If the Turn ends first, it becomes queued input.
- While the owner Thread is idle, the notification is queued and the next queued Turn is started.
  Notifications that are pending when that Turn starts are admitted together as its input, so tasks
  that end together wake the Thread once.
- When the owner Thread is not Active, the notification stays queued for the Thread's next Turn.
- A SubAgent's own tasks notify that SubAgent, not its parent.
- An Agent whose Turn was cancelled because its parent's Turn was cancelled notifies the parent as
  a user stop, but the notification does not start a Turn.

`Tools.BackgroundTasks.CompletionDelivery` selects `wake` (default) or `off`. With `off`,
notifications are queued but never start a Turn. A wake loop is bounded by
`Tools.BackgroundTasks.MaxConsecutiveWakes` (default 3): when the Thread's last N Turns were each
started by notifications and each started another background task, the next notification is queued
without starting a Turn and is delivered with the next user input. A notification is never dropped.

An Agent's ending reaches its parent only as a task notification. The mailbox carries `MESSAGE` and
`NEW_TASK` entries.

## 6. Clients

Clients control tasks through `terminal/*`, `subagent/*`, and `workflow/run/*`, and stop a SubAgent
by interrupting the child thread's active turn with `turn/interrupt`, which leaves it open as the
model's `TaskStop` does. `subagent/close` closes the child.

Desktop:

- A `TaskStop` call renders as one row naming what stopped: "Stopped background terminal
  {command}", "Stopped subagent {name}", "Stopped workflow {name}".
- A Turn started by any task notification marks its activity boundary with the kind's icon instead
  of a bubble, names the task and how it ended on hover, and the icon reveals the tool call that
  started the task in the conversation.

## 7. Constraints

- Role and profile Agent-control tool lists accept only tools that exist; an unknown name is rejected.
- An external SubAgent runtime's `supportsFollowupTask` capability governs whether `SendMessage` can
  continue it.
- CodeMode and direct calls obey the same `TaskStop` and `SendMessage` contracts.
- Tool results and notifications carry English text; Desktop owns localized presentation.

## 8. Acceptance

- The model stops a background command, a SubAgent, and a Workflow with `TaskStop`, gets the settled
  status in the result, and receives no further notification for them.
- A stopped SubAgent continues after `SendMessage`.
- A command, a SubAgent, and a Workflow that end while their owner is idle each wake it with the same
  envelope; several ending together wake it once.
- A notification that arrives during a Turn reaches the model at its next sampling boundary.
- A user stop from any client tells the model it was the user.
- A wake loop stops after the configured limit and the held notification arrives with the next user
  input.
- The background-work tool surface is exactly the one in §4, in tools, prompts, roles,
  configuration, and documentation.
- Turns woken by any task kind show their origin and reveal the starting tool call.
