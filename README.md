<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — the agent runtime for real projects](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Quick Start](https://www.dotcraft.net/getting-started) · [Releases](https://github.com/DotHarness/dotcraft/releases) · [Developer Docs](https://www.dotcraft.net/developing/architecture/overview) · [License](./LICENSE)

### An AI agent that lives with the project — not with a single app.

DotCraft is an open-source, self-hosted, **project-scoped agent runtime** for persistent sessions, memory, background work, automations, and connected applications.

</div>

---

## Put a real project to work

Start with one project in Desktop or CLI. Add specialized agents, background work, channels, or application integrations when you need them.

### One project, one runtime

Conversations, memory, agents, skills, tools, plugins, Automations, and policies stay with the project. Open it from Desktop, CLI, a social channel, or a connected application and continue from the same context.

![Switching between project workspaces in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

[Learn about project-first workspaces →](https://www.dotcraft.net/features/project-first)

### Work with one agent or a team

- **Create a reusable agent.** Agent Profiles save its instructions, model, tools, skills, and permissions. Agent Builder helps you shape it through conversation.

- **Bring agents together.** Agent Teams split complex work among several specialized agents.

- **Keep work running.** Goals and Automations handle long-running, scheduled, or repeatable work.

![Customizing a specialized agent through conversation](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

[Create an agent for the job →](https://www.dotcraft.net/features/agent-system/agent-profiles) · [Bring agents together →](https://www.dotcraft.net/features/agent-system/teams) · [Set a Goal or Automation →](https://www.dotcraft.net/features/agent-system/automations)

### Connect DotCraft to an application

Use DotCraft's APIs, App Binding, or TypeScript, .NET, and Python SDKs to bring the project runtime into another product.

The application keeps its data and workflow. DotCraft handles conversations, tools, approvals, models, memory, and traces.

![The built-in Oratorio project board in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

[Connect an application →](https://www.dotcraft.net/developing/integrations/app-binding) · [Use an SDK →](https://www.dotcraft.net/developing/sdks/) · [See what is built with DotCraft →](#built-with-dotcraft)

### Keep work moving beyond Desktop

- **Automations** run recurring project work.

- **Background Channels** stay connected when Desktop is closed.

- **Channel Handoff** continues an active conversation in a social channel without starting over.

| Stay connected in the background | Continue the same conversation anywhere |
|---|---|
| ![DotCraft keeping social channels connected in the background](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif) | ![Handing off an active DotCraft conversation to a social channel](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif) |

[Continue work in a channel →](https://www.dotcraft.net/features/entry-points/channels) · [Set up recurring work →](https://www.dotcraft.net/features/agent-system/automations)

---

## Why DotCraft?

Most AI coding tools optimize the interaction between one developer and one client.

DotCraft focuses on a different layer:

> **The project—not the client—is the unit of agent state and execution.**

That changes what the agent can become.

| Design goal | What it means in DotCraft |
|---|---|
| **Persistent by project** | Conversations, memory, configuration, skills, plugins, and runtime state belong to the workspace. |
| **Shared across clients** | Desktop, CLI, editors, chat channels, and connected applications can work with the same project runtime. |
| **Runtime-owned work** | Active work, Goals, Automations, Agent Teams, and background memory do not depend on one UI window. |
| **Governed execution** | Approvals, hooks, worktree boundaries, sandbox settings, and traces keep agent work visible and controllable. |
| **Built for applications** | APIs, SDKs, App Binding, plugins, and Desktop extensions let other products reuse the runtime. |
| **Self-hosted and provider-flexible** | Run locally or on your server and connect OpenAI- or Anthropic-compatible providers. |

### Where it fits

| You primarily need… | A common fit |
|---|---|
| The fastest interactive coding experience in one IDE or terminal | An IDE or terminal coding agent |
| Agent primitives embedded directly in application code | An agent framework or SDK |
| A predefined visual business pipeline | A workflow builder |
| A persistent agent runtime owned by a project and shared by several clients or applications | **DotCraft** |

DotCraft is not trying to replace every coding interface. It provides the durable runtime underneath project agents, automations, and applications.

---

## Quick start

### Desktop

1. Download the latest build from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Open a real project folder as a workspace.
3. Configure a supported provider or sign in with a supported ChatGPT plan.
4. Start with a concrete request:

```text
Map this repository, explain the main execution path, and tell me where a new contributor should start.
```

### CLI

macOS / Linux:

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

Run a one-shot project task:

```bash
dotcraft exec "Review this repository and identify the three highest-risk changes."
```

See the complete [Getting Started guide](https://www.dotcraft.net/getting-started) for provider setup, Desktop, CLI, and server deployment.

---

## One runtime, three loops

A DotCraft workspace brings three kinds of continuity together. One runtime can stay in conversation with you, keep work moving, and remember what matters across sessions.

| Loop | What it gives the project |
|---|---|
| **Conversation** | Persistent conversations, approvals, queued input, and the ability to continue from another client. |
| **Work** | Goals, Automations, Agent Teams, and isolated worktrees that keep longer tasks moving under human control. |
| **Memory** | Reviewable project memory and history that carry useful context into future conversations. |

The result is a project agent whose conversation, ongoing work, and memory share the same boundary instead of being scattered across separate tools.

---

## Built with DotCraft

### Oratorio

![Oratorio — a project board powered by DotCraft](https://github.com/DotHarness/resources/raw/master/oratorio/banner-1280x640.png)

Oratorio is DotCraft Desktop's built-in project board for local tasks, GitHub and GitLab issues, reviews, and implementation work. The bundled Oratorio Server runs on demand locally and as part of the official remote Stack.

[Explore Oratorio →](https://www.dotcraft.net/features/oratorio)

---

## Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot), [codex](https://github.com/openai/codex) and [agent-framework](https://github.com/microsoft/agent-framework).

Special thanks to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

# Articles

Technical articles related to dotcraft.

[Why is your agent so expensive: Troubleshooting records of a 0% Prompt Cache hit rate.](https://zhuanlan.zhihu.com/p/2044201072466588522)

## License

Apache License 2.0
