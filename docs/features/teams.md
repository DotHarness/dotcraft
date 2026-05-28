# Teams

Teams is an installable team workspace where a Team Leader agent breaks a Mission into a dependency-aware task board and dispatches it to a fixed roster of specialist robots. A scheduler runs teammates in their own Mission threads and only wakes the Leader when synthesis, handoffs, or final review is needed.

Teams ships behind the built-in `agent-teams` plugin. After enabling it from the Plugins catalog, Desktop shows a Team sidebar entry; the Team panel is the primary entry point for creating Missions and inspecting the team board.

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

> [!NOTE]
> Teams is a managed App Binding runtime built on Session Core. Each teammate's Mission thread is an ordinary top-level DotCraft Thread, with its own context, tools, history, and audit.

## When to Use Teams

| Scenario | Recommendation |
|---|---|
| One-shot self-contained delegation inside an existing chat | [SubAgents](./subagents.md) |
| A user request that needs planning, parallel work, and a synthesized final reply | Teams |
| Recurring or scheduled work driven by a workspace task | [Automations](./automations.md) |
| Keeping one Thread moving toward a long-running objective | [Goals](./automations.md#goals) |

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

Per-role customization (Skills, MCP servers, plugins, prompt, permission mode, tool surface) is on the roadmap. Dynamic team or member creation is intentionally out of scope.

## Mission Lifecycle

A Mission is the user-facing delivery unit. Each Mission owns a task board, mailbox events, and a final response.

| Status | Meaning |
|---|---|
| `planning` | Mission created; Leader input queued or running. |
| `active` | Leader has recorded a plan or dispatched at least one task. |
| `awaitingLeaderReview` | All required Tasks and review gates complete; Leader must write `finalResponse`. |
| `done` | Leader has finalized the Mission with `finalResponse`. |
| `cancelled` | User cancelled the Mission; non-done Tasks under it are cancelled. |

Only terminal Missions (`done` or `cancelled`) can be archived. Archiving keeps state — it does not delete the Mission or its teammate threads.

> [!TIP]
> In Desktop, cancellation and archival are card actions: drag a Mission card to the discard pile and confirm. The discard pile is not a clickable button — the drag is the deliberate gesture.

## Task Board

Tasks (`TeamTask`) form a Mission-scoped shared board with explicit assignee, status, dependencies, blockers, output summary, and metadata.

| Status | Meaning |
|---|---|
| `pending` | Created, waiting for scheduler evaluation. |
| `waitingDependencies` | Blocked on upstream tasks. |
| `ready` | Can run, but assignee thread is not yet ready. |
| `running` | Queued or running in the assignee's Mission thread. |
| `blocked` | Assignee reported a blocker requiring action elsewhere. |
| `review` | Work complete; a review gate has not yet accepted it. |
| `done` | Accepted for Mission finalization. |
| `failed` | Cannot continue without Mission-level recovery. |
| `cancelled` | Cancelled with its Mission or by an explicit Teams decision. |

Tasks reference each other by short Mission-scoped aliases (`t1`, `t2`), and artifacts use similar aliases (`a1`, `a2`); canonical ids (`task_...`, `artifact_...`) remain stored for compatibility.

### Dependencies and Leader Synthesis

- The Leader records explicit dependencies via `dependsOnTaskIds` when assigning a task.
- A task can be marked `requiresLeaderSynthesis` when downstream work needs the Leader to interpret upstream results before dispatch — the scheduler wakes the Leader once upstream finishes and holds teammate dispatch until synthesis is supplied.
- Reviews are a task property (`kind = "review"`), not a hardcoded role rule. Any member can be assigned a review task.

## Collaboration Loop

Teammates communicate through:

- **Mailbox messages** (`SendMessage`) — lightweight Mission-scoped events between members or to the Leader. They are not turns in any thread; the scheduler coalesces them and may wake the recipient when the message is actionable.
- **Artifacts** (`PublishArtifact`) — explicit handoff records with title, path or URI, and summary. Publication does not complete a task; teammates still call `MarkTaskDone` or `ReportProgress(status: "blocked")`.
- **Progress** (`ReportProgress`) — running progress or blocker updates; not a completion signal.

The Leader does not poll. After dispatching work or sending a message, the Leader ends its turn; the scheduler wakes it when task results, blockers, teammate messages, synthesis needs, or final review require attention.

## Desktop UI

The Team panel is a card-game collaboration board:

- Robot teammate cards, Mission cards, and Task cards with live status pulled from Teams state.
- A right-side detail rail for the selected teammate, mission, or task.
- A primary Mission Draft workflow on the board for creating new Missions.
- Thread links that open the real Mission teammate thread when context allows; Mission teammate threads also appear in the ordinary conversation list while the plugin is enabled.

Status pills surface scheduler-relevant states (`waitingDependencies`, `blocked`, `review`, `awaitingLeaderReview`, `done`) so a stalled Mission can be diagnosed from the board.

## Configuration

Teams is gated by the built-in `agent-teams` plugin. State is persisted under:

```text
.craft/teams/state.json
.craft/teams/missions/{missionId}/
```

The mission scratchpad / artifact workspace path is mounted into each Mission teammate thread as an App Context Block, so teammates know where to drop durable handoff material.

Runtime fields live in the `teams` config section. See [Configuration Reference](../developing/configuration.md) for available keys.

## See Also

- [SubAgents](./subagents.md) — single-level delegation when Teams is overkill.
- [Automations & Goals](./automations.md) — schedule-driven tasks and Thread-level objectives that pair with Teams Missions.
- [Unified Session Core](./session-core.md) — Thread / Turn / Item model that underpins Mission teammate threads.
