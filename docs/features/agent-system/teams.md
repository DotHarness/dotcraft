# Teams

Hand DotCraft a complex request and it puts a small team on it. A Team Leader breaks the request into a task board and dispatches the work in parallel to a fixed roster of specialists — Explorer, Builder, Reviewer, Operator — then pulls their results back into one finished answer. You give a single ask and get the completed Mission, not a pile of subtasks to babysit.

Teams ships as the built-in `agent-teams` plugin. Enable it from **Plugins** in Desktop and a **Team** entry appears in the sidebar, where Missions, teammates, and tasks all live.

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

> [!NOTE]
> Each teammate works in its own conversation, with its own context, tools, and history. Open any teammate's thread and you can follow exactly what it did.

## When to use Teams

Teams is overkill when the work fits comfortably inside one conversation. It earns its place when the work splits naturally across roles, has dependencies between tasks, or needs someone to synthesize the results into a single answer at the end.

| Scenario | Recommendation |
|---|---|
| A one-shot, self-contained delegation inside an existing chat | [Subagents](./subagents) |
| A request that needs planning, parallel work, and one synthesized reply | Teams |
| Routine work on a schedule or triggered by hand | [Automations & Goals](./automations) |

## Five fixed roles

| Member | Responsibility |
|---|---|
| **Team Leader** | Breaks down Missions, assigns work, coordinates the team, writes the final answer. |
| **Explorer** | Researches, inspects, and maps unknowns. |
| **Builder** | Implements changes and produces artifacts. |
| **Reviewer** | Checks quality, risks, and correctness. |
| **Operator** | Handles app- and computer-oriented operational tasks. |

The roster is fixed — you don't create or remove members. Each one already carries the role and tool set suited to its part of the work.

## How a Mission moves

A Mission is what you get delivered: a task board plus the final answer. The Leader plans first, dispatches the tasks, and once they're all done writes the answer that comes back to you.

Every task on the board carries an assignee, a status, its dependencies, and an output summary, so you can see at a glance who's doing what and what's holding things up. The Leader sets the dependencies as it assigns work, and can hold downstream tasks until it has reviewed the upstream results. Reviews are tasks too — any member can be asked to review another's work.

A finished Mission can be archived. Archiving keeps the record and deletes neither the Mission nor its teammate threads.

> [!TIP]
> Cancelling and archiving are card actions: drag a Mission card to the discard pile and confirm. The discard pile isn't a clickable button — the drag is the deliberate gesture.

## How teammates work together

Teammates have three ways to reach each other:

- **Messages** — lightweight notes to another member or to the Leader, to flag something or ask for input.
- **Artifacts** — explicit handoffs: a named result with its location and a short summary, so the next teammate knows what they're picking up.
- **Progress updates** — running status or a raised blocker, so the board stays current.

The Leader doesn't sit and poll. It dispatches work and steps back, and is brought in again only when results land, a blocker appears, a teammate needs an answer, or the Mission is ready for its final review.

Missions, tasks, and handoff files all live inside the workspace, so they travel with the project and are still there when you continue from another entry point. Each Mission also gives its teammates a shared scratchpad for handoff material they'll come back to.

## Related docs

- [Subagents](./subagents) — far lighter when you only need one delegation
- [Agent Profiles](./agent-profiles) — give teammates a fixed role, tool boundary, and style
- [Automations & Goals](./automations) — run routine work on a schedule, or pin a long-running goal to a conversation
