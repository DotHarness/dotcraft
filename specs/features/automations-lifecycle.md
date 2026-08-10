# DotCraft Automations Lifecycle

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-05-18 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Session Core](../architecture/session-core.md) |

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
| `automation/task/read` | `{ taskId }` | `AutomationTaskWire` |
| `automation/task/create` | `{ title, description, workflowTemplate?, approvalPolicy?, workspaceMode?, schedule?, threadBinding?, templateId?, agentProfileId? }` | `{ taskId: string, taskDirectory: string }` |
| `automation/task/run` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/updateBinding` | `{ taskId, threadBinding?: AutomationThreadBindingWire | null }` | `{ task: AutomationTaskWire }` |
| `automation/task/discardWorktree` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/delete` | `{ taskId }` | `{ ok: true }` |
| `automation/template/list` | `{ locale? }` | `{ templates: AutomationTemplateWire[] }` |
| `automation/template/save` | `{ id?, title, description?, icon?, category?, workflowMarkdown, defaultSchedule?, defaultWorkspaceMode?, defaultApprovalPolicy?, needsThreadBinding?, defaultTitle?, defaultDescription?, defaultAgentProfileId? }` | `{ template: AutomationTemplateWire }` |
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
  "agentProfileId": "reviewer",
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
`workspaceMode` uses the canonical `project` or `worktree` names. Other values are rejected.
`worktree` is populated after a managed task worktree is provisioned and is
`null` for project-mode tasks, bound tasks, and non-Git fallback execution.
`agentProfileId` is the optional Agent Profile bound to the task; `null` when
the task runs with the default automation agent. Only the id is persisted; the
profile is resolved to a `ThreadConfiguration` at dispatch (see
[Agent Profile Binding](#agent-profile-binding)).

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
6. When the task is bound to an Agent Profile, the orchestrator resolves the
   profile into the task thread configuration and applies the automation
   operational overrides on top (see [Agent Profile Binding](#agent-profile-binding)).
   A missing or invalid bound profile fails the run.
7. The local task tool profile is registered with `CompleteLocalTask`.
8. Completion writes the Agent summary and emits `automation/task/updated`.

## Agent Profile Binding

A task may bind an [Agent Profile](../protocols/appserver-protocol.md#23a-agent-profile-management-methods)
via `agentProfileId`. Binding is optional; an unbound task runs with the default
automation agent and the source tool profile, unchanged from prior behavior.

Capability vs. operation is split:

- The **Agent Profile governs capabilities** — the resolved `ThreadConfiguration`
  supplies tools, MCP servers, skills, model, and agent instructions.
- The **automation governs operation** — regardless of the profile, the
  orchestrator force-overrides the operational fields it owns:
  `approvalPolicy` is forced to auto-approve (unattended runs must never block on
  approval), `automationTaskDirectory` is set to the task directory,
  `requireApprovalOutsideWorkspace` is derived from the task's approval policy,
  and the automation `toolProfile` is applied so the `CompleteLocalTask`
  completion tool is injected. Workspace mode and schedule remain task-owned.

When a profile is bound, the profile's tool / MCP / skills policy is the source
of truth for the agent's general capabilities; the default source tool profile's
capability set is not merged on top. The one exception is operational: the
completion tool (`CompleteLocalTask`) is always injected and kept reachable even
under a restrictive profile allow-list, so the run can always finish.

Only the binding is persisted in `task.md`, as the optional `agent_profile_id`
front-matter key. The profile is resolved to a `ThreadConfiguration` snapshot at
each dispatch, so edits to the profile take effect on the task's next run. If the bound profile cannot be resolved (deleted
or invalid) at dispatch, the run **fails** (`status: failed`) rather than
silently falling back to the default agent — the task explicitly requested that
capability set. Clients are expected to surface the failure reason.

Templates carry the binding as `defaultAgentProfileId`, a default that pre-fills
the Agent picker when a task is created from the template; it is not itself
executable. A template referencing a profile id that no longer resolves
pre-fills as the default agent.

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
