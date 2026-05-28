# Automations & Goals

DotCraft splits "schedule the agent to do something at a certain time" and "keep one Thread moving toward a declared objective" into two complementary capabilities:

- **Automations** — Workspace-level task scheduling that runs the agent on a timer or on-demand
- **Goals** — Thread-level long-running objectives that DotCraft can continue when the Thread is idle

![DotCraft Automations and Goals overview](/automations-goals-overview.svg)

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

## Automations

DotCraft native Automations covers local tasks only. It reads task files from `.craft/tasks/` in the current workspace, runs Agents manually or on a schedule, and supports thread binding, templates, retries, activity display, and the `CompleteLocalTask` completion path.

### Create a Local Task

A local task lives under `.craft/tasks/<task-id>/`:

```text
.craft/
  tasks/
    weekly-report/
      task.md
      workflow.md
```

`task.md` describes the title, body, schedule, and thread binding. `workflow.md` describes the Agent workflow prompt.

### Common Capabilities

| Capability | Description |
|---|---|
| Manual run | Desktop Automations panel or AppServer `automation/task/run` |
| Scheduled run | Configure `schedule` in the task file |
| Thread binding | Bind a task to an existing thread so future runs submit there |
| Templates | Save reusable task templates under `.craft/automations/templates/` |
| Completion tool | Agent calls `CompleteLocalTask` with a summary |
| Delete task | Delete the task folder, optionally with the linked thread |

### Workflow Template Variables

| Variable | Description |
|---|---|
| `task.id` | Task id |
| `task.title` | Task title |
| `task.description` | `task.md` body |
| `task.thread_id` | Bound thread id |

AppServer methods and automation configuration fields are listed in [Automations, Goals, and Hooks](../developing/configuration.md#automations-goals-and-hooks).

### `CompleteLocalTask` Tool

| Parameter | Description |
|---|---|
| `summary` | Short completion summary |

The agent calls it after finishing the work so the task can record its summary and complete.

### Directory Layout

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
    automations/
      templates/
        <template-id>/
          template.md
```

---

## Goals

Goals are long-running objectives attached to a single Thread. After a user explicitly sets a goal, DotCraft stores it in Session Core so future Turns can see it. When the Thread is idle and auto-continue is enabled, the system can keep advancing the goal until it is completed, paused, cleared, or budget-limited.

### Good Fits

- Refactors, documentation passes, migrations, or investigations that need multiple turns.
- Long-running work that users may pause, resume, replace, or clear at any time.
- Work where token usage, elapsed time, and completion state should persist with the Thread.

### Lifecycle

| Status | Meaning |
|---|---|
| `active` | The goal is in effect; idle Threads may auto-continue |
| `paused` | The goal is retained but does not auto-continue |
| `budgetLimited` | Token budget was reached; user action is required |
| `complete` | The agent audited the work and marked the goal complete |

### Common Entry Points

| Action | Entry point |
|---|---|
| Set / replace a goal | Desktop Goal control, clients with `threadGoals`, AppServer `thread/goal/set` |
| Pause / resume a goal | Desktop Goal control or AppServer `thread/goal/set` status update |
| Clear a goal | Desktop Goal control or AppServer `thread/goal/clear` |
| Read goal state | Thread lists, Thread detail, AppServer `thread/goal/get` |

Goal configuration fields and AppServer goal methods are listed in [Automations, Goals, and Hooks](../developing/configuration.md#automations-goals-and-hooks).

### Goals × Automations

Automations are best for "run this task on a schedule or on demand." Goals are best for "keep this Thread moving toward one long-running objective." They can be combined: an Automation wakes up or submits a check, while the Goal preserves the Thread's long-term direction and completion state.

## Common Scenarios

| Scenario | Recommendation |
|---|---|
| Weekly / daily reports, scheduled checks | Automations on a schedule |
| Run a test suite and write the summary to a thread | Automations + `CompleteLocalTask` |
| Keep advancing a refactor or documentation pass | Goals |
| Make scheduled work follow the same long-running objective | Automations + Goals |
| Auto-lint / format after file writes | [Hooks](./security.md#hooks) `AfterToolCall` |
| Block dangerous shell commands | [Hooks](./security.md#hooks) `BeforeToolCall` |

## Troubleshooting

### Goal does not continue

Confirm the goal feature and auto-continue are enabled in configuration, the goal status is `active`, and the current Thread has no running Turn or pending user approval.

### Goal entered budgetLimited

The goal reached its token budget and auto-continuation stopped. Increase the budget, replace the goal, pause it, or ask the agent to summarize progress before setting a smaller follow-up goal.

### Client does not show Goal controls

Confirm AppServer `initialize` returns `capabilities.threadGoals = true`. If Goals are disabled, the server does not expose goal controls.

### Automation tasks not visible in Desktop

The Automations panel requires Gateway to load the Automations module. The local Dashboard works for single-workspace debugging but does not orchestrate full automation runs.

## Related

- [Project Workspace](./workspace.md) — where `.craft/tasks/` and `.craft/automations/` sit
- [Unified Session Core](./session-core.md) — how Thread / Turn / Item relate to goal state
- [Observability](./observability.md) — Trace and approvals in Dashboard
- [Security & Sandbox](./security.md#hooks) — Hooks, security guards, and sandbox isolation
- [Configuration Reference](../developing/configuration.md#automations-goals-and-hooks) for Automations, Goals, and Hooks fields
