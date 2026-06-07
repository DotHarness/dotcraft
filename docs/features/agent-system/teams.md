# Teams

Hand DotCraft a complex request and it puts a small team on it. A Team Leader breaks the request into a task board and dispatches the work in parallel to a fixed roster of specialists — Explorer, Builder, Reviewer, Operator — then pulls their results back into one finished answer. You give a single ask and get the completed mission, not a pile of subtasks to babysit.

Teams ships behind the built-in `agent-teams` plugin. After enabling it from the Plugins catalog, Desktop shows a Team sidebar entry; the Team panel is the primary entry point for creating Missions and inspecting the team board.

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

> [!NOTE]
> Each teammate works in its own conversation, with its own context, tools, history, and audit trail — so you can open any teammate's thread and follow exactly what it did.

## When to Use Teams

| Scenario | Recommendation |
|---|---|
| One-shot self-contained delegation inside an existing chat | [SubAgents](./subagents) |
| A user request that needs planning, parallel work, and a synthesized final reply | Teams |
| Recurring or scheduled work driven by a workspace task | [Automations](./automations) |
| Keeping one Thread moving toward a long-running objective | [Goals](./automations#goals) |

Teams is overkill when the work fits comfortably inside one Thread. It is the right call when work naturally splits across roles, has explicit dependencies, or benefits from a Leader-owned synthesis at the end.

## Default Roster

The team roster is fixed:

| Member | Role |
|---|---|
| **Team Leader** | Breaks down Missions, assigns work, coordinates the team, writes the user-facing final response. |
| **Explorer** | Researches, inspects, and maps unknowns. |
| **Builder** | Implements changes and produces artifacts. |
| **Reviewer** | Checks quality, risks, and correctness. |
| **Operator** | Handles app- and computer-oriented operational tasks. |

The roster is fixed: you don't create or remove members. Each member already carries a focused role and tool set suited to its part of the work.

## Mission Lifecycle

A Mission is the user-facing delivery unit. Each Mission owns a task board, mailbox events, and a final response.

| Status | Meaning |
|---|---|
| `planning` | Mission created; the Leader is working out a plan. |
| `active` | The Leader has a plan and at least one task is dispatched. |
| `awaitingLeaderReview` | All tasks and reviews are complete; the Leader is writing the final answer. |
| `done` | The Leader has delivered the final answer. |
| `cancelled` | You cancelled the Mission; its unfinished tasks are cancelled too. |

Only finished Missions (`done` or `cancelled`) can be archived. Archiving keeps the record — it doesn't delete the Mission or its teammate threads.

> [!TIP]
> In Desktop, cancellation and archival are card actions: drag a Mission card to the discard pile and confirm. The discard pile is not a clickable button — the drag is the deliberate gesture.

## Task Board

Each Mission has a shared board. Every task on it carries an assignee, a status, its dependencies, any blockers, and an output summary, so you can see at a glance who's doing what and what's holding things up.

| Status | Meaning |
|---|---|
| `pending` | Created, waiting to be scheduled. |
| `waitingDependencies` | Blocked on an upstream task. |
| `ready` | Cleared to run, but its assignee isn't free yet. |
| `running` | Running in the assignee's thread. |
| `blocked` | The assignee hit a blocker that needs action elsewhere. |
| `review` | Work is done but hasn't passed review yet. |
| `done` | Accepted toward the Mission's final answer. |
| `failed` | Can't continue without the Leader stepping in. |
| `cancelled` | Cancelled with its Mission or by an explicit decision. |

The Leader sets dependencies between tasks when it assigns them, and can hold downstream work until it has reviewed the upstream results first. Reviews are just tasks too — any member can be asked to review another's work.

## Collaboration Loop

Teammates work together in three ways:

- **Messages** — lightweight notes between members or to the Leader, used to flag something or ask for input.
- **Artifacts** — explicit handoffs: a named result with its location and a short summary, so the next teammate knows what they're picking up.
- **Progress updates** — running status or a raised blocker, so the board stays current.

The Leader doesn't sit and poll. It dispatches work, then steps back; it's brought back in only when results land, a blocker appears, a teammate needs an answer, or the Mission is ready for its final review.

## Desktop UI

The Team panel is a card-game collaboration board:

- Robot teammate cards, Mission cards, and Task cards with live status pulled from Teams state.
- A right-side detail rail for the selected teammate, mission, or task.
- A primary Mission Draft workflow on the board for creating new Missions.
- Thread links that open the real Mission teammate thread when context allows; Mission teammate threads also appear in the ordinary conversation list while the plugin is enabled.

Status pills surface scheduler-relevant states (`waitingDependencies`, `blocked`, `review`, `awaitingLeaderReview`, `done`) so a stalled Mission can be diagnosed from the board.

## Configuration

Teams is gated by the built-in `agent-teams` plugin. Its Missions, tasks, and handoff files all live inside the workspace, so they travel with the project and stay available across entry points. Each Mission gives its teammates a shared scratchpad for durable handoff material.

Runtime settings live in the `teams` config section. See the [Configuration Reference](../../developing/configuration) for available keys, and [Unified Session Core](../../developing/architecture/session-core) for how Mission threads are stored.

## Related docs

- [SubAgents](./subagents) — single-level delegation when Teams is overkill.
- [Automations & Goals](./automations) — schedule-driven tasks and Thread-level objectives that pair with Teams Missions.
- [Unified Session Core](../../developing/architecture/session-core) — Thread / Turn / Item model that underpins Mission teammate threads.
