# Subagents

A subagent is how the main agent delegates. Hand it a self-contained task, it works in its own context, and only the result comes back to the main chat. Give exploration and research — the work that produces a lot of intermediate output — to a subagent, and the main chat stays readable.

![How DotCraft delegates a task to a subagent](/subagent-delegation-overview.svg)

A subagent runs inside DotCraft with its own context and a role that decides what it can do. The defaults are safe to use as they are — the main agent can create one level of subagents, and a subagent does not spawn further ones. Full configuration fields are in the [Subagents configuration](../configuration/subagents).

## The role decides what it can do

| Role | Best for | Permissions |
|---|---|---|
| `explorer` | Read-only code exploration and research | Reads and searches files, runs observation commands such as `git diff`, cannot write |
| `worker` | Implementation, verification, file changes | Reads and writes files, runs commands, accesses the network |
| `default` | Summaries, analysis, general collaboration | A conservative tool set with no high-privilege tools |

Role limits are enforced at the moment a tool is called. A subagent that calls a tool it is not allowed gets a clear reason and can take another route. You can also define your own roles in workspace configuration to fix the delegation patterns your team uses most.

## Follow subagent progress in Desktop

![Following several background subagents and their status in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/subagents.webp)

Desktop keeps the main chat available while subagents work. Open the background-agent summary to see which tasks are running or complete. The finished result still returns to the main chat.

## Approvals still apply

File and command operations from a subagent go through the same approval flow as the current chat, and every request is labeled with the subagent it came from. When you need stronger isolation, tighten a role's tool list and approvals as described in [Security](../self-hosting/security).

## How to choose

| Situation | Recommendation |
|---|---|
| Bounded read-only exploration | `explorer` |
| Implementation inside the current workspace | `worker` |
| Summaries, analysis, or general collaboration | `default` |
| The strongest tool isolation | A custom role with a tightened tool list and Ask for approval |

A subagent's chat is saved as a child of the main chat. Archiving, restoring, or deleting the main chat handles the child chats with it, and role and tool limits survive a restart.

## Related docs

- [Security](../self-hosting/security) — tighten what a subagent can reach with the workspace boundary and approvals
- [Observability](../self-hosting/observability) — view subagent calls and approvals in Dashboard
