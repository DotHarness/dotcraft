# DotCraft Automations Lifecycle

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-05-18 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Session Core](../core/session-core.md) |

DotCraft native Automations covers local tasks only. It watches local task files in the current workspace, dispatches Agents for runnable tasks, preserves scheduling and thread binding, and records completion through `CompleteLocalTask`.

## Scope

- Local task files under `.craft/tasks/` or `Automations.LocalTasksRoot`.
- Local schedule initialization and re-arm.
- Manual run.
- Thread binding.
- Local templates under `.craft/automations/templates/` or `Automations.UserTemplatesRoot`.
- Managed worktree review, discard, and conservative retention cleanup.
- Task deletion.
- Activity notifications via `automation/task/updated`.
- Completion via `CompleteLocalTask`.

## Task Identity

Tasks are addressed by `taskId` only. The AppServer automation task methods do not accept or return source identifiers.

## AppServer Surface

| Method | Params | Result |
|--------|--------|--------|
| `automation/task/list` | `{}` | `{ tasks: AutomationTaskWire[] }` |
| `automation/task/read` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/create` | `{ title, description, workflowTemplate?, approvalPolicy?, workspaceMode?, schedule?, threadBinding?, templateId? }` | `{ task: AutomationTaskWire }` |
| `automation/task/run` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/updateBinding` | `{ taskId, threadBinding?: AutomationThreadBindingWire | null }` | `{ task: AutomationTaskWire }` |
| `automation/task/discardWorktree` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/delete` | `{ taskId }` | `{ ok: true }` |
| `automation/template/list` | `{ locale? }` | `{ templates: AutomationTemplateWire[] }` |
| `automation/template/save` | `{ id?, title, description?, icon?, category?, workflowMarkdown, defaultSchedule?, defaultWorkspaceMode?, defaultApprovalPolicy?, needsThreadBinding?, defaultTitle?, defaultDescription? }` | `{ template: AutomationTemplateWire }` |
| `automation/template/delete` | `{ id }` | `{ ok: true }` |

## `AutomationTaskWire`

```json
{
  "id": "weekly-report",
  "title": "Weekly report",
  "description": "Summarize recent work",
  "status": "pending",
  "threadId": null,
  "approvalPolicy": "workspaceScope",
  "workspaceMode": "worktree",
  "worktree": {
    "branchName": "dotcraft/task-weekly-report",
    "path": "<workspace>/.craft/worktrees/task-weekly-report"
  },
  "createdAt": "2026-05-05T00:00:00Z",
  "updatedAt": "2026-05-05T00:00:00Z",
  "schedule": null,
  "threadBinding": null,
  "nextRunAt": null
}
```

`status` is one of `pending`, `running`, `completed`, or `failed`.
`workspaceMode` is canonicalized to `project` or `worktree`; legacy input
`isolated` is accepted on reads and create requests as an alias for `worktree`.
`worktree` is populated after a managed task worktree is provisioned and is
`null` for project-mode tasks, bound tasks, and non-Git fallback execution.

## Workspace Modes

Unbound tasks run in either `project` or `worktree` mode. `project` mode uses
the project workspace as the execution root. `worktree` mode is canonical for
isolated task execution and keeps task thread state in the project workspace
while setting only the execution workspace to the task worktree.

For Git workspaces, worktree-mode tasks use one reusable managed worktree per
task under `.craft/worktrees/`, on a branch named
`dotcraft/task-<sanitizedTaskId>`. Provisioning does not copy uncommitted
project workspace changes into the task worktree. If Git worktree provisioning
fails, the task falls back to its task-local `workspace/` directory and still
reports `workspaceMode: "worktree"` with `worktree: null`.

Bound tasks submit into their bound thread and ignore `workspaceMode`.

## Local Task Files

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

The local file store owns parsing and persistence. `task.md` contains task metadata and description. `workflow.md` is the Agent workflow prompt. Templates copy their workflow body into new local tasks.

## Dispatch

1. The orchestrator polls local task files.
2. Runnable tasks are keyed by `taskId`.
3. Scheduled tasks initialize `nextRunAt`; recurring schedules re-arm after a run.
4. Bound tasks submit into the bound thread when it is active and available.
5. Unbound tasks create or resume the task conversation in the project workspace.
   Worktree-mode tasks then ensure a managed Git worktree under
   `.craft/worktrees/` and use it only as the execution workspace.
6. The local task tool profile is registered with `CompleteLocalTask`.
7. Completion writes the Agent summary and emits `automation/task/updated`.

## Managed Worktree Review

For unbound `worktree` tasks, clients use `worktree/status` on the task thread to
refresh review indicators:

- `hasUncommittedChanges` reports dirty worktree state.
- `hasCommitsAheadOfBase` and `aheadCount` report commits on the task branch
  ahead of the recorded creation base.

`automation/task/discardWorktree` removes the task worktree and managed branch
while preserving the task. It rejects running tasks. If the task runs again, the
orchestrator provisions a fresh managed worktree.

## Managed Worktree Retention

When `Automations.WorktreeRetentionEnabled` is true, the orchestrator
periodically removes idle automation task worktrees that are clean and have no
commits ahead of their recorded base. `Automations.WorktreeRetentionIdlePeriod`
defaults to 21 days and must be at least 14 days.

Retention never removes a running task's worktree, a worktree with uncommitted
changes, or a worktree with commits ahead of base.
