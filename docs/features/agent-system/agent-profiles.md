# Agent Profiles

An Agent Profile saves a purpose-built DotCraft agent so you can reach for it whenever that working style fits. Build one through conversation with Agent Builder, then reuse its role instructions, model defaults, tools, skills, MCP access, and approval behavior.

![DotCraft Agent Profiles](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

Start with one specialist or build out a whole agent team: an Explorer for research, a Builder for implementation, or an Operator for app workflows. Each role is an independent Profile you can choose whenever the work calls for it.

## Where profiles pay off

| Place | How it helps |
|---|---|
| Chat | Pick a saved agent in the composer and the conversation runs with that role and capability set. |
| [Automations & Goals](./automations) | Bind a task to a profile so scheduled and manual runs use only that agent's tools, skills, and model. |

A profile takes effect when a conversation or task starts. A conversation that's already running keeps the setup it started with, until you refresh it or start a new conversation or task with the updated profile.

## Built-in profiles

DotCraft includes five starting points for your agent team. Use one as-is or open it in Agent Builder and shape it around your project.

| Profile | Best fit |
|---|---|
| <img src="/leader.svg" alt="Leader Agent Profile" width="64" height="64"> **Leader** | Plans complex work, delegates to specialists, verifies results, and combines the delivery. |
| <img src="/explorer.svg" alt="Explorer Agent Profile" width="64" height="64"> **Explorer** | Investigates unfamiliar systems, resolves unknowns, and reports evidence without changing state. |
| <img src="/builder.svg" alt="Builder Agent Profile" width="64" height="64"> **Builder** | Implements focused changes and verifies the result. |
| <img src="/reviewer.svg" alt="Reviewer Agent Profile" width="64" height="64"> **Reviewer** | Independently checks correctness, risk, test coverage, and maintainability. |
| <img src="/operator.svg" alt="Operator Agent Profile" width="64" height="64"> **Operator** | Operates apps, browsers, MCP servers, and workflows with explicit control over side effects. |

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder lets you customize an agent just by chatting. Start from a built-in profile or describe a new specialist, then refine its instructions, tools, skills, model, and approval style in a guided conversation.

You can still edit the structured profile directly when you need precision. Agent Builder and the editor work on the same draft, so the conversation stays grounded in the definition you'll actually save.

## Related docs

- [Automations & Goals](./automations) — run a scheduled task as a saved agent
- [Subagents](./subagents) — a one-off delegation from the current conversation, no profile needed
