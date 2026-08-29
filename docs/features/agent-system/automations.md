# Automations & Goals

DotCraft has two ways to keep the agent working when you are not driving every turn. Automations run a task on a schedule or on demand, so routine work like reports, checks, and cleanups happens on its own. A Goal gives one conversation a long-running direction, and DotCraft keeps advancing it whenever that conversation goes idle.

![How DotCraft runs Automations and Goals](/automations-goals-overview.svg)

![Setting a goal on a conversation](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

## Automations

Automations run local work in your workspace, on a schedule or whenever you trigger them. Use them for the routine jobs you would otherwise remember to do yourself: a weekly report, a nightly check, a cleanup pass.

Create and manage tasks from the Desktop **Automations** panel. A task has two parts: a short brief (what to do, and when) and a workflow prompt (how to do it). Tasks live with your project under `.craft/tasks/`, so they travel with the repository. A task you run often can be saved as a template to start from next time.

A task can be bound to an existing conversation so every later run continues there. It can also run as a saved [Agent Profile](./agent-profiles), using just that agent's tools, skills, and model — without one, it runs as the workspace agent. When the work is done, the agent writes a short completion summary.

Schedule formats, workflow variables, and the full set of task fields are in the [Configuration Reference](../../developing/configuration#automations-goals-and-hooks).

## Review task output

A task that is not bound to an existing conversation runs in a managed Git worktree when the project is a Git repository, so its changes stay out of the workspace you are working in. The Desktop review panel shows the branch it used, whether the worktree has uncommitted changes, and whether it has commits ahead of the base.

From the review panel you can open the task's conversation, hand the worktree back to your local workspace, or discard it. Discarding removes the task's worktree output along with its managed branch, so use it once you are sure you no longer need the changes.

## Goals

A Goal gives one conversation a long-running direction. Once you set it, the goal stays with that conversation, and every time the conversation goes idle (with auto-continue on) DotCraft keeps advancing it — until it is complete, until you pause or clear it, or until the token budget runs out and it waits on you.

Goals suit the work that takes many turns to move: refactors, documentation passes, migrations, investigations. Progress, time spent, and completion state stay with the conversation, and you can pause, resume, replace, or clear the goal at any time.

Set, pause, and clear a goal from the Desktop goal control. The conversation list and detail view show its current state: active, paused, budget limited, or complete.

## Common scenarios

Automations decide when something runs once. Goals decide which direction the work keeps moving. The two combine: a scheduled task can run inside the same conversation and keep pushing the same goal forward.

| Scenario | Recommendation |
|---|---|
| Weekly or daily reports, scheduled checks | Automations on a schedule |
| Run a test suite and write the summary to a conversation | Automations with a completion summary |
| Keep advancing a refactor or documentation pass | Goals |
| Make scheduled work follow the same long-running objective | Automations + Goals |
| Format or lint after file writes | [Lifecycle Hooks](./hooks), after a tool finishes |
| Block dangerous shell commands | [Lifecycle Hooks](./hooks), before a tool runs |

## Related docs

- [Lifecycle Hooks](./hooks) — for work that triggers at a moment in a tool call or session, rather than on a schedule
- [Observability](../self-hosted/observability) — review task runs and approvals in Dashboard
