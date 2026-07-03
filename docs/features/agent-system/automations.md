# Automations & Goals

DotCraft gives the agent two ways to keep working without you driving every turn:

- **Automations** — run the agent on a schedule or on demand, so routine work like reports, checks, and cleanups happens on its own.
- **Goals** — pin a long-running objective to a conversation, and DotCraft keeps pushing it forward whenever that conversation goes idle.

![DotCraft Automations and Goals overview](/automations-goals-overview.svg)

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

## Automations

Automations run local work in your workspace — on a schedule or whenever you trigger them. Use them for the routine jobs you'd otherwise remember to do yourself: a weekly report, a nightly check, a cleanup pass.

### Create a task

Create and manage tasks from the Desktop Automations panel. Each task pairs a short brief (what to do, and when) with a workflow prompt that tells the agent how to do it. Tasks live with your project under `.craft/tasks/`, so they travel with the repository.

### What you can do with a task

| Capability | Description |
|---|---|
| Manual run | Trigger a task on demand from Desktop |
| Scheduled run | Give the task a schedule so it runs on its own |
| Thread binding | Bind a task to an existing conversation so future runs continue there |
| Agent profile | Run the task as a saved Agent Profile, so it uses just that agent's tools, skills, and model (optional — defaults to the workspace agent) |
| Templates | Save a task as a reusable template |
| Completion summary | The agent writes a short summary when the work is done |
| Delete | Remove the task, optionally with its linked conversation |

### Review worktree output

Unbound automation tasks run in a managed Git worktree when the project is a Git repository. The Desktop review panel shows the branch, whether the worktree has uncommitted changes, and whether it has commits ahead of the base.

From the review panel you can open the task thread, hand the worktree back to your local workspace, or discard the task worktree. Discarding removes the task's worktree output and its managed branch, so use it only when you no longer need the changes.

Schedule formats, workflow variables, and the full set of task fields are in [Automations, Goals, and Hooks](../../developing/configuration#automations-goals-and-hooks).

---

## Goals

A Goal pins a long-running objective to one conversation. Once you set it, the goal stays with that conversation, and whenever the conversation goes idle (with auto-continue on) DotCraft keeps advancing it — until it's complete, paused, cleared, or stopped by its token budget.

### Good fits

- Refactors, documentation passes, migrations, or investigations that span many turns.
- Long-running work you may pause, resume, replace, or clear at any time.
- Work whose progress, time spent, and completion state should stay with the conversation.

### Lifecycle

| Status | Meaning |
|---|---|
| `active` | In effect; an idle conversation may auto-continue |
| `paused` | Kept, but won't auto-continue |
| `budgetLimited` | The token budget ran out; it's waiting on you |
| `complete` | The agent reviewed the work and marked the goal done |

### How to manage a goal

| Action | Where |
|---|---|
| Set or replace a goal | Desktop Goal control |
| Pause or resume a goal | Desktop Goal control |
| Clear a goal | Desktop Goal control |
| Check goal state | Conversation list and detail view |

Goal fields and the AppServer methods behind these controls are listed in [Automations, Goals, and Hooks](../../developing/configuration#automations-goals-and-hooks).

### Goals × Automations

Automations are best for "run this task on a schedule or on demand." Goals are best for "keep this Thread moving toward one long-running objective." They can be combined: an Automation wakes up or submits a check, while the Goal preserves the Thread's long-term direction and completion state.

## Common Scenarios

| Scenario | Recommendation |
|---|---|
| Weekly / daily reports, scheduled checks | Automations on a schedule |
| Run a test suite and write the summary to a conversation | Automations with a completion summary |
| Keep advancing a refactor or documentation pass | Goals |
| Make scheduled work follow the same long-running objective | Automations + Goals |
| Auto-lint / format after file writes | [Hooks](../self-hosted/security#hooks) `PostToolUse` |
| Block dangerous shell commands | [Hooks](../self-hosted/security#hooks) `PreToolUse` |

## Related docs

- [Project Workspace](../project-first) — where `.craft/tasks/` and `.craft/automations/` sit
- [Unified Session Core](../../developing/architecture/session-core) — how Thread / Turn / Item relate to goal state
- [Observability](../self-hosted/observability) — Trace and approvals in Dashboard
- [Security & Sandbox](../self-hosted/security#hooks) — Hooks, security guards, and sandbox isolation
- [Configuration Reference](../../developing/configuration#automations-goals-and-hooks) for Automations, Goals, and Hooks fields
