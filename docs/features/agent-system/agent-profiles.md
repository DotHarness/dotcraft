# Agent Profiles

An Agent Profile saves a purpose-built DotCraft agent so you can reach for it whenever that working style fits. Build one through conversation with Agent Builder, then reuse its role instructions, model defaults, tools, skills, MCP access, and approval behavior.

![DotCraft Agent Profiles](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

Start with one specialist or build out a whole agent team: an Explorer for research, a Builder for implementation, or an Operator for app workflows. Each role is an independent Profile you can choose whenever the work calls for it.

DotCraft derives each profile's avatar from its name, so the same name keeps the same visual identity wherever the profile appears.

Names support spaces and Unicode (1–240 characters, without control characters). They are trimmed and NFC-normalized while preserving case. Display, lookup, and avatars use that one name. Storage filenames are generated safely; no separate ID is required.

In Desktop, choose **Agents → New agent** to open the editor and builder conversation together. Describe what you need in the conversation or fill in the profile directly, then choose **Create** to save it. Select a built-in template from the Agents gallery to start from an existing role.

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
| **Night Shift** | Works through an overnight queue and prepares a morning digest. |
| **Inbox Triage** | Sorts supplied email and drafts replies. |
| **Chief of Staff** | Tracks the team plan and brings decisions to the user. |
| **Negotiator** | Researches pricing and drafts negotiation messages. |
| **Prototyper** | Turns ideas into working prototypes. |
| **Researcher** | Investigates questions and reports evidence. |
| **Lookout** | Checks pages and reports meaningful changes. |
| **Competitor Watcher** | Tracks competitor pricing and launches, and prepares a brief. |

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder lets you customize an agent just by chatting. Start from a built-in profile or describe a new specialist, then refine its instructions, tools, skills, model, and approval style in a guided conversation.

You can still edit the structured profile directly when you need precision. Agent Builder and the editor work on the same draft, so the conversation stays grounded in the definition you'll actually save.

## Related docs

- [Automations & Goals](./automations) — run a scheduled task as a saved agent
- [Subagents](./subagents) — a one-off delegation from the current conversation, no profile needed
