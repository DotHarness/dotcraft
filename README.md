<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Getting Started](https://www.dotcraft.net/getting-started) · [Download Release](https://github.com/DotHarness/dotcraft/releases) · [DeepWiki](https://deepwiki.com/DotHarness/dotcraft) · [License](./LICENSE)

AI Agent lives in your project. All in one workspace.

</div>

## About

DotCraft is a .NET 10 / C# Agent Harness. It organizes AI workflows around a real project folder, allowing multiple entry points to share one session core, configuration, skills, tools, tasks, and observability surface.

- Project first: plugins, skills, sessions, and memory are integrated with the project, the agent can better understand your project.
- Unified session model: CLI, Desktop, TUI, chatbots, etc, all applications reuse the same execution engine.
- Observability and governance: approvals, traces, Dashboard, Hooks, and sandbox settings make agent workflows easier to inspect and control.
- Extensibility and integration: AppServer, SDKs, and plugins support custom entry points and business workflows.
- Bring your own model: works with any OpenAI- or Anthropic-protocol provider, reuses an active ChatGPT (Codex) subscription, and natively supports DeepSeek V4 and MiMo V2.5 reasoning models.

## Highlights

### Multi-Workspace — Every Project Gets Its Own Agent

![DotCraft multi-workspace](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

Most agents stretch one workspace across every project. DotCraft does the opposite: every project is its own workspace, with its own `.craft/` memory, skills, config, and model choices. Desktop keeps several open at once and switches between them instantly — each project resumes right where you left it, and moving, handing off, or backing one up carries its whole agent along.

[Learn more →](https://www.dotcraft.net/features/project-first)

### Desktop Extensions — Plugins with a Full View Inside Desktop

![DotCraft Desktop extensions](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

Plugins can now render their own UI right inside Desktop, not just add tools. The Oratorio board is the first: connect it to a thread and its board opens as a full view, reading items and queuing work through the same approvals and audit trail as any tool.

[Learn more →](https://www.dotcraft.net/developing/integrations/desktop-extensions)

### Goal — Persistent Conversation Objectives

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

Pin a long-term objective to a conversation and set a token or time budget — whenever the conversation goes idle, DotCraft will quietly keep working toward it. You decide when it pauses, resumes, or wraps up.

[Learn more →](https://www.dotcraft.net/features/agent-system/automations#goals)

### Teams — Multi-agent Mission Board

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

For a complex request, DotCraft assembles a small team — Explorer, Builder, Reviewer, Operator — and a Team Leader who splits the work, dispatches it in parallel, and brings the results back together. You hand over one ask; you get the finished mission.

[Learn more →](https://www.dotcraft.net/features/agent-system/teams)

### Dreams — Background Memory Consolidation

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

While you work, Dreams quietly reviews recent activity in the background and drafts memory entries worth keeping. You approve them at your own pace, so the agent only remembers what you've actually agreed to.

[Learn more →](https://www.dotcraft.net/features/agent-system/memory)

### Cross Channels — One Conversation, Any Platform

![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

Start a conversation on Desktop, continue it in the TUI, and pick it back up later on QQ, WeCom, Feishu, Telegram, or WeChat. It's the same conversation everywhere, with tool approvals rendered natively on each platform.

[Learn more →](https://www.dotcraft.net/features/entry-points/channels)

### App — SDK-driven App Binding for External Extensions

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

With the DotCraft SDK you can wrap your own service into an App and plug it straight into the agent — bringing custom tools, data, and workflows into the conversation with no extra middleware. Per-tool approval and full audit trails come built in.

[Learn more →](https://www.dotcraft.net/developing/integrations/app-binding)

### ChatGPT Plan — Sign in with ChatGPT, No Extra API Costs

![Sign in with ChatGPT](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/chatgpt.gif)

Already paying for ChatGPT Plus, Pro, Team, Business, or Enterprise? Sign in with your ChatGPT account and DotCraft will run on that subscription — no separate API key, no extra usage fees.

[Learn more →](https://www.dotcraft.net/getting-started)

## Get Started

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

1. Download the desktop app from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Choose a real project folder as your workspace.
3. Follow the initialization guide to configure the workspace and model provider.
4. Create a session and send your first repository-understanding request.

See [Getting Started](https://www.dotcraft.net/getting-started) for the full guided flow.

## Documentation

| Goal | Document |
|------|----------|
| Install, configure, and run DotCraft for the first time | [Getting Started](https://www.dotcraft.net/getting-started) |
| Use the graphical desktop client | [Desktop](https://www.dotcraft.net/features/entry-points/desktop) |
| Use the full terminal interface | [TUI](https://www.dotcraft.net/features/entry-points/tui) |
| Run local automation tasks | [Automations & Hooks](https://www.dotcraft.net/features/agent-system/automations) |
| Connect clients, bots, or custom adapters | [Channels & Bots](https://www.dotcraft.net/features/entry-points/channels) |
| Deploy DotCraft and channel bots on a server | [Server Deployment](https://www.dotcraft.net/developing/lifecycle/server-deployment) |
| Architecture, SDKs, and protocols | [Architecture](https://www.dotcraft.net/developing/architecture/overview) |

## Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot) and [codex](https://github.com/openai/codex), and built on [agent-framework](https://github.com/microsoft/agent-framework).

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
