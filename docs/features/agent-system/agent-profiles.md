# Agent Profiles

Agent Profiles let you save a purpose-built DotCraft agent and reuse it wherever that working style makes sense. A profile can carry role instructions, model defaults, tool and skill choices, MCP access, and approval behavior.

![DotCraft Agent Profiles](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

Use profiles when you want a repeatable agent for a specific job: a reviewer with read-heavy tools, a builder with implementation tools, an operator for app workflows, or a teammate with a focused mission role.

## Where Profiles Run

| Place | How it helps |
|---|---|
| Chat | Pick a saved agent from the composer and run the thread with that role and capability set. |
| Teams | Assign profiles to Team members so each teammate keeps a stable role, style, and tool boundary. |
| Automations | Bind a task to a saved agent so scheduled or manual runs use that agent's tools, skills, and model. |

Profiles are resolved when a thread or task starts. Existing profile-backed threads keep their saved runtime snapshot until you explicitly refresh them or start a new thread or task with the updated profile.

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder lets you customize your agent just by chatting. Describe what the agent should do, then refine its instructions, tools, skills, model, and approval style in a guided conversation.

You can still edit the structured profile directly when you need precision. Agent Builder and the editor work on the same profile draft, so the conversation stays grounded in the saved agent definition.

## Related Docs

- [Teams](./teams) — role-based multi-agent Missions.
- [Automations & Goals](./automations) — bind automation tasks to saved agents.
- [SubAgents](./subagents) — one-off delegation from an existing conversation.
